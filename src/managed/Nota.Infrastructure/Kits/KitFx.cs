// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The factory kits' per-pad effects: the insert chain a pad gets when its kit loads — as
// real built-in devices on the Drum Rack pad's chain, or on the Nota Rhythm voice it lands on,
// from the same recipe. The rendered one-shots stay dry (the Room baked into some of them
// aside); the FX are live devices the user can tweak or delete.
//
// What each voice gets, by its role in the kit:
//
//   Kick         parallel saturation (Forge, Tube; Tape on the lo-fi kits) — harmonics that
//                carry the kick on small speakers while the dry sub and transient stay intact.
//                No reverb: a reverberant kick muddies everything under 200 Hz.
//   Snare        a short plate with a little pre-delay and its lows cut, so the tail opens the
//                hit up without clouding the low mids.
//   Clap         a room, a touch longer and wetter than the snare's — a clap is its space.
//   Closed hat   a 1/16 ping-pong echo, very quiet and high-passed: it lands on the grid, so it
//                adds shimmer and motion to the groove rather than smearing it.
//   Open hat,    a dotted-1/8 ping-pong delay, quiet, band-limited: the off-grid echo that makes
//   percussion   a pattern breathe.
//   Toms, cymbals, 808 bass: none — they already ring, and a sub needs to stay mono and dry.
//
// A kit scales this with FxSpace (reverb size / decay / level, delay level) and FxDrive (the
// saturation), and FxTape swaps the kick's Tube for Tape. A pad with Room baked into its
// sample gets proportionally less reverb, and none past 0.4.
//
// Params are named as the devices name them and given in the devices' own units: Reverb,
// Delay and Forge are normalized 0..1 (the helpers below map seconds / hertz / dB onto them).
// A reverb or delay keeps its dry at unity — its Dry Level is set against the mix, since the
// devices crossfade dry and wet — so an effect only ever adds to the hit.

using System;
using System.Collections.Generic;

namespace Nota.Infrastructure.Kits;

/// <summary>One effect of a pad's chain: a built-in device kind (as <c>RackAddChainDevice</c>
/// takes it) and the params to set, by the device's param name, in its own units.</summary>
public sealed record FxDevice(int Kind, IReadOnlyList<(string Param, float Value)> Params);

/// <summary>What a pad is in its kit, as far as its effects are concerned.</summary>
public enum PadRole { Kick, Sub, Snare, Clap, ClosedHat, OpenHat, Tom, Cymbal, Perc }

public static class KitFx
{
    public const int Reverb = 2, Delay = 3, Forge = 17;

    /// <summary>The insert chain for <paramref name="pad"/> of <paramref name="kit"/> (empty = none).</summary>
    public static IReadOnlyList<FxDevice> For(KitDefinition kit, KitPad pad)
    {
        double space = kit.FxSpace;
        // Room already rendered into the sample: less reverb on top, none on a wet sample.
        double baked = Math.Clamp(1.0 - pad.Room / 0.4, 0.0, 1.0);
        var fx = new List<FxDevice>();
        switch (RoleOf(pad))
        {
            case PadRole.Kick:
                fx.Add(Saturation(kit.FxTape, kit.FxDrive));
                break;
            case PadRole.Snare:
                if (baked > 0.05) fx.Add(Space(algo: Plate, seconds: 0.85 + 0.45 * space, preMs: 18, size: 0.3 + 0.25 * space,
                                               lowCutHz: 320, highCutHz: 9000, mix: 0.2 * space * baked));
                break;
            case PadRole.Clap:
                if (baked > 0.05) fx.Add(Space(algo: Room, seconds: 1.0 + 0.6 * space, preMs: 12, size: 0.45 + 0.25 * space,
                                               lowCutHz: 380, highCutHz: 10000, mix: 0.2 * space * baked));
                break;
            case PadRole.ClosedHat:
                fx.Add(Echo(div: Sixteenth, feedback: 0.12, lowCutHz: 2000, highCutHz: 12000, mix: 0.1 * Math.Sqrt(space)));
                break;
            case PadRole.OpenHat:
                fx.Add(Echo(div: DottedEighth, feedback: 0.22, lowCutHz: 1200, highCutHz: 10000, mix: 0.12 * Math.Sqrt(space)));
                break;
            case PadRole.Perc:
                fx.Add(Echo(div: DottedEighth, feedback: 0.3, lowCutHz: 450, highCutHz: 7500, mix: 0.16 * Math.Sqrt(space)));
                break;
        }
        return fx;
    }

