// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The recipe for one drum one-shot, and its placement on a Drum Rack pad. A kit is
// just a list of these: the whole factory library is code, not audio files, so the
// installer carries recipes (a few kilobytes) instead of a sample library, and the
// WAVs are rendered on the user's machine (see KitLibrary).
//
// Parameters are deliberately shared across models rather than one struct per voice —
// "Freq / Decay / Tone / Body / Noise / Click / Drive" mean the analogous thing
// everywhere, so a recipe reads the same whether it drives a kick or a cowbell. The
// per-model meaning of Tone and Body is documented on DrumModel.

namespace Nota.Infrastructure.Kits;

/// <summary>The synthesis model behind a one-shot. Each is a compact physical or
/// analog-circuit sketch rather than a general synth voice.</summary>
public enum DrumModel
{
    /// <summary>Bridged-T style analog kick: a long sine ring with a pitch drop and a
    /// trigger click. Tone = knock (upper body) amount. Body = sub sustain.</summary>
    KickAnalog,
    /// <summary>Punchy dance kick: deep, fast pitch envelope over a shorter ring, with a
    /// pronounced attack pulse. Tone = attack brightness.</summary>
    KickPunch,
    /// <summary>Acoustic bass drum: shell resonances excited by a beater, over a short
    /// sub. Tone = beater brightness. Body = shell ring.</summary>
    KickAcoustic,
    /// <summary>A long, deep, tuned sine hit — the sub-bass "808" that gets played as a
    /// pitched note rather than a drum. Tone = harmonic drive.</summary>
    SubHit,
    /// <summary>Analog snare: two shell tones plus high-passed noise. Tone = tone↔noise
    /// balance. Body = tone-oscillator ring.</summary>
    SnareAnalog,
    /// <summary>Noisier dance snare: band-passed noise over two short tones and a crack.
    /// Tone = noise brightness.</summary>
    SnarePunch,
    /// <summary>Acoustic snare: shell modes, snare-wire noise and a stick crack. Tone =
    /// wire brightness. Body = shell ring.</summary>
    SnareAcoustic,
    /// <summary>Hand clap: a burst train into a resonant band, then a reverberant tail.
    /// Tone = band brightness. Body = tail level.</summary>
    Clap,
    /// <summary>Rimshot / side stick: a very short cluster of high-Q modes.</summary>
    Rim,
    /// <summary>Metallic hat: six detuned square oscillators through a bright band.
    /// Tone = noise blended into the metal. Body = band resonance.</summary>
    HatMetal,
    /// <summary>Noise hat: filtered noise with a pair of resonances — the softer,
    /// airier hat used by acoustic and lo-fi kits.</summary>
    HatNoise,
    /// <summary>Cymbal (crash / ride): a dense partial cluster plus noise, decaying in
    /// three bands so the highs wash out before the body does. Tone = ping vs. wash.</summary>
    Cymbal,
    /// <summary>Tom: pitched membrane with a pitch drop, skin noise and optional shell
    /// modes. Body = shell ring.</summary>
    Tom,
    /// <summary>Hand drum (conga / bongo / tabla-ish): membrane modes struck with a
    /// filtered slap. Tone = slap brightness.</summary>
    Conga,
    /// <summary>Shaker / maraca: granular band-passed noise with a swell. Tone = band
    /// centre. Body = grain density.</summary>
    Shaker,
    /// <summary>Cowbell: two square oscillators through a narrow band.</summary>
    Cowbell,
    /// <summary>Woodblock / clave: a couple of very short, very high-Q modes.</summary>
    Block,
    /// <summary>Zap / laser: a fast downward pitch sweep with optional FM.</summary>
    Zap,
    /// <summary>Metallic percussion: ring-modulated partials with a modal ring — bells,
    /// clanks, triangle-ish hits. Tone = modulator ratio.</summary>
    PercMetal,
}

/// <summary>One pad of a factory kit: the recipe for the one-shot plus where it sits on
/// the Drum Rack. Defaults are the neutral case, so a recipe only names what it bends.</summary>
public sealed record KitPad
{
    // --- identity / placement ---------------------------------------------
    public required string Name { get; init; }
    public required DrumModel Model { get; init; }
    /// <summary>MIDI note the pad triggers (36 = C1, the first pad of bank 1).</summary>
    public required int Note { get; init; }
    /// <summary>Drum Rack choke group (0 = none). Hats share one so closed cuts open.</summary>
    public int Choke { get; init; }
    /// <summary>Pad pan in the rack, -1..+1 — the file itself stays centred.</summary>
    public float Pan { get; init; }
    /// <summary>Pad gain in the rack (1 = unity), for kit balance beyond the rendered level.</summary>
    public float Gain { get; init; } = 1f;

