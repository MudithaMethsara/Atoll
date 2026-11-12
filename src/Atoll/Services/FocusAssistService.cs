using Atoll.Core.Window;
using Microsoft.Win32;

namespace Atoll.Services;

public sealed class FocusAssistService
{
    private const string NotificationsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string GlobalToastsValueName = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

    // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED
    private const ulong WnfQuietHoursProfile = 0x0D83063EA3BE0875UL;

    public bool IsDoNotDisturbEnabled()
    {
        try
        {
            ulong stateName = WnfQuietHoursProfile;
            uint size = 4;
            int status = Win32Interop.NtQueryWnfStateData(
                ref stateName,
                IntPtr.Zero,
                IntPtr.Zero,
                out _,
                out uint profile,
                ref size);

            if (status == 0 && profile > 0)
            {
                return true;
            }
        }
        catch
        {
            // Fallback to registry check
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NotificationsKeyPath, writable: false);
            if (key?.GetValue(GlobalToastsValueName) is int val)
            {
                return val == 0;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    public bool ToggleDoNotDisturb()
    {
        bool nextEnabled = !IsDoNotDisturbEnabled();
        SetDoNotDisturb(nextEnabled);
        return nextEnabled;
    }

    public void SetDoNotDisturb(bool enabled)
    {
        try
        {
            ulong stateName = WnfQuietHoursProfile;
            uint newProfile = enabled ? 2u : 0u; // 2 = Alarms only / DND, 0 = Off
            Win32Interop.NtUpdateWnfStateData(
                ref stateName,
                ref newProfile,
                4,
                IntPtr.Zero,
                IntPtr.Zero,
                0,
                0);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("FocusAssist", $"WNF update failed: {ex.Message}");
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(NotificationsKeyPath, writable: true);
            key?.SetValue(GlobalToastsValueName, enabled ? 0 : 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warn("FocusAssist", "Failed to toggle Windows notification toast setting.", ex);
        }
    }
}

