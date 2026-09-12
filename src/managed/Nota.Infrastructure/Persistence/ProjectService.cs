// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M7-6a/b/c: read/write the `.nota` project bundle. C# owns serialization; the
// engine never touches the file (see ARCHITECTURE.md § Project format). Save queries
// live engine state into a ProjectDocument, dumps referenced samples into
// `samples/` and plugin state blobs into `plugin-states/`, and writes
// project.json atomically (temp + rename) with an auto-backup; Load replays the
// document through the existing structural-edit ops. Covered: transport,
// tracks/mixer, sends+return buses, built-in Synth + Sampler, built-in devices
// (params+bypass), hosted AU/VST3 instruments + effects (by stable identifier +
// state blob), MIDI clips/notes, arrangement audio clips, session MIDI/audio
// slots. A plugin missing on the opening machine is reported as a warning and
// downgraded (instrument -> empty Synth; effect -> skipped), never crashing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nota.Infrastructure;

public sealed class ProjectService
{
    /// <summary>On-disk format version. Files with a higher version are refused; older
    /// files are upgraded on Load via <see cref="ProjectMigrations"/>. v2 (M9-A4) adds
    /// automation lanes; v3 (M9-B4) adds hosted-plugin-param lanes (PluginParamId). To
    /// change the schema, bump this and append a migration (M7-9). v4 (M9-D) adds
    /// per-segment curvature (automationPoint.curve). v5 adds audio-clip pitch + warp
    /// (pitchSemitones / warpEnabled / warpMode / warpBeats). v6 adds warp markers.</summary>
    /// v18 (Phase 3) adds CV modulation: per-track LFO modulators + CV links to device params.
    public const int CurrentFormatVersion = 19;

    /// <summary>Manifest file name inside the bundle folder.</summary>
    public const string ManifestName = "project.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // --- capture (engine -> document) --------------------------------------

    /// <summary>Snapshots the engine's current state into a document. Content the
    /// format can't represent yet (hosted plugins) is appended to
    /// <paramref name="warnings"/>. Referenced samples are recorded in
    /// <see cref="ProjectDocument.SampleRefs"/> for <see cref="Save"/> to write.</summary>
    public static ProjectDocument Capture(IAudioEngine engine, TransportState transport, List<string> warnings)
    {
        var doc = new ProjectDocument
        {
            SceneCount = engine.SceneCount,
            Transport = new TransportDto
            {
                Bpm = transport.Bpm,
                MasterVolume = transport.MasterVolume,
                MetronomeOn = transport.MetronomeOn,
                LoopOn = transport.LoopOn,
                TimeSigNumerator = transport.TimeSigNumerator,
                TimeSigDenominator = transport.TimeSigDenominator,
            },
        };

        int trackCount = engine.TrackCount;
        // Track id -> document index, for resolving cross-track references (sidechain, Phase B).
        var idToIndex = new Dictionary<int, int>();
        for (int i = 0; i < trackCount; i++)
            if (engine.TryGetTrackInfo(i, out var ti0)) idToIndex[ti0.Id] = i;

        for (int i = 0; i < trackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti)) continue;
            var t = new TrackDto
            {
                Type = ti.Type,
                Name = engine.GetTrackName(ti.Id) is { Length: > 0 } tn ? tn : null,
                Volume = ti.Volume,
                Pan = ti.Pan,
                Mute = ti.Muted != 0,
                Solo = ti.Soloed != 0,
                Armed = ti.Armed != 0,
                ReturnIndex = engine.TrackReturnIndex(ti.Id),
                GroupId = ti.GroupId >= 0 && idToIndex.TryGetValue(ti.GroupId, out var gix) ? gix : -1,   // parent → doc index
                ColorIndex = engine.GetTrackColorIndex(ti.Id),
                RecordInput = engine.GetTrackRecordInput(ti.Id) is var rin && rin > 0
                    ? (idToIndex.TryGetValue(rin, out var rix) ? rix + 2 : 0)   // track source → doc index + 2
                    : rin,                                                       // 0 hardware / -1 master
                MidiFrom = engine.GetTrackMidiSource(ti.Id) is var mfrom && mfrom > 0
                    && idToIndex.TryGetValue(mfrom, out var mix) ? mix : -1,      // source → doc index, else off
            };
            for (int b = 0; b < t.Sends.Length; b++) t.Sends[b] = engine.GetTrackSend(ti.Id, b);

            if (ti.IsInstrument)
            {
                int kind = engine.TrackInstrumentKind(ti.Id);
                var instDto = new InstrumentDto { Kind = kind };
                if (kind == 1 || kind == 10)   // Sampler + Nota Grain both bundle their sample
                {
                    var sd = new SamplerDto();
                    NotaSamplerInfo si;
                    bool ok = kind == 1 ? engine.TryGetSamplerInfo(ti.Id, out si) : engine.TryGetGrainInfo(ti.Id, out si);
                    if (ok)
                    {
                        sd.RootNote = si.RootNote;
                        sd.Loop = si.Loop != 0;
                        if (si.SampleId != 0) sd.Sample = RegisterSample(doc, engine, si.SampleId, warnings) ?? "";
                    }
                    int pc = engine.PluginParamCount(ti.Id, -1);
                    sd.Params = new float[pc];
                    for (int p = 0; p < pc; p++) sd.Params[p] = engine.PluginParamGet(ti.Id, -1, p);
                    instDto.Sampler = sd;
                }
                else if (kind < 0)
                {
                    string pid = engine.TrackInstrumentPluginId(ti.Id);
                    if (pid.Length == 0)
                        warnings.Add($"Track {i + 1}: plugin instrument couldn't be identified and was skipped.");
                    else
                    {
                        instDto.PluginId = pid;
                        instDto.State = RegisterBlob(doc, engine.GetPluginState(ti.Id, -1));
                    }
                }
                else   // every other built-in instrument (Synth/Physical/Aurora/Volt/Instrument
                {      // Rack/Drum Rack/Flux) persists its state blob — the load path rebuilds from it.
                    instDto.State = RegisterBlob(doc, engine.GetPluginState(ti.Id, -1));
                }
                // Nota Rhythm Phase 2: bundle each sample-source voice's one-shot separately
                // (the state blob holds only the source flags, not the audio).
                if (kind == 12)
                {
                    var vs = new System.Collections.Generic.List<VoiceSampleDto>();
                    for (int v = 0; v < 8; v++)
                    {
                        if (engine.RhythmVoiceSource(ti.Id, v) != 1) continue;
                        if (!engine.TryGetRhythmVoiceInfo(ti.Id, v, out var vi) || vi.SampleId == 0) continue;
                        string? rel = RegisterSample(doc, engine, vi.SampleId, warnings);
                        if (rel != null) vs.Add(new VoiceSampleDto { Voice = v, Sample = rel });
                    }
                    if (vs.Count > 0) instDto.VoiceSamples = vs;
                }
                // React source (Nota Flux): store as a doc index, resolved on load.
                int instScSrc = engine.InstrumentSidechainSource(ti.Id);
                instDto.SidechainSource = instScSrc >= 0 && idToIndex.TryGetValue(instScSrc, out var isi) ? isi : -1;
                t.Instrument = instDto;
            }

