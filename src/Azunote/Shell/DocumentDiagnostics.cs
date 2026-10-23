using Azunyan.Core;

namespace Azunote;

/// <summary>One problem placed in the document, with the tool that reported it.</summary>
public sealed record DocumentDiagnostic(
    string SourceKey,
    string Source,
    TextRange Range,
    DiagnosticSeverity Severity,
    string Message);

/// <summary>
/// The problems external tools reported for the document in one window. A run
/// of a tool replaces what that tool reported before. Edits move the problems
/// after them along and drop the ones they touch, since the text a problem was
/// about has changed.
/// </summary>
public sealed class DocumentDiagnostics
{
    private List<DocumentDiagnostic> _items = [];

    /// <summary>
    /// Raised when a run or <see cref="Clear"/> changes the problems. Edits
    /// raise nothing: the editor reads the problems again after every edit.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The problems in document order.</summary>
    public IReadOnlyList<DocumentDiagnostic> Items => _items;

    public void Replace(string sourceKey, IEnumerable<DocumentDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var items = _items
            .Where(item => item.SourceKey != sourceKey)
            .Concat(diagnostics)
            .OrderBy(item => item.Range.Start)
            .ThenBy(item => item.Severity)
            .ToList();
        if (items.SequenceEqual(_items))
        {
            return;
        }

        _items = items;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items = [];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Apply(TextChange change)
    {
        if (_items.Count == 0)
        {
            return;
        }

        var delta = change.NewText.Length - change.OldRange.Length;
        var items = new List<DocumentDiagnostic>(_items.Count);
        foreach (var item in _items)
        {
            if (change.OldRange.End <= item.Range.Start)
            {
                items.Add(item with { Range = new TextRange(item.Range.Start + delta, item.Range.Length) });
            }
            else if (change.OldRange.Start >= item.Range.End)
            {
                items.Add(item);
            }
        }

        _items = items;
    }

    /// <summary>The problems whose range contains or ends at <paramref name="position"/>.</summary>
    public IEnumerable<DocumentDiagnostic> At(int position) =>
        _items.Where(item => item.Range.Start <= position && position <= item.Range.End);

    /// <summary>The first problem starting after <paramref name="position"/>, wrapping to the first.</summary>
    public DocumentDiagnostic? Next(int position) =>
        _items.FirstOrDefault(item => item.Range.Start > position) ?? _items.FirstOrDefault();

    /// <summary>The last problem starting before <paramref name="position"/>, wrapping to the last.</summary>
    public DocumentDiagnostic? Previous(int position) =>
        _items.LastOrDefault(item => item.Range.Start < position) ?? _items.LastOrDefault();

    public int Count(DiagnosticSeverity severity) => _items.Count(item => item.Severity == severity);
}

/// <summary>
/// Shows the problems in the editor: each as a wavy underline in the color of
/// its severity, and their messages when the pointer rests on one.
/// </summary>
public sealed class DocumentDiagnosticsProvider : IDecorationProvider, ITooltipProvider
{
    private readonly DocumentDiagnostics _diagnostics;

    public DocumentDiagnosticsProvider(DocumentDiagnostics diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public static string GetDecorationKind(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "information"
    };

    public ValueTask<IReadOnlyList<TextDecoration>> GetDecorationsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TextDecoration> decorations = _diagnostics.Items
            .Where(item => item.Range.End <= context.Snapshot.Length)
            .Select(item => new TextDecoration(item.Range, GetDecorationKind(item.Severity), item.Message))
            .ToArray();
        return ValueTask.FromResult(decorations);
    }

    public ValueTask<TooltipData?> GetTooltipAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var items = _diagnostics.Items
            .Where(item => item.Range.Start <= context.Position && context.Position < item.Range.End)
            .ToArray();
        if (items.Length == 0)
        {
            return ValueTask.FromResult<TooltipData?>(null);
        }

        var range = TextRange.FromBounds(items.Min(item => item.Range.Start), items.Max(item => item.Range.End));
        var content = string.Join(
            Environment.NewLine,
            items.Select(item => $"{item.Severity}: {item.Message} ({item.Source})"));
        return ValueTask.FromResult<TooltipData?>(new TooltipData(range, content));
    }
}
