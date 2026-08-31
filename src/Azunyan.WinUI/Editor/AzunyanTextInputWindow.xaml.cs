using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Azunyan.WinUI;

/// <summary>
/// A small native text-service host for an arbitrary document position.
///
/// The inner <see cref="TextBox"/> only contains the current sliding window;
/// this component has no document, projection, or drawing responsibilities.
/// </summary>
public sealed partial class AzunyanTextInputWindow : UserControl
{
    private bool _synchronizing;
    private string _synchronizedText = string.Empty;
    private string _synchronizedNativeText = string.Empty;
    private TextRange? _compositionRange;
    private int _windowStart;
    private long _generation;
    private string? _preferredLineEnding;

    public AzunyanTextInputWindow()
    {
        InitializeComponent();
        IsTabStop = false;
        AutomationProperties.SetName(this, "Text input");
        AutomationProperties.SetHelpText(
            this,
            "Native text input window for the projected document editor");
        NativeTextBox.IsSpellCheckEnabled = false;
        NativeTextBox.IsTextPredictionEnabled = false;
        NativeTextBox.BeforeTextChanging += OnBeforeTextChanging;
        NativeTextBox.TextChanged += OnTextChanged;
        NativeTextBox.SelectionChanged += OnSelectionChanged;
        NativeTextBox.TextCompositionStarted += OnTextCompositionStarted;
        NativeTextBox.TextCompositionChanged += OnTextCompositionChanged;
        NativeTextBox.TextCompositionEnded += OnTextCompositionEnded;
        NativeTextBox.ExceptionSink = ReportCallbackException;
    }

    public int WindowStart => _windowStart;

    internal AzunyanNativeTextBox NativeTextBoxControl => NativeTextBox;

    public long Generation => _generation;

    public TextRange WindowRange => new(_windowStart, _synchronizedText.Length);

    public string WindowText => _synchronizedText;

    public TextSelection Selection => ToDocumentSelection();

    public bool IsComposing => _compositionRange is not null;

    public TextRange? CompositionRange => _compositionRange;

    internal string? PreferredLineEnding
    {
        get => _preferredLineEnding;
        set => _preferredLineEnding = value;
    }

    public event EventHandler<AzunyanTextInputChangedEventArgs>? InputChanged;

    public event EventHandler<AzunyanTextInputSelectionChangedEventArgs>? InputSelectionChanged;

    public event EventHandler<AzunyanTextInputCompositionChangedEventArgs>? CompositionChanged;

    public event EventHandler? NativeFocusChanged;

    /// <summary>
    /// Receives exceptions that crossed a native TextBox callback boundary.
    /// These must not escape into CoreMessaging, which terminates the process
    /// with 0xc000027b instead of raising a managed application exception.
    /// </summary>
    internal Action<string, Exception>? ExceptionSink { get; set; }

    /// <summary>
    /// Replaces the native window atomically. All offsets in the supplied
    /// selection and composition range are document offsets.
    /// </summary>
    public void SetWindow(
        long generation,
        int windowStart,
        string text,
        TextSelection selection,
        TextRange? compositionRange = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(windowStart);
        ArgumentNullException.ThrowIfNull(text);
        ValidateDocumentRange(selection.Range, windowStart, text.Length);
        if (compositionRange is { } composition)
        {
            ValidateDocumentRange(composition, windowStart, text.Length);
        }

        var localSelection = new TextSelection(
            selection.Anchor - windowStart,
            selection.Active - windowStart);
        var nativeText = ToNativeText(text);
        _synchronizing = true;
        try
        {
            _windowStart = windowStart;
            _generation = generation;
            _synchronizedText = text;
            _synchronizedNativeText = nativeText;
            _compositionRange = compositionRange;
            NativeTextBox.Text = nativeText;
            var nativeSelectionStart = ToNativeOffset(text, localSelection.Start);
            NativeTextBox.SelectionStart = nativeSelectionStart;
            NativeTextBox.SelectionLength =
                ToNativeOffset(text, localSelection.End) - nativeSelectionStart;
        }
        finally
        {
            _synchronizing = false;
        }
    }

    public new bool Focus(FocusState value) => NativeTextBox.Focus(value);

    /// <summary>
    /// Returns the native caret rectangle transformed into the requested
    /// ancestor's coordinate space.
    /// </summary>
    public Rect GetCaretRect(UIElement relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        var localRect = GetNativeCaretRect();
        var transform = NativeTextBox.TransformToVisual(relativeTo);
        var origin = transform.TransformPoint(new Point(localRect.X, localRect.Y));
        return new Rect(origin.X, origin.Y, localRect.Width, localRect.Height);
    }

    /// <summary>
    /// Places the one-character native input surface in a parent Canvas.
    /// </summary>
    public void SetCaretRect(Rect rect)
    {
        Width = Math.Max(1, rect.Width);
        Height = Math.Max(1, rect.Height);
        Canvas.SetLeft(this, rect.X);
        Canvas.SetTop(this, rect.Y);
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        try
        {
            base.OnGotFocus(e);
            NativeFocusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ReportCallbackException("GotFocus", exception);
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        try
        {
            base.OnLostFocus(e);
            NativeFocusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ReportCallbackException("LostFocus", exception);
        }
    }

    private void OnBeforeTextChanging(
        TextBox sender,
        TextBoxBeforeTextChangingEventArgs args)
    {
        // This event is intentionally observed at the native boundary. The
        // replacement is calculated in TextChanged, after Text is committed,
        // so IME and paste operations follow the same path.
    }

    private void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_synchronizing)
        {
            return;
        }

