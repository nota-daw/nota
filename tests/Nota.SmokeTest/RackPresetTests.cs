// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// User presets for structural devices (#1044): racks retain their complete state and Grain
// retains the referenced source sample. The in-place rack path must remain one undo step.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class RackPresetTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        using var source = new NotaEngine();
        int rack = source.AddInstrumentRackTrack();
        int chain = source.RackAddChain(rack, 6);
        source.RackAddChainDevice(rack, chain, 3);
        var rackPreset = PresetService.Capture(source, rack, -1, "Rack test");

        using var recalled = new NotaEngine();
        string rackWarn = rackPreset is null ? "missing" : PresetService.Apply(rackPreset, recalled, -1);
        yield return (rackPreset is { BuiltinKind: 3, StateBase64.Length: > 0 } && rackWarn == "" && recalled.RackChainCount(1) == 1
                      && recalled.RackChainInstrumentKind(1, 0) == 6 && recalled.RackChainDeviceCount(1, 0) == 1,
            "preset: an Instrument Rack recalls its chains and devices");

        using var inPlace = new NotaEngine();
        int target = inPlace.AddInstrumentRackTrack();
        inPlace.RackAddChain(target, 0);
        inPlace.RackAddChain(target, 2);
        inPlace.ClearUndoHistory();
        string inPlaceWarn = rackPreset is null ? "missing" : PresetService.ApplyInPlace(rackPreset, inPlace, target, -1);
        bool restored = inPlace.RackChainCount(target) == 1 && inPlace.RackChainInstrumentKind(target, 0) == 6;
        bool undo = inPlace.Undo() && inPlace.RackChainCount(target) == 2;
        yield return (inPlaceWarn == "" && restored && undo, "preset: applying an Instrument Rack in place undoes as one step");

        int audio = source.AddAudioTrack();
        int effectRack = source.AddBuiltinDevice(audio, 5);
        int effectChain = source.RackDevAddChain(audio, effectRack, -1);
        source.RackDevAddChainDevice(audio, effectRack, effectChain, 1);
        var effectPreset = PresetService.Capture(source, audio, effectRack, "Effect rack test");
        using var effectRecalled = new NotaEngine();
        int effectTarget = effectRecalled.AddAudioTrack();
        string effectWarn = effectPreset is null ? "missing" : PresetService.Apply(effectPreset, effectRecalled, effectTarget);
        int effectChains = effectRecalled.RackDevChainCount(effectTarget, 0);
        int effectDevices = effectChains > 1 ? effectRecalled.RackDevChainDeviceCount(effectTarget, 0, 1) : -1;
        yield return (effectPreset is { BuiltinKind: 5, StateBase64.Length: > 0 } && effectWarn == "" && effectChains == 2 && effectDevices == 1,
            "preset: an Audio Effect Rack recalls its chains and devices");

        // Sources under the OS temp directory are deliberately not remembered as user samples.
        string wav = Path.Combine(Environment.CurrentDirectory, $"nota-rack-preset-{Guid.NewGuid():N}.wav");
        try
        {
            WavWriter.WriteSine(wav, seconds: 0.05, freq: 220, sampleRate: 44100);
            int rackWithGrain = source.AddDrumRackTrack();
            int grainChain = source.RackAddChain(rackWithGrain, 10);
            source.RackDevChainGrainSetSample(rackWithGrain, -1, grainChain, wav, 49);
            var rackGrainPreset = PresetService.Capture(source, rackWithGrain, -1, "Rack Grain test");
            using var rackGrainRecalled = new NotaEngine();
            string rackGrainWarn = rackGrainPreset is null ? "missing" : PresetService.Apply(rackGrainPreset, rackGrainRecalled, -1);
            bool rackGrain = rackGrainRecalled.RackDevChainGrainInfo(1, -1, 0, out var rackGrainInfo)
                             && rackGrainInfo.RootNote == 49 && rackGrainInfo.SampleId != 0
                             && rackGrainRecalled.SampleSource(rackGrainInfo.SampleId) == Path.GetFullPath(wav);
            yield return (rackGrainPreset is { RackGrainSamples: [{ RootNote: 49 }] } && rackGrainWarn == "" && rackGrain,
                "preset: a Rack recalls a chained Nota Grain sample");

            int grain = source.AddGrainSynthTrack();
            source.SetTrackGrainSample(grain, wav, 54);
            var grainPreset = PresetService.Capture(source, grain, -1, "Grain test");
            using var grainRecalled = new NotaEngine();
            string grainWarn = grainPreset is null ? "missing" : PresetService.Apply(grainPreset, grainRecalled, -1);
            int grainTrack = Enumerable.Range(0, grainRecalled.TrackCount).Select(i => grainRecalled.TryGetTrackInfo(i, out var t) ? t.Id : -1)
                .FirstOrDefault(id => id > 0 && grainRecalled.TrackInstrumentKind(id) == 10);
            NotaSamplerInfo info = default;
            bool sample = grainTrack > 0 && grainRecalled.TryGetGrainInfo(grainTrack, out info) && info.SampleId != 0 && info.RootNote == 54
                          && grainRecalled.SampleSource(info.SampleId) == Path.GetFullPath(wav);
            yield return (grainPreset is { BuiltinKind: 10, GrainSample: not null, GrainRootNote: 54 } && grainWarn == "" && sample,
                "preset: Nota Grain recalls its referenced sample and root note");
        }
        finally { try { File.Delete(wav); } catch (IOException) { } }
    }
}
