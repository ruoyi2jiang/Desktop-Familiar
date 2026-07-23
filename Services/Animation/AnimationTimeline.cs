namespace DesktopFamiliar.Services.Animation;

/// <summary>
/// Describes the result of advancing an animation by one frame.
/// </summary>
public readonly record struct AnimationAdvanceResult(
    int FrameIndex,
    bool FrameChanged,
    bool CycleCompleted,
    bool AnimationCompleted);

/// <summary>
/// Pure frame-index state machine. It has no WPF, timer, or file-system
/// dependencies, so intro/loop/outro behaviour can be verified independently.
/// </summary>
public sealed class AnimationTimeline
{
    private int? _loopStartIndex;
    private int? _loopEndIndex;
    private bool _finishLoopRequested;

    public int FrameCount { get; }
    public int CurrentFrameIndex { get; private set; }
    public bool IsLooping { get; private set; }
    public bool IsStaticLoop => FrameCount == 1 && IsLooping;

    public AnimationTimeline(
        int frameCount,
        bool loop,
        int? loopStartIndex = null,
        int? loopEndIndex = null)
    {
        if (frameCount < 1)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (loopStartIndex.HasValue != loopEndIndex.HasValue)
            throw new ArgumentException("Both loop range boundaries must be supplied together.");
        if (loopStartIndex is int start
            && (start < 0 || loopEndIndex < start || loopEndIndex >= frameCount))
        {
            throw new ArgumentOutOfRangeException(nameof(loopStartIndex), "The loop range must be inside the animation.");
        }

        FrameCount = frameCount;
        IsLooping = loop;
        _loopStartIndex = loopStartIndex;
        _loopEndIndex = loopEndIndex;
    }

    /// <summary>
    /// Requests the current loop to finish its current pass. A selected range
    /// then continues through its outro; a whole-animation loop stops at its
    /// final frame. Static one-frame loops must be stopped by the caller.
    /// </summary>
    public bool TryFinishLoop()
    {
        if (!IsLooping || _finishLoopRequested || FrameCount == 1)
            return false;

        _finishLoopRequested = true;
        return true;
    }

    public AnimationAdvanceResult Advance()
    {
        if (FrameCount == 1)
        {
            return IsLooping
                ? new AnimationAdvanceResult(0, FrameChanged: false, CycleCompleted: false, AnimationCompleted: false)
                : new AnimationAdvanceResult(0, FrameChanged: false, CycleCompleted: true, AnimationCompleted: true);
        }

        var nextIndex = CurrentFrameIndex + 1;
        if (_loopStartIndex is int loopStart && _loopEndIndex is int loopEnd && nextIndex > loopEnd)
        {
            if (!_finishLoopRequested)
            {
                CurrentFrameIndex = loopStart;
                return new AnimationAdvanceResult(CurrentFrameIndex, FrameChanged: true, CycleCompleted: true, AnimationCompleted: false);
            }

            _loopStartIndex = null;
            _loopEndIndex = null;
            _finishLoopRequested = false;
            IsLooping = false;
        }

        if (nextIndex >= FrameCount)
        {
            if (IsLooping)
            {
                if (_finishLoopRequested)
                {
                    IsLooping = false;
                    _finishLoopRequested = false;
                    CurrentFrameIndex = FrameCount - 1;
                    return new AnimationAdvanceResult(CurrentFrameIndex, FrameChanged: false, CycleCompleted: true, AnimationCompleted: true);
                }

                CurrentFrameIndex = 0;
                return new AnimationAdvanceResult(CurrentFrameIndex, FrameChanged: true, CycleCompleted: true, AnimationCompleted: false);
            }

            CurrentFrameIndex = FrameCount - 1;
            return new AnimationAdvanceResult(CurrentFrameIndex, FrameChanged: false, CycleCompleted: true, AnimationCompleted: true);
        }

        CurrentFrameIndex = nextIndex;
        return new AnimationAdvanceResult(CurrentFrameIndex, FrameChanged: true, CycleCompleted: false, AnimationCompleted: false);
    }
}