        try
        {
            var currentText = NativeTextBox.Text;
            if (string.Equals(_synchronizedNativeText, currentText, StringComparison.Ordinal))
            {
                RaiseSelectionChanged();
                return;
            }

            var (localNativeRange, insertedNativeText) = FindReplacement(
                _synchronizedNativeText,
                currentText);
            var localStart = ToDocumentOffset(_synchronizedText, localNativeRange.Start);
            var localEnd = ToDocumentOffset(_synchronizedText, localNativeRange.End);
            var localRange = TextRange.FromBounds(localStart, localEnd);
            var insertedText = FromNativeText(
                insertedNativeText,
                _synchronizedText,
                _preferredLineEnding);
            var change = new TextChange(
                new TextRange(_windowStart + localRange.Start, localRange.Length),
                localRange.Length == 0
                    ? string.Empty
                    : _synchronizedText.Substring(localRange.Start, localRange.Length),
                insertedText);
            _synchronizedText = ReplaceText(_synchronizedText, localRange, insertedText);
            _synchronizedNativeText = currentText;
            InputChanged?.Invoke(
                this,
                new AzunyanTextInputChangedEventArgs(
                    _generation,
                    change,
                    ToDocumentSelection(),
                    _compositionRange));
        }
        catch (Exception exception)
        {
            ReportCallbackException("TextChanged", exception);
        }
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (_synchronizing)
        {
            return;
        }

