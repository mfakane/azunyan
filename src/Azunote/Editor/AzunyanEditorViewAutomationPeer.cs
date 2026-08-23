using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Azunote;

/// <summary>
/// Keeps the outer editor in the UI Automation tree. The native TextBox peer
/// remains the compatibility implementation for custom renderers, while the
/// projected surface exposes snapshot-bound text ranges and geometry.
/// </summary>
internal sealed class AzunyanEditorViewAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly AzunyanEditorView _owner;
    private ProjectedTextAutomationProvider? _projectedTextProvider;

    public AzunyanEditorViewAutomationPeer(AzunyanEditorView owner)
        : base(owner)
    {
        _owner = owner;
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (_owner.IsProjectedTextSurfaceActive
            && patternInterface is PatternInterface.Text or PatternInterface.Text2)
        {
            return _projectedTextProvider ??= new ProjectedTextAutomationProvider(_owner);
        }

        var nativePeer = FrameworkElementAutomationPeer.CreatePeerForElement(
            _owner.InputHost);
        return nativePeer?.GetPattern(patternInterface);
    }

    internal IRawElementProviderSimple GetRawProvider() => ProviderFromPeer(this)!;

    internal static IRawElementProviderSimple? GetRawProvider(AutomationPeer peer) =>
        peer switch
        {
            AzunyanEditorViewAutomationPeer editorPeer => editorPeer.GetRawProvider(),
            ProjectedTextAutomationButtonPeer buttonPeer => buttonPeer.GetRawProvider(),
            _ => null
        };

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(AzunyanEditorView);

    protected override string GetNameCore()
    {
        var name = AutomationProperties.GetName(_owner);
        return string.IsNullOrWhiteSpace(name) ? "Text editor" : name;
    }
}
