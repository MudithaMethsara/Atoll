using System.Windows;
using System.Windows.Media;
using Atoll.Core.Activities;
using Atoll.Core.Window;
using Atoll.Services;
using Microsoft.Win32;

namespace Atoll.Modules.Privacy;

public sealed class PrivacyActivity : IslandActivityBase
{
    private bool _cameraActive;
    private bool _micActive;
    private bool _screenCaptureActive;

    public override ActivityKind Kind => ActivityKind.Privacy;
    public override Size PreferredCompactSize => new(168, 34);
    public override Size PreferredExpandedSize => new(340, 126);

    public bool CameraActive
    {
        get => _cameraActive;
        internal set => SetField(ref _cameraActive, value);
    }

    public bool MicActive
    {
        get => _micActive;
        internal set => SetField(ref _micActive, value);
    }

    public bool ScreenCaptureActive
    {
        get => _screenCaptureActive;
        internal set => SetField(ref _screenCaptureActive, value);
    }

    internal PrivacyActivity() : base("privacy.indicators", "privacy")
    {
        Priority = ActivityPriority.Low;
        Glyph = "\uE72E";
    }

    internal void Update(bool cam, bool mic, bool screen, string activeApp)
    {
        CameraActive = cam;
        MicActive = mic;
        ScreenCaptureActive = screen;

        var parts = new List<string>(3);
        if (cam) parts.Add("Camera");
        if (mic) parts.Add("Mic");
        if (screen) parts.Add("Screen");

        Title = string.Join(" + ", parts);
        Subtitle = string.IsNullOrWhiteSpace(activeApp) ? "Active sensor access" : activeApp;
        CompactTrailingText = screen ? "REC" : (cam ? "CAM" : "MIC");

        if (screen) AccentColor = Color.FromRgb(255, 69, 58);
        else if (cam) AccentColor = Color.FromRgb(48, 209, 88);
        else AccentColor = Color.FromRgb(255, 159, 10);
    }
}

public sealed class PrivacyModule : IIslandModule
{
    private const string ConsentBase = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private static readonly string[] WatchedCapabilities =
    [
        "webcam",
        "microphone",
        "graphicsCaptureProgrammatic",
        "graphicsCaptureWithoutBorder"
    ];

    private ModuleContext? _ctx;
    private PrivacyActivity? _activity;
    private CancellationTokenSource? _watchCts;
    private Task? _watchTask;
    private bool _disposed;

    public string ModuleId => "privacy";
    public string DisplayName => "Privacy Indicators (Cam/Mic/Screen)";
    public bool IsEnabled { get; private set; }
    public bool IsActive => _activity is not null && IsEnabled;

    public bool CameraActive { get; private set; }
    public bool MicActive { get; private set; }
    public bool ScreenCaptureActive { get; private set; }
    public event EventHandler? PrivacyDotsChanged;

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnablePrivacy;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;

