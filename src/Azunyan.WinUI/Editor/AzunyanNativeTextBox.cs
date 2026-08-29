using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Azunyan.WinUI;

/// <summary>
/// TextBox whose owner can intercept editing keys before the platform text
/// service processes them. The projected editor must update its document and
/// then resynchronize the sliding window without re-entering TextBox's key
/// handling.
/// </summary>
public sealed partial class AzunyanNativeTextBox : TextBox
{
    public event KeyEventHandler? BeforeKeyDown;
    public event KeyEventHandler? AfterKeyUp;

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        BeforeKeyDown?.Invoke(this, e);
        if (e.Handled)
        {
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        base.OnKeyUp(e);
        AfterKeyUp?.Invoke(this, e);
    }
}
