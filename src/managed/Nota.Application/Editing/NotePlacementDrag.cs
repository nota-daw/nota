// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shaping a note while it is placed (specs/066-piano-roll-place-drag, #45): after the
// double-click that adds a note, dragging with the button still held moves it, ⇧ sets its
// length, ⌘/Ctrl its velocity. Pure and UI-free: the smoke test drives it directly.

using System;

namespace Nota.Application.Editing;

public enum PlaceDragKind { Move, Length, Velocity }

public static class NotePlacementDrag
{
    /// <summary>Pixels of vertical drag per MIDI velocity step (full range in ~250 px).</summary>
    public const double PxPerVelocityStep = 2;

    /// <summary>What the held drag does, read live from the modifiers: ⌘/Ctrl wins over ⇧.</summary>
    public static PlaceDragKind KindFor(bool shift, bool command)
        => command ? PlaceDragKind.Velocity : shift ? PlaceDragKind.Length : PlaceDragKind.Move;

    /// <summary>The velocity after dragging <paramref name="dyPx"/> pixels down from where
    /// the velocity drag started at <paramref name="anchor"/>: up is louder, clamped to 1..127.</summary>
    public static float Velocity(float anchor, double dyPx)
        => NoteOverwrite.VelocityBy(anchor, (int)Math.Round(-dyPx / PxPerVelocityStep));

    /// <summary>Where a beat that was <paramref name="anchored"/> when this drag (or a modifier
    /// switch) started should sit after the pointer moves <paramref name="deltaBeats"/>. No
    /// movement keeps the anchored beat, so switching into the mode never snaps an off-grid
    /// note; later movement snaps the beat the pointer now points at.</summary>
    public static double BeatAfter(double anchored, double deltaBeats, Func<double, double> snap)
        => Math.Abs(deltaBeats) < 1e-9 ? anchored : snap(anchored + deltaBeats);
}
