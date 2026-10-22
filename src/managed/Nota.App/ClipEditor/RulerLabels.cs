// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Ruler text shared by the arrangement and both clip editors (specs/036-clip-editor-batch): a
// position reads in bars (9, 9.2, 9.2.3) or, while the transport read-out shows time, in time
// (0:16, 0:16.5 …), so every ruler on screen speaks the same unit as the read-out. Free of
// Avalonia so the smoke test can link it.

using System;
using System.Globalization;

namespace Nota.App;

internal static class RulerLabels
{
    /// <summary>Time steps (seconds) the rulers choose from, finest first.</summary>
    public static readonly double[] TimeSteps =
        { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200, 1800, 3600 };

    /// <summary>1-based bar.beat.16th of a beat position: "9" on a bar, "9.2" on a beat, else
    /// "9.2.3". <paramref name="stepBeats"/> is the label step (a 16th step shows 16ths).</summary>
    public static string Bars(double beat, double stepBeats, int beatsPerBar, IFormatProvider fmt)
    {
        beat = Math.Max(0, beat);
        int bpb = Math.Max(1, beatsPerBar);
        int bar = (int)Math.Floor(beat / bpb + 1e-9) + 1;
        double inBar = Math.Max(0, beat - (bar - 1) * bpb);
        int bt = (int)Math.Floor(inBar + 1e-9) + 1;
        double sub = inBar - (bt - 1);
        if (Math.Abs(inBar) < 1e-6 || Math.Abs(inBar - bpb) < 1e-6) return bar.ToString(fmt);
        if (sub < 1e-6 || stepBeats >= 1) return string.Format(fmt, "{0}.{1}", bar, bt);
        int six = (int)Math.Floor(sub * 4 + 1e-6) + 1;
        return string.Format(fmt, "{0}.{1}.{2}", bar, bt, six);
    }

    /// <summary>Seconds of a beat position at a fixed tempo, as the transport read-out counts.</summary>
    public static double Seconds(double beat, double bpm) => bpm > 0 ? Math.Max(0, beat) * 60.0 / bpm : 0;
    public static double BeatsAt(double seconds, double bpm) => seconds * bpm / 60.0;

    /// <summary>m:ss for steps of a second or more, then m:ss.f / .ff / .fff as the step shrinks.</summary>
    public static string Time(double seconds, double stepSeconds, IFormatProvider fmt)
    {
        seconds = Math.Max(0, seconds);
        int dec = stepSeconds >= 1 - 1e-9 ? 0 : stepSeconds >= 0.1 - 1e-9 ? 1 : stepSeconds >= 0.01 - 1e-9 ? 2 : 3;
        double scale = Math.Pow(10, dec);
        long units = (long)Math.Round(seconds * scale, MidpointRounding.AwayFromZero);   // round once, then split, so 59.9999 → 1:00
        long whole = units / (long)scale;
        long m = whole / 60, s = whole % 60;
        if (dec == 0) return string.Format(fmt, "{0}:{1:00}", m, s);
        long frac = units % (long)scale;
        return string.Format(fmt, "{0}:{1:00}.{2}", m, s, frac.ToString(new string('0', dec), CultureInfo.InvariantCulture));
    }
}

/// <summary>Whether the rulers read in time (true) or bars, following the transport read-out.</summary>
internal static class RulerDisplay
{
    public static bool ShowTime { get; private set; }
    public static event Action? Changed;

    public static void Set(bool showTime)
    {
        if (showTime == ShowTime) return;
        ShowTime = showTime;
        Changed?.Invoke();
    }
}
