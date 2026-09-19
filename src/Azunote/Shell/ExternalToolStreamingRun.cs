using Azunyan.Core;

namespace Azunote;

public sealed partial class ExternalToolController
{
    /// <summary>
    /// Applies the output of a run's streamed channels while the tool is still
    /// running. Chunks arrive on whatever thread read them, are queued, and are
    /// applied together on the UI thread, so a tool that writes faster than the
    /// editor can redraw costs one edit per turn rather than one per chunk.
    /// </summary>
    private sealed class ExternalToolStreamingRun : IExternalToolStreamSink, IDisposable
    {
        private readonly ExternalToolController _owner;
        private readonly Dictionary<ExternalToolOutputChannel, StreamTarget> _targets = [];
        private readonly List<(StreamTarget Target, string Text)> _pending = [];
        private readonly object _gate = new();
        private bool _draining;
        private bool _undoGroupOpen;

        private ExternalToolStreamingRun(
            ExternalToolController owner,
            ExternalToolDefinition definition,
            TextSelection selection)
        {
            _owner = owner;
            foreach (var (channel, actions) in new[]
                     {
                         (ExternalToolOutputChannel.Mixed, definition.Output),
                         (ExternalToolOutputChannel.Stdout, definition.Stdout),
                         (ExternalToolOutputChannel.Stderr, definition.Stderr)
                     })
            {
                if (!ExternalToolStreaming.Streams(definition.Stream, channel))
                {
                    continue;
                }

                _targets[channel] = actions.OnSuccess switch
                {
                    ExternalToolOutputMode.ReplaceDocument => new EditorStreamTarget(this, selection, wholeDocument: true),
                    ExternalToolOutputMode.ReplaceSelection => new EditorStreamTarget(this, selection, wholeDocument: false),
                    ExternalToolOutputMode.NewDocument => new NewDocumentStreamTarget(this),
                    _ => throw new InvalidOperationException(
                        $"An external-tool channel using {actions.OnSuccess} cannot stream.")
                };
            }
        }

        /// <summary>
        /// Returns the run that applies this definition's streamed channels, or
        /// null when it streams none.
        /// </summary>
        public static ExternalToolStreamingRun? TryCreate(
            ExternalToolController owner,
            ExternalToolDefinition definition,
            TextSelection selection) =>
            definition.Stream == ExternalToolStreamChannels.None
                ? null
                : new ExternalToolStreamingRun(owner, definition, selection);

        public void Write(ExternalToolOutputChannel channel, string text)
        {
            if (!_targets.TryGetValue(channel, out var target))
            {
                return;
            }

            var schedule = false;
            lock (_gate)
            {
                _pending.Add((target, text));
                if (!_draining)
                {
                    _draining = true;
                    schedule = true;
                }
            }

            if (schedule)
            {
                Post(() => _ = DrainAsync());
            }
        }

        /// <summary>
        /// Applies whatever is still queued and finishes each target, on the UI
        /// thread and after every chunk this run produced.
        /// <paramref name="apply"/> is false when no process ran at all, which
        /// is the one case where a streamed channel leaves the document alone.
        /// </summary>
        public async Task FinishAsync(bool apply)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async () =>
            {
                try
                {
                    await DrainAsync();
                    if (apply)
                    {
                        foreach (var target in _targets.Values)
                        {
                            await target.FinishAsync();
                        }
                    }

                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });

            await completion.Task;
            EndUndoGroup();
            foreach (var target in _targets.Values)
            {
                if (target.Error is { } error)
                {
                    await _owner._prompt.ShowErrorAsync(
                        "Could not apply external tool output",
                        error);
                    break;
                }
            }
        }

        /// <summary>
        /// Closes the undo group a cancelled or failed run left open, so that
        /// what was already applied is still one undo step.
        /// </summary>
        public void Dispose()
        {
            EndUndoGroup();
            foreach (var target in _targets.Values)
            {
                target.Dispose();
            }
        }

        private async Task DrainAsync()
        {
            while (true)
            {
                List<(StreamTarget Target, string Text)> batch;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        _draining = false;
                        return;
                    }

                    batch = [.. _pending];
                    _pending.Clear();
                }

