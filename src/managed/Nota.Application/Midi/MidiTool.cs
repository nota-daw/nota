// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The clip-tool contract. A tool is a pure function
// (input notes, settings, context) -> output notes, described well enough that the
// UI can build its whole parameter rail from the descriptor — no per-tool panel.
// Everything is deterministic: the only randomness comes from the Seed in the
// context, so the same settings always give the same notes.

namespace Nota.Application.Midi;

public enum MidiToolKind
{
    /// <summary>Rewrites the notes it is given.</summary>
    Transform,
    /// <summary>Produces notes; may read the input as source material.</summary>
    Generate,
}

public enum MidiParamKind { Float, Int, Choice, Toggle, Note }

/// <summary>One knob on a tool. <see cref="Id"/> is the stable key used by settings and
/// presets; <see cref="Name"/> is what the rail shows.</summary>
public sealed record MidiToolParam(
    string Id,
    string Name,
    MidiParamKind Kind,
    double Min,
    double Max,
    double Default,
    string[]? Choices = null,
    string Unit = "",
    string Format = "0.##")
{
    public static MidiToolParam Float(string id, string name, double min, double max, double def, string unit = "", string format = "0.##")
        => new(id, name, MidiParamKind.Float, min, max, def, null, unit, format);

    public static MidiToolParam Int(string id, string name, int min, int max, int def, string unit = "")
        => new(id, name, MidiParamKind.Int, min, max, def, null, unit, "0");

    public static MidiToolParam Percent(string id, string name, double def)
        => new(id, name, MidiParamKind.Float, 0, 1, def, null, "%", "0");

    public static MidiToolParam Choice(string id, string name, string[] choices, int def)
        => new(id, name, MidiParamKind.Choice, 0, choices.Length - 1, def, choices);

    public static MidiToolParam Toggle(string id, string name, bool def)
        => new(id, name, MidiParamKind.Toggle, 0, 1, def ? 1 : 0);

    /// <summary>A MIDI pitch, shown by name (C3) rather than as a number.</summary>
    public static MidiToolParam Note(string id, string name, int def)
        => new(id, name, MidiParamKind.Note, 0, 127, def);

    /// <summary>A note-division picker; the stored value indexes <see cref="MidiRates.Labels"/>.</summary>
    public static MidiToolParam Rate(string id, string name, int def = MidiRates.Sixteenth)
        => new(id, name, MidiParamKind.Choice, 0, MidiRates.Labels.Length - 1, def, MidiRates.Labels);

    public string Display(double v) => Kind switch
    {
        MidiParamKind.Toggle => v >= 0.5 ? "On" : "Off",
        MidiParamKind.Choice => Choices is { Length: > 0 }
            ? Choices[Math.Clamp((int)Math.Round(v), 0, Choices.Length - 1)]
            : "",
        MidiParamKind.Note => NoteName((int)Math.Round(v)),
        MidiParamKind.Int => $"{(int)Math.Round(v)}{Unit}",
        _ => Unit == "%"
            ? $"{v * 100:0}%"
            : $"{v.ToString(Format, System.Globalization.CultureInfo.InvariantCulture)}{Unit}",
    };

    public static string NoteName(int pitch)
    {
        int p = Math.Clamp(pitch, 0, 127);
        return $"{MidiScales.KeyNames[p % 12]}{p / 12 - 1}";
    }
}

/// <summary>Note divisions in beats (one beat = a 1/4 note), mirroring the piano roll's
/// grid picker so "Rate" reads the same everywhere.</summary>
public static class MidiRates
{
    public const int Sixteenth = 4;

    public static readonly string[] Labels =
        { "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/4T", "1/8T", "1/16T", "1/8.", "1/16." };

    public static readonly double[] Beats =
        { 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 2.0 / 3, 1.0 / 3, 1.0 / 6, 0.75, 0.375 };

    public static double At(double index) =>
        Beats[Math.Clamp((int)Math.Round(index), 0, Beats.Length - 1)];
}

/// <summary>Musical surroundings a tool works inside: how long the clip is, what the
/// editor's grid is set to, the key the user picked, and the seed that makes every
/// random choice repeatable.</summary>
public sealed class MidiToolContext
{
    public double LengthBeats { get; init; } = 4;
    public double Grid { get; init; } = 0.25;
    public bool ScaleOn { get; init; }
    public int ScaleRoot { get; init; }
    public ushort ScaleMask { get; init; } = MidiScales.Chromatic;
    public int Seed { get; init; }

    /// <summary>The mask to write notes through — chromatic when the overlay is off, so a
    /// tool's "in scale" option is a no-op rather than a surprise.</summary>
    public ushort Mask => ScaleOn ? ScaleMask : MidiScales.Chromatic;

    public int[] Degrees => MidiScales.Degrees(Mask);

    public int Snap(int pitch, int prefer = 0) => MidiScales.Snap(Mask, ScaleRoot, pitch, prefer);
}

/// <summary>Parameter values for one tool, keyed by <see cref="MidiToolParam.Id"/>.
/// Missing keys fall back to the descriptor's default, so a tool can gain a parameter
/// without invalidating stored settings.</summary>
public sealed class MidiToolSettings
{
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<MidiToolParam> _params;

    public MidiToolSettings(IReadOnlyList<MidiToolParam> parameters)
    {
        _params = parameters;
        foreach (var p in parameters) _values[p.Id] = p.Default;
    }

    public double this[string id] => _values.TryGetValue(id, out var v) ? v : Default(id);
    public int Int(string id) => (int)Math.Round(this[id]);
    public bool Flag(string id) => this[id] >= 0.5;
    /// <summary>A "Rate"-style parameter resolved to beats.</summary>
    public double RateBeats(string id) => MidiRates.At(this[id]);

    public void Set(string id, double value)
    {
        var p = Find(id);
        _values[id] = p is null ? value : Math.Clamp(value, p.Min, p.Max);
    }

    public void Reset()
    {
        foreach (var p in _params) _values[p.Id] = p.Default;
    }

    private MidiToolParam? Find(string id)
    {
        foreach (var p in _params) if (p.Id == id) return p;
        return null;
    }

    private double Default(string id) => Find(id)?.Default ?? 0;
}

/// <summary>A clip tool: its identity, its knobs, and the pure function that runs it.</summary>
public sealed record MidiTool(
    string Id,
    string Name,
    MidiToolKind Kind,
    string Blurb,
    IReadOnlyList<MidiToolParam> Params,
    Func<IReadOnlyList<NotaNote>, MidiToolSettings, MidiToolContext, List<NotaNote>> Run)
{
    public MidiToolSettings NewSettings() => new(Params);
}
