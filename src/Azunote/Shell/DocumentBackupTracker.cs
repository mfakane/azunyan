namespace Azunote;

/// <summary>
/// Keeps a backup of one window's document while it has unsaved changes, so
/// the changes survive a crash, a Windows shutdown, or closing the last window.
/// A clean document has no backup. Windows that share a document write the
/// same backup.
/// </summary>
internal sealed class DocumentBackupTracker : IDisposable
{
    private readonly DocumentBackupStore _store;
    private readonly DocumentSession _session;
    private readonly Func<string> _currentText;

    public DocumentBackupTracker(
        DocumentBackupStore store,
        DocumentSession session,
        Func<string> currentText)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _currentText = currentText ?? throw new ArgumentNullException(nameof(currentText));
        _session.StateChanged += Session_StateChanged;
    }

    /// <summary>
    /// False for a document whose text belongs to a command line, which has
    /// nothing to restore later.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    public void Disable()
    {
        IsEnabled = false;
        Discard();
    }

    /// <summary>Writes the backup now, or removes it when there is nothing unsaved.</summary>
    public void Flush()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (!_session.State.IsDirty)
        {
            Discard();
            return;
        }

        Run(() => _store.Write(new DocumentBackup(
            _session.BackupId,
            _session.State.FilePath,
            _currentText())));
    }

    public void Discard() => Run(() => _store.Delete(_session.BackupId));

    public void Dispose() => _session.StateChanged -= Session_StateChanged;

    private void Session_StateChanged(object? sender, EventArgs e)
    {
        if (!_session.State.IsDirty)
        {
            Discard();
        }
    }

    /// <summary>A failed backup must never interrupt editing; it is logged instead.</summary>
    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorReporter.LogException("Document backup", exception);
        }
    }
}
