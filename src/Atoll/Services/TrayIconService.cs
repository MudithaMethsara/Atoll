using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Atoll.Core.Window;

namespace Atoll.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint TrayIconId = 1001;
    private const uint CmdToggleExpand = 1;
    private const uint CmdSettings = 2;
    private const uint CmdPause = 3;
    private const uint CmdDiagnostics = 4;
    private const uint CmdExit = 5;

    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _added;
    private bool _disposed;

    public bool IsPaused { get; set; }
    public bool DiagnosticsVisible { get; set; }

    public event EventHandler? ToggleExpandRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? PauseToggleRequested;
    public event EventHandler? DiagnosticsToggleRequested;
    public event EventHandler? ExitRequested;

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _hIcon = LoadTrayIconFromResources() ?? CreateDynamicIslandTrayIcon();

        var data = BuildNotifyIconData();
        data.uFlags = Win32Interop.NIF_MESSAGE | Win32Interop.NIF_ICON | Win32Interop.NIF_TIP;
        data.uCallbackMessage = Win32Interop.WM_TRAYICON;
        data.hIcon = _hIcon;
        data.szTip = "Atoll Dynamic Island";

        _added = Win32Interop.Shell_NotifyIcon(Win32Interop.NIM_ADD, ref data);
    }

    public void UpdateTooltip(string tooltip)
    {
        if (!_added || _disposed) return;
        var data = BuildNotifyIconData();
        data.uFlags = Win32Interop.NIF_TIP;
        data.szTip = tooltip.Length > 120 ? tooltip[..120] : tooltip;
        Win32Interop.Shell_NotifyIcon(Win32Interop.NIM_MODIFY, ref data);
    }

    public void HandleTrayMessage(int lParam)
    {
        if (_disposed) return;
        int msg = lParam & 0xFFFF;
        if (msg == Win32Interop.WM_LBUTTONDBLCLK || msg == Win32Interop.WM_LBUTTONUP)
        {
            ToggleExpandRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (msg == Win32Interop.WM_RBUTTONUP)
        {
            ShowContextMenu();
        }
    }

    private void ShowContextMenu()
    {
        IntPtr hMenu = Win32Interop.CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            Win32Interop.AppendMenu(hMenu, Win32Interop.MF_STRING, (UIntPtr)CmdToggleExpand, "Toggle Island");
            Win32Interop.AppendMenu(hMenu, Win32Interop.MF_STRING, (UIntPtr)CmdSettings, "Settings...");
            Win32Interop.AppendMenu(hMenu, Win32Interop.MF_STRING | (IsPaused ? Win32Interop.MF_CHECKED : 0u), (UIntPtr)CmdPause, IsPaused ? "Resume Island" : "Pause Island");
            Win32Interop.AppendMenu(hMenu, Win32Interop.MF_STRING | (DiagnosticsVisible ? Win32Interop.MF_CHECKED : 0u), (UIntPtr)CmdDiagnostics, "Diagnostics Overlay");
            Win32Interop.AppendMenu(hMenu, Win32Interop.MF_SEPARATOR, UIntPtr.Zero, null);
            Win32Interop.AppendMenu(hMenu, Win32Interop.MF_STRING, (UIntPtr)CmdExit, "Exit Atoll");

            Win32Interop.GetCursorPos(out var pt);
            Win32Interop.SetForegroundWindow(_hwnd);
            uint selected = Win32Interop.TrackPopupMenuEx(
                hMenu,
                Win32Interop.TPM_RETURNCMD | Win32Interop.TPM_RIGHTBUTTON,
                pt.X,
                pt.Y,
                _hwnd,
                IntPtr.Zero);

            switch (selected)
            {
                case CmdToggleExpand:
                    ToggleExpandRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdSettings:
                    SettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdPause:
                    PauseToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdDiagnostics:
                    DiagnosticsToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CmdExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            Win32Interop.DestroyMenu(hMenu);
        }
    }

    private Win32Interop.NOTIFYICONDATAW BuildNotifyIconData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32Interop.NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = TrayIconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    private static IntPtr? LoadTrayIconFromResources()
    {
        try
        {
            var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/Atoll.ico"));
            if (streamInfo?.Stream is null) return null;

            using var resStream = streamInfo.Stream;
            using var ms = new MemoryStream();
            resStream.CopyTo(ms);
            byte[] ico = ms.ToArray();
            if (ico.Length < 22) return null;

            int count = BitConverter.ToUInt16(ico, 4);
            int targetSize = Math.Max(16, GetSystemMetrics(49 /* SM_CXSMICON */));

            int bestOffset = -1;
            int bestSize = 0;
            int bestDiff = int.MaxValue;
            int bestActualSize = 32;

            for (int i = 0; i < count; i++)
            {
                int entry = 6 + i * 16;
                if (entry + 16 > ico.Length) break;

                int w = ico[entry] == 0 ? 256 : ico[entry];
                int bytesInRes = BitConverter.ToInt32(ico, entry + 8);
                int imgOffset = BitConverter.ToInt32(ico, entry + 12);
                if (imgOffset < 0 || imgOffset + bytesInRes > ico.Length) continue;

                int diff = Math.Abs(w - targetSize);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestOffset = imgOffset;
                    bestSize = bytesInRes;
                    bestActualSize = w;
                }
            }

            if (bestOffset < 0 || bestSize <= 0) return null;

            byte[] frame = new byte[bestSize];
            Buffer.BlockCopy(ico, bestOffset, frame, 0, bestSize);
            IntPtr hIcon = CreateIconFromResourceEx(frame, (uint)frame.Length, true, 0x00030000, bestActualSize, bestActualSize, 0);
            return hIcon != IntPtr.Zero ? hIcon : null;
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr CreateDynamicIslandTrayIcon()
    {
        const int size = 32;
        byte[] bgra = new byte[size * size * 4];

        // Draw a sleek pill capsule with subtle white/blue highlight
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                // Pill centered vertically: x in [2..29], y in [9..22], corner radius = 6.5
                double cx = Math.Clamp(x, 8.5, 22.5);
                double cy = 15.5;
                double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

                if (dist <= 6.2)
                {
                    // Inner capsule
                    bool isBorder = dist >= 5.1;
                    bool isCameraDot = ((x - 21.5) * (x - 21.5) + (y - 15.5) * (y - 15.5)) <= 3.2;
                    if (isBorder)
                    {
                        bgra[idx] = 220;     // B
                        bgra[idx + 1] = 220; // G
                        bgra[idx + 2] = 220; // R
                        bgra[idx + 3] = 235; // A
                    }
                    else if (isCameraDot)
                    {
                        bgra[idx] = 255;     // B
                        bgra[idx + 1] = 140; // G
                        bgra[idx + 2] = 35;  // R
                        bgra[idx + 3] = 255; // A
                    }
                    else
                    {
                        bgra[idx] = 10;
                        bgra[idx + 1] = 10;
                        bgra[idx + 2] = 10;
                        bgra[idx + 3] = 255;
                    }
                }
            }
        }

        IntPtr hBitmap = CreateBitmap(size, size, 1, 32, bgra);
        IntPtr hMask = CreateBitmap(size, size, 1, 1, new byte[size * size / 8]);

        var iconInfo = new ICONINFO
        {
            fIcon = true,
            hbmMask = hMask,
            hbmColor = hBitmap
        };

        IntPtr hIcon = CreateIconIndirect(ref iconInfo);
        DeleteObject(hBitmap);
        DeleteObject(hMask);
        return hIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, byte[] lpvBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] pbIconBits, uint cbIconBits, bool fIcon, uint dwVersion, int cxDesired, int cyDesired, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var data = BuildNotifyIconData();
            Win32Interop.Shell_NotifyIcon(Win32Interop.NIM_DELETE, ref data);
            _added = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            Win32Interop.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}

