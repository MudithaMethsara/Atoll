using System.Net.Http;
using System.Text.Json;
using Atoll.Services;

namespace Atoll.Modules.Glance;

public sealed class GlanceModule : IIslandModule
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private ModuleContext? _ctx;
    private DateTime _lastFetchUtc = DateTime.MinValue;
    private string _cachedWeatherText = string.Empty;
    private bool _disposed;

    public string ModuleId => "glance";
    public string DisplayName => "Weather & Clock Glance";
    public bool IsEnabled { get; private set; }
    public bool IsActive => false;

    public string CurrentTimeText => DateTime.Now.ToString("HH:mm");
    public string CurrentDateText => DateTime.Now.ToString("ddd, MMM d");
    public string WeatherSummary => string.IsNullOrWhiteSpace(_cachedWeatherText) ? "Weather unavailable" : _cachedWeatherText;

    public event EventHandler? GlanceUpdated;

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableWeatherGlance;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        if (!enabled)
        {
            _cachedWeatherText = string.Empty;
            _lastFetchUtc = DateTime.MinValue;
        }
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableWeatherGlance != IsEnabled)
        {
            SetEnabled(settings.EnableWeatherGlance);
        }
    }

    public async Task RefreshOnIdleExpandAsync()
    {
        if (!IsEnabled || _disposed || _ctx is null) return;
        GlanceUpdated?.Invoke(this, EventArgs.Empty);

        if ((DateTime.UtcNow - _lastFetchUtc) < TimeSpan.FromMinutes(30) && !string.IsNullOrWhiteSpace(_cachedWeatherText))
        {
            return;
        }

        await FetchWeatherAsync();
    }

    private async Task FetchWeatherAsync()
    {
        if (_ctx is null) return;
        try
        {
            double lat = 37.7749;
            double lon = -122.4194;
            string city = _ctx.SettingsService.Current.WeatherLocationQuery.Trim();

            if (!string.IsNullOrEmpty(city))
            {
                string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1";
                string geoJson = await Http.GetStringAsync(geoUrl);
                using var geoDoc = JsonDocument.Parse(geoJson);
                if (geoDoc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var first = results[0];
                    lat = first.GetProperty("latitude").GetDouble();
                    lon = first.GetProperty("longitude").GetDouble();
                    city = first.GetProperty("name").GetString() ?? city;
                }
            }
            else
            {
                // Lightweight coarse IP-based location fallback
                string ipJson = await Http.GetStringAsync("https://ipapi.co/json/");
                using var ipDoc = JsonDocument.Parse(ipJson);
                if (ipDoc.RootElement.TryGetProperty("latitude", out var latEl) &&
                    ipDoc.RootElement.TryGetProperty("longitude", out var lonEl))
                {
                    lat = latEl.GetDouble();
                    lon = lonEl.GetDouble();
                    city = ipDoc.RootElement.TryGetProperty("city", out var cEl) ? (cEl.GetString() ?? "Local") : "Local";
                }
            }

            string unit = _ctx.SettingsService.Current.UseCelsius ? "celsius" : "fahrenheit";
            string unitSymbol = _ctx.SettingsService.Current.UseCelsius ? "°C" : "°F";
            string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat:F4}&longitude={lon:F4}&current_weather=true&temperature_unit={unit}";
            string wJson = await Http.GetStringAsync(weatherUrl);
            using var wDoc = JsonDocument.Parse(wJson);
            var current = wDoc.RootElement.GetProperty("current_weather");
            double temp = current.GetProperty("temperature").GetDouble();
            int code = current.GetProperty("weathercode").GetInt32();

            _cachedWeatherText = $"{city} · {Math.Round(temp)}{unitSymbol} · {DescribeWmoCode(code)}";
            _lastFetchUtc = DateTime.UtcNow;

            await _ctx.UiDispatcher.InvokeAsync(() => GlanceUpdated?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Glance", $"Weather fetch failed: {ex.Message}");
        }
    }

    private static string DescribeWmoCode(int code) => code switch
    {
        0 => "Clear",
        1 or 2 or 3 => "Partly Cloudy",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        71 or 73 or 75 => "Snow",
        80 or 81 or 82 => "Showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Cloudy"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ctx is not null)
        {
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}

