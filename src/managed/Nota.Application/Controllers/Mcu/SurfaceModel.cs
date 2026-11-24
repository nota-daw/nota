// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What a Mackie Control / HUI surface should show (specs/051, data-model § SurfaceStripState):
// eight channel strips and the global lights, independent of the protocol. The app fills a
// wanted state from the engine each tick; the FeedbackDiffer compares it with what was last sent
// and the codec turns the differences into bytes (FR-023), so an unchanged surface costs nothing.

namespace Nota.Application.Controllers.Mcu;

/// <summary>One channel strip. Fader is 14-bit (0..16383); PanRing 0..11 (6 = centre, 0 = off);
/// Meter 0..12; Name up to the protocol's width (7 for MCU, 4 for HUI).</summary>
public record struct SurfaceStripState(int? TrackId, int Fader, int PanRing, bool Mute, bool Solo, bool Arm, bool Select, string Name, int Meter)
{
    public static SurfaceStripState Empty => new(null, 0, 0, false, false, false, false, "", 0);
}

/// <summary>The whole surface: strips, the master fader, transport lights, the time display.</summary>
public sealed class SurfaceState
{
    public const int Strips = 8;

    public SurfaceStripState[] Strip { get; } = Enumerable.Repeat(SurfaceStripState.Empty, Strips).ToArray();
    public int MasterFader { get; set; }
    public bool Play { get; set; }
    public bool Stop { get; set; }
    public bool Record { get; set; }
    public bool Loop { get; set; }
    /// <summary>Ten characters, left to right (bars, beats, sixteenths, ticks); MCU only.</summary>
    public string Time { get; set; } = "";
    /// <summary>The bottom scribble line (the knob mode) — MCU only.</summary>
    public string Assignment { get; set; } = "";

    public SurfaceState Clone()
    {
        var c = new SurfaceState { MasterFader = MasterFader, Play = Play, Stop = Stop, Record = Record, Loop = Loop, Time = Time, Assignment = Assignment };
        Array.Copy(Strip, c.Strip, Strips);
        return c;
    }

    /// <summary>A pan of −1..1 as a ring position 1..11 (6 = centre).</summary>
    public static int PanToRing(double pan) => Math.Clamp((int)Math.Round(6 + pan * 5), 1, 11);

    /// <summary>A peak (linear, 1 = 0 dBFS) on the 0..12 meter: 12 at 0 dBFS, one step per 5 dB, 0 below −60.</summary>
    public static int MeterLevel(double peak)
    {
        if (peak <= 1e-6) return 0;
        double db = 20 * Math.Log10(peak);
        return Math.Clamp((int)Math.Floor((db + 60) / 5) + 1, 0, 12);
    }

    /// <summary>The time display for <paramref name="beats"/> (quarter notes from the start) in a
    /// bar of <paramref name="beatsPerBar"/>: "BBBbbsstt" style — bar (3), beat (2), sixteenth (2), ticks (3).</summary>
    public static string TimeText(double beats, double beatsPerBar)
    {
        beats = Math.Max(0, beats);
        double bpb = Math.Max(0.25, beatsPerBar);
        int bar = (int)Math.Floor(beats / bpb + 1e-9);
        double inBar = beats - bar * bpb;
        int beat = (int)Math.Floor(inBar + 1e-9);
        double inBeat = inBar - beat;
        int six = (int)Math.Floor(inBeat * 4 + 1e-9);
        int ticks = (int)Math.Floor((inBeat * 4 - six) * 1000);
        return string.Concat(
            Pad(bar + 1, 3), Pad(beat + 1, 2), Pad(six + 1, 2), Pad(Math.Clamp(ticks, 0, 999), 3, '0'));

        static string Pad(int v, int width, char fill = ' ')
        {
            var s = (v % (int)Math.Pow(10, width)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return s.PadLeft(width, fill);
        }
    }
}
