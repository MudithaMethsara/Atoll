using System.IO;
using Atoll.Core.Window;
using Atoll.Services;
using Atoll.Settings;
using Xunit;

namespace Atoll.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void SettingsViewModel_CommitsChangesToSettingsService()
    {
        string path = Path.Combine(Path.GetTempPath(), $"atoll_vm_{Guid.NewGuid():N}.json");
        try
        {
            var svc = new SettingsService(path);
            var monitors = new DisplayMonitorService();
            var startup = new StartupService();
            var vm = new SettingsViewModel(svc, monitors, startup);

            Assert.NotEmpty(vm.Monitors);
            Assert.Equal(string.Empty, vm.Monitors[0].DeviceName);

            vm.NotchMode = true;
            vm.LowPowerMode = true;
            vm.EnableWeatherGlance = true;
            vm.ToggleExpandHotkey = "Ctrl+Shift+I";
            vm.AnimationSpeed = 1.4;
            vm.AccentColorHex = "30d158";

            Assert.True(svc.Current.NotchMode);
            Assert.True(svc.Current.LowPowerMode);
            Assert.True(svc.Current.EnableWeatherGlance);
            Assert.Equal("Ctrl+Shift+I", svc.Current.ToggleExpandHotkey);
            Assert.Equal(1.4, svc.Current.AnimationSpeed, precision: 2);
            Assert.Equal("#30D158", svc.Current.AccentColorHex);

            vm.ResetDefaults();
            Assert.False(svc.Current.NotchMode);
            Assert.Equal("#0A84FF", svc.Current.AccentColorHex);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

