using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

/// <summary>Samples one source timeline across its embedded clips, including the intro and outro.</summary>
internal sealed class ModelClipTimeline
{
    public readonly record struct Frame(string? Clip, double Time, bool Complete, double SourceTime);
    private readonly List<(string Clip, double Start, double End)> _parts = [];
    private readonly bool _loop;
    private readonly bool _hold;
    private readonly double _start, _end, _middle, _pass;
    private double? _release;
    public double Duration { get; }
    public static bool ValidSettings(ModelLoopSettings? settings, double duration = double.PositiveInfinity)
    {
        if (settings == null) return true;
        return Enum.IsDefined(settings.Mode) && double.IsFinite(settings.StartSeconds) && settings.StartSeconds >= 0
            && (settings.StartSeconds < duration || (duration == 0 && settings.StartSeconds == 0))
            && (settings.EndSeconds is not double end || (double.IsFinite(end) && end > settings.StartSeconds && end <= duration + 0.000001))
            && double.IsFinite(settings.DurationSeconds) && settings.DurationSeconds is > 0 and <= 600;
    }

    public ModelClipTimeline(AnimationScene scene, Func<string, double> nativeDuration, bool loop)
    {
        foreach (var clip in scene.SourceClips)
        {
            var end = Duration + nativeDuration(clip);
            _parts.Add((clip, Duration, end));
            Duration = end;
        }
        var settings = ValidSettings(scene.ModelLoop, Duration) ? scene.ModelLoop : null;
        _loop = loop;
        _start = settings?.StartSeconds ?? 0;
        _end = Math.Min(settings?.EndSeconds ?? Duration, Duration);
        _middle = settings?.Mode == ModelLoopMode.FixedDuration ? settings.DurationSeconds : _end - _start;
        _pass = _start + _middle + Duration - _end;
        _hold = loop && settings?.Mode == ModelLoopMode.UntilDialogueEnds;
    }

    public bool FinishLoop(double elapsed)
    {
        if (!_loop || Duration <= 0) return false;
        if (_release != null) return true;
        var period = _end - _start;
        _release = _hold ? _start + Math.Max(1, Math.Ceiling((elapsed - _start) / period)) * period
            : (Math.Floor(elapsed / _pass) + 1) * _pass;
        return true;
    }

    public Frame Sample(double elapsed)
    {
        if (Duration <= 0) return SampleSource(0, !_loop);
        if (_hold && elapsed >= _start)
        {
            if (_release is not double release || elapsed < release)
                return SampleSource(_start + (elapsed - _start) % (_end - _start));
            var outro = _end + (elapsed - release);
            return SampleSource(outro, outro >= Duration);
        }
        if (_loop && !_hold && (_release == null || elapsed < _release))
            elapsed %= _pass;
        else if (elapsed >= _pass)
            return SampleSource(Duration, true);
        if (elapsed < _start) return SampleSource(elapsed);
        if (elapsed < _start + _middle)
            return SampleSource(_start + (elapsed - _start) % (_end - _start));
        return SampleSource(_end + (elapsed - _start - _middle));
    }

    public Frame SampleSource(double seconds, bool complete = false)
    {
        var position = Math.Clamp(seconds, 0, Duration);
        foreach (var part in _parts)
            if (position < part.End) return new(part.Clip, position - part.Start, complete, position);
        var last = _parts.LastOrDefault();
        return new(last.Clip, last.End - last.Start, complete, position);
    }
}
