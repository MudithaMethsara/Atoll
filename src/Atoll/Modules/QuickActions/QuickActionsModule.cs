using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Atoll.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Atoll.Modules.QuickActions;

public sealed record ClipboardPreviewItem(string Id, string PreviewText, string FullText);

public sealed class QuickActionsModule : IIslandModule
{
    private ModuleContext? _ctx;
    private bool _disposed;

    public string ModuleId => "quickactions";
    public string DisplayName => "Quick Actions (Clipboard, Screenshot, DND)";
    public bool IsEnabled { get; private set; }
    public bool IsActive => false;

    public bool IsDndEnabled { get; private set; }
    public ObservableCollection<ClipboardPreviewItem> RecentClipboardItems { get; } = new();

    public event EventHandler? StateUpdated;

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableQuickActions;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;
        IsDndEnabled = _ctx.FocusAssist.IsDoNotDisturbEnabled();
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableQuickActions != IsEnabled)
        {
            SetEnabled(settings.EnableQuickActions);
        }
    }

    public async Task RefreshOnExpandAsync()
    {
        if (!IsEnabled || _ctx is null || _disposed) return;
        IsDndEnabled = _ctx.FocusAssist.IsDoNotDisturbEnabled();
        await LoadClipboardHistoryAsync();
        StateUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleDoNotDisturb()
    {
        if (_ctx is null) return;
        IsDndEnabled = _ctx.FocusAssist.ToggleDoNotDisturb();
        StateUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void TriggerScreenshot()
    {
        _ctx?.StateMachine.Collapse();
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("SnippingTool.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warn("QuickActions", "Failed launching screenshot tool.", ex);
            }
        }
    }

    public void CopyClipboardItem(ClipboardPreviewItem item)
    {
        try
        {
            System.Windows.Clipboard.SetText(item.FullText);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("QuickActions", $"Clipboard write failed: {ex.Message}");
        }
    }

    private async Task LoadClipboardHistoryAsync()
    {
        try
        {
            if (!Windows.ApplicationModel.DataTransfer.Clipboard.IsHistoryEnabled())
            {
                RecentClipboardItems.Clear();
                return;
            }

            var result = await Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync();
            if (result.Status != ClipboardHistoryItemsResultStatus.Success) return;

            var list = new List<ClipboardPreviewItem>();
            foreach (var item in result.Items.Take(5))
            {
                if (!item.Content.Contains(StandardDataFormats.Text)) continue;
                string text = await item.Content.GetTextAsync();
                if (string.IsNullOrWhiteSpace(text)) continue;

                string singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                string preview = singleLine.Length > 42 ? singleLine[..42] + "…" : singleLine;
                list.Add(new ClipboardPreviewItem(item.Id, preview, text));
            }

            RecentClipboardItems.Clear();
            foreach (var entry in list) RecentClipboardItems.Add(entry);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("QuickActions", $"Clipboard history unavailable: {ex.Message}");
        }
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