                foreach (var (target, text) in batch)
                {
                    await target.AppendAsync(text);
                }
            }
        }

        /// <summary>
        /// Runs the work on the UI thread. Without a dispatcher, which is the
        /// case outside the shell, it runs where it was queued instead; the
        /// order is the same either way.
        /// </summary>
        private void Post(Action work)
        {
            if (_owner._dispatcher is null || !_owner._dispatcher.TryEnqueue(work))
            {
                work();
            }
        }

        private void BeginUndoGroup()
        {
            if (_undoGroupOpen || _owner._editor is not IEditorView view)
            {
                return;
            }

            view.BeginUndoGroup();
            _undoGroupOpen = true;
        }

        private void EndUndoGroup()
        {
            if (!_undoGroupOpen || _owner._editor is not IEditorView view)
            {
                return;
            }

            _undoGroupOpen = false;
            view.EndUndoGroup();
        }

        private abstract class StreamTarget
        {
            public string? Error { get; protected set; }

            public abstract Task AppendAsync(string text);

            public abstract Task FinishAsync();

            public virtual void Dispose()
            {
            }
        }

        /// <summary>
        /// Applies output to the running document, replacing the selection or
        /// the whole text with the first output and appending what follows.
        /// The run owns a region of the document: the selection until the first
        /// output arrives, and the output itself after that. An edit made
        /// elsewhere while the tool runs moves that region, so the next output
        /// still lands where the earlier output ended.
        /// </summary>
        private sealed class EditorStreamTarget : StreamTarget
        {
            private readonly ExternalToolStreamingRun _run;
            private readonly TextSelection _selection;
            private readonly bool _wholeDocument;
            private int _regionStart;
            private int _regionLength;
            private int _expectedLength;
            private bool _started;
            private bool _applying;

            public EditorStreamTarget(
                ExternalToolStreamingRun run,
                TextSelection selection,
                bool wholeDocument)
            {
                _run = run;
                _selection = selection;
                _wholeDocument = wholeDocument;
                var editor = run._owner._editor;
                _expectedLength = editor.Text.Length;
                _regionStart = wholeDocument ? 0 : Math.Min(selection.Start, _expectedLength);
                _regionLength = wholeDocument
                    ? _expectedLength
                    : Math.Min(selection.Length, _expectedLength - _regionStart);
                editor.Edited += OnEdited;
            }

            public override Task AppendAsync(string text)
            {
                Apply(text);
                return Task.CompletedTask;
            }

            public override Task FinishAsync()
            {
                if (Error is not null)
                {
                    return Task.CompletedTask;
                }

                if (!_started)
                {
                    // A run that produced nothing still replaces what it was
                    // pointed at, the way a buffered run with empty output does.
                    Apply(string.Empty);
                    if (Error is not null)
                    {
                        return Task.CompletedTask;
                    }
                }

                var editor = _run._owner._editor;
                if (!_wholeDocument && editor.Text.Length == _expectedLength)
                {
                    var end = _regionStart + _regionLength;
                    editor.SetSelection(_selection.IsReversed
                        ? new TextSelection(end, _regionStart)
                        : new TextSelection(_regionStart, end));
                }

                _run._owner._documents.Session.ObserveText(editor.Text);
                return Task.CompletedTask;
            }

            public override void Dispose() => _run._owner._editor.Edited -= OnEdited;

            /// <summary>
            /// Moves the region an edit made elsewhere in the document shifted,
            /// and gives up when the edit took part of the region with it.
            /// </summary>
            private void OnEdited(object? sender, TextChange change)
            {
                if (_applying)
                {
                    // Applying keeps the bookkeeping for the edits it makes.
                    return;
                }

                var delta = change.NewText.Length - change.OldRange.Length;
                _expectedLength += delta;
                if (Error is not null)
                {
                    return;
                }

                var end = _regionStart + _regionLength;
                if (change.OldRange.End <= _regionStart)
                {
                    _regionStart += delta;
                }
                else if (change.OldRange.Start >= end)
                {
                    // After the region: nothing of ours moved.
                }
                else if (change.OldRange.Start >= _regionStart
                    && change.OldRange.End <= end)
                {
                    _regionLength += delta;
                }
                else
                {
                    Error = "The document changed while the tool was running.";
                }
            }

            private void Apply(string text)
            {
                if (Error is not null)
                {
                    return;
                }

                var editor = _run._owner._editor;
                // An edit that did not reach the region, such as the whole
                // document being replaced at once, leaves the length disagreeing
                // with what the region says it should be.
                if (editor.Text.Length != _expectedLength
                    || _regionStart + _regionLength > editor.Text.Length)
                {
                    Error = _started
                        ? "The document changed while the tool was running."
                        : "The selection changed while the tool was running.";
                    return;
                }

                var target = _started
                    ? new TextRange(_regionStart + _regionLength, 0)
                    : new TextRange(_regionStart, _regionLength);
                if (!_started)
                {
                    _run.BeginUndoGroup();
                    _started = true;
                    _regionLength = 0;
                }

                _applying = true;
                try
                {
                    editor.Replace(target, text);
                }
                finally
                {
                    _applying = false;
                }

                _expectedLength += text.Length - target.Length;
                _regionLength += text.Length;
                _run._owner._documents.Session.ObserveText(editor.Text);
            }
        }

        /// <summary>
        /// Opens a window when the first output arrives and appends the rest to
        /// it, so a tool that produces nothing opens no window. Without a way
        /// to open one that can be appended to, the output is collected and the
        /// window is opened once with all of it.
        /// </summary>
        private sealed class NewDocumentStreamTarget : StreamTarget
        {
            private readonly ExternalToolStreamingRun _run;
            private readonly System.Text.StringBuilder _collected = new();
            private IExternalToolDocument? _document;

            public NewDocumentStreamTarget(ExternalToolStreamingRun run) => _run = run;

            public override async Task AppendAsync(string text)
            {
                if (_run._owner._openStreamedDocument is null)
                {
                    _collected.Append(text);
                    return;
                }

                _document ??= await _run._owner._openStreamedDocument();
                _document.Append(text);
            }

            public override async Task FinishAsync()
            {
                if (_document is not null)
                {
                    _document.Complete();
                    return;
                }

                if (_collected.Length > 0)
                {
                    await _run._owner._openTextInNewWindow(_collected.ToString());
                }
            }
        }
    }
}

/// <summary>
/// Appends streamed external-tool output to a window's document, keeping the
/// whole run one undo step.
/// </summary>
internal sealed class EditorStreamedDocument : IExternalToolDocument
{
    private readonly IEditorView _editor;
    private readonly DocumentSession _session;
    private bool _started;

    public EditorStreamedDocument(IEditorView editor, DocumentSession session)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!_started)
        {
            _editor.BeginUndoGroup();
            _started = true;
        }

        _editor.Replace(new TextRange(_editor.Text.Length, 0), text);
        _session.ObserveText(_editor.Text);
    }

    public void Complete()
    {
        if (_started)
        {
            _started = false;
            _editor.EndUndoGroup();
        }

        _session.ObserveText(_editor.Text);
    }
}
