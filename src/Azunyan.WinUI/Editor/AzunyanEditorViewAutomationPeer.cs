using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Azunyan.WinUI;

/// <summary>
/// Keeps the outer editor in the UI Automation tree. The native TextBox peer
/// remains the compatibility implementation for custom renderers, while the
/// projected surface exposes snapshot-bound text, value, ranges, and geometry.
/// </summary>
internal sealed partial class AzunyanEditorViewAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly AzunyanEditorView _owner;
    private ProjectedTextAutomationProvider? _projectedTextProvider;
    private bool _reportedKeyboardFocus;

    public AzunyanEditorViewAutomationPeer(AzunyanEditorView owner)
        : base(owner)
    {
        _owner = owner;
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (_owner.IsProjectedTextSurfaceActive
            && patternInterface is PatternInterface.Text
                or PatternInterface.Text2
                or PatternInterface.Value)
        {
            return _projectedTextProvider ??= new ProjectedTextAutomationProvider(_owner);
        }

        var nativePeer = FrameworkElementAutomationPeer.CreatePeerForElement(
            _owner.InputHost);
        return nativePeer?.GetPattern(patternInterface);
    }

    internal IRawElementProviderSimple GetRawProvider() => ProviderFromPeer(this)!;

    internal void NotifyDocumentChanged(DocumentChangedEventArgs args)
    {
        if (string.Equals(
                args.Change.OldText,
                args.Change.NewText,
                StringComparison.Ordinal))
        {
            return;
        }

        if (ListenerExists(AutomationEvents.TextPatternOnTextChanged))
        {
            RaiseAutomationEvent(AutomationEvents.TextPatternOnTextChanged);
        }

        // Snapshot.Text materializes the complete persistent text tree. Keep
        // that O(document length) work off the typing path unless a UIA client
        // is actively listening for the old and new Value values.
        if (ListenerExists(AutomationEvents.PropertyChanged))
        {
            RaisePropertyChangedEvent(
                ValuePatternIdentifiers.ValueProperty,
                args.OldSnapshot.Text,
                args.NewSnapshot.Text);
        }
    }

    internal void NotifyTextChanged(string oldText, string newText)
    {
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            return;
        }

        if (ListenerExists(AutomationEvents.TextPatternOnTextChanged))
        {
            RaiseAutomationEvent(AutomationEvents.TextPatternOnTextChanged);
        }

        if (ListenerExists(AutomationEvents.PropertyChanged))
        {
            RaisePropertyChangedEvent(
                ValuePatternIdentifiers.ValueProperty,
                oldText,
                newText);
        }
    }

    internal void NotifySelectionChanged()
    {
        if (ListenerExists(AutomationEvents.TextPatternOnTextSelectionChanged))
        {
            RaiseAutomationEvent(
                AutomationEvents.TextPatternOnTextSelectionChanged);
        }
    }

    internal void NotifyReadOnlyChanged(bool oldValue, bool newValue)
    {
        if (ListenerExists(AutomationEvents.PropertyChanged))
        {
            RaisePropertyChangedEvent(ValuePatternIdentifiers.IsReadOnlyProperty, oldValue, newValue);
        }
    }

    internal void NotifyLayoutChanged()
    {
        if (ListenerExists(AutomationEvents.LayoutInvalidated))
        {
            RaiseAutomationEvent(AutomationEvents.LayoutInvalidated);
        }
    }

    internal void NotifyStructureChanged()
    {
        if (ListenerExists(AutomationEvents.StructureChanged))
        {
            RaiseStructureChangedEvent(
                AutomationStructureChangeType.ChildrenInvalidated,
                this);
        }
    }

    internal void NotifyFocusChanged()
    {
        var hasKeyboardFocus = HasKeyboardFocusCore();
        if (hasKeyboardFocus == _reportedKeyboardFocus)
        {
            return;
        }

        var oldValue = _reportedKeyboardFocus;
        _reportedKeyboardFocus = hasKeyboardFocus;
        if (ListenerExists(AutomationEvents.PropertyChanged))
        {
            RaisePropertyChangedEvent(
                AutomationElementIdentifiers.HasKeyboardFocusProperty,
                oldValue,
                hasKeyboardFocus);
        }

        if (hasKeyboardFocus
            && ListenerExists(AutomationEvents.AutomationFocusChanged))
        {
            RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
        }
    }

    internal static IRawElementProviderSimple? GetRawProvider(AutomationPeer peer) =>
        peer switch
        {
            AzunyanEditorViewAutomationPeer editorPeer => editorPeer.GetRawProvider(),
            ProjectedTextAutomationButtonPeer buttonPeer => buttonPeer.GetRawProvider(),
            _ => null
        };

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.Edit;

    protected override bool IsKeyboardFocusableCore() => true;

    protected override bool HasKeyboardFocusCore() =>
        _owner.InputHost.FocusState != FocusState.Unfocused;

    protected override string GetClassNameCore() => nameof(AzunyanEditorView);

    protected override string GetNameCore()
    {
        var name = AutomationProperties.GetName(_owner);
        return string.IsNullOrWhiteSpace(name) ? "Text editor" : name;
    }
}
