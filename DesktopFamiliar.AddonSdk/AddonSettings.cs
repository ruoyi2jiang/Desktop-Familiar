using System.Windows;

namespace DesktopFamiliar.AddonSdk;

/// <summary>Provides atomic JSON storage scoped to one installed addon.</summary>
public interface IAddonSettingsStore
{
    T? Read<T>(string addonId) where T : class;

    void Write<T>(string addonId, T settings) where T : class;
}

/// <summary>Resolves the host's localized user-facing strings for an addon.</summary>
public interface IAddonStringProvider
{
    string Get(string key);

    string Format(string key, params object[] arguments);
}

/// <summary>Presents a short status message in the active companion dialogue bubble.</summary>
public interface IAddonCompanionStatusPresenter
{
    void Show(string message);
}

/// <summary>An addon-owned settings visual contributed to the host's Addons page.</summary>
public interface IAddonSettingsSection
{
    string Id { get; }

    int Order { get; }

    FrameworkElement CreateView();
}

/// <summary>
/// Optional host API 1.3 contract for presenting an addon's settings inside its installed-addon card.
/// </summary>
public interface IInlineAddonSettingsSection : IAddonSettingsSection
{
    AddonInlineSettingsContent CreateInlineContent();
}

/// <summary>Addon-owned visual fragments composed by the host inside one installed-addon card.</summary>
public sealed record AddonInlineSettingsContent(FrameworkElement Header, FrameworkElement Settings)
{
    /// <summary>Optional host API 1.4 action placed at the right of the installed addon's title row.</summary>
    public FrameworkElement? TitleAction { get; init; }
}

/// <summary>Host service for registering addon-owned settings visuals (host API 1.2 or later).</summary>
public interface IAddonSettingsSectionRegistry
{
    IDisposable Register(IAddonSettingsSection section);

    IReadOnlyList<IAddonSettingsSection> GetSections();
}

/// <summary>Thread-safe settings-section registry. Registrations are removed when an addon unloads.</summary>
public sealed class AddonSettingsSectionRegistry : IAddonSettingsSectionRegistry
{
    private readonly object _gate = new();
    private readonly List<IAddonSettingsSection> _sections = [];

    public IDisposable Register(IAddonSettingsSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(section.Id);
        lock (_gate)
        {
            if (_sections.Any(existing => string.Equals(existing.Id, section.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"An addon settings section with ID '{section.Id}' is already registered.");
            _sections.Add(section);
        }

        return new Registration(this, section);
    }

    public IReadOnlyList<IAddonSettingsSection> GetSections()
    {
        lock (_gate)
        {
            return _sections
                .OrderBy(section => section.Order)
                .ThenBy(section => section.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private void Unregister(IAddonSettingsSection section)
    {
        lock (_gate)
            _sections.Remove(section);
    }

    private sealed class Registration : IDisposable
    {
        private AddonSettingsSectionRegistry? _owner;
        private readonly IAddonSettingsSection _section;

        public Registration(AddonSettingsSectionRegistry owner, IAddonSettingsSection section)
        {
            _owner = owner;
            _section = section;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Unregister(_section);
        }
    }
}
