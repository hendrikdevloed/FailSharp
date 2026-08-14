using System.Text.Json;

namespace FailSharp.Core;

public sealed class FailureSettings
{
    public bool Enabled { get; set; }
    public double ProbabilityPercent { get; set; } = 5.0;
    public double CheckIntervalMinutes { get; set; } = 5.0;
    public HashSet<int> EligibleEngines { get; set; } = new() { 1, 2, 3, 4 };
}

public static class SettingsStore
{
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FailSharp", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static FailureSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<FailureSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings file: fall back to defaults rather than crash.
        }

        return new FailureSettings();
    }

    public static void Save(FailureSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}
