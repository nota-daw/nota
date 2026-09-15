// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The registry every clip-tool surface reads: the piano roll's Tools rail, and
// anything else that wants to run a tool (MCP, tests). Adding a tool means adding
// it to All — nothing else in the app enumerates them.

namespace Nota.Application.Midi;

public static class MidiToolCatalog
{
    public static IReadOnlyList<MidiTool> All { get; } = new[]
    {
        // Generators first: they are what you reach for on an empty clip.
        MidiGenerators.Rhythm,
        MidiGenerators.Seed,
        MidiGenerators.Stacks,
        MidiGenerators.Euclidean,
        MidiGenerators.MelodicSteps,
        MidiGenerators.Shape,

        MidiTransforms.Arpeggiate,
        MidiTransforms.Connect,
        MidiTransforms.Ornament,
        MidiTransforms.Quantize,
        MidiTransforms.Recombine,
        MidiTransforms.Span,
        MidiTransforms.Strum,
        MidiTransforms.TimeWarp,
        MidiTransforms.VelocityShaper,
    };

    public static IEnumerable<MidiTool> Generators => All.Where(t => t.Kind == MidiToolKind.Generate);
    public static IEnumerable<MidiTool> Transforms => All.Where(t => t.Kind == MidiToolKind.Transform);

    public static MidiTool? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}

/// <summary>How a tool's output meets the notes already in the clip.</summary>
public enum MidiToolBlend
{
    /// <summary>The tool's output stands in for whatever it was given.</summary>
    Replace,
    /// <summary>The tool's output is layered on top of the untouched clip.</summary>
    Add,
}

/// <summary>Runs a tool over a clip, honouring the selection as the working scope: with a
/// selection the tool sees only those notes and the rest of the clip is carried through
/// untouched, without one it sees everything. This is the single place that decides what
/// "apply" means, so the UI, MCP and tests can't drift on it.</summary>
public static class MidiToolRunner
{
    public static List<NotaNote> Apply(
        MidiTool tool,
        IReadOnlyList<NotaNote> clip,
        IReadOnlyCollection<int>? selection,
        MidiToolSettings settings,
        MidiToolContext ctx,
        MidiToolBlend blend = MidiToolBlend.Replace)
    {
        var scoped = new List<NotaNote>();
        var untouched = new List<NotaNote>();
        bool hasSelection = selection is { Count: > 0 };

        for (int i = 0; i < clip.Count; i++)
        {
            if (!hasSelection || selection!.Contains(i)) scoped.Add(clip[i]);
            else untouched.Add(clip[i]);
        }

        var produced = tool.Run(scoped, settings, ctx);

        var result = new List<NotaNote>(untouched);
        if (blend == MidiToolBlend.Add) result.AddRange(scoped);
        result.AddRange(produced);
        return MidiToolUtil.Deoverlap(MidiToolUtil.Tidy(result, ctx.LengthBeats));
    }
}
