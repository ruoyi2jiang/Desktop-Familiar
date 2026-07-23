using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DesktopFamiliar.Services;

namespace DesktopFamiliar.Localization;

public static class LocalizationService
{
    private const string DefaultLanguage = "en";
    private const string EmbeddedResourceName = "DesktopFamiliar.Localization.Strings.en.json";
    private static readonly object Sync = new();
    private static readonly HashSet<string> ReportedMissingKeys = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private static bool _initialized;

    public static string Language { get; private set; } = DefaultLanguage;

    public static string ApplicationMappingPath => Path.Combine(
        AppContext.BaseDirectory,
        "Resources",
        "Strings",
        $"{Language}.json");

    public static string UserOverridePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopFamiliar",
        "Localization",
        $"custom.{Language}.json");

    public static void Initialize(string language = DefaultLanguage)
    {
        lock (Sync)
        {
            if (_initialized && string.Equals(Language, language, StringComparison.OrdinalIgnoreCase))
                return;

            Language = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Trim();
            var merged = LoadEmbeddedDefaults();
            MergeFile(merged, ApplicationMappingPath, "application translation mapping");
            EnsureUserOverrideTemplate(merged);
            MergeFile(merged, UserOverridePath, "user translation override");
            _strings = merged;
            ReportedMissingKeys.Clear();
            _initialized = true;
        }
    }

    public static string Get(string key)
    {
        EnsureInitialized();
        lock (Sync)
        {
            if (_strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
            if (ReportedMissingKeys.Add(key))
                AppLogger.Warning($"Localization key '{key}' is missing for language '{Language}'.");
            return $"[{key}]";
        }
    }

    public static string Format(string key, params object?[] arguments)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }
        catch (FormatException exception)
        {
            AppLogger.Warning($"Localization key '{key}' contains invalid format placeholders.", exception);
            return template;
        }
    }

    private static void EnsureInitialized()
    {
        if (!_initialized)
            Initialize();
    }

    private static Dictionary<string, string> LoadEmbeddedDefaults()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"The embedded localization resource '{EmbeddedResourceName}' is missing.");
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("The embedded English localization mapping is empty.");
        return new Dictionary<string, string>(values, StringComparer.Ordinal);
    }

    private static void MergeFile(Dictionary<string, string> target, string path, string description)
    {
        if (!File.Exists(path))
            return;
        try
        {
            using var stream = File.OpenRead(path);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (values is null)
                return;
            foreach (var (key, value) in values)
            {
                if (!key.StartsWith('_') && !string.IsNullOrWhiteSpace(value))
                    target[key] = value;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"The {description} at '{path}' could not be loaded; lower-priority wording will be used.", exception);
        }
    }

    private static void EnsureUserOverrideTemplate(IReadOnlyDictionary<string, string> wording)
    {
        if (File.Exists(UserOverridePath))
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserOverridePath)!);
            var instructions = wording.TryGetValue("Localization.OverrideInstructions", out var configuredInstructions)
                ? configuredInstructions
                : "Add only keys you want to replace, then restart Desktop Familiar.";
            var template = JsonSerializer.Serialize(
                               new Dictionary<string, string> { ["_instructions"] = instructions },
                               new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
            File.WriteAllText(UserOverridePath, template, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception)
        {
            AppLogger.Warning("The user localization override template could not be created.", exception);
        }
    }
}
