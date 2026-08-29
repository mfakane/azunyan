using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Text;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Azunyan.WinUI;

internal sealed record ProjectedTextAutomationTarget(
    string Id,
    string Kind,
    DocumentAnchor Anchor,
    TextRange Range,
    string Name,
    bool IsFold,
    bool IsBlock,
    Rect Bounds);

/// <summary>
/// UI Automation provider for the projected surface. The native TextBox
/// remains the input/IME host, but automation clients see the same immutable
/// document text, editable value, ranges, and projected geometry that the
/// renderer uses.
/// </summary>
internal sealed partial class ProjectedTextAutomationProvider :
    ITextProvider,
    ITextProvider2,
    IValueProvider
{
    private readonly AzunyanEditorView _owner;

    public ProjectedTextAutomationProvider(AzunyanEditorView owner)
    {
        _owner = owner;
    }

    public ITextRangeProvider DocumentRange =>
        _owner.InvokeOnEditorThread(() =>
            CreateRange(TextRange.FromBounds(0, _owner.Snapshot.Length)));

    public SupportedTextSelection SupportedTextSelection =>
        SupportedTextSelection.Single;

    public bool IsReadOnly => false;

    public string Value => _owner.InvokeOnEditorThread(
        () => _owner.Snapshot.Text);

    public void SetValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _owner.InvokeOnEditorThread(() => _owner.SetAutomationValue(value));
    }

    public ITextRangeProvider[] GetSelection() =>
        _owner.InvokeOnEditorThread(() =>
            new[] { CreateRange(_owner.AutomationSelection.Range) });

    public ITextRangeProvider[] GetVisibleRanges() =>
        _owner.InvokeOnEditorThread(() =>
            _owner.TryGetProjectedVisibleDocumentRange(out var range)
                ? new[] { CreateRange(range) }
                : new[] { CreateRange(TextRange.FromBounds(0, _owner.Snapshot.Length)) });

    public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement) =>
        _owner.InvokeOnEditorThread(() =>
            _owner.TryGetProjectedAutomationChildRange(childElement, out var range)
                ? CreateRange(range)
                : CreateRange(TextRange.FromBounds(0, _owner.Snapshot.Length)));

    public ITextRangeProvider RangeFromPoint(Point screenLocation) =>
        _owner.InvokeOnEditorThread(() =>
            _owner.TryGetProjectedRangeFromPoint(screenLocation, out var range)
                ? CreateRange(range)
                : CreateRange(TextRange.FromBounds(0, _owner.Snapshot.Length)));

    public ITextRangeProvider GetCaretRange(out bool isActive)
    {
        var result = _owner.InvokeOnEditorThread(() =>
            (
                IsActive: _owner.InputHost.FocusState != FocusState.Unfocused,
                Range: CreateRange(TextRange.Empty(_owner.AutomationSelection.CaretPosition))
            ));
        isActive = result.IsActive;
        return result.Range;
    }

    public ITextRangeProvider RangeFromAnnotation(
        IRawElementProviderSimple annotationElement) => DocumentRange;

    internal ProjectedTextRangeProvider CreateRange(TextRange range) =>
        new(_owner, _owner.Snapshot, range);
}

internal sealed partial class ProjectedTextAutomationButton : Button
{
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new ProjectedTextAutomationButtonPeer(this);
}

internal sealed partial class ProjectedTextAutomationButtonPeer : ButtonAutomationPeer
{
    public ProjectedTextAutomationButtonPeer(ProjectedTextAutomationButton owner)
        : base(owner)
    {
    }

    internal IRawElementProviderSimple GetRawProvider() => ProviderFromPeer(this)!;
}

/// <summary>
/// Snapshot-bound implementation of the UI Automation text range contract.
/// Operations that change editor state are forwarded to the same document
/// selection and projected scroll path used by keyboard and pointer input.
/// </summary>
internal sealed partial class ProjectedTextRangeProvider : ITextRangeProvider
{
    private readonly AzunyanEditorView _owner;
    private readonly TextSnapshot _snapshot;
    private int _start;
    private int _end;

    public ProjectedTextRangeProvider(
        AzunyanEditorView owner,
        TextSnapshot snapshot,
        TextRange range)
    {
        _owner = owner;
        _snapshot = snapshot;
        _start = Math.Clamp(range.Start, 0, _snapshot.Length);
        _end = Math.Clamp(range.End, _start, _snapshot.Length);
    }

    private TextRange CurrentRange => TextRange.FromBounds(_start, _end);

    private bool IsCurrent => ReferenceEquals(_snapshot, _owner.Snapshot);

    public ITextRangeProvider Clone() =>
        new ProjectedTextRangeProvider(_owner, _snapshot, CurrentRange);

