using System.Collections.ObjectModel;

namespace Azunyan.Core;

public sealed class FoldStateTracker
{
    private TextSnapshot? _snapshot;
    private readonly List<FoldRange> _candidates = new();
    private readonly ReadOnlyCollection<FoldRange> _candidateView;
    private readonly Dictionary<string, FoldRange> _collapsed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedIds = new(StringComparer.Ordinal);

    public FoldStateTracker() => _candidateView = _candidates.AsReadOnly();

    public TextSnapshot Snapshot => _snapshot ?? throw new InvalidOperationException("The tracker has not been reset.");

    public IReadOnlyList<FoldRange> CandidateFolds => _candidateView;

    public IReadOnlyList<FoldRange> Folds => CandidateFolds;

    public IReadOnlySet<string> CollapsedIds => _collapsedIds;

    public IReadOnlySet<string> CollapsedFoldIds => CollapsedIds;

    public void Reset(TextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _candidates.Clear();
        _collapsed.Clear();
        _collapsedIds.Clear();
    }

    public void ApplyTextChange(TextSnapshot oldSnapshot, TextSnapshot newSnapshot, TextChange change)
    {
        EnsureSnapshot(oldSnapshot);
        ArgumentNullException.ThrowIfNull(newSnapshot);
        TextChangeMapper.Validate(change, oldSnapshot, newSnapshot);
        var mapped = new List<FoldRange>(_candidates.Count);
        var collapsed = new Dictionary<string, FoldRange>(StringComparer.Ordinal);
        foreach (var fold in _candidates)
        {
            if (Touches(oldSnapshot, fold, change))
            {
                continue;
            }

            var mappedFold = new FoldRange(
                fold.Id,
                TextChangeMapper.MapRange(change, fold.Range),
                fold.Placeholder);
            mapped.Add(mappedFold);
            if (_collapsed.ContainsKey(fold.Id))
            {
                collapsed[fold.Id] = mappedFold;
            }
        }

        _snapshot = newSnapshot;
        _candidates.Clear();
        _candidates.AddRange(mapped);
        _collapsed.Clear();
        _collapsedIds.Clear();
        foreach (var item in collapsed)
        {
            _collapsed.Add(item.Key, item.Value);
            _collapsedIds.Add(item.Key);
        }
    }

    public void ApplyProviderFolds(TextSnapshot snapshot, IEnumerable<FoldRange> folds, bool isComplete)
    {
        EnsureSnapshot(snapshot);
        ArgumentNullException.ThrowIfNull(folds);
        var provider = folds.ToArray();
        var result = new List<FoldRange>(provider.Length);
        var nextCollapsed = new Dictionary<string, FoldRange>(StringComparer.Ordinal);
        var usedCollapsedIds = new HashSet<string>(StringComparer.Ordinal);
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        var collapsedByRange = _collapsed.Values
            .GroupBy(fold => fold.Range)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var fold in provider)
        {
            if (fold.Range.Start > snapshot.Length || fold.Range.End > snapshot.Length)
            {
                throw new ArgumentException("A fold does not belong to the snapshot.", nameof(folds));
            }

            var semantic = FindSemanticMatch(
                fold,
                collapsedByRange,
                usedCollapsedIds);
            if (semantic is null
                && !isComplete
                && _collapsed.ContainsKey(fold.Id))
            {
                // An ordinal/path ID can move to another semantic fold while
                // the provider is recovering from incomplete input. Keep the
                // mapped collapsed candidate instead of collapsing that fold.
                continue;
            }

            if (semantic is not null)
            {
                usedCollapsedIds.Add(semantic.Id);
                nextCollapsed[fold.Id] = fold;
            }

            if (resultIds.Add(fold.Id))
            {
                result.Add(fold);
            }
        }

        if (!isComplete)
        {
            foreach (var item in _collapsed.Values)
            {
                if (usedCollapsedIds.Contains(item.Id))
                {
                    continue;
                }

                if (resultIds.Add(item.Id))
                {
                    result.Add(item);
                }

                nextCollapsed[item.Id] = item;
            }
        }

        result.Sort(static (left, right) => left.Range.Start.CompareTo(right.Range.Start));
        _candidates.Clear();
        _candidates.AddRange(result);
        _collapsed.Clear();
        _collapsedIds.Clear();
        foreach (var item in nextCollapsed)
        {
            _collapsed[item.Key] = item.Value;
            _collapsedIds.Add(item.Key);
        }
    }

    public void SetCollapsed(string foldId, bool collapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(foldId);
        var fold = FindFold(foldId);
        if (collapsed)
        {
            if (fold is not null)
            {
                _collapsed[foldId] = fold;
                _collapsedIds.Add(foldId);
            }
        }
        else
        {
            _collapsed.Remove(foldId);
            _collapsedIds.Remove(foldId);
        }
    }

    public void ExpandAll()
    {
        _collapsed.Clear();
        _collapsedIds.Clear();
    }

    public FoldRange? FindFold(string foldId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(foldId);
        return _candidates.FirstOrDefault(fold => fold.Id == foldId);
    }

    private FoldRange? FindSemanticMatch(
        FoldRange fold,
        Dictionary<TextRange, FoldRange[]> collapsedByRange,
        HashSet<string> used)
    {
        if (collapsedByRange.TryGetValue(fold.Range, out var exact))
        {
            var match = exact.FirstOrDefault(item => !used.Contains(item.Id));
            if (match is not null)
            {
                return match;
            }
        }

        return _collapsed.TryGetValue(fold.Id, out var sameId)
            && !used.Contains(sameId.Id)
            && Related(sameId.Range, fold.Range)
                ? sameId
                : null;
    }

    private static bool Touches(TextSnapshot snapshot, FoldRange fold, TextChange change)
    {
        var edit = change.OldRange;
        var rangeTouched = edit.IsEmpty
            ? edit.Start >= fold.Range.Start && edit.Start < fold.Range.End
            : edit.Start < fold.Range.End && fold.Range.Start < edit.End;
        var line = snapshot.Lines.GetLineColumn(fold.Range.Start).Line;
        var headerEnd = line + 1 < snapshot.Lines.LineCount
            ? snapshot.Lines.GetLineStart(line + 1)
            : snapshot.Length;
        var header = TextRange.FromBounds(snapshot.Lines.GetLineStart(line), headerEnd);
        var headerTouched = edit.IsEmpty
            ? edit.Start >= header.Start && edit.Start <= header.End
            : edit.Start < header.End && header.Start < edit.End;
        return rangeTouched || headerTouched;
    }

    private static bool Related(TextRange left, TextRange right) =>
        left.Start < right.End && right.Start < left.End;

    private void EnsureSnapshot(TextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ReferenceEquals(_snapshot, snapshot))
        {
            throw new InvalidOperationException("The operation snapshot does not match the tracker snapshot.");
        }
    }
}
