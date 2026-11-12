using System.Windows.Input;
using Atoll.Core.Window;

namespace Atoll.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4154; // "AT"
    private IntPtr _hwnd;
    private bool _registered;
    private bool _disposed;

    public bool Register(IntPtr hwnd, string hotkeyExpression)
    {
        Unregister();
        _hwnd = hwnd;

        if (_hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(hotkeyExpression)) return false;

        if (!TryParseHotkey(hotkeyExpression, out uint modifiers, out uint vk))
        {
            LoggingService.Instance.Warn("Hotkey", $"Invalid hotkey expression '{hotkeyExpression}'.");
            return false;
        }

        _registered = Win32Interop.RegisterHotKey(_hwnd, HotkeyId, modifiers, vk);
        if (!_registered)
        {
            LoggingService.Instance.Warn("Hotkey", $"Failed to register global hotkey '{hotkeyExpression}'.");
        }
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            Win32Interop.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    public static bool TryParseHotkey(string expr, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        string[] parts = expr.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length; i++)
        {
            string token = parts[i];
            if (i < parts.Length - 1)
            {
                switch (token.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= 0x0002;
                        break;
                    case "ALT":
                        modifiers |= 0x0001;
                        break;
                    case "SHIFT":
                        modifiers |= 0x0004;
                        break;
                    case "WIN":
                    case "WINDOWS":
                        modifiers |= 0x0008;
                        break;
                    default:
                        return false;
                }
            }
            else
            {
                if (Enum.TryParse<Key>(token, ignoreCase: true, out var key))
                {
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                }
                else
                {
                    return false;
                }
            }
        }

        return vk != 0;
    }

    public int RegisteredHotkeyId => HotkeyId;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}