            // MIDI effects (before the instrument): built-in kind + generic params.
            int midiCount = engine.TrackMidiEffectCount(ti.Id);
            for (int m = 0; m < midiCount; m++)
            {
                var mdev = new MidiDeviceDto { Kind = engine.MidiEffectKind(ti.Id, m), Bypassed = engine.MidiEffectBypassed(ti.Id, m) };
                int pc = engine.MidiEffectParamCount(ti.Id, m);
                mdev.Params = new float[pc];
                for (int p = 0; p < pc; p++) mdev.Params[p] = engine.MidiEffectGetParam(ti.Id, m, p);
                mdev.CcDestDevice = engine.MidiEffectCcDestDevice(ti.Id, m);
                mdev.CcDestParam = engine.MidiEffectCcDestParam(ti.Id, m);
                mdev.CcDepth = engine.MidiEffectCcDepth(ti.Id, m);
                t.MidiEffects.Add(mdev);
            }

            // Devices: built-in parameters, or hosted-plugin identity + state.
            t.Devices.AddRange(CaptureDevices(engine, ti.Id, doc, idToIndex, warnings, $"Track {i + 1}"));

            // Freeze (M7): bundle the frozen-audio blob so the track re-opens frozen.
            if (engine.IsTrackFrozen(ti.Id))
                t.FrozenState = RegisterBlob(doc, engine.GetFreezeState(ti.Id));

            // Arrangement clips.
            if (ti.IsInstrument)
            {
                for (int c = 0; c < ti.ClipCount; c++)
                {
                    if (!engine.TryGetClipInfo(ti.Id, c, out var ci) || !ci.IsMidi) continue;
                    var vel = engine.GetMidiClipEnvelope(ti.Id, c, MidiClipEnvelope.Velocity);   // v10
                    var vol = engine.GetMidiClipEnvelope(ti.Id, c, MidiClipEnvelope.Volume);
                    t.MidiClips.Add(new MidiClipDto
                    {
                        Name = engine.GetClipName(ti.Id, c) is { Length: > 0 } mn ? mn : null,
                        Active = ci.IsActive,
                        StartBeat = ci.StartBeat,
                        LengthBeats = ci.LengthBeats,
                        Notes = ToDtos(engine.GetClipNotes(ti.Id, c)),
                        VelocityEnvelope = vel.Length > 0 ? System.Array.ConvertAll(vel, p => new AutomationPointDto(p)) : null,
                        VolumeEnvelope = vol.Length > 0 ? System.Array.ConvertAll(vol, p => new AutomationPointDto(p)) : null,
                    });
                }
            }
            else if (ti.Type == 0) // audio track
            {
                for (int c = 0; c < ti.ClipCount; c++)
                {
                    if (!engine.TryGetAudioClipInfo(ti.Id, c, out var ac)) continue;
                    string? rel = RegisterSample(doc, engine, ac.SampleId, warnings);
                    if (rel == null) continue;
                    var dto = new AudioClipDto
                    {
                        Sample = rel,
                        Name = engine.GetClipName(ti.Id, c) is { Length: > 0 } an ? an : null,
                        Active = !engine.TryGetClipInfo(ti.Id, c, out var gci) || gci.IsActive,
                        StartBeat = ac.StartBeat,
                        SourceOffsetFrames = ac.SourceOffsetFrames,
                        LengthFrames = ac.LengthFrames,
                        Gain = ac.Gain,
                        PitchSemitones = ac.PitchSemitones,
                        WarpEnabled = ac.WarpEnabled,
                        WarpMode = ac.WarpMode,
                        WarpBeats = ac.WarpBeats,
                        WarpPlayStart = ac.WarpPlayStart,
                        WarpPlayEnd = ac.WarpPlayEnd,
                        Reversed = ac.Reversed != 0,
                    };
                    if (ac.WarpEnabled != 0)
                    {
                        var ms = new double[128]; var mb = new double[128];
                        int nm = Math.Min(engine.GetClipWarpMarkers(ti.Id, c, ms, mb), 128);
                        if (nm >= 2)
                        {
                            dto.WarpMarkers = new List<WarpMarkerDto>(nm);
                            for (int k = 0; k < nm; k++) dto.WarpMarkers.Add(new WarpMarkerDto { Src = ms[k], Beat = mb[k] });
                        }
                    }
                    var env = engine.GetClipVolumeEnvelope(ti.Id, c);   // clip volume envelope (v8)
                    if (env.Length > 0) dto.VolumeEnvelope = System.Array.ConvertAll(env, p => new AutomationPointDto(p));
                    var penv = engine.GetClipPanEnvelope(ti.Id, c);     // clip pan envelope (v9)
                    if (penv.Length > 0) dto.PanEnvelope = System.Array.ConvertAll(penv, p => new AutomationPointDto(p));
                    t.AudioClips.Add(dto);
                }
            }

