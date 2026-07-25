namespace DesktopFamiliar.Addons.AutoClicker.Services;

public sealed record AutoClickHotkey(string DisplayText, uint Modifiers, uint VirtualKey);

/// <summary>Parses and validates the portable representation of a global hotkey.</summary>
public static class AutoClickHotkeyPolicy
{
    public const uint ModifierAlt = 0x0001;
    public const uint ModifierControl = 0x0002;
    public const uint ModifierShift = 0x0004;
    public const uint ModifierWin = 0x0008;

    private static readonly IReadOnlyDictionary<string, uint> NamedKeys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E
    };

    public static bool TryParse(string? value, out AutoClickHotkey hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierControl;
                    break;
                case "ALT":
                    modifiers |= ModifierAlt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierShift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierWin;
                    break;
                default:
                    return false;
            }
        }

        return TryCreate(modifiers, ParseVirtualKey(parts[^1]), out hotkey);
    }

    public static bool TryCreate(uint modifiers, uint virtualKey, out AutoClickHotkey hotkey)
    {
        hotkey = null!;
        if ((modifiers & (ModifierAlt | ModifierControl | ModifierWin)) == 0 || !IsSupportedVirtualKey(virtualKey))
            return false;

        var parts = new List<string>(4);
        if ((modifiers & ModifierControl) != 0)
            parts.Add("Ctrl");
        if ((modifiers & ModifierAlt) != 0)
            parts.Add("Alt");
        if ((modifiers & ModifierShift) != 0)
            parts.Add("Shift");
        if ((modifiers & ModifierWin) != 0)
            parts.Add("Win");
        parts.Add(FormatVirtualKey(virtualKey));
        hotkey = new AutoClickHotkey(string.Join('+', parts), modifiers, virtualKey);
        return true;
    }

    private static uint ParseVirtualKey(string value)
    {
        if (value.Length == 1 && char.IsAsciiLetterOrDigit(value[0]))
            return char.ToUpperInvariant(value[0]);
        if (value.Length is 2 or 3 && value[0] is 'F' or 'f'
            && int.TryParse(value[1..], out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            return (uint)(0x6F + functionNumber);
        }

        return NamedKeys.TryGetValue(value, out var virtualKey) ? virtualKey : 0;
    }

    private static bool IsSupportedVirtualKey(uint virtualKey) =>
        (virtualKey is >= '0' and <= '9')
        || (virtualKey is >= 'A' and <= 'Z')
        || (virtualKey is >= 0x70 and <= 0x87)
        || NamedKeys.Values.Contains(virtualKey);

    private static string FormatVirtualKey(uint virtualKey)
    {
        if (virtualKey is >= '0' and <= '9' or >= 'A' and <= 'Z')
            return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87)
            return $"F{virtualKey - 0x6F}";
        return NamedKeys.Single(pair => pair.Value == virtualKey).Key;
    }
}
