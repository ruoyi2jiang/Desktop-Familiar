using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Services;

public sealed class AnimationFramePresentedEventArgs(int frameIndex) : EventArgs
{
    public int FrameIndex { get; } = frameIndex;
}

public sealed class FrameAnimationPlayer : IDisposable
{
    private readonly DispatcherTimer _timer = new();
    private readonly Action<BitmapSource?> _frameChanged;
    private readonly IAnimationFrameLoader _frameLoader;
    private IReadOnlyList<BitmapSource> _frames = [];
    private IReadOnlyList<TimeSpan> _delays = [];
    private AnimationTimeline? _timeline;

    public bool IsRunning => _timer.IsEnabled;
    public bool IsLooping => _timeline?.IsLooping == true;
    public int FrameCount => _frames.Count;
    public int CurrentFrameIndex => _timeline?.CurrentFrameIndex ?? 0;
    public event EventHandler? CycleCompleted;
    public event EventHandler<AnimationFramePresentedEventArgs>? FramePresented;

    public FrameAnimationPlayer(
        Action<BitmapSource?> frameChanged,
        IAnimationFrameLoader? frameLoader = null)
    {
        _frameChanged = frameChanged;
        _frameLoader = frameLoader ?? new WpfAnimationFrameLoader();
        _timer.Tick += OnTick;
    }

    public void Play(string path, bool loop)
    {
        Stop(clearFrame: false);
        var loaded = _frameLoader.Load(path);
        Start(loaded.Frames, loaded.Delays, loop);
    }

    public void Play(AnimationScene scene, bool loop)
    {
        Stop(clearFrame: false);
        var loaded = _frameLoader.Load(scene);
        if (loop && AnimationFrameSettingsPolicy.TryGetRuntimeLoopRange(
                scene,
                loaded.Frames.Count,
                out var loopStart,
                out var loopEnd))
            Start(loaded.Frames, loaded.Delays, loop: true, loopStart, loopEnd);
        else
            Start(loaded.Frames, loaded.Delays, loop);
    }

    /// <summary>
    /// Finishes the current loop pass and any configured outro. Returns false
    /// for a non-looping animation or a static one-frame loop.
    /// </summary>
    public bool FinishLoop()
    {
        return _timeline?.TryFinishLoop() == true;
    }

    private void Start(
        IReadOnlyList<BitmapSource> frames,
        IReadOnlyList<TimeSpan> delays,
        bool loop,
        int? loopStart = null,
        int? loopEnd = null)
    {
        if (frames.Count == 0 || frames.Count != delays.Count)
            throw new InvalidDataException("An animation must contain matching frame and delay collections.");

        _frames = frames;
        _delays = delays;
        _timeline = new AnimationTimeline(frames.Count, loop, loopStart, loopEnd);
        _frameChanged(_frames[0]);
        FramePresented?.Invoke(this, new AnimationFramePresentedEventArgs(0));

        if (_timeline.IsStaticLoop)
            return;

        _timer.Interval = _delays[0];
        _timer.Start();
    }

    public void Stop(bool clearFrame = false)
    {
        _timer.Stop();
        _frames = [];
        _delays = [];
        _timeline = null;
        if (clearFrame)
            _frameChanged(null);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var timeline = _timeline;
        if (timeline is null || _frames.Count == 0)
            return;

        var result = timeline.Advance();
        if (result.AnimationCompleted)
            _timer.Stop();
        if (result.FrameChanged)
        {
            _frameChanged(_frames[result.FrameIndex]);
            _timer.Interval = _delays[result.FrameIndex];
            FramePresented?.Invoke(this, new AnimationFramePresentedEventArgs(result.FrameIndex));
        }

        // Notify after the timer/frame state is complete. Event handlers may
        // synchronously start another animation on this same player.
        if (result.CycleCompleted)
            CycleCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop(clearFrame: true);
}
