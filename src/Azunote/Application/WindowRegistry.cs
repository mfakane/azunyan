namespace Azunote;

/// <summary>
/// Keeps the application's windows in creation order and tracks the window
/// that most recently became active. The actual window operations remain in
/// the WinUI shell so this type can be tested without creating windows.
/// </summary>
internal sealed class WindowRegistry<T>
    where T : class
{
    private readonly List<T> _windows = [];

    public IReadOnlyList<T> Windows => _windows;

    public T? Active { get; private set; }

    public void Register(T window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (FindIndex(window) >= 0)
        {
            throw new InvalidOperationException("The window is already registered.");
        }

        _windows.Add(window);
        Active ??= window;
    }

    public bool Unregister(T window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var index = FindIndex(window);
        if (index < 0)
        {
            return false;
        }

        var wasActive = ReferenceEquals(Active, window);
        _windows.RemoveAt(index);
        if (wasActive)
        {
            Active = _windows.Count == 0
                ? null
                : _windows[Math.Min(index, _windows.Count - 1)];
        }

        return true;
    }

    public void MarkActive(T window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (FindIndex(window) < 0)
        {
            throw new InvalidOperationException("The window is not registered.");
        }

        Active = window;
    }

    public T CycleFrom(T current, int direction)
    {
        ArgumentNullException.ThrowIfNull(current);
        var index = FindIndex(current);
        if (index < 0)
        {
            throw new InvalidOperationException("The window is not registered.");
        }

        if (_windows.Count < 2)
        {
            return current;
        }

        var step = direction < 0 ? -1 : 1;
        var nextIndex = (index + step + _windows.Count) % _windows.Count;
        return _windows[nextIndex];
    }

    private int FindIndex(T window)
    {
        for (var index = 0; index < _windows.Count; index++)
        {
            if (ReferenceEquals(_windows[index], window))
            {
                return index;
            }
        }

        return -1;
    }
}