        try
        {
            RaiseSelectionChanged();
        }
        catch (Exception exception)
        {
            ReportCallbackException("SelectionChanged", exception);
        }
    }

    private void OnTextCompositionStarted(
        TextBox sender,
        TextCompositionStartedEventArgs args)
    {
        try
        {
            SetComposition(args.StartIndex, args.Length);
        }
        catch (Exception exception)
        {
            ReportCallbackException("TextCompositionStarted", exception);
        }
    }

    private void OnTextCompositionChanged(
        TextBox sender,
        TextCompositionChangedEventArgs args)
    {
        try
        {
            SetComposition(args.StartIndex, args.Length);
        }
        catch (Exception exception)
        {
            ReportCallbackException("TextCompositionChanged", exception);
        }
    }

    private void OnTextCompositionEnded(
        TextBox sender,
        TextCompositionEndedEventArgs args)
    {
        _compositionRange = null;
        try
        {
            CompositionChanged?.Invoke(
                this,
                new AzunyanTextInputCompositionChangedEventArgs(
                    _generation,
                    null,
                    isComposing: false));
        }
        catch (Exception exception)
        {
            ReportCallbackException("TextCompositionEnded", exception);
        }
    }

    private void SetComposition(int start, int length)
    {
        var safeStart = Math.Clamp(start, 0, _synchronizedNativeText.Length);
        var safeEnd = Math.Clamp(
            safeStart + length,
            safeStart,
            _synchronizedNativeText.Length);
        var documentStart = ToDocumentOffset(_synchronizedText, safeStart);
        var documentEnd = ToDocumentOffset(_synchronizedText, safeEnd);
        _compositionRange = new TextRange(
            _windowStart + documentStart,
            documentEnd - documentStart);
        try
        {
            CompositionChanged?.Invoke(
                this,
                new AzunyanTextInputCompositionChangedEventArgs(
                    _generation,
                    _compositionRange,
                    isComposing: true));
        }
        catch (Exception exception)
        {
            ReportCallbackException("TextCompositionChanged", exception);
        }
    }

    private void RaiseSelectionChanged() =>
        InputSelectionChanged?.Invoke(
            this,
            new AzunyanTextInputSelectionChangedEventArgs(
                _generation,
                ToDocumentSelection()));

    private void ReportCallbackException(string source, Exception exception)
    {
        try
        {
            ExceptionSink?.Invoke(source, exception);
        }
        catch (Exception sinkException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Azunyan text input exception sink failed: {sinkException}");
        }
    }

    private TextSelection ToDocumentSelection()
    {
        var nativeStart = Math.Clamp(
            NativeTextBox.SelectionStart,
            0,
            _synchronizedNativeText.Length);
        var nativeEnd = Math.Clamp(
            nativeStart + NativeTextBox.SelectionLength,
            nativeStart,
            _synchronizedNativeText.Length);
        var start = ToDocumentOffset(_synchronizedText, nativeStart);
        var end = ToDocumentOffset(_synchronizedText, nativeEnd);
        return new TextSelection(_windowStart + start, _windowStart + end);
    }

    private Rect GetNativeCaretRect()
    {
        if (_synchronizedNativeText.Length == 0)
        {
            return new Rect(0, 0, 1, Math.Max(1, NativeTextBox.ActualHeight));
        }

        var index = Math.Clamp(
            NativeTextBox.SelectionStart,
            0,
            _synchronizedNativeText.Length);
        if (index == _synchronizedNativeText.Length)
        {
            return NativeTextBox.GetRectFromCharacterIndex(index - 1, trailingEdge: true);
        }

        return NativeTextBox.GetRectFromCharacterIndex(index, trailingEdge: false);
    }

    private static string ToNativeText(string text)
    {
        if (text.IndexOf('\r') < 0 && text.IndexOf('\n') < 0)
        {
            return text;
        }

        var nativeText = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                nativeText.Append('\r');
            }
            else if (text[index] == '\n')
            {
                nativeText.Append('\r');
            }
            else
            {
                nativeText.Append(text[index]);
            }
        }

        return nativeText.ToString();
    }

    private static string FromNativeText(
        string text,
        string referenceText,
        string? preferredLineEnding)
    {
        var lineEnding = preferredLineEnding ?? GetPreferredLineEnding(referenceText);
        if (text.IndexOf('\r') < 0)
        {
            return text;
        }

        var documentText = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                documentText.Append(lineEnding);
            }
            else
            {
                documentText.Append(text[index]);
            }
        }

        return documentText.ToString();
    }

    private static string GetPreferredLineEnding(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                return index + 1 < text.Length && text[index + 1] == '\n'
                    ? "\r\n"
                    : "\r";
            }

            if (text[index] == '\n')
            {
                return "\n";
            }
        }

        return Environment.NewLine;
    }

    private static int ToNativeOffset(string text, int documentOffset)
    {
        var safeOffset = Math.Clamp(documentOffset, 0, text.Length);
        var documentIndex = 0;
        var nativeOffset = 0;
        while (documentIndex < safeOffset)
        {
            if (text[documentIndex] == '\r'
                && documentIndex + 1 < text.Length
                && text[documentIndex + 1] == '\n')
            {
                documentIndex += 2;
            }
            else
            {
                documentIndex++;
            }

            nativeOffset++;
        }

        return nativeOffset;
    }

    private static int ToDocumentOffset(string text, int nativeOffset)
    {
        var safeOffset = Math.Clamp(
            nativeOffset,
            0,
            ToNativeOffset(text, text.Length));
        var documentIndex = 0;
        var currentNativeOffset = 0;
        while (documentIndex < text.Length && currentNativeOffset < safeOffset)
        {
            if (text[documentIndex] == '\r'
                && documentIndex + 1 < text.Length
                && text[documentIndex + 1] == '\n')
            {
                documentIndex += 2;
            }
            else
            {
                documentIndex++;
            }

            currentNativeOffset++;
        }

        return documentIndex;
    }

    private static string ReplaceText(string text, TextRange range, string replacement) =>
        text[..range.Start] + replacement + text[range.End..];

    private static void ValidateDocumentRange(
        TextRange range,
        int windowStart,
        int windowLength)
    {
        if (range.Start < windowStart
            || range.End > windowStart + windowLength)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }
    }

    private static (TextRange Range, string InsertedText) FindReplacement(
        string previousText,
        string currentText)
    {
        var prefixLength = 0;
        var commonLength = Math.Min(previousText.Length, currentText.Length);
        while (prefixLength < commonLength
            && previousText[prefixLength] == currentText[prefixLength])
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (suffixLength < previousText.Length - prefixLength
            && suffixLength < currentText.Length - prefixLength
            && previousText[previousText.Length - suffixLength - 1]
                == currentText[currentText.Length - suffixLength - 1])
        {
            suffixLength++;
        }

        var oldLength = previousText.Length - prefixLength - suffixLength;
        var newLength = currentText.Length - prefixLength - suffixLength;
        return (
            new TextRange(prefixLength, oldLength),
            currentText.Substring(prefixLength, newLength));
    }
}

public sealed class AzunyanTextInputChangedEventArgs : EventArgs
{
    public AzunyanTextInputChangedEventArgs(
        long generation,
        TextChange change,
        TextSelection selection,
        TextRange? compositionRange)
    {
        Generation = generation;
        Change = change;
        Selection = selection;
        CompositionRange = compositionRange;
    }

    public long Generation { get; }

    public TextChange Change { get; }

    public TextSelection Selection { get; }

    public TextRange? CompositionRange { get; }
}

public sealed class AzunyanTextInputSelectionChangedEventArgs : EventArgs
{
    public AzunyanTextInputSelectionChangedEventArgs(
        long generation,
        TextSelection selection)
    {
        Generation = generation;
        Selection = selection;
    }

    public long Generation { get; }

    public TextSelection Selection { get; }
}

public sealed class AzunyanTextInputCompositionChangedEventArgs : EventArgs
{
    public AzunyanTextInputCompositionChangedEventArgs(
        long generation,
        TextRange? compositionRange,
        bool isComposing)
    {
        Generation = generation;
        CompositionRange = compositionRange;
        IsComposing = isComposing;
    }

    public long Generation { get; }

    public TextRange? CompositionRange { get; }

    public bool IsComposing { get; }
}
