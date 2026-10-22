// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Live warp-marker drag preview (#140, specs/036-clip-editor-batch). The waveform's peaks were
// laid out by the engine under the committed markers; while a marker moves, the audio at a new
// beat is the source frame the live markers put there, which sat at some old beat under the
// committed ones. Drawing each column from that old beat shows the stretch before the engine has
// rendered it. Display only — nothing reaches the engine until release.

using System;

namespace Nota.App;

internal static class WarpRemap
{
    /// <summary>The committed-layout beat whose audio the live layout puts at <paramref name="newBeat"/>.
    /// Both marker lists hold the same sources in the same order.</summary>
    public static double OldBeatAt(
        ReadOnlySpan<double> src, ReadOnlySpan<double> committedBeat, ReadOnlySpan<double> liveBeat, double newBeat)
        => ClipTempo.BeatAt(src, committedBeat, ClipTempo.SrcAt(src, liveBeat, newBeat));

    /// <summary>Where the live layout puts the audio that sat at <paramref name="oldBeat"/> under the
    /// committed one (the inverse of <see cref="OldBeatAt"/>) — used to move each drawn bucket.</summary>
    public static double NewBeatAt(
        ReadOnlySpan<double> src, ReadOnlySpan<double> committedBeat, ReadOnlySpan<double> liveBeat, double oldBeat)
        => ClipTempo.BeatAt(src, liveBeat, ClipTempo.SrcAt(src, committedBeat, oldBeat));
}
