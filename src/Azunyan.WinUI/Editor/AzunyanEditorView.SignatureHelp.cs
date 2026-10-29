using Azunyan.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Run = Microsoft.UI.Xaml.Documents.Run;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private string[] _signatureHelpTriggerCharacters = [];
    private bool _signatureHelpRequested;
    private long _signatureHelpGeneration;
    private SignatureHelpResults? _signatureHelpResult;

    /// <summary>The signature chosen with Up and Down, or -1 for the provider's.</summary>
    private int _selectedSignature = -1;

    /// <summary>
    /// Literal strings which ask for the signatures of the call at the caret
    /// after they are inserted, such as <c>(</c> and <c>,</c>.
    /// </summary>
    public IReadOnlyList<string> SignatureHelpTriggerCharacters
    {
        get => _signatureHelpTriggerCharacters;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _signatureHelpTriggerCharacters = [.. value
                .Where(trigger => !string.IsNullOrEmpty(trigger))
                .Distinct(StringComparer.Ordinal)];
        }
    }

    /// <summary>Asks for the signatures of the call at the caret.</summary>
    public void RequestSignatureHelp()
    {
        if (IsReadOnly || !IsLoaded || IsComposing)
        {
            return;
        }

        StartSignatureHelp();
        RequestSignatureHelpProvider();
    }

    private bool IsSignatureHelpPopupOpen => SignatureHelpPopup.IsOpen;

    private void StartSignatureHelp()
    {
        if (!_signatureHelpRequested)
        {
            _signatureHelpRequested = true;
            _selectedSignature = -1;
        }
    }

    /// <summary>
    /// Asks again at the caret while the signatures are shown, so the active
    /// parameter follows typing and caret moves. The help shown stays until
    /// the answer, which ends it when the caret has left the call.
    /// </summary>
    private void RequestSignatureHelpProvider()
    {
        if (!_signatureHelpRequested || !IsLoaded)
        {
            return;
        }

        if (_providers.SignatureHelp is null)
        {
            EndSignatureHelp();
            return;
        }

        var generation = NextProviderGeneration(ref _signatureHelpGeneration);
        var selection = Document.Selection;
        _ = ApplySignatureHelpResultAsync(
            _providerScheduler.RequestSignatureHelpAsync(Snapshot, selection.CaretPosition, selection),
            generation);
    }

    private async Task ApplySignatureHelpResultAsync(
        Task<SignatureHelpResults?> request,
        long generation)
    {
        SignatureHelpResults? result;
        try
        {
            result = await request.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        if (result is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded
                || generation != _signatureHelpGeneration
                || !ReferenceEquals(result.Context.Snapshot, Snapshot))
            {
                return;
            }

            if (result.SignatureHelp is null)
            {
                EndSignatureHelp();
                return;
            }

            _signatureHelpResult = result;
            UpdateSignatureHelpPopup();
        });
    }

    private void EndSignatureHelp()
    {
        NextProviderGeneration(ref _signatureHelpGeneration);
        _signatureHelpRequested = false;
        _signatureHelpResult = null;
        _selectedSignature = -1;
        SignatureHelpPopup.IsOpen = false;
    }

    private void MoveSignatureSelection(int delta)
    {
        if (_signatureHelpResult?.SignatureHelp is not { } help)
        {
            return;
        }

        var count = help.Signatures.Count;
        var current = _selectedSignature >= 0 ? _selectedSignature : help.ActiveSignature;
        _selectedSignature = ((current + delta) % count + count) % count;
        UpdateSignatureHelpPopup();
    }

    /// <summary>
    /// Shows the signature above the caret's line, out of the way of the
    /// completion list below it, with the parameter being typed in bold.
    /// </summary>
    private void UpdateSignatureHelpPopup()
    {
        if (!IsLoaded
            || !IsProjectedTextSurface
            || IsComposing
            || !_signatureHelpRequested
            || _signatureHelpResult?.SignatureHelp is not { } help
            || !TryGetRendererCaretRect(DocumentAnchor.Before(Document.Selection.CaretPosition), out var caretRect))
        {
            SignatureHelpPopup.IsOpen = false;
            return;
        }

        var index = _selectedSignature >= 0 && _selectedSignature < help.Signatures.Count
            ? _selectedSignature
            : help.ActiveSignature;
        var signature = help.Signatures[index];
        var parameter = signature.ActiveParameter >= 0 && signature.ActiveParameter < signature.Parameters.Count
            ? signature.Parameters[signature.ActiveParameter]
            : null;
        var label = signature.Label;
        var bold = parameter is { } active && active.Label.End <= label.Length
            ? active.Label
            : new TextRange(label.Length, 0);

        var inlines = SignatureHelpLabel.Inlines;
        inlines.Clear();
        if (help.Signatures.Count > 1)
        {
            inlines.Add(new Run
            {
                Text = $"{index + 1}/{help.Signatures.Count}  ",
                Foreground = new SolidColorBrush(_colorScheme.InlayForeground)
            });
        }

        inlines.Add(new Run { Text = label[..bold.Start] });
        inlines.Add(new Run { Text = label[bold.Start..bold.End], FontWeight = FontWeights.Bold });
        inlines.Add(new Run { Text = label[bold.End..] });

        var documentation = string.Join(
            Environment.NewLine,
            new[] { parameter?.Documentation, signature.Documentation }.Where(text => !string.IsNullOrWhiteSpace(text)));
        SignatureHelpDocumentation.Text = documentation;
        SignatureHelpDocumentation.Visibility = documentation.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        var inputOrigin = ProjectedSurfaceHost.TransformToVisual(RootGrid)
            .TransformPoint(new Point(0, 0));
        SignatureHelpBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var height = SignatureHelpBorder.DesiredSize.Height;
        var above = inputOrigin.Y + caretRect.Y - height - 4;
        SignatureHelpPopup.HorizontalOffset = Math.Max(inputOrigin.X, inputOrigin.X + caretRect.X);
        // On the top line there is no room above, so the help goes below
        // unless the completion list is there.
        SignatureHelpPopup.VerticalOffset = above < 0 && !IsCompletionPopupOpen
            ? inputOrigin.Y + caretRect.Y + caretRect.Height + 4
            : above;
        SignatureHelpPopup.IsOpen = true;
    }
}
