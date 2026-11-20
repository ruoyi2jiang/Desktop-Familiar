using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Voice;

/// <summary>Only the current utterance may advance its line, even if an interrupted voice finishes late.</summary>
internal sealed class VoicePlayback(Func<string, CompanionVoice, CancellationToken, Task> speak,
    Action<Action>? invokeOnOwner = null)
{
    private readonly Action<Action> _invokeOnOwner = invokeOnOwner ?? (action => action());
    private CancellationTokenSource? _current;
    public bool Playing => _current != null;

    public void Stop()
    {
        var previous = _current;
        _current = null;
        previous?.Cancel();
    }

    public async Task PlayAsync(string text, CompanionVoice voice, Action<bool>? finished = null)
    {
        Stop();
        using var operation = new CancellationTokenSource();
        _current = operation;
        bool played = false;
        try
        {
            await speak(text, voice, operation.Token);
            played = true;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { AppLogger.Warning("Companion speech playback was unavailable.", ex); }
        _invokeOnOwner(() =>
        {
            if (!ReferenceEquals(_current, operation)) return;
            _current = null;
            finished?.Invoke(played);
        });
    }
}