            // Session slots.
            for (int s = 0; s < engine.SceneCount; s++)
            {
                // Persist any slot that holds a clip — skip only truly empty ones. (A slot
                // that's playing/queued/recording is state 2-4, not 1, but still has content;
                // gating on == 1 dropped it when saving mid-jam.)
                if (engine.SessionSlotState(ti.Id, s) == 0) continue;
                if (ti.IsInstrument)
                {
                    t.SessionSlots.Add(new SessionSlotDto
                    {
                        Scene = s,
                        LengthBeats = engine.SessionSlotLength(ti.Id, s),
                        Notes = ToDtos(engine.GetSessionNotes(ti.Id, s)),
                    });
                }
                else if (ti.Type == 0 && engine.TryGetSessionAudioSlot(ti.Id, s, out var sa))
                {
                    string? rel = RegisterSample(doc, engine, sa.SampleId, warnings);
                    if (rel == null) continue;
                    t.SessionSlots.Add(new SessionSlotDto
                    {
                        Scene = s,
                        LengthBeats = sa.LengthBeats,
                        Audio = new AudioClipDto
                        {
                            Sample = rel,
                            SourceOffsetFrames = sa.SourceOffsetFrames,
                            LengthFrames = sa.LengthFrames,
                            Gain = sa.Gain,
                        },
                    });
                }
            }

            // Automation lanes (M9-A4; plugin-param lanes carry a stable id, M9-B4).
            int laneCount = engine.AutomationLaneCount(ti.Id);
            for (int l = 0; l < laneCount; l++)
            {
                var info = engine.AutomationLaneInfo(ti.Id, l);
                t.Automation.Add(new AutomationLaneDto
                {
                    Target = (int)info.Target,
                    DeviceIndex = info.DeviceIndex,
                    ParamIndex = info.ParamIndex,
                    PluginParamId = info.Target == AutomationTarget.PluginParam
                        ? engine.AutomationLaneParamId(ti.Id, l) : null,
                    Points = System.Array.ConvertAll(engine.GetAutomationPoints(ti.Id, l),
                        p => new AutomationPointDto(p)),
                });
            }

            // CV modulation (Phase 3): LFO sources + links to device params.
            int modCount = engine.ModulatorCount(ti.Id);
            for (int m = 0; m < modCount; m++)
            {
                int id = engine.ModulatorIdAt(ti.Id, m);
                t.Modulators.Add(new ModulatorDto
                {
                    Id = id,
                    Kind = engine.ModulatorKind(ti.Id, id),
                    Waveform = (int)engine.ModulatorGet(ti.Id, id, 0),
                    TempoSync = engine.ModulatorGet(ti.Id, id, 1) > 0.5f,
                    RateHz = engine.ModulatorGet(ti.Id, id, 2),
                    RateSyncBeats = engine.ModulatorGet(ti.Id, id, 3),
                    Depth = engine.ModulatorGet(ti.Id, id, 4),
                    Phase = engine.ModulatorGet(ti.Id, id, 5),
                    Attack = engine.ModulatorGet(ti.Id, id, 6),
                    Release = engine.ModulatorGet(ti.Id, id, 7),
                    Decay = engine.ModulatorGet(ti.Id, id, 8),
                    Sustain = engine.ModulatorGet(ti.Id, id, 9),
                    InputA = (int)engine.ModulatorGet(ti.Id, id, 10),
                    InputB = (int)engine.ModulatorGet(ti.Id, id, 11),
                    MathOffset = engine.ModulatorGet(ti.Id, id, 12),
                });
            }
            int cvCount = engine.CvLinkCount(ti.Id);
            for (int c = 0; c < cvCount; c++)
            {
                int tgtId = engine.CvLinkTargetTrack(ti.Id, c);   // -1 = self
                t.CvLinks.Add(new CvLinkDto
                {
                    Source = engine.CvLinkSource(ti.Id, c),
                    SourceKind = engine.CvLinkSourceKind(ti.Id, c),
                    SourceDevice = engine.CvLinkSourceDevice(ti.Id, c),
                    SourceParam = engine.CvLinkSourceParam(ti.Id, c),
                    TargetKind = engine.CvLinkTargetKind(ti.Id, c),
                    TargetTrack = tgtId >= 0 && idToIndex.TryGetValue(tgtId, out var tix) ? tix : -1,
                    Device = engine.CvLinkDevice(ti.Id, c),
                    Param = engine.CvLinkParam(ti.Id, c),
                    Depth = engine.CvLinkDepth(ti.Id, c),
                    Mode = engine.CvLinkMode(ti.Id, c),
                    Base = engine.CvLinkBase(ti.Id, c),
                });
            }

