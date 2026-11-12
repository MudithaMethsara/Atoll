using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Atoll.Core.Activities;

public abstract class IslandActivityBase : IIslandActivity
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _compactTrailingText = string.Empty;
    private string _glyph = "\uE946";
    private Color _accentColor = Color.FromRgb(10, 132, 255);
    private ImageSource? _thumbnail;
    private double? _progress;
    private ActivityPriority _priority = ActivityPriority.Normal;

    public string Id { get; }
    public string ModuleId { get; }
    public abstract ActivityKind Kind { get; }
    public virtual bool SupportsMinimal => true;
    public virtual bool SupportsExpanded => true;
    public virtual bool IsAlertDismissible => true;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public ActivityPriority Priority
    {
        get => _priority;
        protected set => SetField(ref _priority, value);
    }

    public string Title
    {
        get => _title;
        protected set => SetField(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        protected set => SetField(ref _subtitle, value);
    }

    public string CompactTrailingText
    {
        get => _compactTrailingText;
        protected set => SetField(ref _compactTrailingText, value);
    }

    public string Glyph
    {
        get => _glyph;
        protected set => SetField(ref _glyph, value);
    }

    public Color AccentColor
    {
        get => _accentColor;
        protected set => SetField(ref _accentColor, value);
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        protected set => SetField(ref _thumbnail, value);
    }

    public double? Progress
    {
        get => _progress;
        protected set => SetField(ref _progress, value);
    }

    public virtual Size PreferredCompactSize => new(220, 36);
    public virtual Size PreferredExpandedSize => new(380, 168);
    public virtual Size PreferredAlertSize => new(336, 78);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected IslandActivityBase(string id, string moduleId)
    {
        Id = id;
        ModuleId = moduleId;
    }

    public virtual void OnActivated() { }
    public virtual void OnDismissed() { }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ActivityManager
{
    private readonly List<IIslandActivity> _activities = new();
    private string? _pinnedPrimaryId;

    public IReadOnlyList<IIslandActivity> Activities => _activities;
    public IIslandActivity? PrimaryActivity => GetPrimary();
    public IIslandActivity? SecondaryActivity => GetSecondary();

    public event EventHandler? ActivitiesChanged;

    public void RegisterOrUpdate(IIslandActivity activity)
    {
        int existingIndex = _activities.FindIndex(a => string.Equals(a.Id, activity.Id, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            _activities[existingIndex] = activity;
        }
        else
        {
            _activities.Add(activity);
        }

        SortActivities();
        ActivitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string activityId)
    {
        int index = _activities.FindIndex(a => string.Equals(a.Id, activityId, StringComparison.Ordinal));
        if (index < 0) return false;

        var removed = _activities[index];
        _activities.RemoveAt(index);

        if (string.Equals(_pinnedPrimaryId, activityId, StringComparison.Ordinal))
        {
            _pinnedPrimaryId = null;
        }

        removed.OnDismissed();
        ActivitiesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RemoveByModule(string moduleId)
    {
        bool changed = false;
        for (int i = _activities.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_activities[i].ModuleId, moduleId, StringComparison.Ordinal))
            {
                var removed = _activities[i];
                _activities.RemoveAt(i);
                if (string.Equals(_pinnedPrimaryId, removed.Id, StringComparison.Ordinal))
                {
                    _pinnedPrimaryId = null;
                }
                removed.OnDismissed();
                changed = true;
            }
        }

        if (changed)
        {
            ActivitiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CycleNext()
    {
        if (_activities.Count <= 1) return;
        var current = PrimaryActivity;
        int idx = current is null ? -1 : _activities.IndexOf(current);
        int nextIdx = (idx + 1) % _activities.Count;
        _pinnedPrimaryId = _activities[nextIdx].Id;
        ActivitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CyclePrevious()
    {
        if (_activities.Count <= 1) return;
        var current = PrimaryActivity;
        int idx = current is null ? 0 : _activities.IndexOf(current);
        int prevIdx = (idx - 1 + _activities.Count) % _activities.Count;
        _pinnedPrimaryId = _activities[prevIdx].Id;
        ActivitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Promote(string activityId)
    {
        if (_activities.Any(a => string.Equals(a.Id, activityId, StringComparison.Ordinal)))
        {
            _pinnedPrimaryId = activityId;
            ActivitiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IIslandActivity? GetPrimary()
    {
        if (_activities.Count == 0) return null;
        if (!string.IsNullOrEmpty(_pinnedPrimaryId))
        {
            var pinned = _activities.FirstOrDefault(a => string.Equals(a.Id, _pinnedPrimaryId, StringComparison.Ordinal));
            if (pinned is not null) return pinned;
        }
        return _activities[0];
    }

    private IIslandActivity? GetSecondary()
    {
        if (_activities.Count <= 1) return null;
        var primary = PrimaryActivity;
        return _activities.FirstOrDefault(a => a != primary && a.SupportsMinimal)
            ?? _activities.FirstOrDefault(a => a != primary);
    }

    private void SortActivities()
    {
        _activities.Sort((a, b) =>
        {
            int prio = b.Priority.CompareTo(a.Priority);
            return prio != 0 ? prio : b.CreatedAt.CompareTo(a.CreatedAt);
        });
    }
}

