// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The synthesis models behind the factory kits. Each writes one dry mono one-shot into
// a buffer running at the renderer's oversampled rate (see KitRenderer) — the analog
// glue, room, lo-fi and normalization stages live there, so everything in this file is
// just the sound source.
//
// The models are sketches of how the real instruments make sound rather than generic
// synth patches: analog drum voices are modelled as rung resonators with a trigger
// pulse (which is what the bridged-T networks in those machines actually are), and the
// acoustic ones as a struck body — a short excitation feeding a handful of modes.
// That is what keeps them from sounding like a sine with an envelope on it.

using System;

namespace Nota.Infrastructure.Kits;

internal static class KitVoices
{
    private const double TwoPi = Math.PI * 2;

    /// <summary>Renders the dry voice for <paramref name="p"/> into <paramref name="buf"/>
    /// (mono, <paramref name="sr"/> = the oversampled render rate).</summary>
    public static void Render(KitPad p, double[] buf, double sr, Rng rng)
    {
        // Analog drift: a small, per-sample-but-fixed detune, so no two pads of a kit sit
        // at exactly their nominal pitch — real machines never do.
        double freq = p.Freq * (1 + (rng.Next() * 0.012 + 0.004 * rng.Next()) * p.Drift * 2);

        switch (p.Model)
        {
            case DrumModel.KickAnalog:    Kick(p, buf, sr, rng, freq, analog: true); break;
            case DrumModel.KickPunch:     Kick(p, buf, sr, rng, freq, analog: false); break;
            case DrumModel.KickAcoustic:  KickAcoustic(p, buf, sr, rng, freq); break;
            case DrumModel.SubHit:        SubHit(p, buf, sr, rng, freq); break;
            case DrumModel.SnareAnalog:   SnareAnalog(p, buf, sr, rng, freq); break;
            case DrumModel.SnarePunch:    SnarePunch(p, buf, sr, rng, freq); break;
            case DrumModel.SnareAcoustic: SnareAcoustic(p, buf, sr, rng, freq); break;
            case DrumModel.Clap:          Clap(p, buf, sr, rng, freq); break;
            case DrumModel.Rim:           Rim(p, buf, sr, rng, freq); break;
            case DrumModel.HatMetal:      Metal(p, buf, sr, rng, freq, cymbal: false); break;
            case DrumModel.Cymbal:        Metal(p, buf, sr, rng, freq, cymbal: true); break;
            case DrumModel.HatNoise:      HatNoise(p, buf, sr, rng, freq); break;
            case DrumModel.Tom:           Tom(p, buf, sr, rng, freq); break;
            case DrumModel.Conga:         Conga(p, buf, sr, rng, freq); break;
            case DrumModel.Shaker:        Shaker(p, buf, sr, rng, freq); break;
            case DrumModel.Cowbell:       Cowbell(p, buf, sr, rng, freq); break;
            case DrumModel.Block:         Block(p, buf, sr, rng, freq); break;
            case DrumModel.Zap:           Zap(p, buf, sr, rng, freq); break;
            default:                      PercMetal(p, buf, sr, rng, freq); break;
        }
    }

    // --- shared helpers ----------------------------------------------------

    // A 0.4 ms raised-cosine fade-in. Drum voices start at full level; without this the
    // buffer opens on a step, which reads as a click and eats headroom after saturation.
    private static double AttackRamp(int i, double sr)
    {
        int n = (int)(sr * 0.0004);
        return i >= n ? 1.0 : 0.5 - 0.5 * Math.Cos(Math.PI * i / n);
    }

    // Band-limited noise. Real drum noise — wires, skin, beads, air — is not white: it
    // rolls off well before 20 kHz, and noise that does not is the single clearest tell
    // of a synthetic drum. Every noise layer here goes through one of these.
    private sealed class NoiseGen
    {
        private readonly Rng _rng;
        private readonly Biquad _lp;
        public NoiseGen(Rng rng, double cutoff, double sr)
        {
            _rng = rng;
            _lp = Biquad.LowPass(cutoff, 0.7071, sr);
        }
        public double Next() => _lp.Process(_rng.NextGauss());
    }