    public static PadRole RoleOf(KitPad pad)
    {
        switch (pad.Model)
        {
            case DrumModel.KickAnalog: case DrumModel.KickPunch: case DrumModel.KickAcoustic: return PadRole.Kick;
            case DrumModel.SubHit: return PadRole.Sub;
            case DrumModel.SnareAnalog: case DrumModel.SnarePunch: case DrumModel.SnareAcoustic: return PadRole.Snare;
            case DrumModel.Clap: return PadRole.Clap;
            case DrumModel.HatMetal: case DrumModel.HatNoise:
                return pad.Name.Contains("Open", StringComparison.OrdinalIgnoreCase) ? PadRole.OpenHat : PadRole.ClosedHat;
            case DrumModel.Cymbal: return PadRole.Cymbal;
            // A tom-model pad outside the tom slots is a pitched percussion voice (log drums).
            case DrumModel.Tom: return pad.Note is 41 or 43 or 45 or 47 or 48 or 50 ? PadRole.Tom : PadRole.Perc;
            default: return PadRole.Perc;
        }
    }

    // ---- devices -------------------------------------------------------------------------

    private const int Room = 1, Plate = 2;              // Reverb algorithms: Hall, Room, Plate, Chamber
    private const int Sixteenth = 0, DottedEighth = 3;   // Delay's division list: 1/16, 1/8T, 1/8, 1/8., 1/4T, 1/4, 1/4., 1/2

    // Parallel saturation: a few dB of drive into one Tube (or Tape) stage, half wet. The output
    // trim takes back what the wet half adds, so the pad lands at the level it had.
    private static FxDevice Saturation(bool tape, double drive) => new(Forge, new[]
    {
        ("Amount", (float)Math.Clamp(0.14 * drive, 0, 0.5)),
        ("S1 Type", tape ? 2f / 5f : 0f),                  // Tube / Tape
        ("S1 Drive", (float)Math.Clamp(0.32 * drive, 0, 1)),
        ("S1 On", 1f), ("S2 On", 0f), ("S3 On", 0f),
        ("Routing", 0f),
        ("Tone", 0.46f),                                    // a hair darker: the harmonics, not fizz
        ("Wet", 0.5f),
        ("Output", (float)(0.5 + SaturationTrimDb / 48.0)),
    });

    /// <summary>The output trim of the kick saturation, in dB — measured so the pad's RMS stays
    /// within about a dB of the dry sample's (see the smoke test).</summary>
    public const double SaturationTrimDb = -2.0;

    private static FxDevice Space(int algo, double seconds, double preMs, double size,
                                  double lowCutHz, double highCutHz, double mix)
    {
        mix = Math.Clamp(mix, 0, 0.6);
        return new(Reverb, new[]
        {
            ("Algorithm", algo / 3f),
            ("Decay", Exp(seconds, 0.2, 12.0)),
            ("Pre-Delay", (float)Math.Clamp(preMs / 200.0, 0, 1)),
            ("Size", (float)Math.Clamp(size, 0, 1)),
            ("Diffusion", 0.7f),
            ("HF Damp", 0.45f),
            ("Low Cut", Exp(lowCutHz, 20, 1000)),
            ("High Cut", Exp(highCutHz, 1000, 20000)),
            ("Width", 0.85f),
            ("Dry/Wet", (float)mix),
            ("Dry Level", UnityDry(mix)),
        });
    }

    private static FxDevice Echo(int div, double feedback, double lowCutHz, double highCutHz, double mix)
    {
        mix = Math.Clamp(mix, 0, 0.6);
        return new(Delay, new[]
        {
            ("Sync", 1f),
            ("Div L", div / 7f), ("Div R", div / 7f), ("Link L/R", 1f),
            ("Ping-Pong", 1f),
            ("Feedback", (float)Math.Clamp(feedback, 0, 0.9)),
            ("Low Cut", Exp(lowCutHz, 20, 2000)),
            ("High Cut", Exp(highCutHz, 200, 20000)),
            ("Wow Depth", 0.05f),
            ("Dry/Wet", (float)mix),
            ("Dry Level", UnityDry(mix)),
        });
    }

    // The devices' exponential knobs: value = lo · (hi / lo)^norm.
    private static float Exp(double v, double lo, double hi) => (float)Math.Clamp(Math.Log(v / lo) / Math.Log(hi / lo), 0, 1);

    // Dry gain is (1 - mix) · 2 · level²; this level keeps it at 1.
    private static float UnityDry(double mix) => (float)Math.Clamp(Math.Sqrt(0.5 / (1 - mix)), 0, 1);

    /// <summary>Builds <paramref name="fx"/> through a chain's device surface: <paramref name="add"/>
    /// appends a device of a kind and returns its index (-1 on failure); the rest address the
    /// device's params by index. Params the device doesn't have are skipped.</summary>
    public static int Apply(IReadOnlyList<FxDevice> fx, Func<int, int> add, Func<int, int> paramCount,
                            Func<int, int, string> paramName, Action<int, int, float> set)
    {
        int added = 0;
        foreach (var d in fx)
        {
            int di = add(d.Kind);
            if (di < 0) continue;
            added++;
            int n = paramCount(di);
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int p = 0; p < n; p++) index[paramName(di, p)] = p;
            foreach (var (name, value) in d.Params)
                if (index.TryGetValue(name, out int pi)) set(di, pi, value);
        }
        return added;
    }
}
