namespace DesktopFamiliar.AddonSdk;

/// <summary>
/// A single command an addon contributes to the companion viewport's grouped Action submenu.
/// The host owns menu rendering and defers execution until the context menu has closed.
/// </summary>
public sealed class AddonViewportAction
{
    public AddonViewportAction(string id, string header, Action execute, Func<bool>? isVisible = null, int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentNullException.ThrowIfNull(execute);
        Id = id;
        Header = header;
        Execute = execute;
        IsVisible = isVisible ?? (() => true);
        Order = order;
    }

    public string Id { get; }
    public string Header { get; }
    public Action Execute { get; }
    public Func<bool> IsVisible { get; }
    public int Order { get; }
}

/// <summary>Service exposed by hosts that support addon viewport commands (host API 1.1 or later).</summary>
public interface IAddonViewportActionRegistry
{
    IDisposable Register(AddonViewportAction action);
    IReadOnlyList<AddonViewportAction> GetVisibleActions();
}

/// <summary>
/// Thread-safe in-memory registry used by the desktop host. Registrations are removed when an addon is disposed.
/// </summary>
public sealed class AddonViewportActionRegistry : IAddonViewportActionRegistry
{
    private readonly object _gate = new();
    private readonly List<AddonViewportAction> _actions = [];

    public IDisposable Register(AddonViewportAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (_actions.Any(existing => string.Equals(existing.Id, action.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"An addon viewport action with ID '{action.Id}' is already registered.");
            _actions.Add(action);
        }

        return new Registration(this, action);
    }

    public IReadOnlyList<AddonViewportAction> GetVisibleActions()
    {
        lock (_gate)
        {
            return _actions
                .Where(action => action.IsVisible())
                .OrderBy(action => action.Order)
                .ThenBy(action => action.Header, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    private void Unregister(AddonViewportAction action)
    {
        lock (_gate)
            _actions.Remove(action);
    }

    private sealed class Registration : IDisposable
    {
        private AddonViewportActionRegistry? _owner;
        private readonly AddonViewportAction _action;

        public Registration(AddonViewportActionRegistry owner, AddonViewportAction action)
        {
            _owner = owner;
            _action = action;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Unregister(_action);
        }
    }
}
