using DesktopFamiliar.Services;

namespace DesktopFamiliar.Tests;

public sealed class OverlayLayoutPolicyTests
{
    [Theory]
    [InlineData(120, 240, 240, 60)]
    [InlineData(320, 320, 320, 0)]
    [InlineData(900, 480, 900, 0)]
    public void Layout_SeparatesDialogAndCharacterWidths(
        int characterWidth,
        int expectedDialogWidth,
        int expectedWindowWidth,
        double expectedCharacterOffset)
    {
        Assert.Equal(expectedDialogWidth, OverlayLayoutPolicy.GetDialogWidth(characterWidth));
        Assert.Equal(expectedWindowWidth, OverlayLayoutPolicy.GetWindowWidth(characterWidth));
        Assert.Equal(expectedCharacterOffset, OverlayLayoutPolicy.GetCharacterHorizontalOffset(characterWidth));
    }
}
