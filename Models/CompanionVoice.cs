using System.Globalization;

namespace DesktopFamiliar.Models;

public sealed record CompanionVoice
{
    // An empty ID follows the Windows default. "off" disables synthesized speech for this pet.
    public string Id { get; init; } = "";
    public string ReplyLanguage { get; init; } = "";
    public int Speaker { get; init; }
    public int Rate { get; init; }
    public int Pitch { get; init; }
    public int Volume { get; init; } = 90;

    public CompanionVoice Normalize() => this with
    {
        Id = Id ?? "", ReplyLanguage = LanguageTag(ReplyLanguage), Speaker = Math.Max(0, Speaker),
        Rate = Math.Clamp(Rate, -5, 5), Pitch = Math.Clamp(Pitch, -10, 10), Volume = Math.Clamp(Volume, 0, 100)
    };

    internal static string LanguageTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 40) return "";
        try { return CultureInfo.GetCultureInfo(tag).Name; }
        catch (CultureNotFoundException) { return ""; }
    }
}