            doc.Tracks.Add(t);
        }

        // Master-volume automation (graph-level, format v7).
        doc.MasterVolumeAutomation = new List<AutomationPointDto>(
            System.Array.ConvertAll(engine.GetMasterVolumeAutomation(), p => new AutomationPointDto(p)));

        // Master effect chain + its name/colour.
        int masterId = engine.MasterTrackId;
        doc.Master = new MasterDto
        {
            Name = engine.GetTrackName(masterId) is { Length: > 0 } mName ? mName : null,
            ColorIndex = engine.GetTrackColorIndex(masterId),
            Devices = CaptureDevices(engine, masterId, doc, idToIndex, warnings, "Master"),
        };

        return doc;
    }

    // Capture a track's device chain (built-in params / plugin identity+state / rack blob,
    // plus sidechain routing as a doc index). Shared by tracks and the master.
    private static List<DeviceDto> CaptureDevices(IAudioEngine engine, int trackId, ProjectDocument doc,
                                                  Dictionary<int, int> idToIndex, List<string> warnings, string label)
    {
        var list = new List<DeviceDto>();
        int deviceCount = engine.TrackDeviceCount(trackId);
        for (int d = 0; d < deviceCount; d++)
        {
            int bk = engine.TrackDeviceBuiltinKind(trackId, d);
            var dev = new DeviceDto { BuiltinKind = bk, Bypassed = engine.DeviceBypassed(trackId, d) };
            int scSrc = engine.DeviceSidechainSource(trackId, d);
            dev.SidechainSource = scSrc >= 0 && idToIndex.TryGetValue(scSrc, out var si) ? si : -1;
            dev.SidechainGain = engine.DeviceSidechainGain(trackId, d);
            dev.SidechainMix = engine.DeviceSidechainMix(trackId, d);
            dev.SidechainTapPre = engine.DeviceSidechainTapPre(trackId, d);
            if (bk < 0) // hosted plugin
            {
                string pid = engine.TrackDevicePluginId(trackId, d);
                if (pid.Length == 0)
                {
                    warnings.Add($"{label}: plugin effect \"{engine.DeviceName(trackId, d)}\" couldn't be identified and was skipped.");
                    continue;
                }
                dev.PluginId = pid;
                dev.State = RegisterBlob(doc, engine.GetPluginState(trackId, d));
            }
            else if (bk == 5) // Audio Effect Rack: structural blob
            {
                dev.State = RegisterBlob(doc, engine.GetPluginState(trackId, d));
            }
            else // built-in: generic params (+ an optional state blob, e.g. the looper's PCM)
            {
                int pc = engine.DeviceParamCount(trackId, d);
                dev.Params = new float[pc];
                for (int p = 0; p < pc; p++) dev.Params[p] = engine.DeviceGetParam(trackId, d, p);
                dev.State = RegisterBlob(doc, engine.DeviceGetState(trackId, d));
            }
            list.Add(dev);
        }
        return list;
    }

    // Recreate a device chain onto a track (or the master). Sidechain sources are deferred
    // (resolved once every track exists).
    private static void ApplyDevices(IAudioEngine engine, int trackId, IEnumerable<DeviceDto> devices,
                                     string bundleDir, List<(int track, int dev, int srcIndex)> scDeferred, List<string> warnings)
    {
        foreach (var d in devices)
        {
            int di;
            if (d.BuiltinKind < 0) // hosted plugin
            {
                if (d.PluginId is not { Length: > 0 } pid) continue;
                int idx = NotaEngine.PluginIndexOfId(pid);
                di = idx >= 0 ? engine.AddTrackEffectPlugin(trackId, idx) : -1;
                if (di < 0) { warnings.Add($"Plugin effect \"{pid}\" isn't installed and was skipped."); continue; }
                if (d.State is { } stateRel)
                {
                    var blob = ReadBlob(bundleDir, stateRel);
                    if (blob.Length > 0) engine.SetPluginState(trackId, di, blob);
                }
            }
            else
            {
                di = engine.AddBuiltinDevice(trackId, d.BuiltinKind);
                if (di < 0) { warnings.Add($"Couldn't recreate a device (kind {d.BuiltinKind})."); continue; }
                if (d.BuiltinKind == 5)
                {
                    if (d.State is { } rackRel)
                    {
                        var blob = ReadBlob(bundleDir, rackRel);
                        if (blob.Length > 0) engine.SetPluginState(trackId, di, blob);
                    }
                }
                else
                {
                    int pc = engine.DeviceParamCount(trackId, di);
                    for (int p = 0; p < d.Params.Length && p < pc; p++) engine.DeviceSetParam(trackId, di, p, d.Params[p]);
                    if (d.State is { } devRel)
                    {
                        var blob = ReadBlob(bundleDir, devRel);
                        if (blob.Length > 0) engine.DeviceSetState(trackId, di, blob);
                    }
                }
            }
            if (d.Bypassed) engine.SetDeviceBypassed(trackId, di, true);
            if (d.SidechainSource >= 0) scDeferred.Add((trackId, di, d.SidechainSource));
            engine.SetDeviceSidechainGain(trackId, di, d.SidechainGain);
            engine.SetDeviceSidechainMix(trackId, di, d.SidechainMix);
            engine.SetDeviceSidechainTapPre(trackId, di, d.SidechainTapPre);
        }
    }

    // --- apply (document -> engine) ----------------------------------------

    /// <summary>Rebuilds the engine from a document. Resets the engine first, so
    /// call after the user confirms discarding the current project. Transport is
    /// NOT applied here (the UI applies it via its view-model so controls update).
    /// <paramref name="bundleDir"/> resolves sample paths. Returns warnings for
    /// content downgraded on load.</summary>
    public static IReadOnlyList<string> Apply(ProjectDocument doc, IAudioEngine engine, string bundleDir)
    {
        var warnings = new List<string>();
        engine.Reset();

        // Restore the scene count before tracks so their session slots size to it (the doc may
        // hold more/fewer scenes than the fresh default after add/remove-scene edits).
        while (engine.SceneCount < doc.SceneCount) engine.AddScene();
        while (engine.SceneCount > doc.SceneCount && engine.SceneCount > 1) engine.RemoveScene(engine.SceneCount - 1);

        // Doc index -> new live track id, and deferred sidechain wiring (a source may be
        // a track created later), both resolved after every track exists (Phase B).
        var newTrackIds = new List<int>();
        var scDeferred = new List<(int track, int dev, int srcIndex)>();
        var instScDeferred = new List<(int track, int srcIndex)>();   // instrument React source (Nota Flux)
        var recInputDeferred = new List<(int trackId, int encoded)>();
        var groupDeferred = new List<(int trackId, int parentDocIndex)>();   // group membership
        var midiFromDeferred = new List<(int trackId, int sourceDocIndex)>();  // MIDI routing source ("MIDI In")
        // CV links resolved after all tracks exist (cross-track targets are doc-indices).
        var cvLinkDeferred = new List<(int owner, CvLinkDto dto, int modId)>();

        foreach (var t in doc.Tracks)
        {
            int id;
            if (t.Type == 2)
            {
                id = engine.AddReturnTrack();
            }
            else if (t.Type == 1)
            {
                if (t.Instrument is { Kind: 1, Sampler: { } sd })
                {
                    id = sd.Sample.Length == 0
                        ? engine.AddSamplerInstrumentTrack()   // empty Sampler
                        : engine.AddSamplerTrack(ResolveInBundle(bundleDir, sd.Sample), sd.RootNote, sd.Loop);
                    if (id <= 0)
                    {
                        warnings.Add($"Couldn't reload sampler \"{sd.Sample}\" — using an empty Synth track.");
                        id = engine.AddInstrumentTrack();
                    }
                    else
                    {
                        int pc = engine.PluginParamCount(id, -1);
                        for (int p = 0; p < sd.Params.Length && p < pc; p++) engine.PluginParamSet(id, -1, p, sd.Params[p]);
                    }
                }
                else if (t.Instrument is { Kind: 10, Sampler: { } gd })   // Nota Grain: sample + params
                {
                    id = engine.AddGrainSynthTrack();
                    if (id > 0)
                    {
                        if (gd.Sample.Length > 0) engine.SetTrackGrainSample(id, ResolveInBundle(bundleDir, gd.Sample), gd.RootNote);
                        int pc = engine.PluginParamCount(id, -1);
                        for (int p = 0; p < gd.Params.Length && p < pc; p++) engine.PluginParamSet(id, -1, p, gd.Params[p]);
                    }
                }
                else if (t.Instrument is { Kind: < 0, PluginId: { Length: > 0 } pid })
                {
                    int idx = NotaEngine.PluginIndexOfId(pid);
                    id = idx >= 0 ? engine.AddPluginInstrumentTrack(idx) : 0;
                    if (id <= 0)
                    {
                        warnings.Add($"Plugin instrument \"{pid}\" isn't installed — using an empty Synth track.");
                        id = engine.AddInstrumentTrack();
                    }
                    else if (t.Instrument.State is { } stateRel)
                    {
                        var blob = ReadBlob(bundleDir, stateRel);
                        if (blob.Length > 0) engine.SetPluginState(id, -1, blob);
                    }
                }
                else if (t.Instrument is { Kind: 3 or 4 })   // Instrument / Drum Rack: the blob rebuilds every chain/pad
                {
                    id = t.Instrument.Kind == 4 ? engine.AddDrumRackTrack() : engine.AddInstrumentRackTrack();
                    if (t.Instrument.State is { } rackState)
                    {
                        var blob = ReadBlob(bundleDir, rackState);
                        if (blob.Length > 0) engine.SetPluginState(id, -1, blob);
                    }
                }
                else
                {
                    id = t.Instrument switch
                    {
                        { Kind: 2 } => engine.AddPhysicalSynthTrack(),   // Nota Physical
                        { Kind: 5 } => engine.AddWavetableSynthTrack(),  // Nota Aurora
                        { Kind: 6 } => engine.AddVoltSynthTrack(),       // Nota Volt
                        { Kind: 7 } => engine.AddBassSynthTrack(),       // Nota Bass
                        { Kind: 8 } => engine.AddPendulumSynthTrack(),   // Nota Pendulum
                        { Kind: 9 } => engine.AddOperatorSynthTrack(),   // Nota Operator
                        { Kind: 11 } => engine.AddFluxSynthTrack(),      // Nota Flux
                        { Kind: 12 } => engine.AddRhythmTrack(),         // Nota Rhythm
                        { Kind: 13 } => engine.AddMonolithTrack(),       // Nota Monolith
                        { Kind: 14 } => engine.AddPentadTrack(),         // Nota Pentad
                        { Kind: 15 } => engine.AddConsortTrack(),        // Nota Consort
                        _ => engine.AddInstrumentTrack(),                // Nota Synth
                    };
                    if (t.Instrument?.State is { } synthState)
                    {
                        var blob = ReadBlob(bundleDir, synthState);
                        if (blob.Length > 0) engine.SetPluginState(id, -1, blob);
                    }
                    // Nota Rhythm Phase 2: reload each voice's one-shot from the bundle.
                    if (id > 0 && t.Instrument is { Kind: 12, VoiceSamples: { } vsl })
                        foreach (var vs in vsl)
                            if (vs.Sample is { Length: > 0 } && !engine.SetRhythmVoiceSample(id, vs.Voice, ResolveInBundle(bundleDir, vs.Sample)))
                                warnings.Add($"Couldn't reload Rhythm voice sample \"{vs.Sample}\".");
                }
            }
            else if (t.Type == 3)
            {
                id = engine.AddGroupTrack();
            }
            else
            {
                id = engine.AddAudioTrack();
            }

            if (id > 0 && t.Instrument is { SidechainSource: >= 0 } insc)
                instScDeferred.Add((id, insc.SidechainSource));   // wire React source once every track exists
            if (id > 0 && t.GroupId >= 0)
                groupDeferred.Add((id, t.GroupId));               // reparent into its group once every track exists
            newTrackIds.Add(id);   // keep index aligned with doc.Tracks even on failure (id <= 0)
            if (id <= 0)
            {
                warnings.Add($"Couldn't recreate a track (type {t.Type}).");
                continue;
            }

            engine.SetTrackVolume(id, t.Volume);
            engine.SetTrackPan(id, t.Pan);
            engine.SetTrackMute(id, t.Mute);
            engine.SetTrackSolo(id, t.Solo);
            engine.SetTrackArmed(id, t.Armed);
            if (t.Name is { Length: > 0 } trackName) engine.SetTrackName(id, trackName);
            if (t.ColorIndex >= 0) engine.SetTrackColorIndex(id, t.ColorIndex);
            if (t.RecordInput != 0) recInputDeferred.Add((id, t.RecordInput));   // resolve after all tracks exist
            if (t.MidiFrom >= 0) midiFromDeferred.Add((id, t.MidiFrom));           // resolve after all tracks exist
            for (int b = 0; b < t.Sends.Length && b < 4; b++) engine.SetTrackSend(id, b, t.Sends[b]);

            foreach (var m in t.MidiEffects)
            {
                int mi = engine.AddMidiEffect(id, m.Kind);
                if (mi < 0) { warnings.Add($"Couldn't recreate a MIDI effect (kind {m.Kind})."); continue; }
                int mpc = engine.MidiEffectParamCount(id, mi);
                for (int p = 0; p < m.Params.Length && p < mpc; p++) engine.MidiEffectSetParam(id, mi, p, m.Params[p]);
                if (m.Bypassed) engine.SetMidiEffectBypassed(id, mi, true);
                if (m.CcDestDevice >= 0 && m.CcDestParam >= 0)
                {
                    engine.SetMidiEffectCcDest(id, mi, m.CcDestDevice, m.CcDestParam);
                    engine.SetMidiEffectCcDepth(id, mi, m.CcDepth);
                }
            }

            ApplyDevices(engine, id, t.Devices, bundleDir, scDeferred, warnings);

            foreach (var c in t.MidiClips)
            {
                int ci = engine.AddMidiClip(id, c.StartBeat, c.LengthBeats);
                if (ci < 0) continue;
                if (c.Name is { Length: > 0 } mcn) engine.SetClipName(id, ci, mcn);
                if (!c.Active) engine.SetClipActive(id, ci, false);   // clip deactivate (v17)
                if (c.Notes.Length > 0) engine.SetClipNotes(id, ci, ToNotes(c.Notes));
                if (c.VelocityEnvelope is { Length: > 0 } ve)   // MIDI clip envelopes (v10)
                    engine.SetMidiClipEnvelope(id, ci, MidiClipEnvelope.Velocity, System.Array.ConvertAll(ve, p => p.ToPoint()));
                if (c.VolumeEnvelope is { Length: > 0 } vo)
                    engine.SetMidiClipEnvelope(id, ci, MidiClipEnvelope.Volume, System.Array.ConvertAll(vo, p => p.ToPoint()));
            }

            foreach (var ac in t.AudioClips)
            {
                int ci = engine.AddAudioClipEx(id, ResolveInBundle(bundleDir, ac.Sample),
                    ac.StartBeat, ac.SourceOffsetFrames, ac.LengthFrames, ac.Gain);
                if (ci < 0) { warnings.Add($"Couldn't reload audio clip \"{ac.Sample}\"."); continue; }
                if (ac.Name is { Length: > 0 } acn) engine.SetClipName(id, ci, acn);
                if (!ac.Active) engine.SetClipActive(id, ci, false);   // clip deactivate (v17)
                if (ac.PitchSemitones != 0) engine.SetClipPitch(id, ci, ac.PitchSemitones);
                if (ac.Reversed) engine.SetClipReverse(id, ci, true);   // reverse (v19)
                if (ac.WarpEnabled != 0)
                {
                    engine.SetClipWarp(id, ci, true, ac.WarpMode);
                    if (ac.WarpMarkers is { Count: >= 2 } wm)
                    {
                        var ms = new double[wm.Count]; var mb = new double[wm.Count];
                        for (int k = 0; k < wm.Count; k++) { ms[k] = wm[k].Src; mb[k] = wm[k].Beat; }
                        engine.SetClipWarpMarkers(id, ci, ms, mb);   // also sets warpBeats from the last marker
                    }
                    else if (ac.WarpBeats > 0) engine.SetClipWarpLength(id, ci, ac.WarpBeats);
                    if (ac.WarpPlayEnd > 0)   // restore the trim window (v11); 0 = whole warp
                        engine.SetClipWarpTrim(id, ci, ac.WarpPlayStart, ac.WarpPlayEnd);
                }
                if (ac.VolumeEnvelope is { Length: > 0 } venv)   // clip volume envelope (v8)
                    engine.SetClipVolumeEnvelope(id, ci, System.Array.ConvertAll(venv, p => p.ToPoint()));
                if (ac.PanEnvelope is { Length: > 0 } penv)      // clip pan envelope (v9)
                    engine.SetClipPanEnvelope(id, ci, System.Array.ConvertAll(penv, p => p.ToPoint()));
            }

            // Freeze (M7): restore the frozen-audio buffer so the track re-opens frozen
            // (the live chain above is kept intact for a later unfreeze). cloneTrack carries
            // the buffer through the automation/session edits that follow.
            if (t.FrozenState is { } frozenRel)
            {
                var blob = ReadBlob(bundleDir, frozenRel);
                if (blob.Length > 0) engine.SetFreezeState(id, blob);
            }

            foreach (var sl in t.SessionSlots)
            {
                if (sl.Audio is { } sa)
                {
                    if (!engine.AddSessionAudioClip(id, sl.Scene, ResolveInBundle(bundleDir, sa.Sample),
                            sl.LengthBeats, sa.SourceOffsetFrames, sa.LengthFrames, sa.Gain))
                        warnings.Add($"Couldn't reload session take \"{sa.Sample}\".");
                }
                else
                {
                    engine.AddSessionMidiClip(id, sl.Scene, sl.LengthBeats);
                    if (sl.Notes.Length > 0) engine.SetSessionNotes(id, sl.Scene, ToNotes(sl.Notes));
                }
            }

            // Automation lanes (M9-A4). Devices are already rebuilt above, so
            // DeviceParam lanes resolve to the same chain positions; plugin-param
            // lanes resolve by stable id (M9-B4).
            foreach (var lane in t.Automation)
            {
                int li;
                if ((AutomationTarget)lane.Target == AutomationTarget.PluginParam)
                {
                    string pid = lane.PluginParamId ?? "";
                    li = engine.AddPluginAutomationLane(id, lane.DeviceIndex, pid);
                    if (li < 0) { warnings.Add("Couldn't recreate a plugin-param automation lane."); continue; }
                    // Keep the data, but flag if the id no longer maps to a live param
                    // (plugin updated/replaced): the envelope stays saved, just inert.
                    if (!PluginParamExists(engine, id, lane.DeviceIndex, pid))
                        warnings.Add($"Plugin parameter \"{pid}\" not found — its automation was kept but is inactive.");
                }
                else
                {
                    li = engine.AddAutomationLane(id, (AutomationTarget)lane.Target, lane.DeviceIndex, lane.ParamIndex);
                    if (li < 0) { warnings.Add("Couldn't recreate an automation lane."); continue; }
                }
                if (lane.Points.Length > 0)
                    engine.SetAutomationPoints(id, li,
                        System.Array.ConvertAll(lane.Points, p => p.ToPoint()));
            }

            // CV modulation (Phase 3). Devices are rebuilt above, so link device
            // indices resolve. Saved modulator ids are remapped to the fresh ids.
            var modIdMap = new Dictionary<int, int>();
            foreach (var m in t.Modulators)
            {
                int newId = engine.ModulatorAdd(id, m.Kind);
                if (newId < 0) { warnings.Add("Couldn't recreate a modulator."); continue; }
                modIdMap[m.Id] = newId;
                engine.ModulatorSet(id, newId, 0, m.Waveform);
                engine.ModulatorSet(id, newId, 1, m.TempoSync ? 1f : 0f);
                engine.ModulatorSet(id, newId, 2, m.RateHz);
                engine.ModulatorSet(id, newId, 3, m.RateSyncBeats);
                engine.ModulatorSet(id, newId, 4, m.Depth);
                engine.ModulatorSet(id, newId, 5, m.Phase);
                engine.ModulatorSet(id, newId, 6, m.Attack);
                engine.ModulatorSet(id, newId, 7, m.Release);
                engine.ModulatorSet(id, newId, 8, m.Decay);
                engine.ModulatorSet(id, newId, 9, m.Sustain);
            }
            // Math/Scope inputs reference other modulators by id → remap after all exist.
            foreach (var m in t.Modulators)
            {
                if (m.Kind == 5 && modIdMap.TryGetValue(m.Id, out int mathId))
                {
                    engine.ModulatorSet(id, mathId, 10, modIdMap.TryGetValue(m.InputA, out var ia) ? ia : -1);
                    engine.ModulatorSet(id, mathId, 11, modIdMap.TryGetValue(m.InputB, out var ib) ? ib : -1);
                    engine.ModulatorSet(id, mathId, 12, m.MathOffset);
                }
                else if (m.Kind == 6 && modIdMap.TryGetValue(m.Id, out int scopeId))
                    engine.ModulatorSet(id, scopeId, 10, modIdMap.TryGetValue(m.InputA, out var sa) ? sa : -1);
            }
            // Links are deferred: a cross-track target may be a track created later. Param
            // sources need no modulator; modulator sources remap the saved id.
            foreach (var l in t.CvLinks)
                if (l.SourceKind == 1) cvLinkDeferred.Add((id, l, -1));
                else if (modIdMap.TryGetValue(l.Source, out int src)) cvLinkDeferred.Add((id, l, src));
        }

        // CV links (Phase 3/5/14) — resolved after every track exists so cross-track
        // targets (a doc-index into the track list) map to their created ids.
        foreach (var (owner, l, modId) in cvLinkDeferred)
        {
            int targetTrack = l.TargetTrack >= 0 && l.TargetTrack < newTrackIds.Count ? newTrackIds[l.TargetTrack] : -1;
            int idx = l.SourceKind == 1
                ? engine.CvLinkAddFromParamToTarget(owner, l.SourceDevice, l.SourceParam, l.TargetKind, targetTrack, l.Device, l.Param)
                : engine.CvLinkAddToTarget(owner, modId, l.TargetKind, targetTrack, l.Device, l.Param);
            if (idx < 0) { warnings.Add("Couldn't recreate a CV link."); continue; }
            engine.SetCvLinkDepth(owner, idx, l.Depth);
            engine.SetCvLinkMode(owner, idx, l.Mode);
            engine.SetCvLinkBase(owner, idx, l.Base);   // override the auto-captured base with the saved one
        }

        // Master effect chain + its name/colour (before sidechain resolution so master
        // devices' sidechain sources are wired too).
        if (doc.Master is { } md)
        {
            int masterId = engine.MasterTrackId;
            if (md.Name is { Length: > 0 } mnm) engine.SetTrackName(masterId, mnm);
            if (md.ColorIndex >= 0) engine.SetTrackColorIndex(masterId, md.ColorIndex);
            ApplyDevices(engine, masterId, md.Devices, bundleDir, scDeferred, warnings);
        }

        // Sidechain wiring, now that every track exists (Phase B). Source is a doc index.
        foreach (var (track, dev, srcIndex) in scDeferred)
        {
            if (srcIndex < newTrackIds.Count && newTrackIds[srcIndex] > 0)
                engine.SetDeviceSidechainSource(track, dev, newTrackIds[srcIndex]);
        }
        // Instrument React source (Nota Flux), same deferral.
        foreach (var (track, srcIndex) in instScDeferred)
        {
            if (srcIndex < newTrackIds.Count && newTrackIds[srcIndex] > 0)
                engine.SetInstrumentSidechainSource(track, newTrackIds[srcIndex]);
        }
        // Group membership (parent stored as a doc index), now that every track exists.
        foreach (var (trackId, parentDocIndex) in groupDeferred)
        {
            if (parentDocIndex < newTrackIds.Count && newTrackIds[parentDocIndex] > 0)
                engine.SetTrackGroup(trackId, newTrackIds[parentDocIndex]);
        }

        // Record-input routing (internal resampling): decode master / track-source now
        // that every track exists (encoded = -1 master, or doc index + 2 for a track).
        foreach (var (trackId, enc) in recInputDeferred)
        {
            int source = enc == -1 ? -1
                : (enc >= 2 && enc - 2 < newTrackIds.Count && newTrackIds[enc - 2] > 0 ? newTrackIds[enc - 2] : 0);
            if (source != 0) engine.SetTrackRecordInput(trackId, source);
        }
        // MIDI routing source ("MIDI In", stored as a doc index), now that every track exists.
        foreach (var (trackId, sourceDocIndex) in midiFromDeferred)
        {
            if (sourceDocIndex < newTrackIds.Count && newTrackIds[sourceDocIndex] > 0)
                engine.SetTrackMidiSource(trackId, newTrackIds[sourceDocIndex]);
        }

        // Master-volume automation (graph-level, format v7).
        if (doc.MasterVolumeAutomation.Count > 0)
            engine.SetMasterVolumeAutomation(
                doc.MasterVolumeAutomation.ConvertAll(p => p.ToPoint()).ToArray());

        return warnings;
    }

    // --- disk I/O ----------------------------------------------------------

    /// <summary>Writes the document (and its referenced samples) into the `.nota`
    /// bundle folder. Atomic manifest (temp + rename); backs up any existing
    /// manifest into <c>backups/</c>. Sample data is read from
    /// <paramref name="engine"/> (the one <see cref="Capture"/> read from).</summary>
    public static void Save(ProjectDocument doc, string bundleDir, IAudioEngine engine)
    {
        Directory.CreateDirectory(bundleDir);

        if (doc.SampleRefs.Count > 0)
        {
            Directory.CreateDirectory(Path.Combine(bundleDir, "samples"));
            foreach (var kv in doc.SampleRefs)
                WriteSampleWav(engine, kv.Value, ResolveInBundle(bundleDir, kv.Key));
        }

        if (doc.StateBlobs.Count > 0)
        {
            Directory.CreateDirectory(Path.Combine(bundleDir, "plugin-states"));
            foreach (var kv in doc.StateBlobs)
                File.WriteAllBytes(ResolveInBundle(bundleDir, kv.Key), kv.Value);
        }

        string manifest = Path.Combine(bundleDir, ManifestName);
        if (File.Exists(manifest))
        {
            string backups = Path.Combine(bundleDir, "backups");
            Directory.CreateDirectory(backups);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(manifest, Path.Combine(backups, $"project-{stamp}.json"), overwrite: true);
        }

        string tmp = manifest + ".tmp";
        File.WriteAllText(tmp, SerializeManifest(doc));
        File.Move(tmp, manifest, overwrite: true); // atomic replace (NFR-8)
    }

    /// <summary>Serializes just the manifest JSON (same options Save uses). Used by
    /// autosave to detect whether the project changed since the last snapshot (M7-7).</summary>
    public static string SerializeManifest(ProjectDocument doc) => JsonSerializer.Serialize(doc, JsonOpts);

    /// <summary>Reads and validates the document from a `.nota` bundle folder.</summary>
    public static ProjectDocument Load(string bundleDir)
    {
        string manifest = Path.Combine(bundleDir, ManifestName);
        if (!File.Exists(manifest))
            throw new FileNotFoundException($"No {ManifestName} in {bundleDir}", manifest);

        // Parse to the raw JSON tree first: the version gate + migrations (M7-9)
        // run before deserialization, since an older shape may not map onto the
        // current DTOs. Unknown fields still survive via [JsonExtensionData].
        var root = JsonNode.Parse(File.ReadAllText(manifest)) as JsonObject
                   ?? throw new InvalidDataException("Empty or invalid project.json.");
        int version = root["formatVersion"]?.GetValue<int>() ?? 1;
        if (version > CurrentFormatVersion)
            throw new NotSupportedException(
                $"This project uses format v{version}, but this build supports up to v{CurrentFormatVersion}. Update Nota to open it.");
        root = ProjectMigrations.Migrate(root); // no-op at v1; upgrades older files in future
        return root.Deserialize<ProjectDocument>(JsonOpts)
               ?? throw new InvalidDataException("Empty or invalid project.json.");
    }

    // --- helpers -----------------------------------------------------------

    // Records `sampleId` under a bundle-relative path (deduped by id) and returns
    // that path, or null if the sample is missing from the live graph.
    private static string? RegisterSample(ProjectDocument doc, IAudioEngine engine, long sampleId, List<string> warnings)
    {
        if (sampleId <= 0) return null;
        string rel = $"samples/sample-{sampleId}.wav";
        if (!doc.SampleRefs.ContainsKey(rel))
        {
            if (!engine.TryGetSampleInfo(sampleId, out _))
            {
                warnings.Add("A referenced sample was missing and was skipped.");
                return null;
            }
            doc.SampleRefs[rel] = sampleId;
        }
        return rel;
    }

    private static void WriteSampleWav(IAudioEngine engine, long sampleId, string path)
    {
        if (!engine.TryGetSampleInfo(sampleId, out var info) || info.Channels <= 0 || info.Frames <= 0) return;
        var data = engine.ReadSample(sampleId);
        if (data.Length == 0) return;
        int sr = info.SampleRate > 0 ? (int)Math.Round(info.SampleRate) : 44100;
        using var w = new WavWriter(path, sr, info.Channels, WavBitDepth.Float32);
        w.WriteFrames(data, (int)info.Frames);
    }

    // Records a plugin state blob under a unique bundle-relative path, or null if empty.
    private static string? RegisterBlob(ProjectDocument doc, byte[] blob)
    {
        if (blob.Length == 0) return null;
        string rel = $"plugin-states/state-{doc.StateBlobs.Count}.bin";
        doc.StateBlobs[rel] = blob;
        return rel;
    }

    private static byte[] ReadBlob(string bundleDir, string relative)
    {
        string path = ResolveInBundle(bundleDir, relative);
        return File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
    }

    private static string ResolveInBundle(string bundleDir, string relative)
        => Path.Combine(bundleDir, relative.Replace('/', Path.DirectorySeparatorChar));

    // Does a hosted plugin still expose a parameter with this stable id? (M9-B4)
    private static bool PluginParamExists(IAudioEngine engine, int trackId, int deviceIndex, string paramId)
    {
        int n = engine.PluginParamCount(trackId, deviceIndex);
        for (int i = 0; i < n; i++)
            if (engine.PluginParamId(trackId, deviceIndex, i) == paramId) return true;
        return false;
    }

    private static NoteDto[] ToDtos(NotaNote[] notes)
    {
        var arr = new NoteDto[notes.Length];
        for (int i = 0; i < notes.Length; i++) arr[i] = new NoteDto(notes[i]);
        return arr;
    }

    private static NotaNote[] ToNotes(NoteDto[] dtos)
    {
        var arr = new NotaNote[dtos.Length];
        for (int i = 0; i < dtos.Length; i++) arr[i] = dtos[i].ToNote();
        return arr;
    }
}
