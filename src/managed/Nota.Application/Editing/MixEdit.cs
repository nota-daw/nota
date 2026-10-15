// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Volume / pan / master-volume edits that land in the undo history (specs/009-mix-param-undo).
// A gesture is opened on the engine, the value is set through the plain setter, and the
// gesture is closed — one undo step for whatever changed. Used for one-off edits that have no
// press/release of their own: an MCP call, a reset, a typed value. Internal callers (load,
// freeze, the template, automation) keep using the plain setters and never make steps.

using System;
using System.Collections.Generic;

namespace Nota.Application.Editing;

public static class MixEdit
{
    /// <summary>Runs <paramref name="set"/> as one undo step over <paramref name="targets"/>.</summary>
    public static void Step(IAudioEngine engine, Action set, params MixTarget[] targets)
    {
        if (targets.Length <= MaxGestureTargets)
        {
            int handle = engine.BeginMixGesture(targets);
            try { set(); }
            finally { engine.EndMixGesture(handle); }
            return;
        }
        // More targets than one gesture holds (a plug-in's whole parameter set, specs/016):
        // several gestures inside an undo group still undo as one step.
        engine.BeginUndoGroup();
        var handles = new List<int>();
        try
        {
            for (int i = 0; i < targets.Length; i += MaxGestureTargets)
                handles.Add(engine.BeginMixGesture(targets[i..Math.Min(targets.Length, i + MaxGestureTargets)]));
            set();
        }
        finally
        {
            foreach (int h in handles) engine.EndMixGesture(h);
            engine.EndUndoGroup();
        }
    }

    /// <summary>The most targets one engine gesture takes (<c>nota_mix_gesture_begin</c>).</summary>
    public const int MaxGestureTargets = 64;
}

/// <summary>A gesture held by one control or source: <see cref="Begin"/> on press,
/// <see cref="End"/> on release, and <see cref="Change"/> for every value — inside an open
/// gesture it just sets, outside one (double-click reset, typed value) it is a one-off step.</summary>
public sealed class MixGesture(IAudioEngine engine, Func<MixTarget[]> targets)
{
    private int _handle;

    public MixGesture(IAudioEngine engine, params MixTarget[] targets) : this(engine, () => targets) { }

    public bool IsOpen => _handle != 0;

    public void Begin() { if (_handle == 0) _handle = engine.BeginMixGesture(targets()); }

    public void End()
    {
        if (_handle == 0) return;
        engine.EndMixGesture(_handle);
        _handle = 0;
    }

    public void Change(Action set)
    {
        if (_handle != 0) set();
        else MixEdit.Step(engine, set, targets());
    }

    /// <summary><see cref="Change(Action)"/> without a closure per value: pass the value and a
    /// setter delegate made once — a fader drag then allocates nothing per move.</summary>
    public void Change<T>(T value, Action<T> set)
    {
        if (_handle != 0) { set(value); return; }
        int handle = engine.BeginMixGesture(targets());
        try { set(value); }
        finally { engine.EndMixGesture(handle); }
    }
}

/// <summary>A stream of values with no press/release (a MIDI controller knob): the first
/// <see cref="Touch"/> opens a gesture and <see cref="Poll"/> ends it once no touch came for
/// <c>pause</c> — one undo step per movement. Time is passed in (milliseconds, any monotonic
/// clock), so the caller's tick drives it and a test can too.</summary>
public sealed class MixMovement(IAudioEngine engine, MixTarget target, TimeSpan pause)
{
    private readonly MixGesture _gesture = new(engine, target);
    private long _lastMs;

    public bool IsOpen => _gesture.IsOpen;

    public void Touch(Action set, long nowMs)
    {
        _gesture.Begin();
        set();
        _lastMs = nowMs;
    }

    public void Poll(long nowMs)
    {
        if (_gesture.IsOpen && nowMs - _lastMs >= (long)pause.TotalMilliseconds) _gesture.End();
    }

    public void End() => _gesture.End();
}
