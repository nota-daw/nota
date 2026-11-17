// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// The Arrangement's horizontal axis under a changing tempo (specs/086, kept by specs/099):
// time-linear, so bars widen where the tempo is slower and unwarped audio keeps its physical
// length. The tempo itself is the master track's Tempo automation lane; these only convert.

using System;
using System.Threading.Tasks;

namespace Nota.App;

public sealed partial class ArrangementView
{
    private bool _hasTempoMap;
    private bool TempoAxis => _hasTempoMap || _frozenAxisSeconds is not null;
    private double TempoSeconds(double beat)
        => _frozenAxisSeconds is { } f ? FrozenSeconds(f, beat) : _engine?.BeatToSeconds(beat) ?? beat * .5;
    private double TempoBeat(double seconds)
        => _frozenAxisSeconds is { } f ? FrozenBeat(f, seconds) : _engine?.SecondsToBeat(seconds) ?? seconds * 2;

    // The drag-start axis (see FreezeTempoAxis): linear between samples, extended past the ends.
    private static double FrozenSeconds(double[] f, double beat)
    {
        double x = beat / FrozenAxisStep;
        int i = (int)Math.Clamp(Math.Floor(x), 0, f.Length - 2);
        return f[i] + (f[i + 1] - f[i]) * (x - i);
    }
    private static double FrozenBeat(double[] f, double seconds)
    {
        int lo = 0, hi = f.Length - 1;
        if (seconds <= f[0]) hi = 1;
        else if (seconds >= f[^1]) lo = f.Length - 2;
        else while (hi - lo > 1) { int m = (lo + hi) / 2; if (f[m] <= seconds) lo = m; else hi = m; }
        hi = lo + 1;
        double span = f[hi] - f[lo];
        return (lo + (span > 0 ? (seconds - f[lo]) / span : 0)) * FrozenAxisStep;
    }
    private double AxisOriginSeconds => TempoSeconds(_scrollBeats);
    private double SecondsToX(double seconds) => (seconds - AxisOriginSeconds) * _pixelsPerBeat * 2;
    private double XToSeconds(double x) => AxisOriginSeconds + x / (_pixelsPerBeat * 2);
    private double XToBeat(double x) => TempoAxis ? TempoBeat(XToSeconds(x)) : _scrollBeats + x / _pixelsPerBeat;
    private double ScrollBeatForX(double beat, double x, double? pixelsPerBeat = null)
    {
        double ppb = pixelsPerBeat ?? _pixelsPerBeat;
        return Math.Max(0, TempoAxis ? TempoBeat(TempoSeconds(beat) - x / (ppb * 2)) : beat - x / ppb);
    }
    public int TimeSignatureDenominator { get; set; } = 4;
    private double TempoBarLength => _beatsPerBar * 4.0 / Math.Max(1, TimeSignatureDenominator);
    private double GridBarLength => TempoAxis ? TempoBarLength : _beatsPerBar;

    // Whether the tempo is automated (the axis maps through it). Read on every refresh.
    private void ReloadTempo() => _hasTempoMap = _engine?.IsTempoAutomated ?? false;

    private (double start, double end) ClipScreenEdges(ClipVM clip)
        => (BeatToX(clip.StartBeat), BeatToX(clip.StartBeat + clip.LengthBeats));

    // Warped clips re-stretch to a new tempo map off the UI thread (deferred build): poll it
    // through so they play the new timing as soon as they are ready.
    private async void BuildTempoWarpCaches()
    {
        var engine = _engine;
        if (engine is null) return;
        while (ReferenceEquals(engine, _engine) && engine.WarpBuildPoll() > 0)
            await Task.Delay(15);
    }
}