    // --- core synthesis ----------------------------------------------------
    /// <summary>Base frequency in Hz — the pitch of the fundamental mode or oscillator.</summary>
    public double Freq { get; init; } = 60;
    /// <summary>Amplitude decay to -60 dB, in seconds.</summary>
    public double Decay { get; init; } = 0.4;
    /// <summary>Extra pitch at the attack, as a multiple of <see cref="Freq"/>
    /// (2 = starts an octave and a half up).</summary>
    public double PitchAmt { get; init; }
    /// <summary>Time constant of the pitch drop, in seconds.</summary>
    public double PitchDecay { get; init; } = 0.03;
    /// <summary>Noise layer level, 0..1 (snare wires, skin, breath).</summary>
    public double Noise { get; init; }
    /// <summary>Decay of the noise layer, in seconds.</summary>
    public double NoiseDecay { get; init; } = 0.12;
    /// <summary>Model-specific timbre control, 0..1 — see <see cref="DrumModel"/>.</summary>
    public double Tone { get; init; } = 0.5;
    /// <summary>Model-specific body / resonance amount, 0..1.</summary>
    public double Body { get; init; } = 0.5;
    /// <summary>Transient / attack level, 0..1.</summary>
    public double Click { get; init; }

    // --- colour ------------------------------------------------------------
    /// <summary>Saturation, 0..1 — asymmetric, so it thickens as well as compresses.</summary>
    public double Drive { get; init; }
    /// <summary>Analog glue, 0..1: a low-mid lift plus an upper-treble roll-off. The
    /// difference between a synthetic hit and one that sounds like it went through
    /// circuitry. Applied to every sample; 0 disables it.</summary>
    public double Warmth { get; init; } = 0.35;
    /// <summary>Post high-pass in Hz (0 = off) — clears sub rumble off small voices.</summary>
    public double HpHz { get; init; }
    /// <summary>Post low-pass in Hz (0 = off).</summary>
    public double LpHz { get; init; }
    /// <summary>Random pitch/level variation, 0..1 — the drift of an analog circuit.
    /// Deterministic per sample (seeded), so it varies between pads, not between renders.</summary>
    public double Drift { get; init; }
    /// <summary>Noise-floor bed, 0..1, shaped by the hit's own envelope.</summary>
    public double Hiss { get; init; }

    // --- space -------------------------------------------------------------
    /// <summary>Room send, 0..1. Above 0 the sample is rendered in stereo.</summary>
    public double Room { get; init; }
    public double RoomSize { get; init; } = 0.4;
    public double RoomDecay { get; init; } = 0.15;
    public double RoomDamp { get; init; } = 0.55;
    /// <summary>Stereo width of the room, 0..1 (0 = mono-summed room).</summary>
    public double Width { get; init; } = 0.7;
    /// <summary>Gated reverb: cut the room tail dead this many seconds after the hit
    /// (0 = let it decay). The 80s gated-snare sound.</summary>
    public double RoomGate { get; init; }

    // --- lo-fi -------------------------------------------------------------
    /// <summary>Lo-fi amount, 0..1 — scales the bit/rate crush and its band-limiting.</summary>
    public double Lofi { get; init; }
    /// <summary>Bit depth for the crusher (24 = clean).</summary>
    public double Bits { get; init; } = 24;
    /// <summary>Sample-and-hold rate of the crusher, in Hz (0 = off).</summary>
    public double CrushHz { get; init; }

    // --- output ------------------------------------------------------------
    /// <summary>Rendered length before tail trimming, in seconds.</summary>
    public double Length { get; init; } = 1.2;
    /// <summary>Normalization target in dBFS. Voices are levelled against each other
    /// here, so a kit sounds balanced before the user touches a pad fader.</summary>
    public double PeakDb { get; init; } = -3;
}

/// <summary>A factory kit — a named set of pads plus the kit-level Drum Rack settings.</summary>
public sealed record KitDefinition
{
    public required string Id { get; init; }          // stable, path- and preset-safe
    public required string Name { get; init; }
    public required string Blurb { get; init; }       // one line for the browser row
    public required IReadOnlyList<KitPad> Pads { get; init; }
    /// <summary>Drum Rack swing, 0..1.</summary>
    public float Swing { get; init; }
    /// <summary>Drum Rack humanize, 0..1.</summary>
    public float Humanize { get; init; }

    // --- live pad FX (KitFx) — they don't touch the rendered samples ------------------------
    /// <summary>How much space the kit's pad FX add: scales the reverbs' size, decay and level
    /// and the delays' level. 1 = a studio-dry kit's modest room; 0 would leave only the kick
    /// saturation.</summary>
    public double FxSpace { get; init; } = 1.0;
    /// <summary>Scales the kick's parallel saturation.</summary>
    public double FxDrive { get; init; } = 1.0;
    /// <summary>Tape rather than Tube for the kick saturation — the lo-fi and vintage kits.</summary>
    public bool FxTape { get; init; }
}
