// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application.Samples;

public enum SampleKind : byte { Unknown, Loop, OneShot }

/// <summary>Where a fact about a sample came from — a name the pack's author wrote is
/// trusted over what the analysis heard.</summary>
public enum SampleFactSource : byte { None, Name, Folder, Duration, Audio }

/// <summary>What the engine measured in a sample (<c>NotaSampleAnalysis</c>). Raw: the
/// <see cref="SampleClassifier"/> turns it, with the file's name, into a <see cref="SampleInfo"/>.</summary>
public sealed record SampleAnalysis(
    double DurationSec,
    double SampleRate,
    int Channels,
    double Bpm,              // 0 = no steady pulse
    int KeyTonic,            // -1 = atonal
    int KeyMode,             // 0 major, 1 minor, -1 tonic only
    float KeyConfidence,
    float TailRatio,         // last-tenth RMS / whole RMS
    float PeakDb,
    float RmsDb,
    float[] Timbre)
{
    public const int TimbreDims = 16;

    /// <summary>Bumped when the engine's analysis changes enough that stored results should be redone.</summary>
    public const int Version = 1;
}

/// <summary>The browser's view of one sample: loop or one-shot, tempo, key, and the
/// timbre fingerprint "similar sounds" compares.</summary>
public sealed record SampleInfo(
    string Path,
    double DurationSec,
    SampleKind Kind,
    SampleFactSource KindSource,
    double Bpm,                      // 0 = none
    SampleFactSource BpmSource,
    MusicalKey? Key,
    SampleFactSource KeySource,
    float[] Timbre)
{
    /// <summary>Beats the sample spans at its own tempo (0 without one).</summary>
    public double Beats => Bpm > 0 ? DurationSec * Bpm / 60.0 : 0;

    /// <summary>The row tag: "124 · Am", "93.5", "F#", "" — tempo first, it is what loops are picked by.</summary>
    public string Tag
    {
        get
        {
            var parts = new List<string>(2);
            if (Bpm > 0) parts.Add(FormatBpm(Bpm));
            if (Key is { } k) parts.Add(k.Short);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>"124 BPM · A minor · loop · 7.7 s" (thin spaces before units) for the row's tooltip.</summary>
    public string Describe()
    {
        var parts = new List<string>(4);
        if (Bpm > 0) parts.Add(FormatBpm(Bpm) + "\u2009BPM");
        if (Key is { } k) parts.Add(k.Long);
        if (Kind != SampleKind.Unknown) parts.Add(Kind == SampleKind.Loop ? "loop" : "one-shot");
        if (DurationSec > 0) parts.Add(DurationSec.ToString(DurationSec < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + "\u2009s");
        return string.Join(" · ", parts);
    }

    public static string FormatBpm(double bpm)
    {
        double r = Math.Round(bpm, 1);
        return r.ToString(Math.Abs(r - Math.Round(r)) < 0.05 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
