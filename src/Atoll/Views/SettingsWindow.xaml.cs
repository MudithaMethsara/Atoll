using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Atoll.Core.Window;
using Atoll.Settings;

namespace Atoll.Views;

public partial class SettingsWindow : System.Windows.Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Win32Interop.EnableDarkWindowFrame(new WindowInteropHelper(this).Handle, rounded: true);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnNavTabChecked(object sender, RoutedEventArgs e)
    {
        if (PageDisplay is null) return;

        PageDisplay.Visibility = sender == NavDisplay ? Visibility.Visible : Visibility.Collapsed;
        PageMotion.Visibility = sender == NavMotion ? Visibility.Visible : Visibility.Collapsed;
        PageModules.Visibility = sender == NavModules ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = sender == NavHotkeys ? Visibility.Visible : Visibility.Collapsed;
        PageSystem.Visibility = sender == NavSystem ? Visibility.Visible : Visibility.Collapsed;
        ContentScroller?.ScrollToTop();
    }

    private void OnAccentSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            _viewModel.AccentColorHex = hex;
        }
    }

    private void OnHotkeyBoxGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyCaptureBox.Text = "Press shortcut (e.g. Ctrl+Alt+Space)...";
    }

    private void OnHotkeyBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyCaptureBox.Text = _viewModel.ToggleExpandHotkey;
    }

    private void OnHotkeyBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.DeadCharProcessed)
        {
            return;
        }

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        bool isFunctionKey = key >= Key.F1 && key <= Key.F24;
        if (mods == ModifierKeys.None && !isFunctionKey)
        {
            return;
        }

        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());

        string shortcut = string.Join("+", parts);
        if (Services.GlobalHotkeyService.TryParseHotkey(shortcut, out _, out _))
        {
            _viewModel.ToggleExpandHotkey = shortcut;
            HotkeyCaptureBox.Text = shortcut;
            Keyboard.ClearFocus();
        }
    }

    private void OnResetHotkeyClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleExpandHotkey = "Ctrl+Alt+Space";
        HotkeyCaptureBox.Text = _viewModel.ToggleExpandHotkey;
    }

    private void OnToggleIslandClick(object sender, RoutedEventArgs e) => _viewModel.ToggleIslandPreview();

    private void OnPreviewAlertClick(object sender, RoutedEventArgs e) => _viewModel.TriggerSampleAlert();

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetDefaults();
        HotkeyCaptureBox.Text = _viewModel.ToggleExpandHotkey;
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = _viewModel.LogDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell launch error if Explorer is restricted.
        }
    }
}
