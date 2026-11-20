namespace DesktopFamiliar.Models;

public sealed record ChatPreferences(bool Enabled = false, string? ModelId = null, string Shortcut = "Ctrl+Shift+F9")
{
    public bool VoiceEnabled { get; init; } = true;
    public string InputLanguage { get; init; } = "";
    public string? MicrophoneId { get; init; }
    public bool SendOnRelease { get; init; }
    public bool PreferWhisper { get; init; }
    public string TalkShortcut { get; init; } = "Ctrl+Shift+F10";

    internal ChatPreferences Normalize()
    {
        var defaults = new ChatPreferences();
        var talk = string.IsNullOrWhiteSpace(TalkShortcut) ? defaults.TalkShortcut : TalkShortcut;
        if (string.Equals(Shortcut, talk, StringComparison.OrdinalIgnoreCase))
            talk = string.Equals(Shortcut, defaults.TalkShortcut, StringComparison.OrdinalIgnoreCase)
                ? defaults.Shortcut : defaults.TalkShortcut;
        return this with { TalkShortcut = talk, InputLanguage = CompanionVoice.LanguageTag(InputLanguage) };
    }
}
