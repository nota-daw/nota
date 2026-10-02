// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — the AI models on audio clips: Separate Stems (htdemucs) and audio → MIDI with
// basic-pitch. The models run on this computer and are installed by the user in Settings →
// Downloads → AI Models; an AI can see what is installed but not download anything.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class AiTools(
    IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, IModelStore models, IClipAi ai)
    : EngineTools(engine, dispatch, refresh)
{
    private const string NotInstalled = " isn't installed. Ask the user to install it in Nota: Settings → Downloads → AI Models.";

    public sealed record AiModelInfo(
        string Id, string Name,
        [property: Description("The Nota feature it powers")] string Feature,
        bool Installed,
        [property: Description("What installing it would download now, in MB (the AI runtime included the first time)")] double DownloadMb);

    [McpServerTool(Name = "list_ai_models"), Description(
        "List the AI models Nota can run on this computer (htdemucs → separate_stems, basic-pitch → convert_audio_to_midi) "
        + "and whether each is installed. Only the user can install them, in Settings → Downloads → AI Models.")]
    public AiModelInfo[] ListAiModels()
        => models.Models.Select(m => new AiModelInfo(m.Id, m.Name, m.Feature, models.IsInstalled(m.Id),
               Math.Round(models.DownloadSize(m.Id) / 1048576.0, 1))).ToArray();

    public sealed record StemsResult(
        [property: Description("The new group track holding the stems")] int GroupTrackId,
        [property: Description("Stem track ids in order: drums, bass, other, vocals")] int[] StemTrackIds);

    [McpServerTool(Name = "separate_stems"), Description(
        "Split an audio clip into drums, bass, other and vocals with the htdemucs model: a group of four audio tracks "
        + "appears under the clip's track, each with a clip lined up with the original, and the original clip is switched "
        + "off (one undo step). Takes about a third of the clip's length on a recent computer; clips up to 10 minutes.")]
    public async Task<StemsResult> SeparateStems(
        [Description("Track id of the audio clip")] int trackId,
        [Description("Clip index on that track")] int clipIndex)
    {
        if (!ai.CanSeparate) throw new InvalidOperationException("The htdemucs model" + NotInstalled);
        var job = await Read(() => ai.ReadStemSource(trackId, clipIndex));
        using var stems = await Task.Run(() => ai.Separate(job));
        int group = await Mutate(() => ai.ApplyStems(job, stems));
        var kids = await Read(() => Enumerable.Range(0, E.TrackCount)
            .Select(i => E.TryGetTrackInfo(i, out var ti) ? ti : default)
            .Where(ti => ti.GroupId == group).Select(ti => ti.Id).ToArray());
        return new StemsResult(group, kids);
    }

    public sealed record ConvertResult(int TrackId, int ClipIndex, int Notes);

    [McpServerTool(Name = "convert_audio_to_midi"), Description(
        "Transcribe an audio clip into a MIDI clip on a new Nota Synth track, lined up with it, using the basic-pitch model. "
        + "'harmony' keeps every note (chords, polyphony); 'melody' keeps one line, the strongest note wherever notes overlap. "
        + "Velocities follow how loud each note is.")]
    public async Task<ConvertResult> ConvertAudioToMidi(
        [Description("Track id of the audio clip")] int trackId,
        [Description("Clip index on that track")] int clipIndex,
        [Description("melody | harmony")] string mode = "harmony")
    {
        bool melody = mode.Equals("melody", StringComparison.OrdinalIgnoreCase);
        if (!melody && !mode.Equals("harmony", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("mode must be 'melody' or 'harmony'.");
        if (!ai.CanTranscribe) throw new InvalidOperationException("The basic-pitch model" + NotInstalled);

        var (mono, sr, startBeat, clipBeats) = await Read(() =>
        {
            int n = E.GetClipAudioMono(trackId, clipIndex, null, out var rate);
            if (n <= 0 || rate <= 0) throw new InvalidOperationException("That clip has no audio to convert.");
            var buf = new float[n];
            E.GetClipAudioMono(trackId, clipIndex, buf, out rate);
            double start = 0, beats = 0;
            if (E.TryGetClipInfo(trackId, clipIndex, out var ci)) { start = ci.StartBeat; beats = ci.LengthBeats; }
            if (beats <= 0) beats = n / rate * E.Bpm / 60.0;
            return (buf, rate, start, beats);
        });
        var heard = await Task.Run(() => ai.Transcribe(mono, sr));
        if (melody) heard = Transcription.Monophonic(heard);
        var notes = Transcription.ToClipNotes(heard, mono.Length / sr, clipBeats);
        if (notes.Length == 0) throw new InvalidOperationException($"No {(melody ? "melody" : "notes")} found in that clip.");
        return await Mutate(() =>
        {
            int t = E.AddInstrumentTrack();
            int mc = E.AddMidiClip(t, startBeat, Math.Max(0.25, clipBeats));
            E.SetClipNotes(t, mc, notes);
            return new ConvertResult(t, mc, notes.Length);
        });
    }
}
