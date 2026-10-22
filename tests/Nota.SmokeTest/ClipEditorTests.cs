// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Clip editor math (specs/036-clip-editor-batch): the audio editor's content ↔ arrangement map
// (playhead, ruler seeks and labels), the shared ruler labels, a clip's warp tempo and the live
// warp-drag remap. Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --clipeditor`.

using System.Globalization;
using Nota.App;

namespace Nota.SmokeTest;

internal static class ClipEditorTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static bool Near(double a, double b, double eps = 1e-9) => Math.Abs(a - b) <= eps;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        // ---- ClipTimeMap ------------------------------------------------------------
        // Warped clip at beat 32, 8 beats long, full warp 8 beats, untrimmed.
        var warped = new ClipTimeMap(32, 8, ClipContentDomain.WarpBeats, 8, 0, 8, false, false);
        yield return (Near(warped.ArrangementBeatAtContent(0), 32), "map: warped clip start reads its arrangement beat");
        yield return (Near(warped.ArrangementBeatAtContent(0.5), 36), "map: warped clip middle");
        // Start bracket trimmed one bar in (4 of 12 warp beats); the clip plays 8 beats from 32.
        var trimmed = new ClipTimeMap(32, 8, ClipContentDomain.WarpBeats, 12, 4, 12, false, false);
        yield return (Near(trimmed.ArrangementBeatAtContent(4.0 / 12), 32), "map: trimmed Start bracket reads the clip start");
        yield return (Near(trimmed.ArrangementBeatAtContent(0), 28), "map: audio before the Start bracket reads before the clip");
        yield return (trimmed.PlayFracAtContent(0) < 0, "map: left of the played window is not clamped");
        // Reversed: the content's end plays first.
        var rev = new ClipTimeMap(0, 4, ClipContentDomain.WarpBeats, 4, 0, 4, true, false);
        yield return (Near(rev.ArrangementBeatAtContent(1), 0) && Near(rev.ArrangementBeatAtContent(0), 4), "map: reversed clip plays its end first");
        // Unwarped: 96 000 frames, region 24 000..72 000, plays 4 beats at 16.
        var unwarped = new ClipTimeMap(16, 4, ClipContentDomain.SourceFrames, 96000, 24000, 72000, false, false);
        yield return (Near(unwarped.ArrangementBeatAtContent(0.25), 16) && Near(unwarped.ArrangementBeatAtContent(0.75), 20), "map: unwarped region maps onto the clip");
        bool roundTrip = true;
        foreach (var m in new[] { warped, trimmed, rev, unwarped })
            for (double c = 0; c <= 1.0001; c += 0.125)
            {
                roundTrip &= Near(m.ContentAtPlayFrac(m.PlayFracAtContent(c)), c, 1e-9);
                roundTrip &= Near(m.ContentAtArrangementBeat(m.ArrangementBeatAtContent(c)), c, 1e-9);
            }
        yield return (roundTrip, "map: content ↔ played frac ↔ arrangement beat round-trips");
        var empty = new ClipTimeMap(0, 4, ClipContentDomain.WarpBeats, 0, 0, 0, false, false);
        yield return (empty.PlayFracAtContent(0.5) == 0 && !double.IsNaN(empty.ContentAtPlayFrac(0.5)), "map: an empty clip stays NaN-free");

        // ---- RulerLabels --------------------------------------------------------------
        yield return (RulerLabels.Bars(32, 4, 4, Inv) == "9", "labels: bar 9");
        yield return (RulerLabels.Bars(33, 1, 4, Inv) == "9.2", "labels: beat 9.2");
        yield return (RulerLabels.Bars(33.5, 0.25, 4, Inv) == "9.2.3", "labels: 16th 9.2.3");
        yield return (RulerLabels.Bars(33.5, 1, 4, Inv) == "9.2", "labels: a beat step shows no 16ths");
        yield return (RulerLabels.Bars(-2, 1, 4, Inv) == "1", "labels: negative positions clamp to 1");
        yield return (RulerLabels.Time(16, 1, Inv) == "0:16", "labels: time m:ss");
        yield return (RulerLabels.Time(75.5, 0.5, Inv) == "1:15.5", "labels: time tenths");
        yield return (RulerLabels.Time(1.25, 0.01, Inv) == "0:01.25", "labels: time hundredths");
        yield return (RulerLabels.Time(0.0625, 0.001, Inv) == "0:00.063", "labels: time milliseconds");
        yield return (RulerLabels.Time(59.9996, 0.001, Inv) == "1:00.000", "labels: rounding carries into the minute");
        yield return (Near(RulerLabels.Seconds(32, 120), 16) && Near(RulerLabels.BeatsAt(16, 120), 32), "labels: beats ↔ seconds at the tempo");

        // ---- ClipTempo ----------------------------------------------------------------
        const double sr = 48000;
        // One segment: 2 bars (8 beats) over 4 s of audio = 120 BPM; same formula as the segment chip.
        double[] src1 = { 0, 192000 }, beat1 = { 0, 8 };
        double chip = (beat1[1] - beat1[0]) * sr * 60.0 / (src1[1] - src1[0]);
        double t1 = ClipTempo.Bpm(src1, beat1, 0, 0, sr);
        yield return (Near(t1, 120, 1e-9) && Near(t1, chip, 1e-9), "tempo: one segment equals its chip");
        // Three segments at different tempos; scaling keeps their ratios.
        double[] src3 = { 0, 48000, 120000, 192000 }, beat3 = { 0, 2, 5, 8 };
        double t3 = ClipTempo.Bpm(src3, beat3, 0, 0, sr);
        var (sb, ps, pe) = ClipTempo.Scale(beat3, 0, 0, t3, t3 * 2);
        bool ratios = true;
        for (int i = 1; i < beat3.Length; i++) ratios &= Near(sb[i] / beat3[i], 2, 1e-12);
        yield return (ratios && ps == 0 && pe == 0, "tempo: scaling keeps segment ratios; play end 0 stays 0");
        yield return (Near(ClipTempo.Bpm(src3, sb, 0, 0, sr), t3 * 2, 1e-9), "tempo: the scaled clip reads the new tempo");
        // Trimmed window: only the played part counts, and the window scales with the beats.
        double tTrim = ClipTempo.Bpm(src3, beat3, 2, 5, sr);
        double seg2 = 3 * sr * 60.0 / 72000;
        yield return (Near(tTrim, seg2, 1e-9), "tempo: a trimmed window reads its own tempo");
        var (_, ps2, pe2) = ClipTempo.Scale(beat3, 2, 5, tTrim, tTrim / 2);
        yield return (Near(ps2, 1) && Near(pe2, 2.5), "tempo: the trim window scales");
        yield return (ClipTempo.Half(30) == null && ClipTempo.Double(600) == null, "tempo: ÷2 / ×2 stay in 20–999 BPM");
        yield return (ClipTempo.Half(87) == 43.5 && ClipTempo.Double(87) == 174, "tempo: ÷2 / ×2 in range");
        yield return (ClipTempo.Bpm(new double[] { 0 }, new double[] { 0 }, 0, 0, sr) == 0, "tempo: under two markers reads 0");

        // ---- WarpRemap ----------------------------------------------------------------
        double[] live = { 0, 2, 5, 8 };
        bool identity = true;
        for (double b = 0; b <= 8; b += 0.25) identity &= Near(WarpRemap.OldBeatAt(src3, beat3, live, b), b, 1e-9);
        yield return (identity, "remap: unchanged markers are the identity");
        double[] moved = { 0, 3, 5, 8 };   // the second marker dragged a beat later
        bool mono = true, ends = true;
        double prev = -1;
        for (double b = 0; b <= 8; b += 0.125) { double o = WarpRemap.OldBeatAt(src3, beat3, moved, b); mono &= o > prev; prev = o; }
        ends &= Near(WarpRemap.OldBeatAt(src3, beat3, moved, 0), 0) && Near(WarpRemap.OldBeatAt(src3, beat3, moved, 8), 8);
        yield return (mono, "remap: monotone over a dragged middle marker");
        yield return (ends, "remap: fixed end markers stay put");
        yield return (Near(WarpRemap.OldBeatAt(src3, beat3, moved, 3), 2, 1e-9), "remap: the dragged marker shows the audio it carries");
        bool inverse = true;
        for (double b = 0; b <= 8; b += 0.25) inverse &= Near(WarpRemap.NewBeatAt(src3, beat3, moved, WarpRemap.OldBeatAt(src3, beat3, moved, b)), b, 1e-9);
        yield return (inverse, "remap: NewBeatAt inverts OldBeatAt");
        double[] stretched = { 0, 2, 5, 10 };   // the last marker dragged out
        yield return (Near(WarpRemap.OldBeatAt(src3, beat3, stretched, 10), 8, 1e-9) && Near(WarpRemap.OldBeatAt(src3, beat3, stretched, 7.5), 6.5, 1e-9),
            "remap: dragging the last marker stretches the last segment");
    }
}
