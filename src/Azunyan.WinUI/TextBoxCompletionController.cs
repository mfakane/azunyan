using Azunyan.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace Azunyan.WinUI;

/// <summary>
/// Connects a normal WinUI TextBox to a reusable completion popup. The
/// provider returns a replacement range relative to the TextBox's text.
/// </summary>
public sealed class TextBoxCompletionController : IDisposable
{
    private readonly TextBox _textBox;
    private readonly CompletionPopup _popup;
    private readonly UIElement _coordinateRoot;
    private readonly Func<string, int, CompletionResult?> _provider;
    private readonly KeyEventHandler _keyDownHandler;
    private bool _suppressNextReturn;
    private bool _updatingText;
    private bool _refreshScheduled;
    private bool _disposed;

    public TextBoxCompletionController(
        TextBox textBox,
        CompletionPopup popup,
        UIElement coordinateRoot,
        Func<string, int, CompletionResult?> provider)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(coordinateRoot);
        ArgumentNullException.ThrowIfNull(provider);

        _textBox = textBox;
        _popup = popup;
        _coordinateRoot = coordinateRoot;
        _provider = provider;
        _keyDownHandler = TextBox_KeyDown;

        _textBox.TextChanged += TextBox_TextChanged;
        _textBox.SelectionChanged += TextBox_SelectionChanged;
        if (_textBox is CompletionTextBox completionTextBox)
        {
            completionTextBox.BeforeKeyDown += _keyDownHandler;
        }
        else
        {
            _textBox.AddHandler(UIElement.KeyDownEvent, _keyDownHandler, true);
        }
        _textBox.BeforeTextChanging += TextBox_BeforeTextChanging;
        _textBox.LostFocus += TextBox_LostFocus;
        _popup.Accepted += Popup_Accepted;
    }

    public void Refresh()
    {
        if (_disposed || _updatingText)
        {
            return;
        }

        try
        {
            RefreshCore();
        }
        catch (Exception exception)
        {
            TryHidePopup();
            System.Diagnostics.Debug.WriteLine(
                $"Text-box completion failed: {exception}");
        }
    }

    private void RefreshCore()
    {
        var text = _textBox.Text ?? string.Empty;
        var position = Math.Clamp(_textBox.SelectionStart, 0, text.Length);
        var result = _provider(text, position);

        if (result is null
            || result.Items.Count == 0
            || result.ReplacementRange.Start < 0
            || result.ReplacementRange.End > text.Length
            || position < result.ReplacementRange.Start
            || position > result.ReplacementRange.End
            || !TryGetPopupOffset(position, out var offset))
        {
            TryHidePopup();
            return;
        }

        _popup.Show(result, offset.X, offset.Y, _textBox.XamlRoot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _textBox.TextChanged -= TextBox_TextChanged;
        _textBox.SelectionChanged -= TextBox_SelectionChanged;
        if (_textBox is CompletionTextBox completionTextBox)
        {
            completionTextBox.BeforeKeyDown -= _keyDownHandler;
        }
        else
        {
            _textBox.RemoveHandler(UIElement.KeyDownEvent, _keyDownHandler);
        }
        _textBox.BeforeTextChanging -= TextBox_BeforeTextChanging;
        _textBox.LostFocus -= TextBox_LostFocus;
        _popup.Accepted -= Popup_Accepted;
        TryHidePopup();
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs args) => ScheduleRefresh();

    private void TextBox_SelectionChanged(object sender, RoutedEventArgs args)
    {
        if (_popup.IsOpen)
        {
            ScheduleRefresh();
        }
    }

    private void ScheduleRefresh()
    {
        if (_disposed || _updatingText || _refreshScheduled)
        {
            return;
        }

        _refreshScheduled = true;
        if (!_textBox.DispatcherQueue.TryEnqueue(() =>
            {
                _refreshScheduled = false;
                if (!_disposed)
                {
                    Refresh();
                }
            }))
        {
            _refreshScheduled = false;
        }
    }

    private void TextBox_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_popup.IsOpen)
        {
            _suppressNextReturn = false;
            return;
        }

        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isEnter = args.Key == VirtualKey.Enter;
        var usesNativeKeyInterception = _textBox is not CompletionTextBox;
        if (isEnter && usesNativeKeyInterception)
        {
            // Set this before accepting because the TextBox may process its
            // default return insertion after this routed handler returns.
            // The programmatic completion replacement is ignored while
            // _updatingText is true.
            _suppressNextReturn = true;
        }
        else
        {
            _suppressNextReturn = false;
        }

        try
        {
            var handled = _popup.HandleKeyDown(args.Key, shiftDown);
            if (isEnter && !handled)
            {
                _suppressNextReturn = false;
            }

            if (handled)
            {
                args.Handled = true;
            }
        }
        finally
        {
            if (isEnter && (!args.Handled || !usesNativeKeyInterception))
            {
                _suppressNextReturn = false;
            }
        }
    }

    private void TextBox_BeforeTextChanging(
        TextBox sender,
        TextBoxBeforeTextChangingEventArgs args)
    {
        if (_updatingText)
        {
            return;
        }

        var enterIsDown = InputKeyboardSource.GetKeyStateForCurrentThread(
                VirtualKey.Enter)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!_suppressNextReturn
            && (!_popup.IsOpen || !enterIsDown))
        {
            return;
        }

        var currentText = _textBox.Text ?? string.Empty;
        if (!IsReturnInsertion(currentText, args.NewText))
        {
            return;
        }

        args.Cancel = true;
        _suppressNextReturn = false;
    }

    private void TextBox_LostFocus(object sender, RoutedEventArgs args)
    {
        _textBox.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!_popup.HasFocus)
                {
                    _popup.Hide();
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Text-box completion focus handling failed: {exception}");
            }
        });
    }

    private void Popup_Accepted(
        object? sender,
        CompletionAcceptedEventArgs args)
    {
        var text = _textBox.Text ?? string.Empty;
        var range = args.ReplacementRange;
        if (range.Start < 0
            || range.End > text.Length
            || range.Start > range.End)
        {
            args.Cancel = true;
            return;
        }

        var replacement = args.Item.InsertText;
        _updatingText = true;
        try
        {
            _textBox.Text = text[..range.Start] + replacement + text[range.End..];
            _textBox.SelectionStart = range.Start + replacement.Length;
            _textBox.SelectionLength = 0;
        }
        finally
        {
            _updatingText = false;
        }
    }

    private bool IsReturnInsertion(string currentText, string newText)
    {
        var selectionStart = Math.Clamp(
            _textBox.SelectionStart,
            0,
            currentText.Length);
        var selectionLength = Math.Clamp(
            _textBox.SelectionLength,
            0,
            currentText.Length - selectionStart);
        var suffixStart = selectionStart + selectionLength;

        if (newText.Length < currentText.Length - selectionLength
            || !newText.StartsWith(
                currentText[..selectionStart],
                StringComparison.Ordinal)
            || !newText.EndsWith(
                currentText[suffixStart..],
                StringComparison.Ordinal))
        {
            return false;
        }

        var insertedLength = newText.Length
            - (currentText.Length - selectionLength);
        if (insertedLength <= 0)
        {
            return false;
        }

        var inserted = newText.Substring(selectionStart, insertedLength);
        return inserted.All(character => character is '\r' or '\n');
    }

    private bool TryGetPopupOffset(int position, out Point offset)
    {
        try
        {
            var textLength = (_textBox.Text ?? string.Empty).Length;
            if (textLength == 0 || position < 0 || position > textLength)
            {
                offset = default;
                return false;
            }

            // TextBox character rectangles address characters, not the caret
            // position after the last character. Use the trailing edge of the
            // preceding character when the caret is at the end of the text.
            var characterIndex = Math.Min(position, textLength - 1);
            var caret = _textBox.GetRectFromCharacterIndex(
                characterIndex,
                position >= textLength);
            var origin = _textBox.TransformToVisual(_coordinateRoot)
                .TransformPoint(new Point(0, 0));
            var popupTop = Math.Max(
                caret.Y + caret.Height,
                _textBox.ActualHeight) + 4;
            offset = new Point(
                origin.X + caret.X,
                origin.Y + popupTop);
            return true;
        }
        catch (Exception exception)
        {
            offset = default;
            System.Diagnostics.Debug.WriteLine(
                $"Text-box completion placement failed: {exception}");
            return false;
        }
    }

    private void TryHidePopup()
    {
        try
        {
            _popup.Hide();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Text-box completion hide failed: {exception}");
        }
    }
}
