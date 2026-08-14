using FailSharp.Core;
using Xunit;

namespace FailSharp.Core.Tests;

public class SettingsTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"failsharp-test-{Guid.NewGuid()}.json");

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = TempPath();
        try
        {
            var original = new FailureSettings
            {
                Enabled = true,
                ProbabilityPercent = 12.5,
                CheckIntervalMinutes = 3.5,
                EligibleEngines = new HashSet<int> { 1, 3 },
            };

            SettingsStore.Save(original, path);
            var loaded = SettingsStore.Load(path);

            Assert.Equal(original.Enabled, loaded.Enabled);
            Assert.Equal(original.ProbabilityPercent, loaded.ProbabilityPercent);
            Assert.Equal(original.CheckIntervalMinutes, loaded.CheckIntervalMinutes);
            Assert.Equal(original.EligibleEngines, loaded.EligibleEngines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var path = TempPath();

        var settings = SettingsStore.Load(path);

        Assert.False(settings.Enabled);
        Assert.Equal(new HashSet<int> { 1, 2, 3, 4 }, settings.EligibleEngines);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ not valid json");
        try
        {
            var settings = SettingsStore.Load(path);

            Assert.False(settings.Enabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"failsharp-test-dir-{Guid.NewGuid()}");
        var path = Path.Combine(dir, "settings.json");
        try
        {
            SettingsStore.Save(new FailureSettings(), path);

            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
