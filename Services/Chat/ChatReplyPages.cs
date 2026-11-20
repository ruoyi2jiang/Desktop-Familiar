using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Services.Chat;

internal sealed class ChatReplyPages
{
    internal sealed record Page(string Text, ChatReplyPlayback? Playback);

    private readonly Queue<Page> _pending = new();
    public Page? Current { get; private set; }
    public bool HasPages => Current != null || _pending.Count > 0;

    public void Enqueue(string reply, ChatReplyPlayback? playback)
    {
        foreach (var text in ChatBubbleTiming.Pages(reply))
            _pending.Enqueue(new(text, playback));
    }

    public void Update(TimeSpan readTime, bool speaking)
    {
        if (Current != null && (speaking || readTime < ChatBubbleTiming.ReadingTime(Current.Text))) return;
        Current = _pending.TryDequeue(out var next) ? next : null;
    }

    public void DiscardSuperseded(bool keepVisiblePage)
    {
        _pending.Clear();
        if (!keepVisiblePage) Current = null;
        else if (Current?.Playback is { } playback) playback.SpeechInterrupted = true;
    }

    public void InterruptSpeech()
    {
        if (Current?.Playback is { } current) current.SpeechInterrupted = true;
        foreach (var page in _pending)
            if (page.Playback != null) page.Playback.SpeechInterrupted = true;
    }
}
