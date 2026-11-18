using System.Drawing;
using DesktopFamiliar.Services;

namespace DesktopFamiliar.Tests;

public sealed class OverlayLayoutPolicyTests
{
    [Fact]
    public void PromptSitsAboveThePetWithRoomForSpeechAboveIt()
    {
        var pet = new RectangleF(400, 480, 120, 240);
        var work = new RectangleF(0, 0, 1024, 740);
        var prompt = OverlayLayoutPolicy.PlaceChatPrompt(pet, work, 46, 120);
        Assert.Equal(240, prompt.Width);
        Assert.Equal(460, prompt.Left + prompt.Width / 2);
        Assert.Equal(pet.Top - 6, prompt.Top + 46);
        Assert.Equal(prompt.Top - 6, pet.Top - prompt.DialogueClearance);
    }

    [Theory]
    [InlineData(96, 96)]
    [InlineData(180, 155)]
    public void PromptMovesBelowWhenThereIsTooLittleRoomAboveForBothBubbles(int petTop, int speechHeight)
    {
        var pet = new RectangleF(100, petTop, 240, 200);
        var prompt = OverlayLayoutPolicy.PlaceChatPrompt(pet, new(0, 20, 1024, 720), 46, speechHeight);
        Assert.Equal(pet.Bottom + 6, prompt.Top);
        Assert.Equal(0, prompt.DialogueClearance);
    }

    [Theory]
    [InlineData(-1550, -1500)]
    [InlineData(-540, -716)]
    public void PromptStaysInsideTheSelectedMonitorsWorkArea(int petLeft, int expectedLeft)
    {
        var work = new RectangleF(-1500, -150, 1024, 740);
        var pet = new RectangleF(petLeft, 320, 120, 260);
        var prompt = OverlayLayoutPolicy.PlaceChatPrompt(pet, work, 46, 96);
        Assert.Equal(expectedLeft, prompt.Left);
        Assert.InRange(prompt.Top, work.Top, work.Bottom - 46);
        Assert.True(prompt.Top + 46 < pet.Top);
    }

    [Fact]
    public void PromptRemainsReachableWhenThePetFillsTheWorkArea()
    {
        var work = new RectangleF(20, 30, 200, 160);
        var prompt = OverlayLayoutPolicy.PlaceChatPrompt(work, work, 46, 96);
        Assert.Equal(20, prompt.Left);
        Assert.Equal(200, prompt.Width);
        Assert.Equal(144, prompt.Top);
    }

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