    // A short noise burst — the "strike". Modal bodies need a physical excitation with
    // some width to them; a single-sample impulse rings the modes but sounds thin.
    private static double Excite(int i, double sr, double ms, Rng rng)
    {
        int n = Math.Max(1, (int)(sr * ms * 0.001));
        if (i >= n) return 0;
        double t = 1.0 - (double)i / n;
        return (i == 0 ? 1.0 : rng.NextGauss()) * t * t;
    }

    // --- kicks -------------------------------------------------------------

    private static void Kick(KitPad p, double[] buf, double sr, Rng rng, double freq, bool analog)
    {
        double ampC = KitDsp.DecayCoef(p.Decay, sr);
        // The punchy variant drops faster and further: that steep sweep through the
        // low mids is the "thwack" a long analog ring doesn't have.
        double pitchTau = p.PitchDecay * (analog ? 1.0 : 0.55);
        double pitchAmt = p.PitchAmt <= 0 ? (analog ? 1.6 : 3.2) : p.PitchAmt;
        double pitchC = Math.Exp(-1.0 / Math.Max(1e-4, pitchTau * sr));

        var knock = new Modal(freq * 4.2, Math.Min(p.Decay * 0.25, 0.09), sr);
        var clickBp = Biquad.BandPass(analog ? 1400 : 2600, 0.9, sr);
        var clickHp = Biquad.HighPass(600, 0.7071, sr);
        var clickN = new NoiseGen(rng, analog ? 9000 : 13000, sr);
        double clickC = KitDsp.DecayCoef(analog ? 0.004 : 0.008, sr);

        double env = 1, penv = 1, cenv = 1, ph = 0;
        double sub = 0.55 + p.Body * 0.65;
        for (int i = 0; i < buf.Length; i++)
        {
            double f = freq * (1 + pitchAmt * penv);
            ph += f / sr; if (ph >= 1) ph -= 1;
            double s = Math.Sin(TwoPi * ph) * env * sub;

            // Upper body ("knock") — the mid ring that survives on a small speaker.
            double exc = Excite(i, sr, 1.2, rng);
            s += knock.Process(exc) * p.Tone * 1.4;

            // Trigger click: filtered noise, not a bare impulse, so it sits in the tone.
            double cl = clickHp.Process(clickBp.Process(clickN.Next())) * cenv * p.Click * 2.2;
            s += cl;

            buf[i] = KitDsp.Saturate(s, p.Drive, 0.6) * AttackRamp(i, sr);
            env *= ampC; penv *= pitchC; cenv *= clickC;
        }
    }

