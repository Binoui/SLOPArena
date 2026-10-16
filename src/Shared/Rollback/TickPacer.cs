namespace SlopArena.Shared.Rollback;

/// <summary>
/// Advances a tick timeline one step per update toward a jittery target. Single-tick
/// target noise is absorbed; persistent drift is corrected one extra or skipped step at a
/// time, so presentation never alternates 0/2-tick updates. Large deficits catch up at
/// once; large surpluses slow to half speed instead of freezing.
/// </summary>
public sealed class TickPacer
{
    public const int ImmediateCorrectionTicks = 4;
    private const double Smoothing = 0.1;
    private const double CorrectionThreshold = 0.9;
    private double _drift;
    private bool _skipNext;
    private bool _align;

    /// <summary>
    /// Forget accumulated drift. With <paramref name="alignNext"/>, the next update jumps
    /// straight to its target (a freshly seeded timeline should not ease in).
    /// </summary>
    public void Reset(bool alignNext = false)
    {
        _drift = 0;
        _skipNext = false;
        _align = alignNext;
    }

    /// <summary>Steps to take this update from <paramref name="current"/> toward <paramref name="target"/>.</summary>
    public uint Steps(uint current, uint target, uint maximumSteps)
    {
        long error = (long)target - ((long)current + 1);
        if (_align)
        {
            // A fresh timeline starts at the target instead of easing in while its
            // first inputs would arrive late.
            _align = false;
            return (uint)System.Math.Max(0, System.Math.Min(1 + error, (long)System.Math.Max(1u, maximumSteps)));
        }
        if (error >= ImmediateCorrectionTicks)
        {
            _drift = 0;
            return (uint)System.Math.Min(1 + error, (long)System.Math.Max(1u, maximumSteps));
        }
        if (error <= -ImmediateCorrectionTicks)
        {
            _drift = 0;
            _skipNext = !_skipNext;
            return _skipNext ? 0u : 1u;
        }
        _drift += Smoothing * (error - _drift);
        if (_drift >= CorrectionThreshold && maximumSteps >= 2)
        {
            _drift -= 1;
            return 2;
        }
        if (_drift <= -CorrectionThreshold)
        {
            _drift += 1;
            return 0;
        }
        return 1;
    }
}
