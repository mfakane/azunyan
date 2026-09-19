namespace Azunote;

/// <summary>
/// The output channels a tool applies while it is still running. A channel
/// that is not named here is applied once the tool has exited.
/// </summary>
[Flags]
public enum ExternalToolStreamChannels
{
    None = 0,
    Mixed = 1,
    Stdout = 2,
    Stderr = 4
}

/// <summary>
/// Receives the text a streamed channel has produced so far. Chunks arrive in
/// the order the tool wrote them and are never a partial line ending or a
/// partial surrogate pair.
/// </summary>
internal interface IExternalToolStreamSink
{
    void Write(ExternalToolOutputChannel channel, string text);
}

/// <summary>
/// The rules a streamed channel follows, shared by the settings validation
/// that reports a bad definition and by the code that applies output.
/// </summary>
public static class ExternalToolStreaming
{
    /// <summary>
    /// Whether an action can be applied before the tool has exited.
    /// <see cref="ExternalToolOutputMode.ReloadFile"/> has no output to apply,
    /// and <see cref="ExternalToolOutputMode.ShowCompletion"/> needs the whole
    /// candidate list before it opens a window.
    /// </summary>
    public static bool IsStreamable(ExternalToolOutputMode mode) =>
        mode is ExternalToolOutputMode.ReplaceDocument
            or ExternalToolOutputMode.ReplaceSelection
            or ExternalToolOutputMode.NewDocument;

    public static ExternalToolStreamChannels ToFlag(ExternalToolOutputChannel channel) =>
        channel switch
        {
            ExternalToolOutputChannel.Mixed => ExternalToolStreamChannels.Mixed,
            ExternalToolOutputChannel.Stdout => ExternalToolStreamChannels.Stdout,
            ExternalToolOutputChannel.Stderr => ExternalToolStreamChannels.Stderr,
            _ => ExternalToolStreamChannels.None
        };

    /// <summary>The name a channel has in a tool definition.</summary>
    public static string ToTomlValue(ExternalToolOutputChannel channel) =>
        channel switch
        {
            ExternalToolOutputChannel.Mixed => "output",
            ExternalToolOutputChannel.Stdout => "stdout",
            ExternalToolOutputChannel.Stderr => "stderr",
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };

    public static bool TryParseChannel(
        string? value,
        out ExternalToolOutputChannel channel)
    {
        switch (value)
        {
            case "output":
                channel = ExternalToolOutputChannel.Mixed;
                return true;
            case "stdout":
                channel = ExternalToolOutputChannel.Stdout;
                return true;
            case "stderr":
                channel = ExternalToolOutputChannel.Stderr;
                return true;
            default:
                channel = default;
                return false;
        }
    }

    public static bool Streams(
        ExternalToolStreamChannels channels,
        ExternalToolOutputChannel channel) =>
        (channels & ToFlag(channel)) != ExternalToolStreamChannels.None;

    /// <summary>
    /// Describes what is wrong with a definition's streamed channels, or null
    /// when they are usable. The rules exist because streamed output is
    /// applied before the exit code is known and before any other channel has
    /// had its turn.
    /// </summary>
    public static string? Describe(
        ExternalToolStreamChannels channels,
        ExternalToolOutputActions output,
        ExternalToolOutputActions stdout,
        ExternalToolOutputActions stderr)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (channels == ExternalToolStreamChannels.None)
        {
            return null;
        }

        if (channels.HasFlag(ExternalToolStreamChannels.Mixed)
            && (channels.HasFlag(ExternalToolStreamChannels.Stdout)
                || channels.HasFlag(ExternalToolStreamChannels.Stderr)))
        {
            return "stream cannot name output together with stdout or stderr, "
                + "because output carries the same text.";
        }

        var streamed = new List<(ExternalToolOutputChannel Channel, ExternalToolOutputActions Actions)>();
        foreach (var (channel, actions) in new[]
                 {
                     (ExternalToolOutputChannel.Mixed, output),
                     (ExternalToolOutputChannel.Stdout, stdout),
                     (ExternalToolOutputChannel.Stderr, stderr)
                 })
        {
            if (Streams(channels, channel))
            {
                streamed.Add((channel, actions));
            }
        }

        foreach (var (channel, actions) in streamed)
        {
            var name = ToTomlValue(channel);
            if (actions.OnSuccess != actions.OnFailure)
            {
                return $"stream names {name}, which is applied before the exit "
                    + "code is known, so it needs one action rather than a "
                    + "[zero, non-zero] array.";
            }

            if (actions.OnSuccess == ExternalToolOutputMode.Ignore)
            {
                return $"stream names {name}, which ignores its output.";
            }

            if (!IsStreamable(actions.OnSuccess))
            {
                return $"stream names {name}, which uses "
                    + $"{ExternalToolEnumValues.ToTomlValue(actions.OnSuccess)}; "
                    + "only replaceDocument, replaceSelection, and newDocument "
                    + "can be applied while the tool runs.";
            }
        }

