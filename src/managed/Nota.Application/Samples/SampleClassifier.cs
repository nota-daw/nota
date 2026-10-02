// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application.Samples;

/// <summary>Turns a sample's name hints and measured analysis into a <see cref="SampleInfo"/>.
/// Names are trusted over the ear: an author who wrote "124" meant 124. The analysis fills
/// the gaps and settles a loop's exact tempo from its length — a loop is a whole number of
/// beats, so "16 beats in 7.742 s" is 124.0 BPM even when the onset tracker heard 123.5.</summary>
public static class SampleClassifier
{
    public const double OneShotMaxSec = 1.2;         // shorter than this is a hit, whatever it is called
    private const double DecayedTail = 0.12;         // last-tenth RMS under this share: it rang out
    private const double DecayedMaxSec = 6.0;        // …and a decaying sound longer than this is a texture, not a hit
    private const double FitTolerance = 0.04;        // length-derived tempo within 4 % of the heard one
    private const double KeyMinConfidence = 0.55;
    private const double TempoForLongMinSec = 8.0;   // a non-loop needs a few bars before its tempo means much
    private const double ShortMaxSec = 2.5;          // shorter, with no loop length: a hit, a vowel, a phrase

    // Loop lengths in beats: whole bars of 4/4 first, then the odd ones (3/4, half bars).
    private static readonly int[] LoopBeats = { 4, 8, 16, 32, 64, 2, 12, 24, 48, 3, 6 };

    public static SampleInfo Classify(string path, SampleNameHints hints, SampleAnalysis? a)
    {
        double dur = a?.DurationSec ?? 0;
        float[] timbre = a?.Timbre ?? Array.Empty<float>();

        // --- loop or one-shot -------------------------------------------------------------
        var kind = SampleKind.Unknown;
        var kindSrc = SampleFactSource.None;
        if (hints.NameKind != SampleKind.Unknown) { kind = hints.NameKind; kindSrc = SampleFactSource.Name; }
        else if (hints.FolderKind != SampleKind.Unknown) { kind = hints.FolderKind; kindSrc = SampleFactSource.Folder; }
        // Too short to loop is decisive, whatever a folder said.
        if (a is not null && dur > 0 && dur < OneShotMaxSec && kindSrc != SampleFactSource.Name)
        { kind = SampleKind.OneShot; kindSrc = SampleFactSource.Duration; }

        // --- tempo ------------------------------------------------------------------------
        double bpm = 0;
        var bpmSrc = SampleFactSource.None;
        double fit = a is not null && kind != SampleKind.OneShot ? LoopFit(dur, a.Bpm) : 0;
        if (hints.Bpm > 0 && (hints.BpmExplicit || kind == SampleKind.Loop || Agrees(hints.Bpm, fit, a?.Bpm ?? 0, dur)))
        {
            bpm = hints.Bpm; bpmSrc = SampleFactSource.Name;
            if (kind == SampleKind.Unknown && dur >= OneShotMaxSec) { kind = SampleKind.Loop; kindSrc = SampleFactSource.Name; }
        }
        else if (fit > 0 && kind != SampleKind.OneShot)
        {
            bpm = fit; bpmSrc = SampleFactSource.Duration;
            if (kind == SampleKind.Unknown && !(a!.TailRatio < DecayedTail && dur < DecayedMaxSec))
            { kind = SampleKind.Loop; kindSrc = SampleFactSource.Duration; }
        }
        else if (a is not null && a.Bpm > 0 && kind != SampleKind.OneShot && (kind == SampleKind.Loop || dur >= TempoForLongMinSec))
        {
            bpm = a.Bpm; bpmSrc = SampleFactSource.Audio;
        }

        if (kind == SampleKind.Unknown && a is not null)
        {
            // Rang out to silence, or named like a drum hit and short: a one-shot.
            if (a.TailRatio < DecayedTail && dur < DecayedMaxSec) { kind = SampleKind.OneShot; kindSrc = SampleFactSource.Audio; }
            else if (hints.DrumWord && dur < ShortMaxSec) { kind = SampleKind.OneShot; kindSrc = SampleFactSource.Name; }
            else if (dur < ShortMaxSec && bpm == 0) { kind = SampleKind.OneShot; kindSrc = SampleFactSource.Duration; }
        }
        if (kind == SampleKind.OneShot && bpmSrc != SampleFactSource.Name) { bpm = 0; bpmSrc = SampleFactSource.None; }

        // --- key --------------------------------------------------------------------------
        MusicalKey? key = null;
        var keySrc = SampleFactSource.None;
        if (hints.Key is { } nk) { key = nk; keySrc = SampleFactSource.Name; }
        else if (a is not null && a.KeyTonic is >= 0 and < 12 && a.KeyConfidence >= KeyMinConfidence)
        {
            // A hit has a pitch, not a scale: its "mode" is a coin toss between two profiles.
            var mode = kind == SampleKind.OneShot || a.KeyMode < 0 ? KeyMode.Note : (KeyMode)a.KeyMode;
            key = new MusicalKey(a.KeyTonic, mode);
            keySrc = SampleFactSource.Audio;
        }

        return new SampleInfo(path, dur, kind, kindSrc, Math.Round(bpm, 2), bpmSrc, key, keySrc, timbre);
    }

    /// <summary>The exact tempo of a loop <paramref name="durationSec"/> long whose pulse was
    /// heard at <paramref name="heardBpm"/>: the whole-beat count closest to it (the heard tempo
    /// may be off by an octave), or 0 when no musical loop length is within tolerance.</summary>
    public static double LoopFit(double durationSec, double heardBpm)
    {
        if (durationSec < OneShotMaxSec || heardBpm <= 0) return 0;
        double best = 0, bestErr = double.MaxValue;
        for (int i = 0; i < LoopBeats.Length; i++)
        {
            double bpm = LoopBeats[i] * 60.0 / durationSec;
            if (bpm is < 60 or > 200) continue;
            double err = double.MaxValue;
            foreach (double oct in new[] { 1.0, 2.0, 0.5 })
                err = Math.Min(err, Math.Abs(bpm - heardBpm * oct) / (heardBpm * oct) + (oct == 1.0 ? 0 : 0.005));
            err += i >= 5 ? 0.008 : 0;                   // prefer whole 4/4 bars on a near tie…
            err += bpm is < 75 or > 170 ? 0.015 : 0;     // …and the tempos loops are usually made at
            if (err < bestErr) { bestErr = err; best = bpm; }
        }
        if (bestErr > FitTolerance) return 0;
        // A file a few samples long or short of the grid still means a round tempo.
        double round = Math.Round(best);
        return Math.Abs(best - round) < 0.06 ? round : best;
    }

    // A bare number in a name is a tempo when the audio agrees: the loop-length fit or the
    // heard pulse lands within tolerance of it (or an octave of it).
    private static bool Agrees(double named, double fit, double heard, double dur)
    {
        if (fit > 0 && Math.Abs(fit - named) / named < FitTolerance) return true;
        if (heard > 0)
            foreach (double oct in new[] { 1.0, 2.0, 0.5 })
                if (Math.Abs(heard * oct - named) / named < FitTolerance) return true;
        // Or the file is a loop length of the named tempo's beats, to a few milliseconds.
        if (dur < OneShotMaxSec) return false;
        double beats = dur * named / 60.0;
        return Math.Abs(beats - Math.Round(beats)) < 0.02 && Array.IndexOf(LoopBeats, (int)Math.Round(beats)) >= 0;
    }
}
