// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic (kind 17) on the managed side: the value maps the native Mosaic.h uses (so
// the card, the MCP and the tests speak in dB / ms / voices), the telemetry layout, and a
// one-line summary of a patch.

using System;
using System.Collections.Generic;
using Nota.Application.Mosaic;

namespace Nota.Application;

public static class MosaicModel
{
    public const int Kind = 17;

    public static readonly int[] PolyCounts = { 16, 32, 64, 128 };
    public static readonly string[] VoiceNames = { "Poly", "Mono", "Choke" };
    public static readonly string[] FilterNames = { "Off", "LP", "HP", "BP" };
    public static readonly string[] LoopNames = { "Off", "Fwd", "Ping" };
    public static readonly string[] StealOrder = { "Released", "Quietest", "Oldest" };

    // ---- value maps (native Mosaic.h) --------------------------------------------------------
    public static int Index(float v, int n) => n <= 1 ? 0 : Math.Clamp((int)Math.Round(v * (n - 1)), 0, n - 1);
    public static int Poly(float v) => PolyCounts[Index(v, 4)];
    public static float PolyNorm(int voices) { int i = Array.IndexOf(PolyCounts, voices); return i < 0 ? 2f / 3f : i / 3f; }
    public static int PerKey(float v) => 1 + Index(v, 4);
    public static double OldFadeMs(float v) => Math.Pow(200.0, Math.Clamp(v, 0, 1));                   // 1 … 200 ms
    public static float OldFadeNorm(double ms) => (float)Math.Clamp(Math.Log(Math.Max(1, ms)) / Math.Log(200.0), 0, 1);
    public static double RelVolDb(float v) => -36.0 + 42.0 * Math.Clamp(v, 0, 1);
    public static float RelVolNorm(double db) => (float)Math.Clamp((db + 36.0) / 42.0, 0, 1);
    public static double RelLenDb(float v) => -24.0 * Math.Clamp(v, 0, 1);
    public static float RelLenNorm(double db) => (float)Math.Clamp(-db / 24.0, 0, 1);
    public static int BendSemis(float v) => (int)Math.Round(24.0 * Math.Clamp(v, 0, 1));
    /// <summary>The curve as a signed amount, −100 (soft) … +100 (hard).</summary>
    public static int VelCurve(float v) => (int)Math.Round((Math.Clamp(v, 0, 1) - 0.5) * 200);
    public static double VelCurveAt(double vel, float c) => Math.Pow(Math.Clamp(vel, 0, 1), Math.Pow(4.0, (Math.Clamp(c, 0, 1) - 0.5) * 2.0));
    public static double VolumeDb(float v) => v <= 1e-5f ? double.NegativeInfinity : 20 * Math.Log10(v);
    public static double TransposeSt(float v) => Math.Round((v - 0.5) * 48.0);
    public static double DetuneCents(float v) => (v - 0.5) * 100.0;
    public static double GlideSec(float v) => v <= 0.001f ? 0 : 0.001 * Math.Pow(2000.0, Math.Clamp(v, 0, 1));
    public static double EnvOctaves(float v) => (Math.Clamp(v, 0, 1) - 0.5) * 12.0;
    public static double AttackSec(float v) => 0.0005 * Math.Pow(4.0 / 0.0005, Math.Clamp(v, 0, 1));
    public static double DecaySec(float v) => 0.002 * Math.Pow(6.0 / 0.002, Math.Clamp(v, 0, 1));
    public static double ReleaseSec(float v) => DecaySec(v);
    public static double CutoffHz(float v) => 20.0 * Math.Pow(1000.0, Math.Clamp(v, 0, 1));
    /// <summary>Seconds → the normalized attack (0.5 ms … 4 s) / decay or release (2 ms … 6 s).</summary>
    public static float AttackNorm(double s) => (float)Math.Clamp(Math.Log(Math.Max(0.0005, s) / 0.0005) / Math.Log(4.0 / 0.0005), 0, 1);
    public static float DecayNorm(double s) => (float)Math.Clamp(Math.Log(Math.Max(0.002, s) / 0.002) / Math.Log(6.0 / 0.002), 0, 1);

