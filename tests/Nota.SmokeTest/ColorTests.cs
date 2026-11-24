// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ColorTests
{
    private const int Kind = 27, ParamCount = 32, LegacySoundParams = 24, RetiredMode = 24;
    private const int Mix = 7, Gate = 22, GateThreshold = 23, MuteLow = 25, MuteHigh = 26, View = 27, FftSize = 28;
    private const int Pre = 29, Post = 30, PreEnabled = 31;

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        int track = e.AddAudioTrack(), di = e.AddBuiltinDevice(track, Kind);
        yield return (di >= 0 && e.DeviceName(track, di) == "Nota Color" && e.DeviceParamCount(track, di) == ParamCount &&
            e.TrackLatencySamples(track) == 1024 && e.DeviceParamDefault(track, di, FftSize) == .5f &&
            e.DeviceParamName(track, di, FftSize) == DeviceView.FftSizeParamName &&
            e.DeviceGetParam(track, di, Pre) == .5f && e.DeviceGetParam(track, di, Post) == .5f &&
            e.DeviceGetParam(track, di, PreEnabled) == 0,
            "Nota Color defaults to a 1024-sample FFT with neutral Pre and Post");
        yield return (e.DeviceGetParam(track, di, 21) == 0 && e.DeviceGetParam(track, di, 22) == 0 &&
            Math.Abs(e.DeviceGetParam(track, di, 23) - .25f) < 1e-6, "Nota Color Morph and Gate default off with -60 dBFS threshold");
        yield return (DeviceView.IsSnapshotExcludedParam(e.DeviceParamName(track, di, View)) &&
            DeviceView.IsSnapshotExcludedParam(e.DeviceParamName(track, di, FftSize)) &&
            !DeviceView.IsSnapshotExcludedParam(e.DeviceParamName(track, di, Gate)) &&
            !DeviceView.IsSnapshotExcludedParam(e.DeviceParamName(track, di, PreEnabled)),
            "Nota Color A/B includes sound switches but keeps View and FFT size");
        foreach (int param in new[] { FftSize, Gate, PreEnabled })
        {
            float before = e.DeviceGetParam(track, di, param);
            MixEdit.Step(e, () => e.DeviceSetParam(track, di, param, 1), MixTarget.DeviceParam(track, di, param));
            bool changed = e.DeviceGetParam(track, di, param) == 1;
            e.Undo();
            bool undone = e.DeviceGetParam(track, di, param) == before;
            e.Redo();
            bool redone = e.DeviceGetParam(track, di, param) == 1;
            e.Undo();
            yield return (changed && undone && redone, $"Nota Color {e.DeviceParamName(track, di, param)} supports undo and redo");
        }
        yield return (e.DeviceParamName(track, di, MuteLow) == "Mute low" && e.DeviceParamName(track, di, MuteHigh) == "Mute high" &&
            e.DeviceGetParam(track, di, MuteLow) == 0 && e.DeviceGetParam(track, di, MuteHigh) == 0 &&
            DeviceView.Index(e, track, di) == View && DeviceView.Size(e, track, di) == CardSize.L,
            "Nota Color defaults to L with both sidebands audible");
        float[] expected = Enumerable.Range(0, ParamCount).Select(p => (float)((p * 7 % 19) / 18.0)).ToArray();
        expected[RetiredMode] = 0;
        expected[MuteLow] = expected[MuteHigh] = expected[View] = 1;
        expected[FftSize] = 1;
        expected[Gate] = 1;
        expected[PreEnabled] = 1;
        for (int p = 0; p < expected.Length; p++) e.DeviceSetParam(track, di, p, expected[p]);
        int clone = e.DuplicateTrack(track);
        bool Same(NotaEngine engine, int t, int d, int count) =>
            Enumerable.Range(0, count).All(p => Math.Abs(engine.DeviceGetParam(t, d, p) - expected[p]) < 1e-6);

        yield return (Same(e, clone, di, ParamCount) && DeviceView.Size(e, clone, di) == CardSize.S, "Nota Color clone retains every active parameter");
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
                Same(restored, info.Id, 0, ParamCount) && DeviceView.Size(restored, info.Id, 0) == CardSize.S &&
                restored.TrackLatencySamples(info.Id) > 4096;
            yield return (wasPlaying && warnings.Count == 0 && same,
                "Nota Color project round-trip restores FFT and Gate even when replacing a playing project");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

        foreach (int count in new[] { 21, 24, 25, 28, 29 })
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
                old.TrackLatencySamples(tr.Id) == (count == 29 ? 4096 : count > Gate ? 2048 : 1024) &&
                old.DeviceGetParam(tr.Id, 0, FftSize) == (count == 29 ? 1 : .5f) &&
                old.DeviceGetParam(tr.Id, 0, Pre) == .5f && old.DeviceGetParam(tr.Id, 0, Post) == .5f &&
                old.DeviceGetParam(tr.Id, 0, PreEnabled) == 0;
            if (ok) ok = count >= 28 ? Same(old, tr.Id, 0, count) :
                old.DeviceGetParam(tr.Id, 0, MuteLow) == 0 && old.DeviceGetParam(tr.Id, 0, MuteHigh) == 0 &&
                DeviceView.Size(old, tr.Id, 0) == CardSize.L;
            if (count == 21 && ok)
                ok = old.DeviceGetParam(tr.Id, 0, 21) == 0 && old.DeviceGetParam(tr.Id, 0, 22) == 0 &&
                    Math.Abs(old.DeviceGetParam(tr.Id, 0, 23) - .25f) < 1e-6;
            yield return (ok && warnings.Count == 0, $"Nota Color loads legacy {count}-parameter state in HQ");
            if (count == 25)
                yield return (ok && old.AutomationLaneCount(tr.Id) == 1 &&
                    old.AutomationLaneInfo(tr.Id, 0) is { ParamIndex: RetiredMode, PointCount: 1 } &&
                    old.DeviceGetParam(tr.Id, 0, RetiredMode) == 0,
                    "Nota Color preserves legacy Mode automation data at an inactive parameter index");
        }

        var saved = PresetService.Capture(e, track, di, "Muted bands");
        yield return (saved?.NamedParams is { } captured && captured.GetValueOrDefault("Mute low") == 1 &&
            captured.GetValueOrDefault("Mute high") == 1 && !captured.ContainsKey("View") && !captured.ContainsKey("") &&
            captured.GetValueOrDefault(DeviceView.ColorGateParamName) == 1 &&
            captured.GetValueOrDefault(DeviceView.ColorPreEnabledParamName) == 1 &&
            captured.GetValueOrDefault("Pre") == expected[Pre] && captured.GetValueOrDefault("Post") == expected[Post] &&
            captured.ContainsKey("Gate threshold") && !captured.ContainsKey(DeviceView.FftSizeParamName),
            "Nota Color presets capture Gate, sidebands and Pre/Post settings");
        DeviceView.SetSize(e, track, di, CardSize.L);
        e.DeviceSetParam(track, di, MuteLow, 0);
        e.DeviceSetParam(track, di, MuteHigh, 0);
        e.DeviceSetParam(track, di, FftSize, 0);
        e.DeviceSetParam(track, di, Gate, 0);
        e.DeviceSetParam(track, di, PreEnabled, 0);
        e.DeviceSetParam(track, di, Pre, .5f);
        e.DeviceSetParam(track, di, Post, .5f);
        e.Play();
        e.RenderOffline(new float[512], 256, 48000);
        bool presetWhilePlaying = saved is not null && PresetService.ApplyInPlace(saved, e, track, di).Length == 0 &&
            e.IsPlaying && e.DeviceGetParam(track, di, Gate) == 1 && e.DeviceGetParam(track, di, PreEnabled) == 1 &&
            e.DeviceGetParam(track, di, MuteLow) == 1 && e.DeviceGetParam(track, di, Pre) == expected[Pre] &&
            e.DeviceGetParam(track, di, Post) == expected[Post] && e.TrackLatencySamples(track) == 3072;
        yield return (presetWhilePlaying, "Nota Color loads preset switches and updates latency during playback");
        e.StopTransport();
        e.RenderOffline(new float[512], 256, 48000);
        yield return (saved is not null && PresetService.ApplyInPlace(saved, e, track, di).Length == 0 &&
            e.DeviceGetParam(track, di, MuteLow) == 1 && e.DeviceGetParam(track, di, MuteHigh) == 1 &&
            e.DeviceGetParam(track, di, Gate) == 1 && e.DeviceGetParam(track, di, PreEnabled) == 1 &&
            e.TrackLatencySamples(track) > 1024 &&
            DeviceView.Size(e, track, di) == CardSize.L && e.DeviceGetParam(track, di, FftSize) == 0,
            "Nota Color reapplies saved mutes and retains the current card and FFT sizes");
        DeviceView.SetSize(e, track, di, CardSize.S);
        var catalog = new FactoryPresetCatalog();
        var presets = catalog.All().Where(p => p.Id.StartsWith("color/", StringComparison.Ordinal)).ToArray();
        yield return (presets.Length == 6 && presets.All(p => catalog.ApplyInPlace(e, p.Id, track, di).Length == 0) &&
            DeviceView.Size(e, track, di) == CardSize.S, "Nota Color applies all six factory presets in S");
        foreach (int count in new[] { 21, 24, 25 })
        {
            e.DeviceSetParam(track, di, MuteLow, 1);
            e.DeviceSetParam(track, di, MuteHigh, 1);
            e.DeviceSetParam(track, di, Pre, 1);
            e.DeviceSetParam(track, di, Post, 1);
            e.DeviceSetParam(track, di, PreEnabled, 1);
            float[] values = count == 25 ? [.. expected.Take(LegacySoundParams), 1f] : expected.Take(count).ToArray();
            var positional = new PresetDocument { Type = "builtin-effect", BuiltinKind = Kind, Params = values };
            yield return (PresetService.ApplyInPlace(positional, e, track, di).Length == 0 &&
                Same(e, track, di, Math.Min(count, LegacySoundParams)) &&
                e.DeviceGetParam(track, di, MuteLow) == 0 && e.DeviceGetParam(track, di, MuteHigh) == 0 &&
                e.DeviceGetParam(track, di, Pre) == .5f && e.DeviceGetParam(track, di, Post) == .5f &&
                e.DeviceGetParam(track, di, PreEnabled) == 0 &&
                DeviceView.Size(e, track, di) == CardSize.S,
                $"Nota Color applies a legacy {count}-slot preset and restores both sidebands");
        }
        var named = new PresetDocument { Type = "builtin-effect", BuiltinKind = Kind,
            NamedParams = new() { ["Mode"] = 1f, ["Morph"] = .4f, [DeviceView.FftSizeParamName] = 1f } };
        yield return (PresetService.ApplyInPlace(named, e, track, di).Length == 0 &&
            Math.Abs(e.DeviceGetParam(track, di, 21) - .4f) < 1e-6 && e.DeviceGetParam(track, di, RetiredMode) == 0 &&
            e.DeviceGetParam(track, di, FftSize) == 0, "Nota Color applies only sound parameters from a preset");

        foreach (var result in RenderHq()) yield return result;
        yield return RenderBassSpectrum();
        foreach (var result in RenderFftConfigurations()) yield return result;
        foreach (var result in RenderLiveLatencyChanges()) yield return result;
        foreach (var result in RenderSpectralGate()) yield return result;
        foreach (var result in RenderFineTuning()) yield return result;
        yield return RenderNeutralPreSpectrum();
        foreach (var result in RenderMasterAfterSilence()) yield return result;
        foreach (var result in RenderMidiSource()) yield return result;
    }

    private static IEnumerable<(bool, string)> RenderMidiSource()
    {
        const int rate = 48000, frames = 256;
        const double hz = 277.1826309768721;
        string wav = Path.Combine(Path.GetTempPath(), "nota-color-midi-" + Guid.NewGuid().ToString("N") + ".wav");
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
                    $"Nota Color saves and restores its MIDI key {host} (route {routeRestored}, warnings {warnings.Count})");

                engine.TrackNoteOn(source, 67, .8f);
                Render();
                bool idleKey = !engine.IsPlaying && Mask() == 1 << 7;
                engine.TrackNoteOff(source, 67);
                Render();
                yield return (idleKey && Mask() == 0, $"Nota Color displays live notes with the transport stopped {host}");

                engine.Play();
                Advance(.18);
                bool overlap = Mask() == 1;
                Advance(.32);
                bool next = Mask() == 4;
                Advance(.57);
                yield return (overlap && next && Mask() == 0, $"Nota Color follows two Arrangement chords and releases their overlapping octaves {host}");

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
                    $"Nota Color clears MIDI on Stop and chases at a seek and loop wrap {host}");

                engine.SetLoop(false, 0, 1);
                engine.Seek(.7);
                engine.SetTrackArmed(source, true);
                engine.NoteOn(65, .8f);
                Render();
                bool live = Mask() == 1 << 5;
                engine.NoteOff(65);
                Render();
                yield return (live && Mask() == 0, $"Nota Color responds to live Note On and Note Off {host}");

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
                    $"Nota Color reads the post-Arp stream and clears a removed MIDI source {host}");
            }

            float[] Capture(bool callback, bool midi, bool gate, bool pre)
            {
                using var engine = new NotaEngine();
                var (track, device, _, source) = Setup(engine, false);
                engine.DeviceSetParam(track, device, Gate, gate ? 1 : 0);
                engine.DeviceSetParam(track, device, PreEnabled, pre ? 1 : 0);
                if (pre) engine.DeviceSetParam(track, device, Pre, .75f);
                if (gate) engine.DeviceSetParam(track, device, GateThreshold, 1);
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
            foreach (bool gate in new[] { false, true })
            foreach (bool pre in new[] { false, true })
            {
                var offline = Capture(false, true, gate, pre);
                var dry = Capture(false, false, gate, pre);
                int released = pre ? 19000 : 15000;
                yield return (offline.All(float.IsFinite) && offline.SequenceEqual(Capture(true, true, gate, pre)) &&
                    !offline.AsSpan(2 * 3000, 2 * 7000).SequenceEqual(dry.AsSpan(2 * 3000, 2 * 7000)) &&
                    offline.AsSpan(2 * released, 2 * 3000).SequenceEqual(dry.AsSpan(2 * released, 2 * 3000)),
                    $"Nota Color MIDI matches realtime and offline output and releases correction (Gate {gate}, Pre {pre})");
            }
        }
        finally
        {
            File.Delete(wav);
            if (Directory.Exists(bundle)) Directory.Delete(bundle, true);
        }
    }

    private static IEnumerable<(bool, string)> RenderMasterAfterSilence()
    {
        const int rate = 48000, frames = 256, window = 2048, blocks = rate / frames;
        string wav = Path.Combine(Path.GetTempPath(), "nota-color-idle-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.WriteStereo(wav, 1, rate, i =>
                i is >= 875 and < 3875 or >= 35219 and < 40319 ? (0, .12 * Math.Sin(Math.Tau * 440 * i / rate)) : (0, 0));
            float[] Render(bool withColor, bool gate = false)
            {
                using var engine = new NotaEngine();
                int track = engine.AddAudioTrack();
                engine.AddAudioClip(track, wav, 0);
                if (withColor)
                {
                    int master = engine.MasterTrackId;
                    int device = engine.AddBuiltinDevice(master, Kind);
                    engine.DeviceSetParam(master, device, FftSize, 1);
                    engine.DeviceSetParam(master, device, Gate, gate ? 1 : 0);
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
            foreach (bool gate in new[] { false, true })
            {
                int latency = gate ? 2 * window : window;
                var master = Render(true, gate);
                bool woke = master.AsSpan(2 * (35219 + latency), 2 * 4000).ToArray().Any(v => Math.Abs(v) > .03f);
                yield return (woke && master.Take(2 * latency).All(v => v == 0) &&
                    master.AsSpan(2 * latency).SequenceEqual(dry.AsSpan(0, dry.Length - 2 * latency)),
                    $"Nota Color on the master wakes after silence with exact delay (Gate {gate})");
            }
        }
        finally { File.Delete(wav); }
    }

    private static IEnumerable<(bool, string)> RenderFftConfigurations()
    {
        const int rate = 48000, frames = 256;
        string stem = Path.Combine(Path.GetTempPath(), "nota-color-fft-" + Guid.NewGuid().ToString("N"));
        string left = stem + ".wav", right = stem + "-inverted.wav";
        try
        {
            WavWriter.WriteStereo(left, 4, rate, i => i % 4093 == 900 ? (.35, .35) : (0, 0));
            WavWriter.WriteStereo(right, 4, rate, i => i % 4093 == 900 ? (-.35, -.35) : (0, 0));
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
                float Get(int p) => inRack
                    ? engine.RackDevChainDeviceParamGet(track, device, 0, child, p)
                    : engine.DeviceGetParam(track, device, p);
                void Configure(float fft, float gate, float pre)
                {
                    Set(FftSize, fft);
                    Set(Gate, gate);
                    Set(PreEnabled, pre);
                }
                bool Matches(float fft, float gate, float pre, int latency)
                {
                    int count = inRack ? engine.RackDevChainDeviceScope(track, device, 0, child, scope, scope.Length)
                        : engine.DeviceScope(track, device, scope, scope.Length);
                    return Get(FftSize) == fft && Get(Gate) == gate && Get(PreEnabled) == pre &&
                        engine.TrackLatencySamples(track) == latency && count == scope.Length && scope[1] == latency;
                }
                void Render() => engine.RenderOffline(block, frames, rate);
                void Stop() { engine.StopTransport(); Render(); }

                Set(Mix, 0);
                engine.Play();
                Configure(1, 1, 1);
                Render();
                yield return (engine.IsPlaying && Matches(1, 1, 1, 6144),
                    $"Nota Color accepts FFT, Gate and Pre enable after queued Play {host}");
                Stop();
                engine.SetCountIn(1);
                engine.Seek(0);
                engine.PlayWithCountIn();
                Render();
                Configure(0, 0, 0);
                Render();
                yield return (engine.CountInBeats > 0 && Matches(0, 0, 0, 512),
                    $"Nota Color changes FFT, Gate and Pre enable during count-in {host}");
                Stop();

                int instrument = engine.AddInstrumentTrack();
                engine.SetTrackArmed(instrument, true);
                engine.SetCountIn(0);
                engine.SetRecording(true);
                Render();
                Configure(1, 1, 1);
                Render();
                yield return (engine.IsRecording && Matches(1, 1, 1, 6144),
                    $"Nota Color changes FFT, Gate and Pre enable while recording {host}");
                engine.SetRecording(false);
                Stop();

                if (!inRack)
                {
                    int lfo = engine.ModulatorAdd(track, 0);
                    yield return (lfo >= 0 && engine.CvLinkAdd(track, lfo, device, FftSize) < 0 &&
                        engine.CvLinkAdd(track, lfo, device, Gate) < 0 &&
                        engine.CvLinkAdd(track, lfo, device, PreEnabled) < 0 &&
                        engine.AddAutomationLane(track, AutomationTarget.DeviceParam, device, FftSize) < 0 &&
                        engine.AddAutomationLane(track, AutomationTarget.DeviceParam, device, Gate) < 0 &&
                        engine.AddAutomationLane(track, AutomationTarget.DeviceParam, device, PreEnabled) < 0 &&
                        engine.AddAutomationLane(track, AutomationTarget.DeviceParam, device, GateThreshold) >= 0 &&
                        engine.AddAutomationLane(track, AutomationTarget.DeviceParam, device, Pre) >= 0 &&
                        engine.AddAutomationLane(track, AutomationTarget.DeviceParam, device, Post) >= 0,
                        "Nota Color FFT, Gate and Pre enable stay outside automation while threshold, Pre and Post support it");
                }
                else yield return (engine.RackDevAddMacroMapping(track, device, 0, 0, child, FftSize, 0, 1) < 0 &&
                    engine.RackDevAddMacroMapping(track, device, 0, 0, child, Gate, 0, 1) < 0 &&
                    engine.RackDevAddMacroMapping(track, device, 0, 0, child, PreEnabled, 0, 1) < 0,
                    "Nota Color FFT, Gate and Pre enable stay outside rack macro mappings");
                Set(Pre, .75f);
                engine.Seek(0);
                engine.Play();
                Render();
                foreach (var (choice, window) in new[] { (0f, 512), (1f, 2048), (.5f, 1024) })
                foreach (float gate in new[] { 0f, 1f })
                foreach (float pre in new[] { 0f, 1f })
                {
                    Configure(choice, gate, pre);
                    int expected = window * (gate == 1 ? 2 : 1) + (pre == 1 ? 2048 : 0);
                    for (int i = 0; i < 26; i++) Render(); // Fill the longest configuration (6144 samples).
                    float peak = 0;
                    double before = engine.PositionBeats;
                    for (int i = 0; i < 17; i++)
                    {
                        Render();
                        peak = Math.Max(peak, block.Max(x => Math.Abs(x)));
                    }
                    yield return (engine.IsPlaying && engine.PositionBeats > before &&
                        Matches(choice, gate, pre, expected) && peak < 1e-5f,
                        $"Nota Color FFT {window}, Gate {gate}, Pre {pre} reports {expected} samples and realigns dry while playing {host} (residual {peak:G3})");
                }
                engine.SetTrackMute(reference, true);
                float signal = 0;
                for (int i = 0; i < 20; i++)
                {
                    Render();
                    signal = Math.Max(signal, block.Max(v => Math.Abs(v)));
                }
                yield return (signal > .1f, $"Nota Color still passes the source after the live changes {host}");
            }
        }
        finally { File.Delete(left); File.Delete(right); }
    }

    private static IEnumerable<(bool, string)> RenderLiveLatencyChanges()
    {
        const int rate = 48000, frames = 256;
        using var engine = new NotaEngine();
        int track = engine.AddInstrumentTrack(), color = engine.AddBuiltinDevice(track, Kind);
        var block = new float[2 * frames];
        engine.DeviceSetParam(track, color, Mix, 0);
        engine.TrackNoteOn(track, 60, .8f);
        float Level()
        {
            float peak = 0;
            for (int i = 0; i < 28; i++)
            {
                engine.RenderOffline(block, frames, rate);
                if (i >= 24) peak = Math.Max(peak, block.Max(v => Math.Abs(v)));
            }
            return peak;
        }
        float before = Level();
        engine.DeviceSetParam(track, color, FftSize, 1);
        engine.DeviceSetParam(track, color, Gate, 1);
        engine.DeviceSetParam(track, color, PreEnabled, 1);
        float after = Level();
        yield return (!engine.IsPlaying && before > .01f && after > .01f && engine.TrackLatencySamples(track) == 6144,
            $"Nota Color returns to a held note after reconfiguring with transport stopped ({before:G3}, {after:G3})");

        int rhythm = engine.AddInstrumentTrack();
        engine.SetTrackBuiltinInstrument(rhythm, 12);
        int voiceColor = engine.RhythmAddVoiceDevice(rhythm, 0, Kind);
        engine.Play();
        engine.RenderOffline(block, frames, rate);
        engine.RhythmVoiceDeviceParamSet(rhythm, 0, voiceColor, FftSize, 1);
        engine.RhythmVoiceDeviceParamSet(rhythm, 0, voiceColor, Gate, 1);
        engine.RhythmVoiceDeviceParamSet(rhythm, 0, voiceColor, PreEnabled, 1);
        var scope = new float[2];
        yield return (engine.IsPlaying && engine.RhythmVoiceDeviceScope(rhythm, 0, voiceColor, scope, scope.Length) == 2 &&
            scope[1] == 6144, "Nota Color updates latency in a Rhythm voice during playback");
    }

    private static IEnumerable<(bool, string)> RenderSpectralGate()
    {
        const int rate = 48000, frames = 256;
        const double loud = 1875, quiet = 2812.5, sub = 46.875;
        const double inKey = 261.625565;
        string wav = Path.Combine(Path.GetTempPath(), "nota-color-gate-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            void Render(NotaEngine engine, float[] samples)
            {
                var block = new float[frames * 2];
                for (int i = 0; i < samples.Length; i += frames)
                {
                    engine.RenderOffline(block, frames, rate);
                    for (int j = 0; j < frames; j++) samples[i + j] = block[2 * j];
                }
            }

            static double Level(float[] signal, double hz)
            {
                var sum = System.Numerics.Complex.Zero;
                const int start = frames * 64, end = frames * 96;
                for (int i = start; i < end; i++)
                    sum += signal[i] * System.Numerics.Complex.FromPolarCoordinates(1, Math.Tau * hz * i / rate);
                return 2 * sum.Magnitude / (end - start);
            }

            WavWriter.WriteStereo(wav, 1.5, rate, i =>
            {
                double phase = Math.Tau * i / rate;
                double sample = .25 * Math.Sin(loud * phase) + .012 * Math.Sin(quiet * phase) + .15 * Math.Sin(sub * phase);
                return (sample, -sample);
            });
            using (var engine = new NotaEngine())
            {
                int track = engine.AddAudioTrack();
                engine.AddAudioClip(track, wav, 0);
                int device = engine.AddBuiltinDevice(track, Kind);
                engine.DeviceSetParam(track, device, 3, 0);
                engine.DeviceSetParam(track, device, Gate, 1);
                engine.DeviceSetParam(track, device, GateThreshold, 0);
                var before = new float[frames * 96];
                var after = new float[before.Length];
                engine.Play();
                Render(engine, before);
                engine.DeviceSetParam(track, device, GateThreshold, .625f); // −30 dBFS
                Render(engine, after);
                double loudRatio = Level(after, loud) / Level(before, loud);
                double quietRatio = Level(after, quiet) / Level(before, quiet);
                double subRatio = Level(after, sub) / Level(before, sub);
                yield return (engine.IsPlaying && loudRatio is > .8 and < 1.2 && quietRatio < .15 &&
                    subRatio is > .95 and < 1.05,
                    $"Nota Color Gate threshold works live by frequency and preserves the sideband ({loudRatio:F3}, {quietRatio:F3}, {subRatio:F3})");
            }

            WavWriter.WriteStereo(wav, 1.5, rate, i =>
            {
                double sample = i < rate / 2 ? .15 * Math.Sin(Math.Tau * inKey * i / rate) : 0;
                return (sample, sample);
            });
            double TailRms(bool gated)
            {
                using var engine = new NotaEngine();
                int track = engine.AddAudioTrack();
                engine.AddAudioClip(track, wav, 0);
                int device = engine.AddBuiltinDevice(track, Kind);
                engine.DeviceSetParam(track, device, 3, .8f);
                engine.DeviceSetParam(track, device, 4, .9f);
                engine.DeviceSetParam(track, device, Gate, gated ? 1 : 0);
                engine.DeviceSetParam(track, device, GateThreshold, .5625f); // −35 dBFS
                int start = engine.TrackLatencySamples(track) + 36_000;
                var samples = new float[frames * 248];
                engine.Play();
                Render(engine, samples);
                return Math.Sqrt(samples.AsSpan(start, 4800).ToArray().Average(v => (double)v * v));
            }
            double ungatedTail = TailRms(gated: false), gatedTail = TailRms(gated: true);
            yield return (ungatedTail > 1e-3 && gatedTail < .1 * ungatedTail,
                $"Nota Color Gate shortens the resonant tail of an in-key tone ({ungatedTail:G3} to {gatedTail:G3})");
        }
        finally { File.Delete(wav); }
    }

    private static IEnumerable<(bool, string)> RenderFineTuning()
    {
        const int rate = 48000, frames = 127;
        const double tone = 440, sub = 55, c5 = 523.2511306011972;
        double Ratio(double cents) => Math.Pow(2, cents / 1200);
        string wav = Path.Combine(Path.GetTempPath(), $"nota-color-fine-{Guid.NewGuid():N}.wav");
        try
        {
            WavWriter.WriteStereo(wav, 4, rate, i =>
            {
                double t = Math.Tau * i / rate;
                double value = .12 * Math.Sin(t * tone) + .04 * Math.Sin(t * sub);
                return (value, -value);
            });

            double Level(float[] audio, double frequency)
            {
                double real = 0, imaginary = 0;
                for (int i = 0; i < audio.Length / 2; i++)
                {
                    real += audio[2 * i] * Math.Cos(Math.Tau * frequency * i / rate);
                    imaginary += audio[2 * i] * Math.Sin(Math.Tau * frequency * i / rate);
                }
                return 4 * Math.Sqrt(real * real + imaginary * imaginary) / audio.Length;
            }

            float[] Read(NotaEngine engine, int blocks)
            {
                var audio = new float[2 * frames * blocks];
                var block = new float[2 * frames];
                for (int b = 0; b < blocks; b++)
                {
                    engine.RenderOffline(block, frames, rate);
                    block.CopyTo(audio, b * block.Length);
                }
                return audio;
            }

            using (var engine = new NotaEngine())
            {
                int track = engine.AddAudioTrack();
                engine.AddAudioClip(track, wav, 0);
                int device = engine.AddBuiltinDevice(track, Kind);
                engine.DeviceSetParam(track, device, 1, 1); // empty custom mask
                for (int p = 9; p < 21; p++) engine.DeviceSetParam(track, device, p, 0);
                engine.DeviceSetParam(track, device, 2, 0);
                engine.DeviceSetParam(track, device, PreEnabled, 1);
                engine.DeviceSetParam(track, device, Pre, .75f);
                int lane = engine.AddAutomationLane(track, AutomationTarget.DeviceParam, device, Pre);
                engine.SetAutomationPoints(track, lane, [
                    new AutomationPoint(0, .75f), new AutomationPoint(1.1, .75f), new AutomationPoint(1.13, .5f),
                    new AutomationPoint(2.16, .5f), new AutomationPoint(2.2, .25f)]);
                engine.Play();
                Read(engine, 80);
                var up = Read(engine, 120);
                double raised = Level(up, tone * Ratio(50)), original = Level(up, tone);
                double low = Level(up, sub);
                double stereoError = Enumerable.Range(0, up.Length / 2).Max(i => Math.Abs(up[2 * i] + up[2 * i + 1]));
                int before = engine.TrackLatencySamples(track);
                var transition = Read(engine, 80);
                double jump = Enumerable.Range(1, transition.Length / 2 - 1)
                    .Max(i => Math.Abs(transition[2 * i] - transition[2 * (i - 1)]));
                var zero = Read(engine, 120);
                double neutral = Level(zero, tone), subRatio = low / Level(zero, sub);
                Read(engine, 80);
                double lowered = Level(Read(engine, 120), tone / Ratio(50));
                yield return (up.All(float.IsFinite) && raised > .8 * neutral && original < .1 * raised &&
                    subRatio is > .9 and < 1.1 && stereoError < 1e-5,
                    $"Nota Color Pre raises 440 Hz by 50 cents at zero Amount and preserves the stereo sub ({raised:G3}, original {original:G3}, sub ratio {subRatio:G3}, stereo {stereoError:G3})");
                yield return (lane >= 0 && before == engine.TrackLatencySamples(track) && jump < .04 && neutral > .075 &&
                    lowered > .8 * neutral,
                    $"Nota Color automates Pre through zero at fixed latency (jump {jump:G3}, neutral {neutral:G3}, down {lowered:G3})");
            }

            double shiftedC = c5 * Ratio(25);
            double source = shiftedC * Ratio(100);
            WavWriter.WriteStereo(wav, 1.5, rate, i =>
            {
                double value = .12 * Math.Sin(Math.Tau * source * i / rate);
                return (value, value);
            });
            using (var engine = new NotaEngine())
            {
                int track = engine.AddAudioTrack();
                engine.AddAudioClip(track, wav, 0);
                int device = engine.AddBuiltinDevice(track, Kind);
                engine.DeviceSetParam(track, device, Post, .625f);
                engine.DeviceSetParam(track, device, 3, 0);
                engine.DeviceSetParam(track, device, 1, 1);
                for (int p = 9; p < 21; p++) engine.DeviceSetParam(track, device, p, p == 9 ? 1 : 0);
                engine.Play();
                Read(engine, 160);
                var tuned = Read(engine, 230);
                double shifted = Level(tuned, source * 242 / 256), concert = Level(tuned, c5);
                yield return (shifted > .015 && concert < .2 * shifted && engine.TrackLatencySamples(track) == 1024,
                    $"Nota Color Post corrects toward C plus 25 cents at its original latency ({shifted:G3}, concert {concert:G3})");
            }

            WavWriter.WriteStereo(wav, 1.5, rate, i =>
            {
                double t = Math.Tau * i / rate;
                double value = .03 * (Math.Sin(t * c5) + Math.Sin(t * shiftedC));
                return (value, value);
            });
            double ResonanceRatio(float post)
            {
                using var engine = new NotaEngine();
                int track = engine.AddAudioTrack();
                engine.AddAudioClip(track, wav, 0);
                int device = engine.AddBuiltinDevice(track, Kind);
                engine.DeviceSetParam(track, device, Post, post);
                engine.DeviceSetParam(track, device, 3, 1);
                engine.DeviceSetParam(track, device, 4, .85f);
                engine.DeviceSetParam(track, device, 1, 1);
                for (int p = 9; p < 21; p++) engine.DeviceSetParam(track, device, p, p == 9 ? 1 : 0);
                engine.Play();
                Read(engine, 160);
                var resonated = Read(engine, 230);
                return Level(resonated, shiftedC) / Level(resonated, c5);
            }
            double atConcert = ResonanceRatio(.5f), atShifted = ResonanceRatio(.625f);
            yield return (atConcert < .8 && atShifted > 1.25,
                $"Nota Color Post moves the resonator from concert C to C plus 25 cents ({atConcert:F3} to {atShifted:F3})");
        }
        finally { File.Delete(wav); }
    }

    private static (bool, string) RenderNeutralPreSpectrum()
    {
        const int rate = 48000, frames = 256;
        string wav = Path.Combine(Path.GetTempPath(), $"nota-color-neutral-pre-{Guid.NewGuid():N}.wav");
        try
        {
            uint random = 1867;
            WavWriter.WriteStereo(wav, 1.5, rate, i =>
            {
                random = unchecked(random * 1664525 + 1013904223);
                double phase = Math.Tau * i / rate;
                double noise = (random / 4294967296.0 - .5) * .02;
                double sample = (.13 * Math.Sin(277.18 * phase) + .09 * Math.Sin(442.3 * phase) +
                    .23 * Math.Sin(58 * phase) + noise) * (.004 + .002 * Math.Sin(.2 * phase));
                return (sample, sample);
            });
            using var reference = new NotaEngine();
            using var pre = new NotaEngine();
            (int Track, int Device) SetUp(NotaEngine engine, bool enabled)
            {
                int track = engine.AddAudioTrack();
                engine.AddAudioClip(track, wav, 0);
                int device = engine.AddBuiltinDevice(track, Kind);
                engine.DeviceSetParam(track, device, PreEnabled, enabled ? 1 : 0);
                engine.Play();
                return (track, device);
            }

            var dry = SetUp(reference, false);
            var shifted = SetUp(pre, true);
            int delay = pre.TrackLatencySamples(shifted.Track) - reference.TrackLatencySamples(dry.Track);
            float[] before = new float[2 * frames], after = new float[2 * frames];
            for (int i = 0; i < delay; i += frames) pre.RenderOffline(after, frames, rate);
            float[] beforeScope = new float[1026], afterScope = new float[1026];
            float audioError = 0, scopeError = 0, tunedPeak = 0;
            for (int i = 0; i < 225; i++)
            {
                reference.RenderOffline(before, frames, rate);
                pre.RenderOffline(after, frames, rate);
                if (i < 95) continue;
                reference.DeviceScope(dry.Track, dry.Device, beforeScope, beforeScope.Length);
                pre.DeviceScope(shifted.Track, shifted.Device, afterScope, afterScope.Length);
                for (int j = 0; j < before.Length; j++) audioError = Math.Max(audioError, Math.Abs(before[j] - after[j]));
                for (int j = 2; j < beforeScope.Length; j++) scopeError = Math.Max(scopeError, Math.Abs(beforeScope[j] - afterScope[j]));
                for (int j = 514; j < beforeScope.Length; j++) tunedPeak = Math.Max(tunedPeak, beforeScope[j]);
            }
            return (delay == 2048 && tunedPeak > .05f && audioError < 1e-6f && scopeError < 1e-4f,
                $"Nota Color neutral Pre preserves aligned audio and spectrum (audio {audioError:G3}, scope {scopeError:G3})");
        }
        finally { File.Delete(wav); }
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
                $"Nota Color HQ shifts C sharp down through the C ABI (tuned {tuned:F4}, source {original:F4})");
            var spectrum = new float[1026];
            yield return (engine.DeviceScope(track, device, spectrum, spectrum.Length) == 1026 && spectrum[1] == 1024 &&
                spectrum.AsSpan(2, 512).ToArray().Any(v => v > .3f) && spectrum.AsSpan(514, 512).ToArray().Any(v => v > .3f),
                "Nota Color publishes live Dry and Tuned spectra through the C ABI");
        }
        finally { File.Delete(wav); }
    }

    private static (bool, string) RenderBassSpectrum()
    {
        const int rate = 48000, blockSize = 512, bins = 512;
        string wav = Path.Combine(Path.GetTempPath(), "nota-color-bass-" + Guid.NewGuid().ToString("N") + ".wav");
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
                "Nota Color Dry telemetry resolves 55 Hz and 82 Hz with opposite stereo polarity");
        }
        finally { File.Delete(wav); }
    }
}