        for (var index = 1; index < streamed.Count; index++)
        {
            if (streamed[index].Actions.OnSuccess == streamed[0].Actions.OnSuccess)
            {
                return "stream names two channels that use the same action, so "
                    + "their output would be interleaved into one place.";
            }
        }

        return null;
    }
}

/// <summary>
/// Collects the chunks one channel produces and hands back the text that is
/// safe to apply now. A line ending is never split across two applications,
/// nor is a surrogate pair, and the final line ending is held back until more
/// text arrives so that a run's last line does not first appear with a blank
/// line after it.
/// </summary>
internal sealed class ExternalToolStreamBuffer
{
    private string _pending = string.Empty;

    /// <summary>Takes a chunk and returns the text that can be applied now.</summary>
    public string Append(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0)
        {
            return string.Empty;
        }

        _pending += chunk;
        var start = FindLastLineEndingStart(_pending);
        return start <= 0 ? string.Empty : Take(start);
    }

    /// <summary>
    /// Returns the text that can be applied although the line it ends is still
    /// being written. A tool that reports progress without newlines becomes
    /// visible this way.
    /// </summary>
    public string FlushIdle()
    {
        if (_pending.Length == 0)
        {
            return string.Empty;
        }

        var start = FindLastLineEndingStart(_pending);
        if (start >= 0 && start + LineEndingLength(_pending, start) == _pending.Length)
        {
            // The pending text is one whole line ending: holding it back is
            // the point, so there is nothing to flush yet.
            return start == 0 ? string.Empty : Take(start);
        }

        var length = _pending.Length;
        if (char.IsHighSurrogate(_pending[^1]))
        {
            length--;
        }

        return Take(length);
    }

    /// <summary>Returns everything that is left, exactly as it was written.</summary>
    public string Complete() => Take(_pending.Length);

    private string Take(int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        var text = _pending[..length];
        _pending = _pending[length..];
        return text;
    }

    /// <summary>
    /// The index the last line ending in <paramref name="text"/> starts at, or
    /// -1 when it has none. A trailing carriage return counts, so that a CRLF
    /// split across two chunks is never applied as a lone carriage return.
    /// </summary>
    private static int FindLastLineEndingStart(string text)
    {
        var index = text.LastIndexOfAny(['\r', '\n']);
        return index > 0 && text[index] == '\n' && text[index - 1] == '\r'
            ? index - 1
            : index;
    }

    private static int LineEndingLength(string text, int start) =>
        text[start] == '\r' && start + 1 < text.Length && text[start + 1] == '\n'
            ? 2
            : 1;
}

/// <summary>
/// Feeds the chunks a run produces to one <see cref="ExternalToolStreamBuffer"/>
/// per streamed channel, and flushes a line that is still being written once
/// the tool has been quiet for <see cref="IdleInterval"/>. A run's parts share
/// one relay, so <c>per</c> concatenates its parts exactly as a buffered run
/// does.
/// </summary>
internal sealed class ExternalToolStreamRelay : IDisposable
{
    internal static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(75);

    private readonly IExternalToolStreamSink _sink;
    private readonly Dictionary<ExternalToolOutputChannel, ExternalToolStreamBuffer> _buffers = [];
    private readonly Timer _idleTimer;
    private readonly object _gate = new();
    private bool _completed;

    public ExternalToolStreamRelay(
        IExternalToolStreamSink sink,
        ExternalToolStreamChannels channels)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        foreach (var channel in new[]
                 {
                     ExternalToolOutputChannel.Mixed,
                     ExternalToolOutputChannel.Stdout,
                     ExternalToolOutputChannel.Stderr
                 })
        {
            if (ExternalToolStreaming.Streams(channels, channel))
            {
                _buffers[channel] = new ExternalToolStreamBuffer();
            }
        }

        _idleTimer = new Timer(_ => FlushIdle(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsEmpty => _buffers.Count == 0;

    /// <summary>
    /// Takes a chunk a stream read produced. The caller keeps stdout and stderr
    /// chunks in the order they arrived, which is the order the mixed channel
    /// is assembled in.
    /// </summary>
    public void Append(ExternalToolOutputChannel channel, string chunk)
    {
        if (!_buffers.TryGetValue(channel, out var buffer))
        {
            return;
        }

        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            Write(channel, buffer.Append(chunk));
            _idleTimer.Change(IdleInterval, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes everything the run produced and stops flushing.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
            foreach (var (channel, buffer) in _buffers)
            {
                Write(channel, buffer.Complete());
            }
        }
    }

    public void Dispose()
    {
        Complete();
        _idleTimer.Dispose();
    }

    private void FlushIdle()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            foreach (var (channel, buffer) in _buffers)
            {
                Write(channel, buffer.FlushIdle());
            }
        }
    }

    private void Write(ExternalToolOutputChannel channel, string text)
    {
        if (text.Length > 0)
        {
            _sink.Write(channel, text);
        }
    }
}
