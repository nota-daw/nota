// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ColorTests
{
    private const int Kind = 27, ParamCount = 29, LegacySoundParams = 24, RetiredMode = 24;
    private const int MuteLow = 25, MuteHigh = 26, View = 27, FftSize = 28, Mix = 7;

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        int track = e.AddAudioTrack(), di = e.AddBuiltinDevice(track, Kind);
        yield return (di >= 0 && e.DeviceName(track, di) == "Nota Chroma" && e.DeviceParamCount(track, di) == ParamCount &&
            e.TrackLatencySamples(track) == 1024 && e.DeviceParamDefault(track, di, FftSize) == .5f &&
            e.DeviceParamName(track, di, FftSize) == DeviceView.FftSizeParamName,
            "Chroma defaults to a 1024-sample FFT and exposes it as the last parameter");
        yield return (e.DeviceGetParam(track, di, 21) == 0 && e.DeviceGetParam(track, di, 22) == 0 &&
            Math.Abs(e.DeviceGetParam(track, di, 23) - .25f) < 1e-6, "Chroma Morph and Gate default off with -60 dBFS threshold");
        yield return (e.DeviceParamName(track, di, MuteLow) == "Mute low" && e.DeviceParamName(track, di, MuteHigh) == "Mute high" &&
            e.DeviceGetParam(track, di, MuteLow) == 0 && e.DeviceGetParam(track, di, MuteHigh) == 0 &&
            DeviceView.Index(e, track, di) == View && DeviceView.Size(e, track, di) == CardSize.L,
            "Chroma defaults to L with both sidebands audible");
        float[] expected = Enumerable.Range(0, ParamCount).Select(p => (float)((p * 7 % 19) / 18.0)).ToArray();
        expected[RetiredMode] = 0;
        expected[MuteLow] = expected[MuteHigh] = expected[View] = 1;
        expected[FftSize] = 1;
        for (int p = 0; p < expected.Length; p++) e.DeviceSetParam(track, di, p, expected[p]);
        int clone = e.DuplicateTrack(track);
        bool Same(NotaEngine engine, int t, int d, int count) =>
            Enumerable.Range(0, count).All(p => Math.Abs(engine.DeviceGetParam(t, d, p) - expected[p]) < 1e-6);

        yield return (Same(e, clone, di, ParamCount) && DeviceView.Size(e, clone, di) == CardSize.S, "Chroma clone retains every active parameter");
        string dir = Path.Combine(Path.GetTempPath(), "nota-color-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            ProjectService.Save(ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>()), dir, e);
            using var restored = new NotaEngine();
            restored.AddAudioTrack();
            restored.Play();
            restored.RenderOffline(new float[512], 256, 48000);
            bool wasPlaying = restored.IsPlaying;
            var warnings = ProjectService.Apply(ProjectService.Load(dir), restored, dir);
            bool same = restored.TryGetTrackInfo(0, out var info) && restored.TrackDeviceBuiltinKind(info.Id, 0) == Kind &&
                Same(restored, info.Id, 0, ParamCount) && DeviceView.Size(restored, info.Id, 0) == CardSize.S;
            yield return (wasPlaying && warnings.Count == 0 && same,
                "Chroma project round-trip restores FFT even when replacing a playing project");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

        foreach (int count in new[] { 21, 24, 25, 28 })
        {
            if (count == 25)
            {
                int lane = e.AddAutomationLane(track, AutomationTarget.DeviceParam, di, RetiredMode);
                e.SetAutomationPoints(track, lane, [new AutomationPoint(0, 1f)]);
            }
            var legacy = ProjectService.Capture(e, new TransportState(120, 1, false, false), new List<string>());
            foreach (var sourceTrack in legacy.Tracks)
                foreach (var dev in sourceTrack.Devices.Where(d => d.BuiltinKind == Kind))
                    dev.Params = count == 25 ? [.. dev.Params.Take(LegacySoundParams), 1f] : dev.Params.Take(count).ToArray();
            using var old = new NotaEngine();
            var warnings = ProjectService.Apply(legacy, old, dir);
            bool ok = old.TryGetTrackInfo(0, out var tr) && Same(old, tr.Id, 0, Math.Min(count, LegacySoundParams)) &&
                string.IsNullOrEmpty(old.DeviceParamName(tr.Id, 0, RetiredMode)) &&
                old.TrackLatencySamples(tr.Id) == 1024 && old.DeviceGetParam(tr.Id, 0, FftSize) == .5f;
            if (ok) ok = count == 28 ? Same(old, tr.Id, 0, 28) :
                old.DeviceGetParam(tr.Id, 0, MuteLow) == 0 && old.DeviceGetParam(tr.Id, 0, MuteHigh) == 0 &&
                DeviceView.Size(old, tr.Id, 0) == CardSize.L;
            if (count == 21 && ok)
                ok = old.DeviceGetParam(tr.Id, 0, 21) == 0 && old.DeviceGetParam(tr.Id, 0, 22) == 0 &&
                    Math.Abs(old.DeviceGetParam(tr.Id, 0, 23) - .25f) < 1e-6;
            yield return (ok && warnings.Count == 0, $"Chroma loads legacy {count}-parameter state in HQ");
            if (count == 25)
                yield return (ok && old.AutomationLaneCount(tr.Id) == 1 &&
                    old.AutomationLaneInfo(tr.Id, 0) is { ParamIndex: RetiredMode, PointCount: 1 } &&
                    old.DeviceGetParam(tr.Id, 0, RetiredMode) == 0,
                    "Chroma preserves legacy Mode automation data at an inactive parameter index");
        }

        var saved = PresetService.Capture(e, track, di, "Muted bands");
        yield return (saved?.NamedParams is { } captured && captured.GetValueOrDefault("Mute low") == 1 &&
            captured.GetValueOrDefault("Mute high") == 1 && !captured.ContainsKey("View") && !captured.ContainsKey("") &&
            !captured.ContainsKey(DeviceView.FftSizeParamName), "Chroma presets capture sidebands and omit configuration");
        DeviceView.SetSize(e, track, di, CardSize.L);
        e.DeviceSetParam(track, di, MuteLow, 0);
        e.DeviceSetParam(track, di, MuteHigh, 0);
        e.DeviceSetParam(track, di, FftSize, 0);
        yield return (saved is not null && PresetService.ApplyInPlace(saved, e, track, di).Length == 0 &&
            e.DeviceGetParam(track, di, MuteLow) == 1 && e.DeviceGetParam(track, di, MuteHigh) == 1 &&
            DeviceView.Size(e, track, di) == CardSize.L && e.DeviceGetParam(track, di, FftSize) == 0,
            "Chroma reapplies saved mutes and retains the current card and FFT sizes");
        DeviceView.SetSize(e, track, di, CardSize.S);
        var catalog = new FactoryPresetCatalog();
        var presets = catalog.All().Where(p => p.Id.StartsWith("color/", StringComparison.Ordinal)).ToArray();
        yield return (presets.Length == 6 && presets.All(p => catalog.ApplyInPlace(e, p.Id, track, di).Length == 0) &&
            DeviceView.Size(e, track, di) == CardSize.S, "Chroma applies all six factory presets in S");
        foreach (int count in new[] { 21, 24, 25 })
        {
            e.DeviceSetParam(track, di, MuteLow, 1);
            e.DeviceSetParam(track, di, MuteHigh, 1);
            float[] values = count == 25 ? [.. expected.Take(LegacySoundParams), 1f] : expected.Take(count).ToArray();
            var positional = new PresetDocument { Type = "builtin-effect", BuiltinKind = Kind, Params = values };
            yield return (PresetService.ApplyInPlace(positional, e, track, di).Length == 0 &&
                Same(e, track, di, Math.Min(count, LegacySoundParams)) &&
                e.DeviceGetParam(track, di, MuteLow) == 0 && e.DeviceGetParam(track, di, MuteHigh) == 0 &&
                DeviceView.Size(e, track, di) == CardSize.S,
                $"Chroma applies a legacy {count}-slot preset and restores both sidebands");
        }
        var named = new PresetDocument { Type = "builtin-effect", BuiltinKind = Kind,
            NamedParams = new() { ["Mode"] = 1f, ["Morph"] = .4f, [DeviceView.FftSizeParamName] = 1f } };
        yield return (PresetService.ApplyInPlace(named, e, track, di).Length == 0 &&
            Math.Abs(e.DeviceGetParam(track, di, 21) - .4f) < 1e-6 && e.DeviceGetParam(track, di, RetiredMode) == 0 &&
            e.DeviceGetParam(track, di, FftSize) == 0, "Chroma applies only sound parameters from a preset");

        foreach (var result in RenderHq()) yield return result;
        yield return RenderBassSpectrum();
        foreach (var result in RenderFftConfigurations()) yield return result;
        yield return RenderMasterAfterSilence();
        foreach (var result in RenderMidiSource()) yield return result;
    }

    private static IEnumerable<(bool, string)> RenderMidiSource()
    {
        const int rate = 48000, frames = 256;
        const double hz = 277.1826309768721;
        string wav = Path.Combine(Path.GetTempPath(), "nota-chroma-midi-" + Guid.NewGuid().ToString("N") + ".wav");
        string bundle = Path.ChangeExtension(wav, ".nota");
        try
        {
            WavWriter.WriteStereo(wav, 2, rate, i =>
            {
                double sample = .12 * Math.Sin(Math.Tau * hz * i / rate);
                return (sample, sample);
            });
            (int Track, int Device, int Child, int Source) Setup(NotaEngine engine, bool rack)
            {
                int track = engine.AddAudioTrack(), source = engine.AddInstrumentTrack();
                engine.SetTrackBuiltinInstrument(source, 0);
                engine.SetTrackMute(source, true);
                engine.AddAudioClip(track, wav, 0);
                int clip = engine.AddMidiClip(source, 0, 2);
                engine.SetClipNotes(source, clip,
                    [new NotaNote(60, 0, .25, .8f), new NotaNote(72, 0, .125, .8f),
                        new NotaNote(62, .25, .25, .8f), new NotaNote(74, .25, .25, .8f)]);
                int device = engine.AddBuiltinDevice(track, rack ? 5 : Kind);
                int child = rack ? engine.RackDevAddChainDevice(track, device, 0, Kind) : -1;
                if (rack) engine.RackDevChainDeviceParamSet(track, device, 0, child, 3, 0);
                else engine.DeviceSetParam(track, device, 3, 0);
                return (track, device, child, source);
            }

            foreach (bool rack in new[] { false, true })
            {
                using var engine = new NotaEngine();
                var (track, device, child, source) = Setup(engine, rack);
                string host = rack ? "inside an Audio Effect Rack" : "on a track";
                var block = new float[frames * 2];
                var scope = new float[1027];
                int Mask() => (rack
                    ? engine.RackDevChainDeviceScope(track, device, 0, child, scope, scope.Length)
                    : engine.DeviceScope(track, device, scope, scope.Length)) == scope.Length ? (int)scope[^1] : -1;
                int Route(int from) => rack
                    ? engine.RackChainSetSidechainKey(track, device, 0, child, new NestedSidechain(from, false, 0f, 1f), SidechainKey.WholeTrack)
                    : engine.SetDeviceSidechainKey(track, device, from, SidechainKey.WholeTrack);
                void Render() => engine.RenderOffline(block, frames, rate);
                void Advance(double beat)
                {
                    for (int i = 0; i < 256 && engine.PositionBeats < beat; i++) Render();
                }

                bool routed = Route(source) == 0;
                var doc = ProjectService.Capture(engine, new TransportState(120, 1, false, false), []);
                ProjectService.Save(doc, bundle, engine);
                using var loaded = new NotaEngine();
                var warnings = ProjectService.Apply(ProjectService.Load(bundle), loaded, bundle);
                bool routeRestored = loaded.TryGetTrackInfo(0, out var targetInfo) &&
                    loaded.TryGetTrackInfo(1, out var sourceInfo) &&
                    (rack ? loaded.RackChainSidechain(targetInfo.Id, device, 0, child)?.SourceTrack
                        : loaded.DeviceSidechainSource(targetInfo.Id, device)) == sourceInfo.Id;
                yield return (routed && routeRestored && warnings.Count == 0,
                    $"Chroma saves and restores its MIDI key {host} (route {routeRestored}, warnings {warnings.Count})");

                engine.TrackNoteOn(source, 67, .8f);
                Render();
                bool idleKey = !engine.IsPlaying && Mask() == 1 << 7;
                engine.TrackNoteOff(source, 67);
                Render();
                yield return (idleKey && Mask() == 0, $"Chroma displays live notes with the transport stopped {host}");

                engine.Play();
                Advance(.18);
                bool overlap = Mask() == 1;
                Advance(.32);
                bool next = Mask() == 4;
                Advance(.57);
                yield return (overlap && next && Mask() == 0, $"Chroma follows two Arrangement chords and releases their overlapping octaves {host}");

                engine.Seek(.31);
                Render();
                bool chased = Mask() == 4;
                engine.StopTransport();
                Render();
                bool stopped = Mask() == 0;
                engine.SetLoop(true, 0, .5);
                engine.Seek(.4);
                engine.Play();
                for (int i = 0; i < 13; i++) Render();
                yield return (chased && stopped && engine.PositionBeats < .15 && Mask() == 1,
                    $"Chroma clears MIDI on Stop and chases at a seek and loop wrap {host}");

                engine.SetLoop(false, 0, 1);
                engine.Seek(.7);
                engine.SetTrackArmed(source, true);
                engine.NoteOn(65, .8f);
                Render();
                bool live = Mask() == 1 << 5;
                engine.NoteOff(65);
                Render();
                yield return (live && Mask() == 0, $"Chroma responds to live Note On and Note Off {host}");

                int other = engine.AddInstrumentTrack();
                engine.SetTrackBuiltinInstrument(other, 0);
                engine.SetTrackMute(other, true);
                engine.AddMidiEffect(other, 0); // Nota Arp
                engine.TrackNoteOn(other, 60, .8f);
                engine.TrackNoteOn(other, 64, .8f);
                Render();
                bool changed = Route(other) == 0;
                bool arpeggiated = false;
                for (int i = 0; i < 20; i++)
                {
                    Render();
                    arpeggiated |= Mask() is 1 or 16;
                }
                bool removed = engine.RemoveTrack(other);
                Render();
                yield return (changed && arpeggiated && removed && Mask() == 0,
                    $"Chroma reads the post-Arp stream and clears a removed MIDI source {host}");
            }

            float[] Capture(bool callback, bool midi)
            {
                using var engine = new NotaEngine();
                var (track, device, _, source) = Setup(engine, false);
                if (midi) engine.SetDeviceSidechainKey(track, device, source, SidechainKey.WholeTrack);
                else
                {
                    engine.DeviceSetParam(track, device, 1, 1);
                    for (int p = 9; p < 21; p++) engine.DeviceSetParam(track, device, p, 0);
                }
                var block = new float[2 * frames];
                var audio = new float[100 * block.Length];
                engine.RenderOffline(block, frames, rate);
                engine.Play();
                for (int i = 0; i < 100; i++)
                {
                    if (callback) engine.RenderDeviceBlock(block, frames);
                    else engine.RenderOffline(block, frames, rate);
                    block.CopyTo(audio, i * block.Length);
                }
                return audio;
            }
            var offline = Capture(false, true);
            var dry = Capture(false, false);
            yield return (offline.All(float.IsFinite) && offline.SequenceEqual(Capture(true, true)) &&
                !offline.AsSpan(2 * 3000, 2 * 7000).SequenceEqual(dry.AsSpan(2 * 3000, 2 * 7000)) &&
                offline.AsSpan(2 * 15000, 2 * 3000).SequenceEqual(dry.AsSpan(2 * 15000, 2 * 3000)),
                "Chroma MIDI audio matches realtime and offline output and returns to aligned dry");
        }
        finally
        {
            File.Delete(wav);
            if (Directory.Exists(bundle)) Directory.Delete(bundle, true);
        }
    }

    private static (bool, string) RenderMasterAfterSilence()
    {
        const int rate = 48000, frames = 256, latency = 2048, blocks = rate / frames;
        string wav = Path.Combine(Path.GetTempPath(), "nota-chroma-idle-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.WriteStereo(wav, 1, rate, i =>
                i is >= 875 and < 3875 or >= 35219 and < 40319 ? (0, .12 * Math.Sin(Math.Tau * 440 * i / rate)) : (0, 0));
            float[] Render(bool withChroma)
            {
                using var engine = new NotaEngine();
                int track = engine.AddAudioTrack();
                engine.AddAudioClip(track, wav, 0);
                if (withChroma)
                {
                    int master = engine.MasterTrackId;
                    int device = engine.AddBuiltinDevice(master, Kind);
                    engine.DeviceSetParam(master, device, FftSize, 1);
                    engine.DeviceSetParam(master, device, Mix, 0);
                }
                var block = new float[2 * frames];
                var audio = new float[2 * frames * blocks];
                engine.RenderOffline(block, frames, rate);
                engine.Play();
                for (int i = 0; i < blocks; i++)
                {
                    engine.RenderDeviceBlock(block, frames);
                    block.CopyTo(audio, i * block.Length);
                }
                return audio;
            }

            var dry = Render(false);
            var master = Render(true);
            bool woke = master.AsSpan(2 * 38000, 2 * 4000).ToArray().Any(v => Math.Abs(v) > .03f);
            return (woke && master.Take(2 * latency).All(v => v == 0) &&
                master.AsSpan(2 * latency).SequenceEqual(dry.AsSpan(0, dry.Length - 2 * latency)),
                "Chroma on the master wakes after silence in the device callback with exact delay");
        }
        finally { File.Delete(wav); }
    }

    private static IEnumerable<(bool, string)> RenderFftConfigurations()
    {
        const int rate = 48000, frames = 256;
        string stem = Path.Combine(Path.GetTempPath(), "nota-chroma-fft-" + Guid.NewGuid().ToString("N"));
        string left = stem + ".wav", right = stem + "-inverted.wav";
        try
        {
            WavWriter.WriteStereo(left, 1, rate, i => i % 4096 == 900 ? (.35, .35) : (0, 0));
            WavWriter.WriteStereo(right, 1, rate, i => i % 4096 == 900 ? (-.35, -.35) : (0, 0));
            foreach (bool inRack in new[] { false, true })
            {
                using var engine = new NotaEngine();
                int track = engine.AddAudioTrack(), reference = engine.AddAudioTrack();
                engine.AddAudioClip(track, left, 0);
                engine.AddAudioClip(reference, right, 0);
                int device = engine.AddBuiltinDevice(track, inRack ? 5 : Kind);
                int child = inRack ? engine.RackDevAddChainDevice(track, device, 0, Kind) : -1;
                if (inRack) engine.RackDevSetPdc(track, device, true);
                string host = inRack ? "inside the effect rack" : "on the track";
                var block = new float[2 * frames];
                var scope = new float[1026];
                void Set(int p, float value)
                {
                    if (inRack) engine.RackDevChainDeviceParamSet(track, device, 0, child, p, value);
                    else engine.DeviceSetParam(track, device, p, value);
                }
                float Choice() => inRack
                    ? engine.RackDevChainDeviceParamGet(track, device, 0, child, FftSize)
                    : engine.DeviceGetParam(track, device, FftSize);
                void Render() => engine.RenderOffline(block, frames, rate);
                void Stop() { engine.StopTransport(); Render(); }

                Set(Mix, 0);
                engine.Play();
                Set(FftSize, 1);
                bool blockedQueued = Choice() == .5f;
                Render();
                Set(FftSize, 1);
                yield return (blockedQueued && Choice() == .5f && engine.IsPlaying,
                    $"Chroma FFT rejects changes during queued Play and playback {host}");
                Stop();
                engine.SetCountIn(1);
                engine.PlayWithCountIn();
                Render();
                Set(FftSize, 1);
                yield return (engine.CountInBeats > 0 && Choice() == .5f, $"Chroma FFT rejects a change in count-in {host}");
                Stop();

                if (!inRack)
                {
                    int lfo = engine.ModulatorAdd(track, 0);
                    yield return (lfo >= 0 && engine.CvLinkAdd(track, lfo, device, FftSize) < 0 &&
                        engine.AddAutomationLane(track, AutomationTarget.DeviceParam, device, FftSize) < 0,
                        "Chroma FFT stays outside automation and CV");
                    int instrument = engine.AddInstrumentTrack();
                    engine.SetTrackArmed(instrument, true);
                    engine.SetCountIn(0);
                    engine.SetRecording(true);
                    Render();
                    Set(FftSize, 1);
                    yield return (engine.IsRecording && Choice() == .5f, "Chroma FFT rejects a change while recording");
                    engine.SetRecording(false);
                    Stop();
                }
                else yield return (engine.RackDevAddMacroMapping(track, device, 0, 0, child, FftSize, 0, 1) < 0,
                    "Chroma FFT stays outside rack macro mappings");
                foreach (var (choice, expected) in new[] { (0f, 512), (1f, 2048), (.5f, 1024) })
                {
                    Set(FftSize, choice);
                    bool latency = engine.TrackLatencySamples(track) == expected;
                    int count = inRack ? engine.RackDevChainDeviceScope(track, device, 0, child, scope, scope.Length)
                        : engine.DeviceScope(track, device, scope, scope.Length);
                    engine.Seek(0);
                    engine.Play();
                    float peak = 0;
                    for (int i = 0; i < 32; i++)
                    {
                        Render();
                        peak = Math.Max(peak, block.Max(x => Math.Abs(x)));
                    }
                    yield return (Choice() == choice && latency && count == scope.Length && scope[1] == expected && peak < 1e-5f,
                        $"Chroma FFT {expected} updates scope and aligns an inverted reference {host} (residual {peak:G3})");
                    Stop();
                }
            }
        }
        finally { File.Delete(left); File.Delete(right); }
    }

    private static IEnumerable<(bool, string)> RenderHq()
    {
        const int rate = 48000, blockSize = 512;
        const double source = 277.1826309768721, target = source * 242 / 256;
        string wav = Path.Combine(Path.GetTempPath(), "nota-color-hq-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.WriteStereo(wav, 1, rate, i =>
            {
                double sample = .1 * Math.Sin(Math.Tau * source * i / rate);
                return (sample, sample);
            });
            using var engine = new NotaEngine();
            int track = engine.AddAudioTrack();
            engine.AddAudioClip(track, wav, 0);
            int device = engine.AddBuiltinDevice(track, Kind);
            engine.DeviceSetParam(track, device, 3, 0);
            engine.DeviceSetParam(track, device, 1, 1);
            for (int p = 9; p < 21; p++) engine.DeviceSetParam(track, device, p, p == 9 ? 1 : 0);
            engine.Play();

            var block = new float[blockSize * 2];
            var targetSum = System.Numerics.Complex.Zero;
            var sourceSum = System.Numerics.Complex.Zero;
            bool finite = true;
            int count = 0;
            for (int b = 0; b < 90; b++)
            {
                engine.RenderOffline(block, blockSize, rate);
                finite &= block.All(float.IsFinite);
                if (b < 45) continue;
                for (int i = 0; i < blockSize; i++, count++)
                {
                    targetSum += block[2 * i] * System.Numerics.Complex.FromPolarCoordinates(1, Math.Tau * target * count / rate);
                    sourceSum += block[2 * i] * System.Numerics.Complex.FromPolarCoordinates(1, Math.Tau * source * count / rate);
                }
            }
            double tuned = 2 * targetSum.Magnitude / count, original = 2 * sourceSum.Magnitude / count;
            yield return (finite && tuned > .015 && original < .35 * tuned,
                $"Chroma HQ shifts C sharp down through the C ABI (tuned {tuned:F4}, source {original:F4})");
            var spectrum = new float[1026];
            yield return (engine.DeviceScope(track, device, spectrum, spectrum.Length) == 1026 && spectrum[1] == 1024 &&
                spectrum.AsSpan(2, 512).ToArray().Any(v => v > .3f) && spectrum.AsSpan(514, 512).ToArray().Any(v => v > .3f),
                "Chroma publishes live Dry and Tuned spectra through the C ABI");
        }
        finally { File.Delete(wav); }
    }

    private static (bool, string) RenderBassSpectrum()
    {
        const int rate = 48000, blockSize = 512, bins = 512;
        string wav = Path.Combine(Path.GetTempPath(), "nota-chroma-bass-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.WriteStereo(wav, 1, rate, i =>
            {
                double sample = .12 * (Math.Sin(Math.Tau * 55 * i / rate) + Math.Sin(Math.Tau * 82 * i / rate));
                return (sample, -sample);
            });
            using var engine = new NotaEngine();
            int track = engine.AddAudioTrack();
            engine.AddAudioClip(track, wav, 0);
            int device = engine.AddBuiltinDevice(track, Kind);
            engine.Play();
            var block = new float[2 * blockSize];
            for (int i = 0; i < 90; i++) engine.RenderOffline(block, blockSize, rate);

            var scope = new float[2 + 2 * bins];
            int count = engine.DeviceScope(track, device, scope, scope.Length);
            int Band(double hz) => (int)(bins * Math.Log(hz / 40) / Math.Log(500));
            float Peak(double low, double high) => Enumerable.Range(Band(low), Band(high) - Band(low) + 1)
                .Max(i => scope[2 + i]);
            return (count == scope.Length && scope[0] == rate && scope[1] == 1024 &&
                Math.Min(Peak(51, 59), Peak(78, 86)) > Peak(66, 70) + .15f,
                "Chroma Dry telemetry resolves 55 Hz and 82 Hz with opposite stereo polarity");
        }
        finally { File.Delete(wav); }
    }
}
