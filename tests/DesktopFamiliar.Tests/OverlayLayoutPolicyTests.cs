using System.Drawing;
using DesktopFamiliar.Services;

namespace DesktopFamiliar.Tests;

public sealed class OverlayLayoutPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningSendingAndClosingChatKeepTheDrawingWindowAndCharacterAnchor(bool hasModel)
    {
        var desktop = new RectangleF(-800, 0, 1600, 900);
        var pet = new RectangleF(-500, 480, 240, 240);
        var extent = RectangleF.Empty;
        var restingWindow = RectangleF.Empty;
        // Plain prompt, sent receipt and reply, closed prompt, then a second exchange.
        foreach (var dialogue in new[] { 96, 110, 285, 96, 285 })
        {
            extent = OverlayLayoutPolicy.ReserveDrawingExtent(extent, 240, 240, dialogue, hasModel);
            var window = OverlayLayoutPolicy.GetDrawingWindow(pet, dialogue, extent, desktop);
            if (restingWindow.IsEmpty) restingWindow = window;
            Assert.Equal(restingWindow, window);
            Assert.True(window.Contains(new RectangleF(pet.X, pet.Y - dialogue, pet.Width, pet.Height + dialogue)));
        }
    }

    [Fact]
    public void ExtraSpaceForLongDialogueAndTravelIsReusedAcrossSceneChanges()
    {
        var desktop = new RectangleF(-1000, -100, 2000, 1000);
        var pet = new RectangleF(-250, 560, 200, 220);
        var extent = OverlayLayoutPolicy.ReserveDrawingExtent(RectangleF.Empty, 200, 220, 96, true);
        var rest = new RectangleF(10, 8, 170, 200);
        var dance = new RectangleF(-320, -140, 750, 420);
        var growth = OverlayLayoutPolicy.ExpandModelExtent(extent, dance);
        Assert.NotEqual(extent, growth);
        growth = OverlayLayoutPolicy.ReserveDrawingExtent(growth, 200, 220, 450, true);
        var expanded = OverlayLayoutPolicy.GetDrawingWindow(pet, 450, growth, desktop);
        Assert.True(expanded.Contains(pet.X + dance.Left, pet.Y + dance.Top));
        Assert.True(expanded.Contains(pet.X, pet.Y - 450));
        foreach (var pose in new[] { rest, dance, rest })
        {
            growth = OverlayLayoutPolicy.ExpandModelExtent(growth, pose);
            growth = OverlayLayoutPolicy.ReserveDrawingExtent(growth, 200, 220, 96, true);
            Assert.Equal(expanded, OverlayLayoutPolicy.GetDrawingWindow(pet, 96, growth, desktop));
        }
        var besideEdge = new RectangleF(desktop.Left + 20, desktop.Top + 96, 200, 220);
        var clipped = OverlayLayoutPolicy.GetDrawingWindow(besideEdge, 96, growth, desktop);
        Assert.True(desktop.Contains(clipped));
        Assert.True(clipped.Contains(besideEdge));
    }

    [Fact]
    public void TravelExpandsBehindDialogueAndStopsAtTheDesktopEdges()
    {
        var desktop = new RectangleF(-1000, -100, 1000, 800);
        var pet = new RectangleF(-820, 40, 120, 180);
        var rest = new RectangleF(0, 0, 120, 180);
        var logical = OverlayLayoutPolicy.GetDrawingWindow(pet, 96, RectangleF.Empty, desktop);
        Assert.Equal(new RectangleF(-880, -56, 240, 276), logical);
        Assert.Equal(logical, OverlayLayoutPolicy.GetDrawingWindow(pet, 96, rest, desktop));
        var extent = OverlayLayoutPolicy.ExpandModelExtent(rest, new(-400, -240, 600, 580));
        Assert.Equal(extent, OverlayLayoutPolicy.ExpandModelExtent(extent, rest));
        var expanded = OverlayLayoutPolicy.GetDrawingWindow(pet, 96, extent, desktop);
        Assert.Equal(desktop.Left, expanded.Left);
        Assert.Equal(desktop.Top, expanded.Top);
        Assert.True(expanded.Right > logical.Right && expanded.Bottom > logical.Bottom);
        // Dialogue and pet live inside the same surface, and its bounds depend only on visible content.
        Assert.Equal(expanded, OverlayLayoutPolicy.GetDrawingWindow(pet, 130, extent, desktop));
    }

    [Fact]
    public void PromptSitsAboveThePetWithRoomForSpeechAboveIt()
    {
        var pet = new RectangleF(400, 480, 120, 240);
        var work = new RectangleF(0, 0, 1024, 740);
        var prompt = OverlayLayoutPolicy.PlaceChatPrompt(pet, work, 46, 120);
        Assert.Equal(520, prompt.Width);
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
    [InlineData(-540, -996)]
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
    [InlineData(240, 1024, 520)]
    [InlineData(480, 1024, 520)]
    [InlineData(580, 1024, 580)]
    [InlineData(900, 1024, 640)]
    [InlineData(900, 600, 600)]
    [InlineData(240, 360, 360)]
    public void PromptProvidesTypingRoomWithinTheAvailableWidth(int petWidth, int workWidth, int expectedWidth)
    {
        var work = new RectangleF(0, 0, workWidth, 800);
        var pet = new RectangleF((workWidth - petWidth) / 2f, 450, petWidth, 200);
        var prompt = OverlayLayoutPolicy.PlaceChatPrompt(pet, work, 58, 96);
        Assert.Equal(expectedWidth, prompt.Width);
        Assert.Equal(workWidth / 2d, prompt.Left + prompt.Width / 2);
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
