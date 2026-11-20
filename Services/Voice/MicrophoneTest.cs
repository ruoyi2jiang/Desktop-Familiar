namespace DesktopFamiliar.Services.Voice;

internal static class MicrophoneTest
{
    internal static async Task<double> RunAsync(Func<IVoiceRecording> openMicrophone, TimeSpan duration,
        Func<byte[], CancellationToken, Task> playRecording, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] wave;
        using (var microphone = openMicrophone())
        {
            await Task.Delay(duration, token);
            wave = await microphone.FinishAsync(token);
        }
        token.ThrowIfCancellationRequested();
        var peak = MicrophoneCapture.Peak(wave);
        await playRecording(wave, token);
        token.ThrowIfCancellationRequested();
        return peak;
    }
}
