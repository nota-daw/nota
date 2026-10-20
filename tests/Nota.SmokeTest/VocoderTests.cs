// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Vocoder (effect kind 26, specs/028-vocoder-effect): the track is the voice (modulator),
// the carrier a saw, noise or another track. Identity, the param contract
// (names, defaults, round-trip), clone, the modulator through the device sidechain, the three
// carriers, the band controls, Hold, Unvoiced, click-free sweeps, save / load, determinism, the
// factory presets and the CPU budget against Nota Chamber.

using System.Diagnostics;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class VocoderTests
{
    private const int Sr = 48000, Kind = 26, Block = 512;
    private const int PBands = 0, PLow = 1, PHigh = 2, PQ = 3, PFormant = 4, PAttack = 5, PRelease = 6, PModGain = 7,
                      PCarrier = 8, PTilt = 9, PPitch = 10, PUnvoiced = 11, PMix = 12, PGain = 13, PHold = 14;
    private static readonly string[] Names =
        { "Bands", "Low", "High", "Q", "Formant", "Attack", "Release", "Mod gain", "Carrier", "Tilt", "Pitch", "Unvoiced", "Mix", "Gain", "Hold" };
    private static readonly float[] Defaults =
        { 8f / 24f, 0.3544f, 0.6021f, 4f / 9f, 0.5f, 0.2616f, 0.3925f, 0.5f, 0f, 0.625f, 36f / 84f, 0.35f, 1f, 0.5f, 0f };

    private static string? _chord, _speech;

    // A C-major triad of band-limited saws, −12 dBFS-ish; nothing above ~4 kHz.
    private static string Chord()
    {
        if (_chord != null) return _chord;
        _chord = Path.Combine(Path.GetTempPath(), "nota-smoke-vocoder-chord.wav");
        double[] f0 = { 130.81, 164.81, 196.0 };
        WavWriter.WriteStereo(_chord, 4.0, Sr, i =>
        {
            double s = 0;
            foreach (double f in f0)
                for (int k = 1; k * f < 4000; k++) s += Math.Sin(2 * Math.PI * f * k * i / Sr) / k;
            s *= 0.08;
            return (s, s);
        });
        return _chord;
    }

    // A speech-like modulator: a 150 Hz buzz through two moving formants, syllables at 3 Hz, and a
    // sibilant noise burst (above 4 kHz) every 1.5 s. Deterministic.
    private static string Speech()
    {
        if (_speech != null) return _speech;
        _speech = Path.Combine(Path.GetTempPath(), "nota-smoke-vocoder-speech.wav");
        uint seed = 777; double prev = 0;
        double Next() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 8388608.0 - 1.0; }
        WavWriter.WriteStereo(_speech, 4.0, Sr, i =>
        {
            double t = (double)i / Sr;
            double f1 = 500 + 250 * Math.Sin(2 * Math.PI * 1.7 * t), f2 = 1500 + 700 * Math.Sin(2 * Math.PI * 1.1 * t + 1);
            double v = 0;
            for (int k = 1; k * 150.0 < 5000; k++)
            {
                double f = 150.0 * k;
                double g = Math.Exp(-Math.Pow(Math.Log(f / f1), 2) * 4) + 0.8 * Math.Exp(-Math.Pow(Math.Log(f / f2), 2) * 5);
                v += g * Math.Sin(2 * Math.PI * f * t) / Math.Sqrt(k);
            }
            v *= 0.12 * Math.Abs(Math.Sin(2 * Math.PI * 1.5 * t));
            double ph = t % 1.5;
            if (ph > 1.1 && ph < 1.3) { double n = Next(); v += (n - prev) * 0.25; prev = n; }   // differentiated noise ≈ HF
            return (v, v);
        });
        return _speech;
    }

    // The voice track (speech unless empty) with the vocoder — the modulator — and, when asked, a
    // carrier track (chord unless empty) routed in as Carrier = Track.
    private static (NotaEngine E, int Voice, int Car, int Di) Rig(bool voiceClip = true, bool carrierTrack = false, bool carrierClip = true)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        int voice = e.AddAudioTrack();
        if (voiceClip) e.AddAudioClip(voice, Speech(), 0.0);
        int car = -1;
        if (carrierTrack) { car = e.AddAudioTrack(); if (carrierClip) e.AddAudioClip(car, Chord(), 0.0); }
        int di = e.AddBuiltinDevice(voice, Kind);
        if (carrierTrack)
        {
            e.DeviceSetParam(voice, di, PCarrier, 1f);
            e.SetDeviceSidechainSource(voice, di, car);
            e.SetTrackVolume(car, 0f);   // hear the vocoder alone (the Pre tap is before the fader)
        }
        return (e, voice, car, di);
    }

    private static float[] Play(NotaEngine e, int frames, int block = Block, Action<int>? perBlock = null)
    {
        var all = new float[frames * 2]; var b = new float[block * 2];
        e.Seek(0); e.Play();
        int n = 0;
        for (int done = 0; done < frames; n++)
        {
            perBlock?.Invoke(n);
            int m = Math.Min(block, frames - done);
            e.RenderOffline(b, m, Sr);
            Array.Copy(b, 0, all, done * 2, m * 2);
            done += m;
        }
        e.StopTransport();
        return all;
    }

    private static double Rms(float[] st, int from = 0)
    {
        double s = 0; int n = 0;
        for (int i = from * 2; i < st.Length; i++) { s += st[i] * (double)st[i]; n++; }
        return Math.Sqrt(s / Math.Max(1, n));
    }
    private static double Peak(float[] st) { double p = 0; foreach (var v in st) p = Math.Max(p, Math.Abs(v)); return p; }
    private static bool Finite(float[] st) { foreach (var v in st) if (!float.IsFinite(v) || Math.Abs(v) > 8f) return false; return true; }
    private static double MaxStep(float[] st, int from = 0)
    {
        double d = 0;
        for (int i = (from + 1) * 2; i < st.Length; i++) d = Math.Max(d, Math.Abs(st[i] - st[i - 2]));
        return d;
    }
    private static double MaxDiff(float[] a, float[] b) { double d = 0; for (int i = 0; i < Math.Min(a.Length, b.Length); i++) d = Math.Max(d, Math.Abs(a[i] - b[i])); return d; }
    // Energy at one frequency (Goertzel), mono sum.
    private static double At(float[] st, double hz, int from = 0)
    {
        double w = 2 * Math.PI * hz / Sr, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = from * 2; i < st.Length; i += 2) { double s0 = (st[i] + st[i + 1]) * 0.5 + c * s1 - s2; s2 = s1; s1 = s0; }
        return s1 * s1 + s2 * s2 - c * s1 * s2;
    }
    // Energy above `hz` (first difference of the mono sum, a crude high-pass).
    private static double HighEnergy(float[] st, int from = 0)
    {
        double s = 0, prev = 0;
        for (int i = from * 2; i < st.Length; i += 2) { double m = (st[i] + st[i + 1]) * 0.5; s += (m - prev) * (m - prev); prev = m; }
        return s;
    }

    private static double Db(float[] st, int from) => 20 * Math.Log10(Rms(st, from) + 1e-12);

    public static IEnumerable<(bool, string)> Run()
    {
        // ---- identity and the param contract ------------------------------------------------
        {
            var (e, voice, car, di) = Rig(carrierTrack: true);
            using var _ = e;
            yield return (di >= 0 && e.DeviceName(voice, di) == "Nota Vocoder" && e.TrackDeviceBuiltinKind(voice, di) == Kind,
                $"Nota Vocoder adds as builtin kind 26 ('{e.DeviceName(voice, di)}')");
            int pc = e.DeviceParamCount(voice, di);
            var names = Enumerable.Range(0, pc).Select(i => e.DeviceParamName(voice, di, i)).ToArray();
            yield return (pc == 15 && names.SequenceEqual(Names), $"15 params in the contract order ({string.Join(",", names)})");
            var bad = Enumerable.Range(0, Math.Min(pc, 15)).Where(i => Math.Abs(e.DeviceParamDefault(voice, di, i) - Defaults[i]) > 1e-3f).ToArray();
            yield return (bad.Length == 0, $"defaults match the contract — Carrier Saw ({(bad.Length == 0 ? "all" : "off: " + string.Join(",", bad.Select(i => Names[i])))})");
            var snap = Enumerable.Range(0, pc).Select(i => e.DeviceGetParam(voice, di, i)).ToArray();
            bool rt = true;
            for (int i = 0; i < pc; i++) { float v = (i * 7 % 10) / 10f; e.DeviceSetParam(voice, di, i, v); rt &= Math.Abs(e.DeviceGetParam(voice, di, i) - v) < 1e-5f; }
            yield return (rt, "every param round-trips");
            for (int i = 0; i < pc; i++) e.DeviceSetParam(voice, di, i, snap[i]);
            yield return (e.DeviceAcceptsSidechain(voice, di) && e.DeviceSidechainSource(voice, di) == car, "takes a sidechain (the carrier track)");
            yield return (e.DeviceSidechainTapPre(voice, di), "a new vocoder taps its carrier track pre-FX");

            int dup = e.DuplicateTrack(voice);
            bool same = dup > 0 && Enumerable.Range(0, pc).All(i => Math.Abs(e.DeviceGetParam(dup, di, i) - e.DeviceGetParam(voice, di, i)) < 1e-6f);
            yield return (same, "duplicating the track clones every param");
            yield return (dup > 0 && e.DeviceSidechainSource(dup, di) == car && e.DeviceSidechainTapPre(dup, di),
                $"the clone keeps its carrier track and tap ({e.DeviceSidechainSource(dup, di)})");
        }

        // ---- the default: the track's own voice through the internal saw -----------------
        {
            var (e, voice, _, di) = Rig();
            using var _e = e;
            var robot = Play(e, Sr * 2);
            double rms = Rms(robot, Sr / 2), pk = Peak(robot);
            yield return (Finite(robot) && rms > 1e-3, $"added with its defaults, the voice comes out as a robot (Saw) — audible + finite (RMS {rms:0.0000}, peak {pk:0.000})");
            yield return (pk < 1.0, $"default settings stay below 0 dBFS (peak {pk:0.000})");
            e.SetDeviceBypassed(voice, di, true);
            double voiceDb = Db(Play(e, Sr * 2), Sr / 2);
            e.SetDeviceBypassed(voice, di, false);
            double robotDb = 20 * Math.Log10(rms);
            yield return (Math.Abs(robotDb - voiceDb) < 6, $"the robot sits near the voice's level ({robotDb:0.0} vs {voiceDb:0.0} dB)");

            // The robot follows the syllables: 50 ms windows over the first second swing.
            var win = Enumerable.Range(2, 18).Select(w => { double s = 0; for (int i = w * 2400; i < (w + 1) * 2400; i++) s += robot[2 * i] * (double)robot[2 * i]; return 10 * Math.Log10(s / 2400 + 1e-20); }).ToArray();
            yield return (win.Max() - win.Min() > 8, $"the output follows the syllables ({win.Max() - win.Min():0.0} dB swing)");
            // ...and the voice's level: Mod gain −20 dB brings the robot down with it.
            e.DeviceSetParam(voice, di, PModGain, 0.5f - 20f / 48f);
            double quietDb = Db(Play(e, Sr * 2), Sr / 2);
            e.DeviceSetParam(voice, di, PModGain, 0.5f);
            yield return (robotDb - quietDb is > 12 and < 28, $"with Saw the robot follows the voice's level (−20 dB → {quietDb - robotDb:0.0} dB)");

            var sc = new float[100];
            Play(e, Sr / 6);                               // end on a syllable's peak (the speech's gaps fall on every 1/3 s)
            int n = e.DeviceScope(voice, di, sc, sc.Length);
            yield return (n == 100 && (int)sc[3] == 16 && sc.Skip(4).Take(16).Max() > 0 && sc[68] > 79 && sc[68 + 15] < 8001,
                $"scope: 100 values ({n}), 16 bands ({sc[3]}), env max {sc.Skip(4).Take(16).Max():0.00}, centres {sc[68]:0} … {sc[68 + 15]:0} Hz");

            e.DeviceSetParam(voice, di, PMix, 0f);
            var dry = Play(e, Sr);
            e.SetDeviceBypassed(voice, di, true);
            var byp = Play(e, Sr);
            e.SetDeviceBypassed(voice, di, false);
            double dd = MaxDiff(dry.Skip(Sr / 5).ToArray(), byp.Skip(Sr / 5).ToArray());   // after the Mix glide
            yield return (dd < 1e-4, $"Mix 0 % = the track as it came in (Δ {dd:0.000000})");
        }
        {
            var (e, _, _, _) = Rig(voiceClip: false);   // an empty track: no voice
            using var _e = e;
            var outv = Play(e, Sr);
            yield return (Finite(outv) && Rms(outv, Sr / 4) < 1e-4, "no voice on the track → silence (the saw only speaks through it)");
        }

        // ---- carriers: Saw at Pitch, Noise, another track --------------------------------------
        {
            var (e, voice, _, di) = Rig();
            using var _e = e;
            e.DeviceSetParam(voice, di, PUnvoiced, 0f);                        // the pitch check wants the saw alone
            var saw = Play(e, Sr * 2);
            // Odd harmonics of C3 (3, 5, 7 × 130.8 Hz) are not harmonics of C4: they vanish when Pitch is C4.
            double Odd(float[] o) => At(o, 392.44, Sr / 2) + At(o, 654.06, Sr / 2) + At(o, 915.67, Sr / 2);
            double Even(float[] o) => At(o, 523.25, Sr / 2) + At(o, 784.88, Sr / 2) + At(o, 1046.5, Sr / 2);
            double c3 = Odd(saw) / Even(saw);
            e.DeviceSetParam(voice, di, PPitch, 48f / 84f);   // C4
            var saw4 = Play(e, Sr * 2);
            double c4 = Odd(saw4) / Even(saw4);
            yield return (c3 > 10 * c4, $"Pitch sets the saw's note (odd / even C3 harmonics {c3:0.00} at C3 → {c4:0.000} at C4)");
            e.DeviceSetParam(voice, di, PPitch, Defaults[PPitch]);
            e.DeviceSetParam(voice, di, PUnvoiced, Defaults[PUnvoiced]);
            e.DeviceSetParam(voice, di, PCarrier, 0.5f);
            var nz = Play(e, Sr * 2);
            yield return (Finite(nz) && Rms(nz, Sr / 2) > 1e-3 && Peak(nz) < 1.0, $"Noise carrier whispers (RMS {Rms(nz, Sr / 2):0.0000})");

            // Switching carriers mid-play crossfades: no jump bigger than the signal's own steps.
            e.DeviceSetParam(voice, di, PCarrier, 0f);
            var sw = Play(e, Sr, perBlock: b => { if (b == 40) e.DeviceSetParam(voice, di, PCarrier, 0.5f); if (b == 70) e.DeviceSetParam(voice, di, PCarrier, 0f); });
            double steady = Math.Max(MaxStep(saw, Sr / 2), MaxStep(nz, Sr / 2));
            yield return (Finite(sw) && MaxStep(sw, Sr / 4) < steady * 1.5 + 0.02, $"carrier switch is click-free (max step {MaxStep(sw, Sr / 4):0.000} vs steady {steady:0.000})");

            // Track carrier with no track picked: the track passes through unchanged.
            e.DeviceSetParam(voice, di, PCarrier, 1f);
            var none = Play(e, Sr);
            e.SetDeviceBypassed(voice, di, true);
            var noneByp = Play(e, Sr);
            e.SetDeviceBypassed(voice, di, false);
            double nd = MaxDiff(none.Skip(Sr).ToArray(), noneByp.Skip(Sr).ToArray());   // after the 10 ms fade (0.5 s in)
            yield return (nd < 1e-4, $"Track carrier, no track picked → the track passes unchanged (Δ {nd:0.000000} against bypass)");
        }
        {
            var (e, voice, car, di) = Rig(carrierTrack: true);
            using var _e = e;
            var talk = Play(e, Sr * 2);
            double talkDb = Db(talk, Sr / 2);
            yield return (Finite(talk) && Rms(talk, Sr / 2) > 1e-3 && Peak(talk) < 1.0, $"Track carrier: the chord track speaks the voice ({talkDb:0.0} dB, peak {Peak(talk):0.000})");
            // The level follows the carrier track, not the voice: a voice 20 dB quieter vocodes as loud.
            e.DeviceSetParam(voice, di, PModGain, 0.5f - 20f / 48f);
            double quietDb = Db(Play(e, Sr * 2), Sr / 2);
            e.DeviceSetParam(voice, di, PModGain, 0.5f);
            yield return (Math.Abs(quietDb - talkDb) < 3, $"with a Track carrier a voice 20 dB quieter vocodes as loud ({talkDb:0.0} → {quietDb:0.0} dB)");
            e.SetTrackVolume(car, 1f); e.SetTrackVolume(voice, 0f);
            double carDb = Db(Play(e, Sr * 2), Sr / 2);
            e.SetTrackVolume(car, 0f); e.SetTrackVolume(voice, 1f);
            yield return (Math.Abs(talkDb - carDb) < 6, $"…and sits near the carrier track's level ({talkDb:0.0} vs {carDb:0.0} dB)");
            // Muting the carrier track (to hear only the vocoder) keeps its Pre tap feeding the bands.
            e.SetTrackVolume(car, 1f); e.SetTrackMute(car, true);
            double mutedDb = Db(Play(e, Sr * 2), Sr / 2);
            e.SetTrackMute(car, false); e.SetTrackVolume(car, 0f);
            yield return (Math.Abs(mutedDb - talkDb) < 1.5, $"a muted carrier track still feeds the bands through the Pre tap ({mutedDb:0.0} vs {talkDb:0.0} dB)");

            // Unvoiced adds energy above the chord (which has none past ~4 kHz) where the speech hisses.
            e.DeviceSetParam(voice, di, PUnvoiced, 0f);
            var uv0 = Play(e, Sr * 2);
            e.DeviceSetParam(voice, di, PUnvoiced, 1f);
            var uv1 = Play(e, Sr * 2);
            double hRatio = HighEnergy(uv1, Sr / 4) / Math.Max(1e-12, HighEnergy(uv0, Sr / 4));
            yield return (Finite(uv1) && hRatio > 2, $"Unvoiced brings out sibilants (high-band energy × {hRatio:0.0})");
            e.DeviceSetParam(voice, di, PUnvoiced, Defaults[PUnvoiced]);

            // The carrier track is deleted: the voice passes through unchanged.
            e.SetDeviceBypassed(voice, di, true);
            var byp = Play(e, Sr);
            e.SetDeviceBypassed(voice, di, false);
            e.RemoveTrack(car);
            var o = Play(e, Sr);
            double rd = MaxDiff(o.Skip(Sr).ToArray(), byp.Skip(Sr).ToArray());
            yield return (Finite(o) && rd < 1e-4, $"deleting the carrier track lets the voice pass unchanged, no crash (Δ {rd:0.000000})");
        }
        {
            var (e, _, _, _) = Rig(carrierTrack: true, carrierClip: false);   // the carrier track is silent
            using var _e = e;
            var outv = Play(e, Sr);
            yield return (Finite(outv) && Rms(outv, Sr / 4) < 1e-4, "a silent carrier track → silent wet");
        }

        // ---- shaping -------------------------------------------------------------------------
        {
            var (e, voice, _, di) = Rig();
            using var _e = e;
            var sc = new float[100];
            var basev = Play(e, Sr);
            foreach (var (bn, nb) in new[] { (0f, 8), (1f, 32) })
            {
                e.DeviceSetParam(voice, di, PBands, bn);
                var o = Play(e, Sr);
                e.DeviceScope(voice, di, sc, sc.Length);
                yield return (Finite(o) && Rms(o, Sr / 4) > 1e-3 && (int)sc[3] == nb, $"{nb} bands: audible, finite, scope reports {(int)sc[3]}");
            }
            e.DeviceSetParam(voice, di, PBands, Defaults[PBands]);
            foreach (float fm in new[] { 0f, 1f })
            {
                e.DeviceSetParam(voice, di, PFormant, fm);
                var o = Play(e, Sr);
                yield return (Finite(o) && Rms(o, Sr / 4) > 1e-4 && MaxDiff(o, basev) > 1e-3, $"Formant {(fm - 0.5f) * 24:+0;−0} st is finite and changes the sound");
            }
            e.DeviceSetParam(voice, di, PFormant, 0.5f);

            // Hold freezes the envelopes while the voice keeps moving.
            Play(e, Sr / 2, perBlock: b => { if (b == 20) e.DeviceSetParam(voice, di, PHold, 1f); });
            e.DeviceScope(voice, di, sc, sc.Length); var h1 = sc.Skip(4).Take(16).ToArray();
            var held = new float[4096 * 2]; e.Play(); e.RenderOffline(held, 4096, Sr); e.StopTransport();
            e.DeviceScope(voice, di, sc, sc.Length); var h2 = sc.Skip(4).Take(16).ToArray();
            yield return (h1.Zip(h2).All(p => Math.Abs(p.First - p.Second) < 1e-5f) && h1.Max() > 0, "Hold freezes the band envelopes");
            e.DeviceSetParam(voice, di, PHold, 0f);

            // Sweeping every band control each block stays finite and click-free (SC-003).
            // The reference: the largest sample step of static renders at the corners the sweep visits.
            double ref0 = 0;
            foreach (var (lo, hi, q, fm, bn) in new[] { (0f, 0f, 0f, 0f, 0f), (1f, 1f, 1f, 1f, 1f), (0f, 1f, 1f, 1f, 0f), (1f, 0f, 0f, 0f, 1f), (0.5f, 0.5f, 0.5f, 0.5f, 0f) })
            {
                e.DeviceSetParam(voice, di, PLow, lo); e.DeviceSetParam(voice, di, PHigh, hi); e.DeviceSetParam(voice, di, PQ, q);
                e.DeviceSetParam(voice, di, PFormant, fm); e.DeviceSetParam(voice, di, PBands, bn);
                ref0 = Math.Max(ref0, MaxStep(Play(e, Sr), Sr / 4));
            }
            var sweep = Play(e, Sr * 2, 128, b =>
            {
                double ph = b * 0.05;
                e.DeviceSetParam(voice, di, PLow, (float)(0.5 + 0.5 * Math.Sin(ph)));
                e.DeviceSetParam(voice, di, PHigh, (float)(0.5 + 0.5 * Math.Cos(ph * 0.7)));
                e.DeviceSetParam(voice, di, PQ, (float)(0.5 + 0.5 * Math.Sin(ph * 1.3)));
                e.DeviceSetParam(voice, di, PFormant, (float)(0.5 + 0.5 * Math.Sin(ph * 0.4)));
                if (b % 150 == 0) e.DeviceSetParam(voice, di, PBands, (b / 150) % 2 == 0 ? 1f : 0f);
            });
            double ms = MaxStep(sweep, Sr / 4);
            yield return (Finite(sweep) && ms < ref0 * 1.5 + 0.02, $"sweeping Low / High / Q / Formant / Bands is click-free (max step {ms:0.000} vs static corners {ref0:0.000})");
        }

        // ---- recall, determinism, presets ---------------------------------------------------
        {
            float[] Render()
            {
                var (e, voice, _, di) = Rig(carrierTrack: true);
                using var _e = e;
                e.DeviceSetParam(voice, di, PBands, 0.75f);
                e.DeviceSetParam(voice, di, PUnvoiced, 0.6f);
                return Play(e, Sr);
            }
            var a = Render(); var b = Render();
            yield return (MaxDiff(a, b) == 0, $"two offline renders are identical (Δ {MaxDiff(a, b)})");
        }
        {
            string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-vocoder-" + Guid.NewGuid().ToString("N") + ".nota");
            var transport = new TransportState(120.0, 1.0, false, false);
            var (e, voice, car, di) = Rig(carrierTrack: true);
            using (e)
            {
                e.DeviceSetParam(voice, di, PFormant, 0.75f);
                e.SetDeviceSidechainTapPre(voice, di, false);
                ProjectService.Save(ProjectService.Capture(e, transport, new List<string>()), dir, e);
            }
            using var d = new NotaEngine();
            var warn = ProjectService.Apply(ProjectService.Load(dir), d, dir);
            int dVoice = -1, dCar = -1;
            for (int i = 0; i < d.TrackCount; i++)
                if (d.TryGetTrackInfo(i, out var ti)) { if (i == 0) dVoice = ti.Id; else if (i == 1) dCar = ti.Id; }
            bool ok = dVoice > 0 && d.TrackDeviceBuiltinKind(dVoice, di) == Kind
                      && Math.Abs(d.DeviceGetParam(dVoice, di, PFormant) - 0.75f) < 1e-5f && Math.Abs(d.DeviceGetParam(dVoice, di, PCarrier) - 1f) < 1e-5f;
            yield return (warn.Count == 0 && ok, $"save / load restores the vocoder's params ({warn.Count} warnings)");
            yield return (d.DeviceSidechainSource(dVoice, di) == dCar && !d.DeviceSidechainTapPre(dVoice, di),
                $"save / load restores the carrier track and its tap ({d.DeviceSidechainSource(dVoice, di)} vs {dCar})");
            try { Directory.Delete(dir, true); } catch { }
        }
        {
            var cat = new FactoryPresetCatalog();
            var mine = cat.All().Where(p => p.Id.StartsWith("vocoder/", StringComparison.Ordinal)).ToList();
            yield return (mine.Count >= 5, $"{mine.Count} factory presets (≥ 5)");
            var (e, voice, _, di) = Rig();
            using var _e = e;
            var loud = new List<string>(); var fail = new List<string>(); var silent = new List<string>();
            foreach (var p in mine)
            {
                string err = cat.ApplyInPlace(e, p.Id, voice, di);
                if (err.Length != 0) { fail.Add(p.Id + ": " + err); continue; }
                var o = Play(e, Sr * 2);
                if (!Finite(o) || Peak(o) >= 1.0) loud.Add($"{p.Id} {Peak(o):0.00}");
                if (Rms(o, Sr / 2) < 1e-3) silent.Add(p.Id);
            }
            yield return (fail.Count == 0, $"every vocoder preset applies ({string.Join("; ", fail)})");
            yield return (loud.Count == 0 && silent.Count == 0, $"every preset dropped on a voice track is audible and peaks below 0 dBFS (loud: {string.Join(", ", loud)}; silent: {string.Join(", ", silent)})");
        }

        // ---- CPU: 32 bands at 48 kHz within Nota Chamber's budget (SC-004) ------------------
        {
            double Cost(int kind, bool vocoder)
            {
                using var e = new NotaEngine();
                e.SetBpm(120);
                int t = e.AddAudioTrack(); e.AddAudioClip(t, Speech(), 0.0);
                int m = e.AddAudioTrack(); e.AddAudioClip(m, Chord(), 0.0);
                int d = e.AddBuiltinDevice(t, kind);
                if (vocoder) { e.DeviceSetParam(t, d, PBands, 1f); e.DeviceSetParam(t, d, PCarrier, 1f); e.SetDeviceSidechainSource(t, d, m); }
                Play(e, Sr / 2);
                var sw = Stopwatch.StartNew();
                Play(e, Sr * 3);
                return sw.Elapsed.TotalMilliseconds;
            }
            double baseMs = Cost(-1, false);
            double chamber = Cost(20, false) - baseMs;
            double voc = Cost(Kind, true) - baseMs;
            yield return (voc <= Math.Max(chamber, 1.0) * 1.1, $"32-band vocoder costs ≤ Nota Chamber ({voc:0.0} ms vs {chamber:0.0} ms per 3 s)");
        }
    }
}
