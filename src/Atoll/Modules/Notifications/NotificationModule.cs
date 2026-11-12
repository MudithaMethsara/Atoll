using System.IO;
using System.Windows;
using System.Windows.Media;
using Atoll.Core.Activities;
using Atoll.Services;
using Windows.Foundation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Atoll.Modules.Notifications;

public sealed class NotificationActivity : IslandActivityBase
{
    private readonly NotificationModule _owner;

    public override ActivityKind Kind => ActivityKind.Notification;
    public override System.Windows.Size PreferredCompactSize => new(230, 36);
    public override System.Windows.Size PreferredExpandedSize => new(390, 152);
    public override System.Windows.Size PreferredAlertSize => new(364, 86);

    public uint NotificationId { get; }
    public string AppName { get; }
    public string BodyText { get; }

    internal NotificationActivity(
        NotificationModule owner,
        uint notificationId,
        string appName,
        string title,
        string body,
        ImageSource? icon)
        : base($"notif.{notificationId}", "notifications")
    {
        _owner = owner;
        NotificationId = notificationId;
        AppName = appName;
        BodyText = body;
        Title = string.IsNullOrWhiteSpace(title) ? appName : title;
        Subtitle = string.IsNullOrWhiteSpace(body) ? appName : body;
        CompactTrailingText = appName;
        Thumbnail = icon;
        Glyph = "\uEA8F";
        Priority = ActivityPriority.High;
        AccentColor = Color.FromRgb(10, 132, 255);
    }

    public override void OnDismissed()
    {
        _owner.DismissNotificationFromSystem(NotificationId);
    }
}

public sealed class NotificationModule : IIslandModule
{
    private ModuleContext? _ctx;
    private UserNotificationListener? _listener;
    private readonly HashSet<uint> _seenIds = new();
    private bool _disposed;

    public string ModuleId => "notifications";
    public string DisplayName => "System Notifications";
    public bool IsEnabled { get; private set; }
    public bool IsActive => false;
    public bool IsListenerAccessGranted { get; private set; }

    public async Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableNotifications;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;

        if (IsEnabled)
        {
            await AttachListenerAsync();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (enabled)
        {
            _ = AttachListenerAsync();
        }
        else
        {
            DetachListener();
            _ctx?.ActivityManager.RemoveByModule(ModuleId);
        }
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableNotifications != IsEnabled)
        {
            SetEnabled(settings.EnableNotifications);
        }
    }

    private async Task AttachListenerAsync()
    {
        try
        {
            _listener ??= UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            IsListenerAccessGranted = status == UserNotificationListenerAccessStatus.Allowed;

            if (!IsListenerAccessGranted)
            {
                LoggingService.Instance.Info("Notifications", $"UserNotificationListener access status: {status}. Unpackaged apps may require Settings/MSIX identity.");
                return;
            }

            var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in existing)
            {
                _seenIds.Add(n.Id);
            }

            _listener.NotificationChanged += OnNotificationChanged;
        }
        catch (Exception ex)
        {
            IsListenerAccessGranted = false;
            LoggingService.Instance.Warn("Notifications", "UserNotificationListener unavailable; graceful degradation active.", ex);
        }
    }

    private void DetachListener()
    {
        if (_listener is not null)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (!IsEnabled || _disposed || _ctx is null) return;
        if (args.ChangeKind == UserNotificationChangedKind.Removed)
        {
            _ctx.UiDispatcher.BeginInvoke(() => _ctx.ActivityManager.Remove($"notif.{args.UserNotificationId}"));
            return;
        }

        _ctx.UiDispatcher.BeginInvoke(() => _ = HandleAddedNotificationAsync(args.UserNotificationId));
    }

    private async Task HandleAddedNotificationAsync(uint notificationId)
    {
        if (!IsEnabled || _disposed || _ctx is null || _listener is null) return;
        if (!_seenIds.Add(notificationId)) return;

        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var notif = list.FirstOrDefault(n => n.Id == notificationId);
            if (notif is null) return;

            string appName = notif.AppInfo?.DisplayInfo?.DisplayName ?? "Notification";
            var binding = notif.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var textElements = binding?.GetTextElements().Select(t => t.Text).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                ?? new List<string>();

            string title = textElements.Count > 0 ? textElements[0] : appName;
            string body = textElements.Count > 1 ? string.Join(" — ", textElements.Skip(1)) : string.Empty;

            ImageSource? logo = null;
            try
            {
                var logoRef = notif.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
                if (logoRef is not null)
                {
                    using var stream = await logoRef.OpenReadAsync();
                    using var netStream = stream.AsStreamForRead();
                    using var ms = new MemoryStream();
                    await netStream.CopyToAsync(ms);
                    logo = _ctx.ImageCache.GetOrDecode(ms.ToArray(), $"app.{appName}")?.Image;
                }
            }
            catch
            {
                // Logo optional
            }

            var activity = new NotificationActivity(this, notificationId, appName, title, body, logo);
            _ctx.StateMachine.ShowAlert(activity, TimeSpan.FromSeconds(4.5));
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Notifications", $"Failed processing notification {notificationId}: {ex.Message}");
        }
    }

    internal void DismissNotificationFromSystem(uint id)
    {
        try
        {
            if (IsListenerAccessGranted && _listener is not null)
            {
                _listener.RemoveNotification(id);
            }
        }
        catch
        {
            // Ignore
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachListener();
        if (_ctx is not null)
        {
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}

