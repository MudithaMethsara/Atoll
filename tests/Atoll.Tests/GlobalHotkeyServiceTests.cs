using Atoll.Services;
using Xunit;

namespace Atoll.Tests;

public sealed class GlobalHotkeyServiceTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", 0x0003u, 0x20u)]
    [InlineData("Win+Shift+D", 0x000Cu, 0x44u)]
    [InlineData("Control+F12", 0x0002u, 0x7Bu)]
    public void TryParseHotkey_ParsesValidCombinations(string expr, uint expectedMods, uint expectedVk)
    {
        bool ok = GlobalHotkeyService.TryParseHotkey(expr, out uint mods, out uint vk);

        Assert.True(ok);
        Assert.Equal(expectedMods, mods);
        Assert.Equal(expectedVk, vk);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hyper+Space")]
    [InlineData("Ctrl+NotARealKey")]
    public void TryParseHotkey_RejectsInvalidExpressions(string expr)
    {
        bool ok = GlobalHotkeyService.TryParseHotkey(expr, out _, out uint vk);
        Assert.False(ok);
        Assert.Equal(0u, vk);
    }
}

