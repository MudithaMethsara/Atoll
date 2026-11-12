using System.IO;
using Atoll.Settings;
using Xunit;

namespace Atoll.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void SettingsService_PersistsAndClampsValues()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AtollTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "settings.json");

        try
        {
            var svc = new SettingsService(path);
            svc.Update(s =>
            {
                s.NotchMode = true;
                s.SizeScale = 9.0; // out of range -> clamped to 1.40
                s.EnableWeatherGlance = true;
            });

            var reloaded = new SettingsService(path);
            Assert.True(reloaded.Current.NotchMode);
            Assert.Equal(1.40, reloaded.Current.SizeScale, precision: 2);
            Assert.True(reloaded.Current.EnableWeatherGlance);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SettingsService_RecoversGracefullyFromCorruptFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AtollTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "settings.json");
        File.WriteAllText(path, "{ broken json !!!");

        try
        {
            var svc = new SettingsService(path);
            Assert.False(svc.Current.EnableWeatherGlance);
            Assert.Equal(8.0, svc.Current.TopOffset);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

