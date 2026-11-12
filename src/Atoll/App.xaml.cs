using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Atoll.Core.Activities;
using Atoll.Core.Animation;
using Atoll.Core.State;
using Atoll.Core.Window;
using Atoll.Modules;
using Atoll.Modules.Battery;
using Atoll.Modules.Bluetooth;
using Atoll.Modules.Calendar;
using Atoll.Modules.Calls;
using Atoll.Modules.Glance;
using Atoll.Modules.Media;
using Atoll.Modules.Notifications;
using Atoll.Modules.Osd;
using Atoll.Modules.Privacy;
using Atoll.Modules.QuickActions;
using Atoll.Modules.Shelf;
using Atoll.Modules.Timer;
using Atoll.Services;
using Atoll.Settings;
using Atoll.Views;

namespace Atoll;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private SettingsService? _settingsService;
    private DisplayMonitorService? _monitorService;
    private ActivityManager? _activityManager;
    private IslandStateMachine? _stateMachine;
    private MorphAnimator? _animator;
    private AudioEndpointService? _audioService;
    private BrightnessService? _brightnessService;
    private ImageCacheService? _imageCache;
    private FocusAssistService? _focusAssist;
    private StartupService? _startupService;
    private GlobalHotkeyService? _hotkeyService;
    private TrayIconService? _trayService;
    private FullscreenDetector? _fullscreenDetector;

    private readonly List<IIslandModule> _modules = new();
    private IslandWindow? _islandWindow;
    private SettingsWindow? _settingsWindow;
    private DiagnosticsOverlay? _diagnosticsOverlay;

    private DispatcherTimer? _alertCollapseTimer;
    private Action? _pendingAlertAction;
    private string _registeredHotkey = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "AtollDynamicIslandSingleInstance", out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        RegisterGlobalExceptionHandlers();
        // Avoid loading ~140 MB of D3D9/GPU shader compiler DLLs for a compact layered overlay
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        LoggingService.Instance.Info("App", "Starting Atoll Dynamic Island...");

        _settingsService = new SettingsService();
        ApplyAccentTheme(_settingsService.Current.AccentColorHex);
        _monitorService = new DisplayMonitorService();
        _activityManager = new ActivityManager();
        _animator = new MorphAnimator();
        _audioService = new AudioEndpointService();
        _brightnessService = new BrightnessService();
        _imageCache = new ImageCacheService();
        _focusAssist = new FocusAssistService();
        _startupService = new StartupService();
        _hotkeyService = new GlobalHotkeyService();
        _trayService = new TrayIconService();
        _fullscreenDetector = new FullscreenDetector();

        _stateMachine = new IslandStateMachine(_activityManager, ScheduleAlertTimer, CancelAlertTimer);

        var mediaModule = new MediaModule();
        var timerModule = new TimerModule();
        var callModule = new CallModule();
        var notifModule = new NotificationModule();
        var batteryModule = new BatteryModule();
        var bluetoothModule = new BluetoothModule();
        var osdModule = new OsdModule();
        var privacyModule = new PrivacyModule();
        var calendarModule = new CalendarModule();
        var shelfModule = new FileShelfModule();
        var quickActionsModule = new QuickActionsModule();
        var glanceModule = new GlanceModule();

        _modules.AddRange([
            mediaModule, timerModule, callModule, notifModule, batteryModule, bluetoothModule,
            osdModule, privacyModule, calendarModule, shelfModule, quickActionsModule, glanceModule
        ]);

        _islandWindow = new IslandWindow(
            _settingsService,
            _monitorService,
            _activityManager,
            _stateMachine,
            _animator,
            _audioService,
            _fullscreenDetector,
            mediaModule,
            timerModule,
            callModule,
            privacyModule,
            shelfModule,
            quickActionsModule,
            glanceModule,
            osdModule,
            OpenSettingsWindow);

        _islandWindow.Show();

        IntPtr hwnd = _islandWindow.WindowController.Handle;
        _trayService.Initialize(hwnd);
        _trayService.ToggleExpandRequested += (_, _) => _stateMachine.ToggleExpand();
        _trayService.SettingsRequested += (_, _) => OpenSettingsWindow();
        _trayService.PauseToggleRequested += (_, _) => TogglePause();
        _trayService.DiagnosticsToggleRequested += (_, _) => ToggleDiagnosticsOverlay();
        _trayService.ExitRequested += (_, _) => Shutdown();

        _islandWindow.WindowController.TrayMessageReceived += (_, lParam) => _trayService.HandleTrayMessage(lParam);
        _islandWindow.WindowController.HotkeyPressed += (_, _) => _stateMachine.ToggleExpand();
        _islandWindow.WindowController.PowerBroadcastReceived += (_, _) => batteryModule.HandlePowerBroadcast();

        ApplyHotkey(_settingsService.Current.ToggleExpandHotkey);
        _settingsService.SettingsChanged += OnSettingsChanged;

        if (_settingsService.Current.DiagnosticsVisible)
        {
            ShowDiagnosticsOverlay();
        }

        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.BeginInvoke(OpenSettingsWindow);
        }

        var ctx = new ModuleContext
        {
            ActivityManager = _activityManager,
            StateMachine = _stateMachine,
            SettingsService = _settingsService,
            AudioService = _audioService,
            BrightnessService = _brightnessService,
            ImageCache = _imageCache,
            FocusAssist = _focusAssist,
            UiDispatcher = Dispatcher,
            GetWindowHandle = () => _islandWindow?.WindowController.Handle ?? IntPtr.Zero
        };

        // Initialize modules lazily at ApplicationIdle so the island renders immediately
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var module in _modules)
            {
                try
                {
                    await module.InitializeAsync(ctx);
                    proc.Refresh();
                    LoggingService.Instance.Info("App", $"Init {module.ModuleId}: WS={proc.WorkingSet64 / 1048576.0:F1}MB Priv={proc.PrivateMemorySize64 / 1048576.0:F1}MB");
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warn("App", $"Module '{module.ModuleId}' initialization warning.", ex);
                }
            }

            // Reclaim one-time startup/XAML/WinRT initialization allocations
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(proc.Handle, new IntPtr(-1), new IntPtr(-1));
            proc.Refresh();
            LoggingService.Instance.Info("App", $"Atoll startup complete. WS={proc.WorkingSet64 / 1048576.0:F1}MB Priv={proc.PrivateMemorySize64 / 1048576.0:F1}MB");

            // Trim cold one-time WinRT enumeration pages once background watchers settle
            _ = Task.Delay(4000).ContinueWith(_ =>
            {
                try
                {
                    GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
                    using var p = System.Diagnostics.Process.GetCurrentProcess();
                    SetProcessWorkingSetSize(p.Handle, new IntPtr(-1), new IntPtr(-1));
                }
                catch { }
            });
        });
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    private void ScheduleAlertTimer(TimeSpan delay, Action callback)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CancelAlertTimer();
            _pendingAlertAction = callback;
            _alertCollapseTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = delay
            };
            _alertCollapseTimer.Tick += (_, _) =>
            {
                var action = _pendingAlertAction;
                CancelAlertTimer();
                action?.Invoke();
            };
            _alertCollapseTimer.Start();
        });
    }

    private void CancelAlertTimer()
    {
        _alertCollapseTimer?.Stop();
        _alertCollapseTimer = null;
        _pendingAlertAction = null;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ApplyAccentTheme(settings.AccentColorHex);

            if (!string.Equals(_registeredHotkey, settings.ToggleExpandHotkey, StringComparison.OrdinalIgnoreCase))
            {
                ApplyHotkey(settings.ToggleExpandHotkey);
            }

            if (settings.DiagnosticsVisible) ShowDiagnosticsOverlay();
            else HideDiagnosticsOverlay();
        });
    }

    private void ApplyAccentTheme(string hex)
    {
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            color = Color.FromRgb(0x0A, 0x84, 0xFF);
        }

        var primary = new SolidColorBrush(color);
        primary.Freeze();

        var soft = new SolidColorBrush(Color.FromArgb(0x28, color.R, color.G, color.B));
        soft.Freeze();

        Resources["AccentPrimaryBrush"] = primary;
        Resources["AccentSoftBrush"] = soft;
    }

    private void ApplyHotkey(string hotkey)
    {
        if (_hotkeyService is null || _islandWindow is null) return;
        _registeredHotkey = hotkey;
        _hotkeyService.Register(_islandWindow.WindowController.Handle, hotkey);
    }

    private void TogglePause()
    {
        if (_stateMachine is null || _trayService is null) return;
        bool next = !_stateMachine.IsPaused;
        _stateMachine.SetPaused(next);
        _trayService.IsPaused = next;
        _trayService.UpdateTooltip(next ? "Atoll Dynamic Island (Paused)" : "Atoll Dynamic Island");
    }

    private void OpenSettingsWindow()
    {
        if (_settingsService is null || _monitorService is null || _startupService is null) return;

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(
            _settingsService,
            _monitorService,
            _startupService,
            toggleIslandAction: () => _stateMachine?.ToggleExpand(),
            previewAlertAction: TriggerPreviewAlert);
        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void TriggerPreviewAlert()
    {
        if (_stateMachine is null || _settingsService is null) return;
        Color accent;
        try
        {
            accent = (Color)ColorConverter.ConvertFromString(_settingsService.Current.AccentColorHex);
        }
        catch
        {
            accent = Color.FromRgb(0x0A, 0x84, 0xFF);
        }

        _stateMachine.ShowAlert(new PreviewAlertActivity(accent), TimeSpan.FromSeconds(2.6));
    }

    private void ToggleDiagnosticsOverlay()
    {
        if (_settingsService is null) return;
        _settingsService.Update(s => s.DiagnosticsVisible = !s.DiagnosticsVisible);
    }

    private void ShowDiagnosticsOverlay()
    {
        if (_diagnosticsOverlay is not null || _stateMachine is null || _animator is null || _activityManager is null) return;
        _diagnosticsOverlay = new DiagnosticsOverlay(_stateMachine, _animator, _activityManager);
        _diagnosticsOverlay.Closed += (_, _) => _diagnosticsOverlay = null;
        _diagnosticsOverlay.Show();
        if (_trayService is not null) _trayService.DiagnosticsVisible = true;
    }

    private void HideDiagnosticsOverlay()
    {
        if (_diagnosticsOverlay is null) return;
        _diagnosticsOverlay.Close();
        _diagnosticsOverlay = null;
        if (_trayService is not null) _trayService.DiagnosticsVisible = false;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LoggingService.Instance.Error("UnhandledUI", "Recovered from UI exception.", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LoggingService.Instance.Error("UnhandledDomain", "Unhandled AppDomain exception.", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LoggingService.Instance.Warn("UnobservedTask", "Observed background task fault.", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CancelAlertTimer();
        if (_settingsService is not null) _settingsService.SettingsChanged -= OnSettingsChanged;

        foreach (var module in _modules)
        {
            try { module.Dispose(); } catch { }
        }
        _modules.Clear();

        _diagnosticsOverlay?.Close();
        _settingsWindow?.Close();
        _islandWindow?.Close();

        _fullscreenDetector?.Dispose();
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        _brightnessService?.Dispose();
        _audioService?.Dispose();
        _animator?.Dispose();
        _imageCache?.Clear();

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        LoggingService.Instance.Info("App", "Atoll exited cleanly.");
        LoggingService.Instance.Dispose();

        base.OnExit(e);
    }

    private sealed class PreviewAlertActivity : IIslandActivity
    {
        public string Id => "preview-alert";
        public string ModuleId => "atoll";
        public ActivityKind Kind => ActivityKind.Notification;
        public ActivityPriority Priority => ActivityPriority.High;
        public bool SupportsMinimal => false;
        public bool SupportsExpanded => false;
        public bool IsAlertDismissible => true;
        public string Title => "Atoll Dynamic Island";
        public string Subtitle => "Spring morph physics active";
        public string CompactTrailingText => "Live";
        public string Glyph => "\uE73E";
        public Color AccentColor { get; }
        public ImageSource? Thumbnail => null;
        public double? Progress => null;
        public Size PreferredCompactSize => new(206, 36);
        public Size PreferredExpandedSize => new(380, 120);
        public Size PreferredAlertSize => new(346, 74);
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public PreviewAlertActivity(Color accentColor) => AccentColor = accentColor;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public void OnActivated() { }
        public void OnDismissed() { }
    }
}

