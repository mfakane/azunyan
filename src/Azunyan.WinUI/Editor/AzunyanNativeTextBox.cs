using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Azunyan.WinUI;

/// <summary>
/// TextBox whose owner can intercept editing keys before the platform text
/// service processes them. The projected editor must update its document and
/// then resynchronize the sliding window without re-entering TextBox's key
/// handling.
/// </summary>
public sealed partial class AzunyanNativeTextBox : TextBox
{
    private readonly HashSet<VirtualKey> _handledKeyDowns = new();

    internal Action<string, Exception>? ExceptionSink { get; set; }

    public event KeyEventHandler? BeforeKeyDown;
    public event KeyEventHandler? AfterKeyUp;

    internal void ResetHandledKeyState() => _handledKeyDowns.Clear();

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        try
        {
            BeforeKeyDown?.Invoke(this, e);
        }
        catch (Exception exception)
        {
            ReportCallbackException("KeyDown", exception);
            e.Handled = true;
        }

        if (e.Handled)
        {
            // A handled KeyDown must not be paired with TextBox's native
            // KeyUp processing. The editor changes the native selection after
            // KeyUp; letting TextBox process only the release can leave the
            // WinUI text-service state inconsistent and terminate the process
            // in CoreMessagingXP.dll (not as a managed exception).
            _handledKeyDowns.Add(e.Key);
            return;
        }

        _handledKeyDowns.Remove(e.Key);
        try
        {
            base.OnKeyDown(e);
        }
        catch (Exception exception)
        {
            ReportCallbackException("KeyDown/base", exception);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        // Keep suppressing releases until a later unhandled KeyDown for the
        // same key clears the marker. Key repeat and interrupted input can
        // produce duplicate or late KeyUp messages; removing the marker on
        // the first release can send a subsequent unmatched release into the
        // native TextBox text service.
        if (_handledKeyDowns.Contains(e.Key))
        {
            // Do not let the release continue through the routed-event
            // pipeline either.  Skipping TextBox.OnKeyUp prevents the native
            // text service from seeing the unmatched release; marking the
            // event handled also prevents another class/parent handler from
            // forwarding that same release to CoreMessaging.
            e.Handled = true;
        }
        else
        {
            try
            {
                base.OnKeyUp(e);
            }
            catch (Exception exception)
            {
                ReportCallbackException("KeyUp/base", exception);
            }
        }

        try
        {
            AfterKeyUp?.Invoke(this, e);
        }
        catch (Exception exception)
        {
            ReportCallbackException("KeyUp", exception);
        }
    }

    private void ReportCallbackException(string source, Exception exception)
    {
        try
        {
            ExceptionSink?.Invoke(source, exception);
        }
        catch (Exception sinkException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Azunyan native text box exception sink failed: {sinkException}");
        }
    }
}
