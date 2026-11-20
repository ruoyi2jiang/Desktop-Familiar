using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.Voice;

namespace DesktopFamiliar.Tests;

public sealed class SceneDialogueVoiceTests
{
    [Theory]
    [InlineData("Welcome!", null, "", false, true)]
    [InlineData("Welcome!", "", "kokoro-en-v019", false, true)]
    [InlineData("Welcome!", "custom.wav", "kokoro-en-v019", false, false)]
    [InlineData("", "custom.wav", "", false, false)]
    [InlineData(" ", null, "", false, false)]
    [InlineData("Welcome!", null, "off", false, false)]
    [InlineData("Welcome!", null, "kokoro-en-v019", true, false)]
    public void OnlyUnvoicedTextUsesThePetVoice(string text, string? recording, string id, bool listening, bool expected)
    {
        var line = new DialogLine { Text = text, VoicePath = recording };
        Assert.Equal(expected, ScenePlaybackPolicy.ShouldUseProfileVoice(line, new() { Id = id }, listening));
    }

    [Fact]
    public async Task LineWaitsForAudioAndUsesTheSelectedPetsTuning()
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var petVoice = new CompanionVoice { Id = "kokoro-en-v019", Speaker = 4, Rate = 2 };
        var voice = new VoicePlayback((text, settings, token) =>
        {
            Assert.Equal("Welcome back, Sam!", text);
            Assert.Equal(petVoice, settings);
            return ended.Task.WaitAsync(token);
        });
        bool? played = null;
        var line = voice.PlayAsync("Welcome back, Sam!", petVoice, success => played = success);
        Assert.True(voice.Playing);
        Assert.Null(played);
        ended.SetResult();
        await line;
        Assert.True(played);
        Assert.False(voice.Playing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedCueCannotAdvanceOrRetimeTheNewLine(bool oldVoiceFailed)
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        var voice = new VoicePlayback((text, _, token) =>
        {
            if (text == "second") return second.Task;
            oldToken = token;
            return first.Task;
        });
        var finished = new List<string>();
        var old = voice.PlayAsync("first", new(), _ => finished.Add("first"));
        var current = voice.PlayAsync("second", new(), _ => finished.Add("second"));
        Assert.True(oldToken.IsCancellationRequested);
        if (oldVoiceFailed) first.SetException(new IOException("Interrupted audio device"));
        else first.SetResult();
        await old;
        Assert.Empty(finished);
        Assert.True(voice.Playing);
        second.SetResult();
        await current;
        Assert.Equal(["second"], finished);
    }

    [Fact]
    public async Task LineAdvancesOnTheOverlayOwnerAfterSpeechFinishes()
    {
        var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onOwner = false;
        var advanced = false;
        var voice = new VoicePlayback((_, _, _) => end.Task, action =>
        {
            onOwner = true;
            try { action(); }
            finally { onOwner = false; }
        });
        var line = voice.PlayAsync("Hello", new(), played =>
        {
            Assert.True(onOwner);
            advanced = played;
        });
        end.SetResult();
        await line;
        Assert.True(advanced);
    }

    [Fact]
    public async Task FailureRequestsReadingTimeButStoppingSpeechDiscardsItsCallback()
    {
        var unavailable = new VoicePlayback((_, _, _) => throw new FileNotFoundException());
        bool? result = null;
        await unavailable.PlayAsync("Hello", new(), success => result = success);
        Assert.False(result);
        var voice = new VoicePlayback((_, _, token) => Task.Delay(Timeout.Infinite, token));
        var line = voice.PlayAsync("Hello", new(), _ => result = true);
        voice.Stop();
        await line;
        Assert.False(result);
        Assert.False(voice.Playing);
    }
}
