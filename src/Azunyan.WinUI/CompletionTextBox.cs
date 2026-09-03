using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Azunyan.WinUI;

/// <summary>
/// TextBox that lets a consumer handle a key before TextBox's native input
/// processing runs. Handled key releases are suppressed as well so the
/// native text service does not see an unmatched key-up.
/// </summary>
public sealed partial class CompletionTextBox : TextBox
{
    private readonly HashSet<VirtualKey> _handledKeyDowns = [];

    public event KeyEventHandler? BeforeKeyDown;

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        try
        {
            BeforeKeyDown?.Invoke(this, e);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Completion TextBox key handling failed: {exception}");
            e.Handled = true;
        }

        if (e.Handled)
        {
            _handledKeyDowns.Add(e.Key);
            return;
        }

        _handledKeyDowns.Remove(e.Key);
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        if (_handledKeyDowns.Contains(e.Key))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }
}
