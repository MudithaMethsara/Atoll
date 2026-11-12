using System.IO;
using System.Windows.Media;
using Atoll.Modules.Osd;
using Atoll.Modules.Privacy;
using Xunit;

namespace Atoll.Tests;

public sealed class OsdAndPrivacyTests
{
    [Fact]
    public void OsdActivity_UpdatesGlyphAndClampsVolumeAndBrightness()
    {
        string path = Path.Combine(Path.GetTempPath(), $"atoll_osd_{Guid.NewGuid():N}.json");
        try
        {
            StaTestHelper.Run(() =>
            {
                var ctx = StaTestHelper.CreateTestModuleContext(path);
                using var osdModule = new OsdModule();
                osdModule.InitializeAsync(ctx).GetAwaiter().GetResult();

                var activity = new OsdActivity(osdModule);
                activity.Update(OsdKind.Volume, 130, isMuted: false);
                Assert.Equal(100.0, activity.ValuePercent);
                Assert.Equal("100%", activity.ValueText);
                Assert.Equal("\uE995", activity.Glyph);

                activity.Update(OsdKind.Volume, 50, isMuted: true);
                Assert.Equal("Muted", activity.ValueText);
                Assert.Equal("\uE74F", activity.Glyph);

                activity.Update(OsdKind.Brightness, 72, isMuted: false);
                Assert.Equal("Brightness", activity.Title);
                Assert.Equal("\uE706", activity.Glyph);

                ctx.AudioService.Dispose();
                ctx.BrightnessService.Dispose();
            });
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PrivacyActivity_FormatsTitleAndAccentByActiveSensors()
    {
        var privacy = new PrivacyActivity();

        privacy.Update(cam: true, mic: false, screen: false, "CameraApp");
        Assert.Equal("Camera", privacy.Title);
        Assert.Equal("CAM", privacy.CompactTrailingText);
        Assert.Equal(Color.FromRgb(48, 209, 88), privacy.AccentColor);

        privacy.Update(cam: true, mic: true, screen: false, "Teams");
        Assert.Equal("Camera + Mic", privacy.Title);

        privacy.Update(cam: false, mic: true, screen: true, "OBS");
        Assert.Equal("Mic + Screen", privacy.Title);
        Assert.Equal("REC", privacy.CompactTrailingText);
        Assert.Equal(Color.FromRgb(255, 69, 58), privacy.AccentColor);
    }
}