    // ---- telemetry (Mosaic::scopeRead) -------------------------------------------------------
    public const int ScVoices = 0, ScLimit = 1, ScHeld = 2, ScPedalHeld = 3, ScRelease = 4, ScPedal = 5, ScPeak = 6,
                     ScPos = 7, ScEnv = 8, ScStage = 9, ScNote = 10, ScCutoff = 11, ScZone = 12, ScLastNote = 13,
                     ScLastVel = 14, ScSounding = 15, ScHead = 16, ScKeys = 16 + 112, ScopeLength = 16 + 112 + 8;

    /// <summary>A zone that sounds now: 0 held · 1 held by the pedal · 2 release / fading.</summary>
    public readonly record struct Sounding(int Zone, int State);

    public sealed class Snapshot
    {
        public bool Live;
        public int Voices, Limit, Held, PedalHeld, Release, Zone = -1, LastNote = -1, LastVel, Stage = -1;
        public bool Pedal;
        public double Peak, Pos = -1, Env, Note = -1, CutoffHz = -1;
        public readonly List<Sounding> Zones = new();
        /// <summary>Keys held or sustained now.</summary>
        public readonly HashSet<int> Keys = new();
    }

    public static void Parse(ReadOnlySpan<float> sc, Snapshot s)
    {
        s.Zones.Clear(); s.Keys.Clear();
        if (sc.Length < ScHead) { s.Live = false; s.Voices = 0; return; }
        s.Voices = (int)sc[ScVoices]; s.Limit = (int)sc[ScLimit]; s.Held = (int)sc[ScHeld];
        s.PedalHeld = (int)sc[ScPedalHeld]; s.Release = (int)sc[ScRelease]; s.Pedal = sc[ScPedal] > 0.5f;
        s.Peak = sc[ScPeak]; s.Pos = sc[ScPos]; s.Env = sc[ScEnv]; s.Stage = (int)sc[ScStage];
        s.Note = sc[ScNote]; s.CutoffHz = sc[ScCutoff]; s.Zone = (int)sc[ScZone];
        s.LastNote = (int)sc[ScLastNote]; s.LastVel = (int)sc[ScLastVel];
        s.Live = s.Voices > 0;
        int n = Math.Min((int)sc[ScSounding], sc.Length - ScHead);
        for (int i = 0; i < n; i++)
        {
            int code = (int)sc[ScHead + i];
            s.Zones.Add(new Sounding(code / 4, code % 4));
        }
        if (sc.Length >= ScopeLength)
            for (int i = 0; i < 8; i++)
            {
                int bits = (int)sc[ScKeys + i];
                for (int b = 0; b < 16; b++) if ((bits & (1 << b)) != 0) s.Keys.Add(i * 16 + b);
            }
    }

    // ---- words -----------------------------------------------------------------------------
    public static string Megabytes(long bytes) => bytes <= 0 ? "0 MB" : bytes < 1024L * 1024 * 1024
        ? $"{Math.Max(1, Math.Round(bytes / 1048576.0))} MB"
        : $"{(bytes / 1073741824.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} GB";

    /// <summary>"Osiris Piano · 30 roots × 4 velocity layers · release 30".</summary>
    public static string Summary(MosaicProgram p, Func<string, float> g)
    {
        if (p.IsEmpty) return "Empty · drop samples or a folder, or pick a preset";
        string voices = VoiceNames[Index(g("voicemode"), 3)] == "Poly" ? $"Poly {Poly(g("polyphony"))}" : VoiceNames[Index(g("voicemode"), 3)];
        return $"{(p.Name.Length > 0 ? p.Name + " · " : "")}{p.Summary()} · {voices}";
    }

    public const string Guide =
        "Nota Mosaic plays a multisample: zones (a sample on a key × velocity rectangle, with its root) in groups "
        + "(velocity layers with a shared gain/tune and a round-robin). Every zone covering the key, the velocity and the "
        + "group's round-robin step sounds; release zones sound when the key (or the pedal) lets go, quieter the longer the "
        + "note was held. Samples load in the background into RAM — notes are silent until the program is in. "
        + "Shared controls: amp ADSR, filter (Off/LP/HP/BP, cutoff, reso, key tracking, env → cutoff), glide, Vel → Vol with a "
        + "velocity curve, polyphony 16/32/64/128, per-key strike limit, old-voice fade, sustain pedal (CC64), MPE "
        + "(bend range, pressure → level, slide → cutoff).";
}