    private static void KickAcoustic(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        // A struck shell: three modes with different ring times over a short sub, and a
        // beater transient on top. The mode spacing is inharmonic — a drum shell is not
        // a string, and harmonic ratios here are exactly what sounds synthetic.
        var m1 = new Modal(freq, p.Decay * 0.55, sr);
        var m2 = new Modal(freq * 1.63, p.Decay * 0.30, sr);
        var m3 = new Modal(freq * 2.81, p.Decay * 0.14, sr);
        var beaterBp = Biquad.BandPass(2400 + p.Tone * 3600, 0.8, sr);
        var beaterHp = Biquad.HighPass(1200, 0.7071, sr);
        var beaterN = new NoiseGen(rng, 13000, sr);
        double beatC = KitDsp.DecayCoef(0.007, sr);
        double subC = KitDsp.DecayCoef(Math.Min(p.Decay * 0.4, 0.16), sr);
        double pitchC = Math.Exp(-1.0 / Math.Max(1e-4, p.PitchDecay * sr));

        double subEnv = 1, benv = 1, penv = 1, ph = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            double f = freq * (1 + (p.PitchAmt <= 0 ? 0.8 : p.PitchAmt) * penv);
            ph += f / sr; if (ph >= 1) ph -= 1;
            double s = Math.Sin(TwoPi * ph) * subEnv * (0.7 + p.Body * 0.5);

            double exc = Excite(i, sr, 2.0, rng);
            s += (m1.Process(exc) * 1.0 + m2.Process(exc) * 0.5 + m3.Process(exc) * 0.22) * p.Body * 1.6;
            s += beaterHp.Process(beaterBp.Process(beaterN.Next())) * benv * p.Click * 3.0;

            buf[i] = KitDsp.Saturate(s, p.Drive, 0.4) * AttackRamp(i, sr);
            subEnv *= subC; benv *= beatC; penv *= pitchC;
        }
    }

    private static void SubHit(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        double ampC = KitDsp.DecayCoef(p.Decay, sr);
        double pitchC = Math.Exp(-1.0 / Math.Max(1e-4, p.PitchDecay * sr));
        var lp = Biquad.LowPass(200 + p.Tone * 2400, 0.7071, sr);
        double env = 1, penv = 1, ph = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            double f = freq * (1 + p.PitchAmt * penv);
            ph += f / sr; if (ph >= 1) ph -= 1;
            double s = Math.Sin(TwoPi * ph) * env;
            // Saturating a sine is the whole trick here: it grows the harmonics that let
            // a 45 Hz note be heard on a phone, without lifting the fundamental.
            s = KitDsp.Saturate(s, 0.15 + p.Drive * 0.85, 0.25);
            buf[i] = lp.Process(s) * AttackRamp(i, sr);
            env *= ampC; penv *= pitchC;
        }
    }

    // --- snares ------------------------------------------------------------

    private static void SnareAnalog(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        // Two shell tones a rough fifth-and-a-bit apart plus high-passed noise — the
        // classic two-oscillator analog snare.
        var t1 = new Modal(freq, p.Decay * (0.45 + p.Body * 0.5), sr);
        var t2 = new Modal(freq * 1.588, p.Decay * (0.32 + p.Body * 0.4), sr);
        var nHp = Biquad.HighPass(900 + p.Tone * 2200, 0.7071, sr);
        var nPk = Biquad.Peaking(4200, 1.1, 4 * p.Tone, sr);
        var nGen = new NoiseGen(rng, 12000, sr);
        double nC = KitDsp.DecayCoef(p.NoiseDecay, sr);
        double snapC = KitDsp.DecayCoef(0.012, sr);
        double nEnv = 1, snap = 1;
        for (int i = 0; i < buf.Length; i++)
        {
            double exc = Excite(i, sr, 1.0, rng);
            double tone = t1.Process(exc) * 1.0 + t2.Process(exc) * 0.72;
            double n = nPk.Process(nHp.Process(nGen.Next()));
            double s = tone * (1 - p.Tone) * 1.5
                     + n * nEnv * p.Noise * 2.0
                     + n * snap * p.Click * 1.5;
            buf[i] = KitDsp.Saturate(s, p.Drive, 0.35) * AttackRamp(i, sr);
            nEnv *= nC; snap *= snapC;
        }
    }

    private static void SnarePunch(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        var t1 = new Modal(freq, p.Decay * 0.3, sr);
        var t2 = new Modal(freq * 1.47, p.Decay * 0.22, sr);
        var nHp = Biquad.HighPass(260, 0.7071, sr);
        var nPk = Biquad.Peaking(2600 + p.Tone * 4200, 0.9, 6, sr);
        var nLp = Biquad.LowPass(7000 + p.Tone * 8000, 0.7071, sr);
        var nGen = new NoiseGen(rng, 14000, sr);
        double nC = KitDsp.DecayCoef(p.NoiseDecay, sr);
        double bodyC = KitDsp.DecayCoef(p.NoiseDecay * 2.4, sr);   // the noise "body" under the snap
        double nEnv = 1, bEnv = 1;
        for (int i = 0; i < buf.Length; i++)
        {
            double exc = Excite(i, sr, 0.8, rng);
            double tone = (t1.Process(exc) + t2.Process(exc) * 0.6) * p.Body * 1.6;
            double raw = nGen.Next();
            double n = nLp.Process(nPk.Process(nHp.Process(raw)));
            double s = tone + n * (nEnv * 1.1 + bEnv * 0.45) * (0.4 + p.Noise);
            buf[i] = KitDsp.Saturate(s, p.Drive, 0.3) * AttackRamp(i, sr);
            nEnv *= nC; bEnv *= bodyC;
        }
    }

    private static void SnareAcoustic(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        // Shell modes + snare wires + stick crack. The wires are noise with their own
        // longer, brighter decay; the crack is the 5 ms of broadband the stick makes.
        var m1 = new Modal(freq, p.Decay * 0.5, sr);
        var m2 = new Modal(freq * 1.52, p.Decay * 0.35, sr);
        var m3 = new Modal(freq * 2.34, p.Decay * 0.2, sr);
        var wireHp = Biquad.HighPass(1600 + p.Tone * 2400, 0.7071, sr);
        var wirePk = Biquad.Peaking(6500, 1.0, 3 + p.Tone * 5, sr);
        var crackBp = Biquad.BandPass(3200, 0.7, sr);
        var nGen = new NoiseGen(rng, 14000, sr);
        double wireC = KitDsp.DecayCoef(p.NoiseDecay, sr);
        double crackC = KitDsp.DecayCoef(0.006, sr);
        double wEnv = 1, cEnv = 1;
        for (int i = 0; i < buf.Length; i++)
        {
            double exc = Excite(i, sr, 1.6, rng);
            double shell = (m1.Process(exc) + m2.Process(exc) * 0.6 + m3.Process(exc) * 0.3) * p.Body * 1.5;
            double raw = nGen.Next();
            double wire = wirePk.Process(wireHp.Process(raw)) * wEnv * p.Noise * 1.6;
            double crack = crackBp.Process(raw) * cEnv * p.Click * 2.4;
            buf[i] = KitDsp.Saturate(shell + wire + crack, p.Drive, 0.3) * AttackRamp(i, sr);
            wEnv *= wireC; cEnv *= crackC;
        }
    }

    // --- hands / sticks ----------------------------------------------------

    private static void Clap(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        // Four hands never land together: three short bursts a few milliseconds apart,
        // then the room-ish tail that follows. The spacing jitter is what stops a clap
        // from sounding like one flammed noise gate.
        double[] offs = { 0, 0.0105, 0.0205, 0.0295 };
        var bp = Biquad.BandPass(freq, 0.9 + p.Body * 0.8, sr);
        var hp = Biquad.HighPass(420, 0.7071, sr);
        var pk = Biquad.Peaking(freq * 2.6, 1.2, 4 * p.Tone, sr);
        var nGen = new NoiseGen(rng, 9500, sr);
        double burstC = KitDsp.DecayCoef(0.018, sr);
        double tailC = KitDsp.DecayCoef(p.Decay, sr);

        var starts = new int[offs.Length];
        for (int k = 0; k < offs.Length; k++)
            starts[k] = (int)((offs[k] + offs[k] * rng.Next() * 0.18 * (1 + p.Drift)) * sr);

        double tail = 0, burst = 0;
        int next = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            if (next < starts.Length && i >= starts[next]) { burst = 1.0 - next * 0.12; next++; }
            if (i == starts[^1]) tail = 1;
            double n = nGen.Next();
            double s = pk.Process(hp.Process(bp.Process(n))) * (burst * 1.6 + tail * p.Body * 0.8);
            buf[i] = KitDsp.Saturate(s, p.Drive, 0.25) * AttackRamp(i, sr);
            burst *= burstC; tail *= tailC;
        }
    }

    private static void Rim(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        var m1 = new Modal(freq, p.Decay, sr);
        var m2 = new Modal(freq * 2.74, p.Decay * 0.6, sr);
        var m3 = new Modal(freq * 5.12, p.Decay * 0.35, sr);
        var hp = Biquad.HighPass(220, 0.7071, sr);
        for (int i = 0; i < buf.Length; i++)
        {
            double exc = Excite(i, sr, 0.5, rng);
            double s = m1.Process(exc) * (1 - p.Tone * 0.4)
                     + m2.Process(exc) * (0.6 + p.Tone * 0.5)
                     + m3.Process(exc) * p.Tone * 0.7;
            s = hp.Process(s * (1.4 + p.Body));
            buf[i] = KitDsp.Saturate(s, p.Drive, 0.3) * AttackRamp(i, sr);
        }
    }

    // --- metal -------------------------------------------------------------

    // The six square-wave frequencies of the classic analog hat circuit, as ratios of
    // the lowest. Squares (not sines) because the cluster's value is the intermodulation
    // between all those harmonics once it hits the band-pass.
    private static readonly double[] MetalRatios = { 1.0, 1.4827, 1.8003, 2.5461, 2.6303, 3.8968 };
    private static readonly double[] CymbalExtra = { 5.2331, 6.8072 };

    private static void Metal(KitPad p, double[] buf, double sr, Rng rng, double freq, bool cymbal)
    {
        int n = cymbal ? MetalRatios.Length + CymbalExtra.Length : MetalRatios.Length;
        var ph = new double[n];
        var fr = new double[n];
        for (int k = 0; k < n; k++)
        {
            double ratio = k < MetalRatios.Length ? MetalRatios[k] : CymbalExtra[k - MetalRatios.Length];
            fr[k] = freq * ratio * (1 + rng.Next() * 0.02 * p.Drift);
            ph[k] = rng.Next01();
        }

        var hp = Biquad.HighPass(cymbal ? 2600 : 5200 + p.Tone * 3000, 0.7071, sr);
        var pk = Biquad.Peaking(cymbal ? 9000 : 10500, 0.9 + p.Body, 5 + p.Body * 5, sr);
        var nHp = Biquad.HighPass(6000, 0.7071, sr);
        var nGen = new NoiseGen(rng, 16000, sr);

        // A cymbal's shimmer dies long before its body does; splitting the decay into
        // bands is what gives the wash instead of a single fading hiss.
        var bandHi = Biquad.HighPass(7000, 0.7071, sr);
        var bandMid = Biquad.BandPass(4000, 0.6, sr);
        double envC = KitDsp.DecayCoef(p.Decay, sr);
        double hiC = KitDsp.DecayCoef(p.Decay * (cymbal ? 0.28 : 0.7), sr);
        double midC = KitDsp.DecayCoef(p.Decay * (cymbal ? 0.6 : 0.85), sr);
        double env = 1, hiEnv = 1, midEnv = 1;

        for (int i = 0; i < buf.Length; i++)
        {
            double metal = 0;
            for (int k = 0; k < n; k++)
            {
                ph[k] += fr[k] / sr; if (ph[k] >= 1) ph[k] -= 1;
                metal += ph[k] < 0.5 ? 1 : -1;
            }
            metal /= n;
            double mix = metal * (1 - p.Tone * 0.6) + nGen.Next() * p.Tone * 0.6;
            double s = pk.Process(hp.Process(mix));
            if (cymbal)
                s = s * env * 0.5 + bandHi.Process(s) * hiEnv * 0.7 + bandMid.Process(s) * midEnv * 0.6;
            else
                s *= env;
            s += nHp.Process(nGen.Next()) * env * p.Noise * 0.5;
            buf[i] = KitDsp.Saturate(s, p.Drive, 0.2) * AttackRamp(i, sr);
            env *= envC; hiEnv *= hiC; midEnv *= midC;
        }
    }

    private static void HatNoise(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        var hp = Biquad.HighPass(freq * (4 + p.Tone * 6), 0.7071, sr);
        var pk1 = Biquad.Peaking(7200, 1.4, 6 * p.Body, sr);
        var pk2 = Biquad.Peaking(11500, 1.8, 5 * p.Body, sr);
        var lp = Biquad.LowPass(9000 + p.Tone * 9000, 0.7071, sr);
        var nGen = new NoiseGen(rng, 16000, sr);
        double envC = KitDsp.DecayCoef(p.Decay, sr);
        double tickC = KitDsp.DecayCoef(0.003, sr);
        double env = 1, tick = 1;
        for (int i = 0; i < buf.Length; i++)
        {
            double n = nGen.Next();
            double s = lp.Process(pk2.Process(pk1.Process(hp.Process(n))));
            buf[i] = KitDsp.Saturate(s * (env + tick * p.Click * 2.0), p.Drive, 0.2) * AttackRamp(i, sr);
            env *= envC; tick *= tickC;
        }
    }

    // --- drums & percussion ------------------------------------------------

    private static void Tom(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        double ampC = KitDsp.DecayCoef(p.Decay, sr);
        double pitchC = Math.Exp(-1.0 / Math.Max(1e-4, p.PitchDecay * sr));
        var shell = new Modal(freq * 1.59, p.Decay * 0.45, sr);
        var shell2 = new Modal(freq * 2.14, p.Decay * 0.28, sr);
        var skinBp = Biquad.BandPass(1800 + p.Tone * 2600, 0.8, sr);
        var nGen = new NoiseGen(rng, 9000, sr);
        double skinC = KitDsp.DecayCoef(p.NoiseDecay, sr);
        double env = 1, penv = 1, skin = 1, ph = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            double f = freq * (1 + (p.PitchAmt <= 0 ? 0.5 : p.PitchAmt) * penv);
            ph += f / sr; if (ph >= 1) ph -= 1;
            double s = Math.Sin(TwoPi * ph) * env;
            double exc = Excite(i, sr, 1.5, rng);
            s += (shell.Process(exc) * 0.7 + shell2.Process(exc) * 0.35) * p.Body * 1.5;
            s += skinBp.Process(nGen.Next()) * skin * p.Noise * 1.4;
            buf[i] = KitDsp.Saturate(s, p.Drive, 0.4) * AttackRamp(i, sr);
            env *= ampC; penv *= pitchC; skin *= skinC;
        }
    }

    private static void Conga(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        // Circular-membrane mode ratios (Bessel zeros), which is what makes a hand drum
        // read as a skin rather than a tuned bar.
        var m1 = new Modal(freq, p.Decay, sr);
        var m2 = new Modal(freq * 1.593, p.Decay * 0.55, sr);
        var m3 = new Modal(freq * 2.135, p.Decay * 0.33, sr);
        var m4 = new Modal(freq * 2.917, p.Decay * 0.2, sr);
        var slapBp = Biquad.BandPass(900 + p.Tone * 5000, 0.9, sr);
        var nGen = new NoiseGen(rng, 11000, sr);
        double slapC = KitDsp.DecayCoef(0.009 + p.NoiseDecay * 0.4, sr);
        double slap = 1;
        for (int i = 0; i < buf.Length; i++)
        {
            double exc = Excite(i, sr, 1.4, rng);
            double s = m1.Process(exc) * (1.1 - p.Tone * 0.4)
                     + m2.Process(exc) * 0.55 * p.Body
                     + m3.Process(exc) * 0.3 * p.Body
                     + m4.Process(exc) * 0.16 * p.Body;
            s += slapBp.Process(nGen.Next()) * slap * p.Click * 2.2;
            buf[i] = KitDsp.Saturate(s * 1.5, p.Drive, 0.3) * AttackRamp(i, sr);
            slap *= slapC;
        }
    }

    private static void Shaker(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        // Beads, not hiss: the noise is amplitude-modulated by a fast random contour, so
        // the hit has grain in it. A swell replaces the usual instant attack.
        var bp = Biquad.BandPass(freq, 0.7 + p.Tone * 1.2, sr);
        var hp = Biquad.HighPass(freq * 0.5, 0.7071, sr);
        var nGen = new NoiseGen(rng, 16000, sr);
        int swell = Math.Max(1, (int)(sr * (0.004 + (1 - p.Click) * 0.016)));
        double envC = KitDsp.DecayCoef(p.Decay, sr);
        int grainLen = Math.Max(1, (int)(sr / (700 + p.Body * 2600)));
        double env = 1, grain = 1;
        for (int i = 0; i < buf.Length; i++)
        {
            if (i % grainLen == 0) grain = 0.45 + rng.Next01() * 0.55;
            double atk = i < swell ? (double)i / swell : 1.0;
            double n = nGen.Next();
            double s = hp.Process(bp.Process(n)) * env * atk * (0.5 + grain * 0.9);
            buf[i] = KitDsp.Saturate(s * 1.6, p.Drive, 0.2) * AttackRamp(i, sr);
            if (i >= swell) env *= envC;
        }
    }

    private static void Cowbell(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        double f2 = freq * 1.4815;   // the two-square interval of the analog original
        var bp = Biquad.BandPass(freq * (3.0 + p.Tone * 3.0), 0.7 + p.Body, sr);
        var hp = Biquad.HighPass(freq * 0.8, 0.7071, sr);
        double envC = KitDsp.DecayCoef(p.Decay, sr);
        double clickC = KitDsp.DecayCoef(0.004, sr);
        double env = 1, cl = 1, p1 = 0, p2 = rng.Next01();
        for (int i = 0; i < buf.Length; i++)
        {
            p1 += freq / sr; if (p1 >= 1) p1 -= 1;
            p2 += f2 / sr; if (p2 >= 1) p2 -= 1;
            double sq = ((p1 < 0.5 ? 1 : -1) + (p2 < 0.5 ? 1 : -1)) * 0.5;
            double s = hp.Process(bp.Process(sq)) * (env + cl * p.Click);
            buf[i] = KitDsp.Saturate(s * 1.5, p.Drive, 0.25) * AttackRamp(i, sr);
            env *= envC; cl *= clickC;
        }
    }

    private static void Block(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        var m1 = new Modal(freq, p.Decay, sr);
        var m2 = new Modal(freq * 1.47, p.Decay * 0.55, sr);
        var m3 = new Modal(freq * 2.09, p.Decay * 0.3, sr);
        var hp = Biquad.HighPass(600, 0.7071, sr);
        for (int i = 0; i < buf.Length; i++)
        {
            double exc = Excite(i, sr, 0.4, rng);
            double s = m1.Process(exc) + m2.Process(exc) * p.Tone * 0.8 + m3.Process(exc) * p.Tone * 0.4;
            s = hp.Process(s * (1.6 + p.Body));
            buf[i] = KitDsp.Saturate(s, p.Drive, 0.25) * AttackRamp(i, sr);
        }
    }

    private static void Zap(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        double ampC = KitDsp.DecayCoef(p.Decay, sr);
        double pitchC = Math.Exp(-1.0 / Math.Max(1e-4, p.PitchDecay * sr));
        double env = 1, penv = 1, ph = 0, mph = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            double f = freq * (1 + (p.PitchAmt <= 0 ? 12 : p.PitchAmt) * penv);
            mph += f * (1.5 + p.Body * 3) / sr; if (mph >= 1) mph -= 1;
            double fm = Math.Sin(TwoPi * mph) * p.Tone * 4;
            ph += f / sr; if (ph >= 1) ph -= 1;
            double s = Math.Sin(TwoPi * ph + fm) * env;
            buf[i] = KitDsp.Saturate(s, p.Drive, 0.3) * AttackRamp(i, sr);
            env *= ampC; penv *= pitchC;
        }
    }

    private static void PercMetal(KitPad p, double[] buf, double sr, Rng rng, double freq)
    {
        double ratio = 1.4 + p.Tone * 3.2;
        var ring = new Modal(freq * (1 + ratio) * 0.5, p.Decay * 0.7, sr);
        var ring2 = new Modal(freq * ratio * 1.71, p.Decay * 0.4, sr);
        var hp = Biquad.HighPass(freq * 0.6, 0.7071, sr);
        double envC = KitDsp.DecayCoef(p.Decay, sr);
        double env = 1, ph = 0, mph = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            ph += freq / sr; if (ph >= 1) ph -= 1;
            mph += freq * ratio / sr; if (mph >= 1) mph -= 1;
            double s = Math.Sin(TwoPi * ph) * Math.Sin(TwoPi * mph) * env;
            double exc = Excite(i, sr, 1.0, rng);
            s += (ring.Process(exc) + ring2.Process(exc) * 0.6) * p.Body * 1.4;
            buf[i] = KitDsp.Saturate(hp.Process(s), p.Drive, 0.25) * AttackRamp(i, sr);
            env *= envC;
        }
    }
}