    public bool Compare(ITextRangeProvider range)
    {
        return range is ProjectedTextRangeProvider other
            && ReferenceEquals(_snapshot, other._snapshot)
            && _start == other._start
            && _end == other._end;
    }

    public int CompareEndpoints(
        TextPatternRangeEndpoint endpoint,
        ITextRangeProvider targetRange,
        TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not ProjectedTextRangeProvider other
            || !ReferenceEquals(_snapshot, other._snapshot))
        {
            return 0;
        }

        return GetEndpoint(endpoint).CompareTo(other.GetEndpoint(targetEndpoint));
    }

    public void ExpandToEnclosingUnit(TextUnit unit)
    {
        var units = GetUnitRanges(unit).ToList();
        if (units.Count == 0)
        {
            _start = _end = 0;
            return;
        }

        var startIndex = FindUnitContaining(units, _start);
        var endIndex = FindUnitContaining(units, Math.Max(_start, _end - 1));
        if (startIndex < 0)
        {
            startIndex = Math.Max(0, units.Count - 1);
        }

        if (endIndex < 0)
        {
            endIndex = startIndex;
        }

        if (endIndex < startIndex)
        {
            endIndex = startIndex;
        }

        _start = units[startIndex].Start;
        _end = units[endIndex].End;
    }

    public ITextRangeProvider? FindAttribute(
        int attribute,
        object value,
        bool backward) => null;

    public ITextRangeProvider? FindText(
        string text,
        bool backward,
        bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return null;
        }

        var comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var content = _snapshot.GetText(CurrentRange);
        var index = backward
            ? content.LastIndexOf(text, comparison)
            : content.IndexOf(text, comparison);
        return index < 0
            ? null
            : new ProjectedTextRangeProvider(
                _owner,
                _snapshot,
                TextRange.FromBounds(_start + index, _start + index + text.Length));
    }

    public object? GetAttributeValue(int attribute) => null;

    public void GetBoundingRectangles(out double[] returnValue)
    {
        var rectangles = _owner.InvokeOnEditorThread(() =>
        {
            if (!IsCurrent
                || !_owner.TryGetProjectedRangeRectangles(
                    CurrentRange,
                    out var currentRectangles))
            {
                return Array.Empty<Rect>();
            }

            return currentRectangles.ToArray();
        });

        returnValue = rectangles
            .SelectMany(rect => new[]
            {
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height
            })
            .ToArray();
    }

    public IRawElementProviderSimple[] GetChildren() =>
        _owner.InvokeOnEditorThread(() =>
            IsCurrent
                ? _owner.GetProjectedAutomationChildren(CurrentRange).ToArray()
                : Array.Empty<IRawElementProviderSimple>());

    public IRawElementProviderSimple GetEnclosingElement()
    {
        return _owner.InvokeOnEditorThread(() =>
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(_owner);
            return peer is AzunyanEditorViewAutomationPeer editorPeer
                ? editorPeer.GetRawProvider()
                : null!;
        });
    }

    public string GetText(int maxLength)
    {
        if (maxLength == 0)
        {
            return string.Empty;
        }

        var text = _snapshot.GetText(CurrentRange);
        return maxLength < 0 || text.Length <= maxLength
            ? text
            : text[..maxLength];
    }

    public int Move(TextUnit unit, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var units = GetUnitRanges(unit).ToList();
        if (units.Count == 0)
        {
            return 0;
        }

        if (count > 0)
        {
            var first = units.FindIndex(candidate => candidate.End > _end);
            if (first < 0)
            {
                return 0;
            }

            var target = Math.Min(units.Count - 1, first + count - 1);
            _start = units[target].Start;
            _end = units[target].End;
            return target - first + 1;
        }

        var last = -1;
        for (var index = units.Count - 1; index >= 0; index--)
        {
            if (units[index].Start < _start)
            {
                last = index;
                break;
            }
        }

        if (last < 0)
        {
            return 0;
        }

        var targetIndex = Math.Max(0, last + count + 1);
        _start = units[targetIndex].Start;
        _end = units[targetIndex].End;
        return targetIndex - last - 1;
    }

    public void MoveEndpointByRange(
        TextPatternRangeEndpoint endpoint,
        ITextRangeProvider targetRange,
        TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not ProjectedTextRangeProvider other
            || !ReferenceEquals(_snapshot, other._snapshot))
        {
            return;
        }

        SetEndpoint(endpoint, other.GetEndpoint(targetEndpoint));
    }

    public int MoveEndpointByUnit(
        TextPatternRangeEndpoint endpoint,
        TextUnit unit,
        int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var boundaries = GetUnitRanges(unit)
            .SelectMany(range => new[] { range.Start, range.End })
            .Distinct()
            .OrderBy(position => position)
            .ToArray();
        if (boundaries.Length == 0)
        {
            return 0;
        }

        var current = GetEndpoint(endpoint);
        var currentIndex = Array.BinarySearch(boundaries, current);
        if (currentIndex < 0)
        {
            currentIndex = Math.Clamp(~currentIndex, 0, boundaries.Length - 1);
        }

        var targetIndex = Math.Clamp(
            currentIndex + count,
            0,
            boundaries.Length - 1);
        SetEndpoint(endpoint, boundaries[targetIndex]);
        return targetIndex - currentIndex;
    }

    public void RemoveFromSelection()
    {
        _owner.InvokeOnEditorThread(() =>
        {
            if (IsCurrent)
            {
                _owner.SetDocumentSelection(TextSelection.Caret(_start));
            }
        });
    }

    public void ScrollIntoView(bool alignToTop)
    {
        _owner.InvokeOnEditorThread(() =>
        {
            if (IsCurrent)
            {
                _owner.ScrollProjectedRangeIntoView(CurrentRange, alignToTop);
            }
        });
    }

    public void Select()
    {
        _owner.InvokeOnEditorThread(() =>
        {
            if (IsCurrent)
            {
                _owner.SetDocumentSelection(new TextSelection(_start, _end));
            }
        });
    }

    public void AddToSelection() => Select();

    private int GetEndpoint(TextPatternRangeEndpoint endpoint) =>
        endpoint == TextPatternRangeEndpoint.Start ? _start : _end;

    private void SetEndpoint(TextPatternRangeEndpoint endpoint, int value)
    {
        value = Math.Clamp(value, 0, _snapshot.Length);
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            _start = Math.Min(value, _end);
        }
        else
        {
            _end = Math.Max(value, _start);
        }
    }

    private IEnumerable<TextRange> GetUnitRanges(TextUnit unit)
    {
        switch (unit)
        {
            case TextUnit.Document:
            case TextUnit.Page:
                yield return TextRange.FromBounds(0, _snapshot.Length);
                yield break;
            case TextUnit.Line:
            case TextUnit.Paragraph:
            case TextUnit.Format:
                for (var line = 0; line < _snapshot.Lines.LineCount; line++)
                {
                    yield return _snapshot.Lines.GetLineRange(line);
                }

                yield break;
            case TextUnit.Character:
                foreach (var range in GetCharacterRanges())
                {
                    yield return range;
                }

                yield break;
            case TextUnit.Word:
                foreach (var range in GetWordRanges())
                {
                    yield return range;
                }

                yield break;
            default:
                yield break;
        }
    }

    private IEnumerable<TextRange> GetCharacterRanges()
    {
        var position = 0;
        while (position < _snapshot.Length)
        {
            var next = _snapshot.GetNextTextElementPosition(position);
            if (next <= position)
            {
                next = position + 1;
            }

            yield return TextRange.FromBounds(
                position,
                Math.Min(next, _snapshot.Length));
            position = next;
        }
    }

    private IEnumerable<TextRange> GetWordRanges()
    {
        var text = _snapshot.Text;
        var position = 0;
        while (position < text.Length)
        {
            var word = IsWordTextElement(text, position);
            var end = _snapshot.GetNextTextElementPosition(position);
            if (end <= position)
            {
                end = position + 1;
            }

            while (end < text.Length)
            {
                var next = _snapshot.GetNextTextElementPosition(end);
                if (next <= end || IsWordTextElement(text, end) != word)
                {
                    break;
                }

                end = next;
            }

            yield return TextRange.FromBounds(position, Math.Min(end, text.Length));
            position = end;
        }
    }

    private static bool IsWordTextElement(string text, int position)
    {
        if (text[position] == '_')
        {
            return true;
        }

        var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(
            text,
            position);
        return category is
            System.Globalization.UnicodeCategory.UppercaseLetter or
            System.Globalization.UnicodeCategory.LowercaseLetter or
            System.Globalization.UnicodeCategory.TitlecaseLetter or
            System.Globalization.UnicodeCategory.ModifierLetter or
            System.Globalization.UnicodeCategory.OtherLetter or
            System.Globalization.UnicodeCategory.DecimalDigitNumber or
            System.Globalization.UnicodeCategory.NonSpacingMark or
            System.Globalization.UnicodeCategory.SpacingCombiningMark or
            System.Globalization.UnicodeCategory.EnclosingMark;
    }

    private static int FindUnitContaining(
        List<TextRange> units,
        int position)
    {
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            if (position >= unit.Start
                && (position < unit.End
                    || (unit.IsEmpty && position == unit.Start)))
            {
                return index;
            }
        }

        for (var index = units.Count - 1; index >= 0; index--)
        {
            if (position >= units[index].Start)
            {
                return index;
            }
        }

        return -1;
    }
}
