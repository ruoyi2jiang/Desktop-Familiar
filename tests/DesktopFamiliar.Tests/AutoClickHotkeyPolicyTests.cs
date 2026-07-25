using DesktopFamiliar.Addons.AutoClicker.Services;

namespace DesktopFamiliar.Tests;

public sealed class AutoClickHotkeyPolicyTests
{
    [Fact]
    public void Parse_NormalizesModifierOrderAndFunctionKeys()
    {
        var parsed = AutoClickHotkeyPolicy.TryParse("shift+alt+ctrl+F12", out var hotkey);

        Assert.True(parsed);
        Assert.Equal("Ctrl+Alt+Shift+F12", hotkey.DisplayText);
        Assert.Equal((uint)0x7B, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Unsupported")]
    public void Parse_RejectsUnsafeOrUnsupportedBindings(string value)
    {
        Assert.False(AutoClickHotkeyPolicy.TryParse(value, out _));
    }

    [Fact]
    public void Create_RequiresAProtectiveModifier()
    {
        var created = AutoClickHotkeyPolicy.TryCreate(0, 'C', out _);

        Assert.False(created);
    }
}
