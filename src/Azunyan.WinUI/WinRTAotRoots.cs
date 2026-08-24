using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

// These closed WinRT delegate types cross the ABI when the TextBox subscribes
// to IME composition events. CsWinRT's automatic scan does not discover them,
// so Native AOT must generate their CCW vtables explicitly.
[assembly: WinRT.GeneratedWinRTExposedExternalType(
    typeof(TypedEventHandler<TextBox, TextCompositionStartedEventArgs>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(
    typeof(TypedEventHandler<TextBox, TextCompositionChangedEventArgs>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(
    typeof(TypedEventHandler<TextBox, TextCompositionEndedEventArgs>))]
