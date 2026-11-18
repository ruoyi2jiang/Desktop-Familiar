namespace DesktopFamiliar.Models;

public sealed record ChatPreferences(bool Enabled = false, string? ModelId = null, string Shortcut = "Ctrl+Shift+F9");
