namespace DesktopFamiliar.Addons.AutoClicker.Models;

/// <summary>Serializable settings owned exclusively by the Auto Clicker addon.</summary>
public sealed class AutoClickerSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MinimumIntervalMilliseconds = 10;
    public const int MaximumIntervalMilliseconds = 60000;
    public const int DefaultIntervalMilliseconds = 1000;
    public const string DefaultHotkey = "Ctrl+Alt+C";
    public const string DefaultBackgroundModeHotkey = "Ctrl+Alt+B";
    public const string FallbackBackgroundModeHotkey = "Ctrl+Alt+V";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int IntervalMilliseconds { get; set; } = DefaultIntervalMilliseconds;
    public int IntervalMaximumMilliseconds { get; set; } = DefaultIntervalMilliseconds;
    public string Hotkey { get; set; } = DefaultHotkey;
    public string BackgroundModeHotkey { get; set; } = DefaultBackgroundModeHotkey;
    public bool Enabled { get; set; } = true;
    public bool BackgroundModeEnabled { get; set; }

    public AutoClickerSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        IntervalMilliseconds = IntervalMilliseconds,
        IntervalMaximumMilliseconds = IntervalMaximumMilliseconds,
        Hotkey = Hotkey,
        BackgroundModeHotkey = BackgroundModeHotkey,
        Enabled = Enabled,
        BackgroundModeEnabled = BackgroundModeEnabled
    };
}
