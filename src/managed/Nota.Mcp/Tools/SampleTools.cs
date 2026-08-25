// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — loading audio samples into sample-based instruments: the built-in Sampler (kind 1),
// the Grain granular synth (kind 10), and a Sampler inside a rack chain. rootNote is the MIDI note
// at which the sample plays back at its original pitch (60 = middle C).

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class SampleTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record SamplerInfo(bool HasSample, long SampleId, int RootNote, bool Loop, int Channels, long Frames, double SampleRate);

    [McpServerTool(Name = "add_sampler_track"), Description(
        "Add a Sampler instrument track and load an audio file into it in one step. Returns the track id.")]
    public Task<int> AddSamplerTrack(string path, int rootNote = 60) => Mutate(() =>
    {
        int id = E.AddSamplerInstrumentTrack();
        if (id > 0) E.SetTrackSamplerSample(id, path, rootNote);
        return id;
    });

    [McpServerTool(Name = "load_sampler_sample"), Description("Load an audio file into an existing Sampler track (kind 1).")]
    public Task<bool> LoadSamplerSample(int trackId, string path, int rootNote = 60)
        => Mutate(() => E.SetTrackSamplerSample(trackId, path, rootNote));

    [McpServerTool(Name = "set_sampler_root"), Description("Set the Sampler's root note (the MIDI note that plays the sample at its original pitch).")]
    public Task<bool> SetSamplerRoot(int trackId, int rootNote) => Mutate(() => E.SetTrackSamplerRoot(trackId, rootNote));

    [McpServerTool(Name = "get_sampler_info"), Description("Read a Sampler track's loaded sample: root note, loop flag, channels, frame count, sample rate.")]
    public Task<SamplerInfo> GetSamplerInfo(int trackId) => Read(() =>
    {
        if (!E.TryGetSamplerInfo(trackId, out var si) || si.SampleId == 0)
            return new SamplerInfo(false, 0, 0, false, 0, 0, 0);
        E.TryGetSampleInfo(si.SampleId, out var s);
        return new SamplerInfo(true, si.SampleId, si.RootNote, si.Loop != 0, s.Channels, s.Frames, s.SampleRate);
    });

    [McpServerTool(Name = "add_grain_track"), Description(
        "Add a Grain (granular) instrument track and load an audio file to granulate. Returns the track id.")]
    public Task<int> AddGrainTrack(string path, int rootNote = 60) => Mutate(() =>
    {
        int id = E.AddGrainSynthTrack();
        if (id > 0) E.SetTrackGrainSample(id, path, rootNote);
        return id;
    });

    [McpServerTool(Name = "load_grain_sample"), Description("Load an audio file into an existing Grain granular track (kind 10).")]
    public Task<bool> LoadGrainSample(int trackId, string path, int rootNote = 60)
        => Mutate(() => E.SetTrackGrainSample(trackId, path, rootNote));

    [McpServerTool(Name = "get_grain_info"), Description("Read a Grain track's loaded sample: root note, channels, frame count, sample rate.")]
    public Task<SamplerInfo> GetGrainInfo(int trackId) => Read(() =>
    {
        if (!E.TryGetGrainInfo(trackId, out var si) || si.SampleId == 0)
            return new SamplerInfo(false, 0, 0, false, 0, 0, 0);
        E.TryGetSampleInfo(si.SampleId, out var s);
        return new SamplerInfo(true, si.SampleId, si.RootNote, si.Loop != 0, s.Channels, s.Frames, s.SampleRate);
    });

    [McpServerTool(Name = "load_chain_sample"), Description(
        "Load an audio file into a rack chain's Sampler (Instrument/Drum Rack). Keeps the chain's params + devices.")]
    public Task<bool> LoadChainSample(int trackId, int chain, string path, int rootNote = 60)
        => Mutate(() => E.RackSetChainSamplerSample(trackId, chain, path, rootNote));
}
