// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Soft takeover (specs/051 FR-019, research R7). An absolute knob or fader that is not where
// its target's value is must not make the value jump: it holds still until the physical
// position crosses the value (a fast move that skips over it counts) or comes within 2/127 of
// it — then it has "picked up" and follows. Anything else that changes the value afterwards
// (the mouse, automation, undo, another control) lets go again; so does pointing the control
// at another target (focus, page, bank or knob-mode change). Relative encoders and motorised
// faders never need it — the caller skips the check for them, and when pickup is off.

namespace Nota.Application.Controllers;

public sealed class PickupState
{
    /// <summary>Within this of the value a control has picked up (one 7-bit step of jitter, twice).</summary>
    public const double Tolerance = 2.0 / 127;

    private object? _target;
    private double? _lastPhysical;
    private double? _written, _readBack;

    public bool Caught { get; private set; }
    /// <summary>The control's last position (for the pickup hint), or null before it moved.</summary>
    public double? LastPhysical => _lastPhysical;
    public object? Target => _target;

    /// <summary>The control moved to <paramref name="physical"/> (0..1) while it points at
    /// <paramref name="target"/>, whose value is <paramref name="current"/> (0..1). True when the
    /// move should be applied.</summary>
    public bool Accept(object target, double physical, double current)
    {
        if (!Equals(target, _target)) Release(target);
        // Someone else moved the value since this control last wrote it.
        if (Caught && !Near(current, _written) && !Near(current, _readBack)) Caught = false;
        if (!Caught)
        {
            if (Math.Abs(physical - current) <= Tolerance) Caught = true;
            else if (_lastPhysical is { } was && (was - current) * (physical - current) <= 0) Caught = true;   // crossed it
        }
        _lastPhysical = physical;
        if (Caught) { _written = physical; _readBack = null; }
        return Caught;
    }

    /// <summary>What the target reads after the applied move (a stepped parameter rounds), so a
    /// value that settled on its step is not taken for someone else's change.</summary>
    public void ReadBack(double? value)
    {
        if (Caught) _readBack = value;
    }

    /// <summary>Let go: the next move has to pick up again (target, page, bank or mode changed).</summary>
    public void Release(object? target = null)
    {
        _target = target;
        Caught = false;
        _lastPhysical = null;
        _written = _readBack = null;
    }

    private static bool Near(double current, double? v) => v is { } x && Math.Abs(current - x) <= 1e-3;
}
