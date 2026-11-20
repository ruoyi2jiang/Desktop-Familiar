using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DesktopFamiliar.Services.Voice;

internal sealed record DeviceChoice(string Id, string Name);

internal sealed class MicrophoneCapture : IVoiceRecording
{
    private readonly WasapiCapture _capture;
    private readonly MMDevice _device;
    private readonly MemoryStream _raw = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    internal const int MaximumSeconds = 30;

    public static List<DeviceChoice> Devices()
    {
        using var source = new MMDeviceEnumerator();
        var choices = new List<DeviceChoice>();
        foreach (var endpoint in source.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            using (endpoint) choices.Add(new(endpoint.ID, endpoint.FriendlyName));
        return choices;
    }

    public MicrophoneCapture(string? id)
    {
        using var devices = new MMDeviceEnumerator();
        _device = string.IsNullOrEmpty(id) ? devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia) : devices.GetDevice(id);
        try { _capture = new WasapiCapture(_device); }
        catch { _device.Dispose(); throw; }
        _capture.DataAvailable += Data;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception == null) _stopped.TrySetResult();
            else _stopped.TrySetException(e.Exception);
        };
        try { _capture.StartRecording(); }
        catch { Dispose(); throw; }
    }

    private void Data(object? sender, WaveInEventArgs e)
    {
        var limit = (long)_capture.WaveFormat.AverageBytesPerSecond * MaximumSeconds;
        var count = (int)Math.Min(e.BytesRecorded, limit - _raw.Length);
        if (count > 0) _raw.Write(e.Buffer, 0, count);
        if (_raw.Length >= limit) _capture.StopRecording();
    }

    public async Task<byte[]> FinishAsync(CancellationToken token)
    {
        _capture.StopRecording();
        await _stopped.Task.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        using var raw = new RawSourceWaveStream(new MemoryStream(_raw.ToArray()), _capture.WaveFormat);
        using var pcm = new MediaFoundationResampler(raw, new WaveFormat(16000, 16, 1));
        using var audio = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(audio, pcm);
        return audio.ToArray();
    }

    internal static double Peak(byte[] wav)
    {
        using var input = new WaveFileReader(new MemoryStream(wav));
        var samples = input.ToSampleProvider();
        var buffer = new float[4096];
        double peak = 0;
        int n;
        while ((n = samples.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
        return peak;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _capture.Dispose();
        _device.Dispose();
        _raw.Dispose();
    }
}
