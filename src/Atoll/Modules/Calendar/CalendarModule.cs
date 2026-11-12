using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Atoll.Core.Activities;
using Atoll.Services;
using Windows.ApplicationModel.Appointments;

namespace Atoll.Modules.Calendar;

public sealed class CalendarActivity : IslandActivityBase
{
    public override ActivityKind Kind => ActivityKind.Calendar;
    public override Size PreferredCompactSize => new(224, 36);
    public override Size PreferredExpandedSize => new(374, 144);

    public DateTimeOffset EventStart { get; private set; }
    public string Location { get; private set; } = string.Empty;

    internal CalendarActivity() : base("calendar.next", "calendar")
    {
        Priority = ActivityPriority.Normal;
        AccentColor = Color.FromRgb(255, 69, 58);
        Glyph = "\uE787";
    }

    internal void UpdateEvent(string subject, string location, DateTimeOffset start)
    {
        EventStart = start;
        Location = location;
        Title = string.IsNullOrWhiteSpace(subject) ? "Upcoming Event" : subject;
        RefreshCountdown();
    }

    internal void RefreshCountdown()
    {
        var diff = EventStart - DateTimeOffset.Now;
        if (diff.TotalMinutes <= 0)
        {
            CompactTrailingText = "Now";
            Subtitle = string.IsNullOrWhiteSpace(Location) ? "Starting now" : $"Now · {Location}";
        }
        else if (diff.TotalMinutes < 60)
        {
            int mins = Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes));
            CompactTrailingText = $"in {mins}m";
            Subtitle = string.IsNullOrWhiteSpace(Location)
                ? $"Starts in {mins} min ({EventStart:t})"
                : $"In {mins}m · {Location}";
        }
        else
        {
            CompactTrailingText = EventStart.ToString("t");
            Subtitle = string.IsNullOrWhiteSpace(Location)
                ? $"{EventStart:t}"
                : $"{EventStart:t} · {Location}";
        }
    }
}

public sealed class CalendarModule : IIslandModule
{
    private ModuleContext? _ctx;
    private AppointmentStore? _store;
    private CalendarActivity? _activity;
    private DispatcherTimer? _refreshTimer;
    private bool _disposed;

    public string ModuleId => "calendar";
    public string DisplayName => "Calendar Upcoming Event";
    public bool IsEnabled { get; private set; }
    public bool IsActive => _activity is not null && IsEnabled;

    public async Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableCalendar;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;

        if (IsEnabled)
        {
            await QueryNextEventAsync();
            EnsureRefreshTimer();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (enabled)
        {
            _ = QueryNextEventAsync();
            EnsureRefreshTimer();
        }
        else
        {
            StopRefreshTimer();
            _ctx?.ActivityManager.Remove("calendar.next");
            _activity = null;
        }
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableCalendar != IsEnabled)
        {
            SetEnabled(settings.EnableCalendar);
        }
    }

    private void EnsureRefreshTimer()
    {
        if (_ctx is null || _refreshTimer is not null) return;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, _ctx.UiDispatcher)
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        _refreshTimer.Tick += (_, _) => _ = QueryNextEventAsync();
        _refreshTimer.Start();
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    public async Task QueryNextEventAsync()
    {
        if (!IsEnabled || _disposed || _ctx is null) return;

        try
        {
            _store ??= await AppointmentManager
                .RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(3));
            if (_store is null) return;

            var now = DateTimeOffset.Now;
            var options = new FindAppointmentsOptions { MaxCount = 8, IncludeHidden = false };
            options.FetchProperties.Add(AppointmentProperties.Subject);
            options.FetchProperties.Add(AppointmentProperties.Location);
            options.FetchProperties.Add(AppointmentProperties.StartTime);
            options.FetchProperties.Add(AppointmentProperties.Duration);

            var appts = await _store.FindAppointmentsAsync(now.AddMinutes(-5), TimeSpan.FromHours(12), options);
            var next = appts
                .Where(a => !a.AllDay && a.StartTime + a.Duration > now && a.StartTime <= now.AddMinutes(60))
                .OrderBy(a => a.StartTime)
                .FirstOrDefault();

            if (next is null)
            {
                if (_activity is not null)
                {
                    _ctx.ActivityManager.Remove("calendar.next");
                    _activity = null;
                }
                return;
            }

            _activity ??= new CalendarActivity();
            _activity.UpdateEvent(next.Subject, next.Location, next.StartTime);
            _ctx.ActivityManager.RegisterOrUpdate(_activity);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Calendar", $"Appointment store unavailable: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopRefreshTimer();
        if (_ctx is not null)
        {
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}