        if (IsEnabled)
        {
            EvaluateAndPublish();
            StartRegistryWatch();
        }
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (enabled)
        {
            EvaluateAndPublish();
            StartRegistryWatch();
        }
        else
        {
            StopRegistryWatch();
            CameraActive = false;
            MicActive = false;
            ScreenCaptureActive = false;
            PrivacyDotsChanged?.Invoke(this, EventArgs.Empty);
            _ctx?.ActivityManager.Remove("privacy.indicators");
            _activity = null;
        }
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnablePrivacy != IsEnabled)
        {
            SetEnabled(settings.EnablePrivacy);
        }
    }

    private void StartRegistryWatch()
    {
        if (_watchCts is not null) return;
        _watchCts = new CancellationTokenSource();
        var token = _watchCts.Token;

        _watchTask = Task.Run(() => WatchRegistryLoop(token), token);
    }

    private void StopRegistryWatch()
    {
        if (_watchCts is null) return;
        _watchCts.Cancel();
        _watchCts.Dispose();
        _watchCts = null;
        _watchTask = null;
    }

    private void WatchRegistryLoop(CancellationToken token)
    {
        var openKeys = new List<IntPtr>();
        var events = new List<AutoResetEvent>();
        IntPtr hkeyCurrentUser = new(unchecked((int)0x80000001));

        try
        {
            foreach (string cap in WatchedCapabilities)
            {
                string subKey = $@"{ConsentBase}\{cap}";
                if (Win32Interop.RegOpenKeyEx(hkeyCurrentUser, subKey, 0, 0x0010 /* KEY_NOTIFY */, out IntPtr hKey) == 0)
                {
                    openKeys.Add(hKey);
                    events.Add(new AutoResetEvent(false));
                }
            }

            if (openKeys.Count == 0) return;

            var waitHandles = new WaitHandle[events.Count + 1];
            for (int i = 0; i < events.Count; i++) waitHandles[i] = events[i];
            waitHandles[events.Count] = token.WaitHandle;

            while (!token.IsCancellationRequested)
            {
                for (int i = 0; i < openKeys.Count; i++)
                {
                    // REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET
                    Win32Interop.RegNotifyChangeKeyValue(openKeys[i], true, 0x01 | 0x04, events[i].SafeWaitHandle.DangerousGetHandle(), true);
                }

                int signaled = WaitHandle.WaitAny(waitHandles);
                if (signaled == events.Count || token.IsCancellationRequested) break;

                // Debounce rapid burst of registry writes
                Thread.Sleep(80);
                _ctx?.UiDispatcher.BeginInvoke(EvaluateAndPublish);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Privacy", $"Registry privacy watcher stopped: {ex.Message}");
        }
        finally
        {
            foreach (var ev in events) ev.Dispose();
            foreach (var key in openKeys) Win32Interop.RegCloseKey(key);
        }
    }

    private void EvaluateAndPublish()
    {
        if (!IsEnabled || _disposed || _ctx is null) return;

        bool cam = IsCapabilityActive("webcam", out string camApp);
        bool mic = IsCapabilityActive("microphone", out string micApp);
        bool screen = IsCapabilityActive("graphicsCaptureProgrammatic", out string scrApp)
                   || IsCapabilityActive("graphicsCaptureWithoutBorder", out scrApp);

        CameraActive = cam;
        MicActive = mic;
        ScreenCaptureActive = screen;
        PrivacyDotsChanged?.Invoke(this, EventArgs.Empty);

        if (cam || mic || screen)
        {
            string app = !string.IsNullOrWhiteSpace(scrApp) ? scrApp : (!string.IsNullOrWhiteSpace(camApp) ? camApp : micApp);
            _activity ??= new PrivacyActivity();
            _activity.Update(cam, mic, screen, app);
            _ctx.ActivityManager.RegisterOrUpdate(_activity);
        }
        else if (_activity is not null)
        {
            _ctx.ActivityManager.Remove("privacy.indicators");
            _activity = null;
        }
    }

    private static bool IsCapabilityActive(string capability, out string appName)
    {
        appName = string.Empty;
        try
        {
            using var capKey = Registry.CurrentUser.OpenSubKey($@"{ConsentBase}\{capability}", writable: false);
            if (capKey is null) return false;

            if (CheckSubkeys(capKey, out appName)) return true;

            using var nonPackaged = capKey.OpenSubKey("NonPackaged", writable: false);
            if (nonPackaged is not null && CheckSubkeys(nonPackaged, out appName)) return true;
        }
        catch
        {
            // Ignore registry permission anomalies
        }
        return false;
    }

    private static bool CheckSubkeys(RegistryKey parent, out string appName)
    {
        appName = string.Empty;
        foreach (string sub in parent.GetSubKeyNames())
        {
            if (string.Equals(sub, "NonPackaged", StringComparison.OrdinalIgnoreCase)) continue;
            using var child = parent.OpenSubKey(sub, writable: false);
            if (child is null) continue;

            object? startObj = child.GetValue("LastUsedTimeStart");
            object? stopObj = child.GetValue("LastUsedTimeStop");
            if (startObj is long start && start > 0 && stopObj is long stop && stop == 0)
            {
                appName = CleanRegistryAppName(sub);
                return true;
            }
        }
        return false;
    }

    private static string CleanRegistryAppName(string raw)
    {
        if (raw.Contains('#'))
        {
            string[] parts = raw.Split('#');
            string last = parts[^1];
            return System.IO.Path.GetFileNameWithoutExtension(last);
        }
        int underscore = raw.IndexOf('_');
        return underscore > 0 ? raw[..underscore] : raw;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopRegistryWatch();
        if (_ctx is not null)
        {
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}

