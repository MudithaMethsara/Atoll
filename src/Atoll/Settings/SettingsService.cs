using System.IO;
using System.Text.Json;
using Atoll.Services;

namespace Atoll.Settings;

public sealed class SettingsService : IDisposable
{
    private readonly string _settingsPath;
    private readonly object _sync = new();
    private System.Threading.Timer? _saveTimer;
    private bool _pendingSave;
    private bool _disposed;

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService(string? customSettingsPath = null)
    {
        _settingsPath = customSettingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Atoll",
            "settings.json");

        Current = Load();
    }

    public AppSettings Load()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                    if (loaded is not null)
                    {
                        ClampValues(loaded);
                        Current = loaded;
                        return Current;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warn("Settings", "Failed to read settings.json; reverting to defaults.", ex);
            }

            Current = new AppSettings();
            SaveInternal(Current);
            return Current;
        }
    }

    public void Update(Action<AppSettings> mutator, bool debounced = false)
    {
        AppSettings snapshot;
        lock (_sync)
        {
            var next = Current.Clone();
            mutator(next);
            ClampValues(next);
            Current = next;
            PersistOrScheduleLocked(debounced);
            snapshot = Current.Clone();
        }

        SettingsChanged?.Invoke(this, snapshot);
    }

    public void Replace(AppSettings updated, bool debounced = false)
    {
        AppSettings snapshot;
        lock (_sync)
        {
            var copy = updated.Clone();
            ClampValues(copy);
            Current = copy;
            PersistOrScheduleLocked(debounced);
            snapshot = Current.Clone();
        }

        SettingsChanged?.Invoke(this, snapshot);
    }

    public void Flush()
    {
        lock (_sync)
        {
            _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            if (_pendingSave)
            {
                _pendingSave = false;
                SaveInternal(Current);
            }
        }
    }

    private void PersistOrScheduleLocked(bool debounced)
    {
        if (!debounced || _disposed)
        {
            _pendingSave = false;
            _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            SaveInternal(Current);
            return;
        }

        _pendingSave = true;
        _saveTimer ??= new System.Threading.Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        _saveTimer.Change(250, Timeout.Infinite);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _saveTimer?.Dispose();
            _saveTimer = null;
            if (_pendingSave)
            {
                _pendingSave = false;
                SaveInternal(Current);
            }
        }
    }

    private static void ClampValues(AppSettings s)
    {
        s.TopOffset = Math.Clamp(s.TopOffset, 0.0, 120.0);
        s.HorizontalOffset = Math.Clamp(s.HorizontalOffset, -1000.0, 1000.0);
        s.SizeScale = Math.Clamp(s.SizeScale, 0.75, 1.40);
        s.AnimationSpeed = Math.Clamp(s.AnimationSpeed, 0.5, 2.0);
        if (string.IsNullOrWhiteSpace(s.AccentColorHex))
        {
            s.AccentColorHex = "#0A84FF";
        }
    }

    private void SaveInternal(AppSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempPath = _settingsPath + ".tmp";
            string json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Error("Settings", "Failed to save settings.json", ex);
        }
    }
}

