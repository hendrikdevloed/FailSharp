using System.Text.Json;

namespace FailSharp.Core;

public sealed class FailureSettings
{
    public bool Enabled { get; set; }
    public double ProbabilityPercent { get; set; } = 5.0;
    public double CheckIntervalMinutes { get; set; } = 5.0;
    public HashSet<int> EligibleEngines { get; set; } = new() { 1, 2, 3, 4 };
    public double MinSpeedKts { get; set; } = 60.0;
    public double MaxSpeedKts { get; set; } = 150.0;

    // Aircraft positioning ("Position" tab). Heights are above the airport/runway elevation.
    public double PositionSpeedKts { get; set; } = 90.0;
    public double OverheadAglFt { get; set; } = 2000.0;
    public double DownwindAglFt { get; set; } = 1000.0;
    public double BaseAglFt { get; set; } = 1000.0;
    public double FinalAglFt { get; set; } = 800.0;
    public bool RightHandPattern { get; set; }

    /// <summary>UI preference (window stays topmost so focusing the sim doesn't hide it), not a failure parameter — kept here rather than in a second settings file for one bool.</summary>
    public bool AlwaysOnTop { get; set; } = true;
}

/// <summary>
/// The discrete set of check-interval stops offered in the UI: fine-grained at the low end,
/// coarser as it grows (30s, then 1' steps up to 10', then 5' steps up to 20', then bigger jumps to 60').
/// </summary>
public static class CheckIntervalOptions
{
    public static readonly double[] Minutes =
    {
        0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 30, 45, 60,
    };

    public static string Format(double minutes) =>
        minutes < 1 ? $"{(int)Math.Round(minutes * 60)} sec" : $"{(int)Math.Round(minutes)} min";
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
