using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Atoll.Core.Activities;

namespace Atoll.Modules.Shelf;

public sealed record ShelfFileItem(string Name, string FullPath, string SizeDisplay);

public sealed class FileShelfActivity : IslandActivityBase
{
    private readonly FileShelfModule _owner;

    public override ActivityKind Kind => ActivityKind.FileShelf;
    public override Size PreferredCompactSize => new(206, 36);
    public override Size PreferredExpandedSize => new(392, 184);

    public ObservableCollection<ShelfFileItem> Items => _owner.Items;

    internal FileShelfActivity(FileShelfModule owner) : base("shelf.files", "shelf")
    {
        _owner = owner;
        Priority = ActivityPriority.Normal;
        AccentColor = Color.FromRgb(94, 92, 230);
        Glyph = "\uE8B7";
        Title = "File Shelf";
    }

    internal void RefreshCount()
    {
        int count = Items.Count;
        Subtitle = count == 1 ? Items[0].Name : $"{count} items ready to drag out";
        CompactTrailingText = count.ToString();
    }

    public void RemoveItem(string fullPath) => _owner.RemoveFile(fullPath);
    public void ClearAll() => _owner.Clear();
    public string[] GetExistingPaths() => _owner.GetExistingPaths();
}

public sealed class FileShelfModule : IIslandModule
{
    private ModuleContext? _ctx;
    private FileShelfActivity? _activity;
    private bool _disposed;

    public string ModuleId => "shelf";
    public string DisplayName => "File Shelf (Drag & Drop)";
    public bool IsEnabled { get; private set; }
    public bool IsActive => Items.Count > 0 && IsEnabled;

    public ObservableCollection<ShelfFileItem> Items { get; } = new();

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableFileShelf;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (!enabled) Clear();
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableFileShelf != IsEnabled)
        {
            SetEnabled(settings.EnableFileShelf);
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        if (!IsEnabled || _ctx is null) return;

        bool added = false;
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            if (Items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase))) continue;

            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = path;

            string sizeText = Directory.Exists(path)
                ? "Folder"
                : FormatBytes(new FileInfo(path).Length);

            Items.Add(new ShelfFileItem(name, path, sizeText));
            added = true;
        }

        if (!added) return;

        _activity ??= new FileShelfActivity(this);
        _activity.RefreshCount();
        _ctx.ActivityManager.RegisterOrUpdate(_activity);
        _ctx.StateMachine.Expand();
    }

    public void RemoveFile(string fullPath)
    {
        var match = Items.FirstOrDefault(i => string.Equals(i.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        Items.Remove(match);

        if (Items.Count == 0)
        {
            _ctx?.ActivityManager.Remove("shelf.files");
            _activity = null;
        }
        else
        {
            _activity?.RefreshCount();
        }
    }

    public void Clear()
    {
        Items.Clear();
        _ctx?.ActivityManager.Remove("shelf.files");
        _activity = null;
    }

    public string[] GetExistingPaths()
    {
        return Items
            .Select(i => i.FullPath)
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToArray();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

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

