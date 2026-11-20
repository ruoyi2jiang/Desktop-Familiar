namespace DesktopFamiliar.Services.Voice;

internal interface IVoiceRecording : IDisposable
{
    Task<byte[]> FinishAsync(CancellationToken token);
}

/// <summary>One prompt owns one capture and transcript. Closing it invalidates late recognizer results.</summary>
internal sealed class PushToTalkSession(Func<IVoiceRecording> openMicrophone,
    Func<byte[], CancellationToken, Task<string>> transcribe) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IVoiceRecording? _capture;
    public bool Recording => _capture != null;
    public bool Transcribing { get; private set; }
    public bool Closed { get; private set; }

    internal static string AppendTranscript(string draft, string transcript) =>
        (draft.TrimEnd() + " " + transcript.Trim()).TrimStart();

    public bool Begin()
    {
        if (Closed || Recording || Transcribing) return false;
        _capture = openMicrophone();
        return true;
    }

    public async Task<string?> ReleaseAsync()
    {
        if (_capture is not { } capture || Closed) return null;
        _capture = null;
        Transcribing = true;
        var token = _lifetime.Token;
        try
        {
            byte[] wave;
            using (capture) wave = await capture.FinishAsync(token);
            token.ThrowIfCancellationRequested();
            var text = await transcribe(wave, token);
            token.ThrowIfCancellationRequested();
            return text;
        }
        finally { Transcribing = false; }
    }

    public void Dispose()
    {
        if (Closed) return;
        Closed = true;
        _lifetime.Cancel();
        _capture?.Dispose();
        _capture = null;
        _lifetime.Dispose();
    }
}
