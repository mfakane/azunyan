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
    private TextRange? _compositionRange;
    private int _windowStart;
    private long _generation;

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
    }

    public int WindowStart => _windowStart;

    internal AzunyanNativeTextBox NativeTextBoxControl => NativeTextBox;

    public long Generation => _generation;

    public TextRange WindowRange => new(_windowStart, _synchronizedText.Length);

    public string WindowText => _synchronizedText;

    public TextSelection Selection => ToDocumentSelection();

    public bool IsComposing => _compositionRange is not null;

    public TextRange? CompositionRange => _compositionRange;

    public event EventHandler<AzunyanTextInputChangedEventArgs>? InputChanged;

    public event EventHandler<AzunyanTextInputSelectionChangedEventArgs>? InputSelectionChanged;

    public event EventHandler<AzunyanTextInputCompositionChangedEventArgs>? CompositionChanged;

    public event EventHandler? NativeFocusChanged;

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
        _synchronizing = true;
        try
        {
            _windowStart = windowStart;
            _generation = generation;
            _synchronizedText = text;
            _compositionRange = compositionRange;
            NativeTextBox.Text = text;
            NativeTextBox.SelectionStart = localSelection.Start;
            NativeTextBox.SelectionLength = localSelection.Length;
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
        base.OnGotFocus(e);
        NativeFocusChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        NativeFocusChanged?.Invoke(this, EventArgs.Empty);
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

        var currentText = NativeTextBox.Text;
        if (string.Equals(_synchronizedText, currentText, StringComparison.Ordinal))
        {
            RaiseSelectionChanged();
            return;
        }

        var (localRange, insertedText) = FindReplacement(_synchronizedText, currentText);
        var change = new TextChange(
            new TextRange(_windowStart + localRange.Start, localRange.Length),
            localRange.Length == 0
                ? string.Empty
                : _synchronizedText.Substring(localRange.Start, localRange.Length),
            insertedText);
        _synchronizedText = currentText;
        InputChanged?.Invoke(
            this,
            new AzunyanTextInputChangedEventArgs(
                _generation,
                change,
                ToDocumentSelection(),
                _compositionRange));
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (_synchronizing)
        {
            return;
        }

        RaiseSelectionChanged();
    }

    private void OnTextCompositionStarted(
        TextBox sender,
        TextCompositionStartedEventArgs args) =>
        SetComposition(args.StartIndex, args.Length);

    private void OnTextCompositionChanged(
        TextBox sender,
        TextCompositionChangedEventArgs args) =>
        SetComposition(args.StartIndex, args.Length);

    private void OnTextCompositionEnded(
        TextBox sender,
        TextCompositionEndedEventArgs args)
    {
        _compositionRange = null;
        CompositionChanged?.Invoke(
            this,
            new AzunyanTextInputCompositionChangedEventArgs(
                _generation,
                null,
                isComposing: false));
    }

    private void SetComposition(int start, int length)
    {
        var safeStart = Math.Clamp(start, 0, NativeTextBox.Text.Length);
        var safeLength = Math.Clamp(length, 0, NativeTextBox.Text.Length - safeStart);
        _compositionRange = new TextRange(_windowStart + safeStart, safeLength);
        CompositionChanged?.Invoke(
            this,
            new AzunyanTextInputCompositionChangedEventArgs(
                _generation,
                _compositionRange,
                isComposing: true));
    }

    private void RaiseSelectionChanged() =>
        InputSelectionChanged?.Invoke(
            this,
            new AzunyanTextInputSelectionChangedEventArgs(
                _generation,
                ToDocumentSelection()));

    private TextSelection ToDocumentSelection()
    {
        var start = Math.Clamp(NativeTextBox.SelectionStart, 0, _synchronizedText.Length);
        var length = Math.Clamp(
            NativeTextBox.SelectionLength,
            0,
            _synchronizedText.Length - start);
        return new TextSelection(_windowStart + start, _windowStart + start + length);
    }

    private Rect GetNativeCaretRect()
    {
        if (_synchronizedText.Length == 0)
        {
            return new Rect(0, 0, 1, Math.Max(1, NativeTextBox.ActualHeight));
        }

        var index = Math.Clamp(NativeTextBox.SelectionStart, 0, _synchronizedText.Length);
        if (index == _synchronizedText.Length)
        {
            return NativeTextBox.GetRectFromCharacterIndex(index - 1, trailingEdge: true);
        }

        return NativeTextBox.GetRectFromCharacterIndex(index, trailingEdge: false);
    }

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
