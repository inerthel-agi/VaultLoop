using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// The interface's animations: short, eased transitions driven by one shared UI-thread
/// timer that only runs while something moves.
/// </summary>
/// <remarks>
/// An animation jumps straight to its end when Windows animations are switched off
/// (Settings > Accessibility > Visual effects), or when its control is not on screen yet.
/// The second rule is what keeps <c>--render-preview</c> and the constructor paths
/// deterministic: a state set before the window is shown is drawn at its final value.
/// </remarks>
internal static class Motion
{
    /// <summary>Hover and press feedback.</summary>
    internal const int Quick = 110;

    /// <summary>A change of state: a switch sliding, a color settling.</summary>
    internal const int Standard = 180;

    private static readonly List<Tween> Running = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Timer? _timer;

    internal static bool Enabled => NativeMethods.ClientAreaAnimationEnabled();

    /// <summary>
    /// Calls <paramref name="step"/> with an eased progress from 0 to 1 over
    /// <paramref name="duration"/> milliseconds, then <paramref name="done"/>.
    /// </summary>
    internal static Tween Run(Control owner, int duration, Action<float> step, Action? done = null)
    {
        var tween = new Tween(owner, Clock.ElapsedMilliseconds, duration, step, done);
        if (!Enabled || owner.IsDisposed || !owner.IsHandleCreated || !owner.Visible)
        {
            tween.Finish();
            return tween;
        }

        Running.Add(tween);
        if (_timer is null)
        {
            _timer = new Timer { Interval = 15 };
            _timer.Tick += (_, _) => Tick();
        }
        _timer.Start();
        return tween;
    }

    /// <summary>Ease-out cubic: fast at the start, settling softly at the end.</summary>
    internal static float Ease(float progress)
    {
        var remaining = 1F - progress;
        return 1F - remaining * remaining * remaining;
    }

    private static void Tick()
    {
        var now = Clock.ElapsedMilliseconds;
        // Backwards, so a tween that finishes and starts another one does not disturb the walk.
        for (var index = Running.Count - 1; index >= 0; index--)
        {
            var tween = Running[index];
            if (tween.Cancelled || tween.Owner.IsDisposed)
            {
                Running.RemoveAt(index);
                continue;
            }

            var progress = (now - tween.Start) / (float)Math.Max(1, tween.Duration);
            if (progress >= 1F)
            {
                Running.RemoveAt(index);
                tween.Finish();
            }
            else
            {
                tween.Step(Ease(progress));
            }
        }

        if (Running.Count == 0)
        {
            _timer!.Stop();
        }
    }
}

/// <summary>One running animation.</summary>
internal sealed class Tween
{
    private readonly Action<float> _step;
    private readonly Action? _done;
    private bool _finished;

    internal Tween(Control owner, long start, int duration, Action<float> step, Action? done)
    {
        Owner = owner;
        Start = start;
        Duration = duration;
        _step = step;
        _done = done;
    }

    internal Control Owner { get; }
    internal long Start { get; }
    internal int Duration { get; }
    internal bool Cancelled { get; private set; }

    /// <summary>Stops the animation where it is; its end action does not run.</summary>
    internal void Cancel() => Cancelled = true;

    internal void Step(float progress) => _step(progress);

    internal void Finish()
    {
        if (_finished || Cancelled)
        {
            return;
        }
        _finished = true;
        _step(1F);
        _done?.Invoke();
    }
}

/// <summary>
/// A number a control draws with, moving smoothly to each new target. Retargeting while it
/// moves starts from where it is, so a quick hover in and out never jumps.
/// </summary>
internal sealed class AnimatedFloat
{
    private readonly Control _owner;
    private readonly Action _changed;
    private Tween? _tween;

    internal AnimatedFloat(Control owner, float initial, Action changed)
    {
        _owner = owner;
        _changed = changed;
        Value = initial;
    }

    internal float Value { get; private set; }

    internal void AnimateTo(float target, int duration) => AnimateFrom(Value, target, duration);

    internal void AnimateFrom(float from, float target, int duration)
    {
        _tween?.Cancel();
        Value = from;
        if (from == target)
        {
            _changed();
            return;
        }
        _tween = Motion.Run(_owner, duration, progress =>
        {
            Value = from + (target - from) * progress;
            _changed();
        });
    }

    internal void Snap(float value)
    {
        _tween?.Cancel();
        Value = value;
        _changed();
    }
}
