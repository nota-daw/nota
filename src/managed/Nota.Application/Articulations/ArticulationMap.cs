// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Articulation maps: the named playing techniques of a sample library (Long, Staccato, Legato…),
// each with the MIDI the library wants to switch to it and the timing it needs. A pure model —
// no engine, no UI — so importers, the manager, the scheduler's inputs and persistence all share it.

namespace Nota.Application.Articulations;

/// <summary>What one MIDI action sends. Bank Select is a controller change on CC 0 / CC 32 but is
/// kept apart so ordering (MSB, LSB, then Program Change) is explicit.</summary>
public enum MidiActionKind
{
    /// <summary>A keyswitch note: Data1 = pitch, Data2 = velocity.</summary>
    Keyswitch,
    /// <summary>A controller change: Data1 = CC number, Data2 = value.</summary>
    ControlChange,
    /// <summary>A program change: Data1 = program.</summary>
    ProgramChange,
    /// <summary>Bank Select MSB (CC 0): Data2 = value.</summary>
    BankSelectMsb,
    /// <summary>Bank Select LSB (CC 32): Data2 = value.</summary>
    BankSelectLsb,
}

/// <summary>How an articulation's trigger behaves after it is sent.</summary>
public enum TriggerMode
{
    /// <summary>The switch is sent once and stays in force until another articulation replaces it
    /// (a keyswitch is released shortly after it is pressed).</summary>
    Latching,
    /// <summary>The switch is held for as long as the note sounds and released with it.</summary>
    Momentary,
}

/// <summary>One thing sent to the instrument to select an articulation. Actions run in list order.</summary>
public sealed class MidiAction
{
    public MidiActionKind Kind { get; set; }
    /// <summary>1..16, or null to use the channel of the note the articulation is applied to.</summary>
    public int? Channel { get; set; }
    public int Data1 { get; set; }
    public int Data2 { get; set; }

    public MidiAction Clone() => (MidiAction)MemberwiseClone();

    /// <summary>True when the values are legal MIDI for <see cref="Kind"/>.</summary>
    public bool IsValid() =>
        (Channel is null || Channel is >= 1 and <= 16)
        && Kind switch
        {
            MidiActionKind.Keyswitch or MidiActionKind.ControlChange => Data1 is >= 0 and <= 127 && Data2 is >= 0 and <= 127,
            MidiActionKind.ProgramChange => Data1 is >= 0 and <= 127,
            MidiActionKind.BankSelectMsb or MidiActionKind.BankSelectLsb => Data2 is >= 0 and <= 127,
            _ => false,
        };
}

/// <summary>A named set of mutually exclusive articulations (e.g. "Attribute" vs "Direction").</summary>
public sealed class ArticulationGroup
{
    /// <summary>Stable within the map; 0 is reserved for "no group".</summary>
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Articulation
{
    /// <summary>Stable within the map and never reused; this is what notes store. Always &gt; 0.</summary>
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    /// <summary>Colour as 0xAARRGGBB, or null for "use the default for this slot".</summary>
    public uint? ColorArgb { get; set; }
    /// <summary>The source file's palette index when it gave one instead of an RGB value (Cubase).
    /// Kept so nothing is lost; the colour a user sees is <see cref="ColorArgb"/> or Nota's own
    /// default for the index, never a guess at the source application's palette.</summary>
    public int? SourceColorIndex { get; set; }
    /// <summary>0 = ungrouped, otherwise an <see cref="ArticulationGroup.Id"/>.</summary>
    public int GroupId { get; set; }
    public TriggerMode Trigger { get; set; } = TriggerMode.Latching;
    /// <summary>Ordered MIDI actions that select this articulation.</summary>
    public List<MidiAction> Actions { get; set; } = new();
    /// <summary>Added to the track's MIDI offset for notes with this articulation (ms, negative = earlier).</summary>
    public double NoteOffsetMs { get; set; }
    /// <summary>How long before the (offset) note the actions are sent (ms, ≥ 0). Moves only the
    /// trigger, never the note.</summary>
    public double TriggerLeadMs { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Source details with no model field of their own (descriptions, original ids,
    /// unsupported constructs), preserved for the manager and for export.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    public Articulation Clone()
    {
        var c = (Articulation)MemberwiseClone();
        c.Actions = Actions.Select(a => a.Clone()).ToList();
        c.Metadata = new Dictionary<string, string>(Metadata);
        return c;
    }
}

public sealed class ArticulationMap
{
    /// <summary>Version of the serialized form of this model.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Stable across rename, duplicate-as-new and export.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string? Description { get; set; }
    public int Version { get; set; } = CurrentVersion;
    /// <summary>The articulation notes without one play as; null = the library's own state.</summary>
    public int? DefaultArticulationId { get; set; }
    public List<ArticulationGroup> Groups { get; set; } = new();
    public List<Articulation> Articulations { get; set; } = new();
    /// <summary>Where it came from ("expressionmap", "soundvariation", "nota") and the file name.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    public Articulation? Find(int id) => id > 0 ? Articulations.FirstOrDefault(a => a.Id == id) : null;

    /// <summary>The next unused articulation id (never reuses one, even after a delete).</summary>
    public int NextArticulationId() => Articulations.Count == 0 ? 1 : Articulations.Max(a => a.Id) + 1;

    /// <summary>A deep copy with a new map id and, optionally, a new name — articulation ids are
    /// kept, so notes written for the original still resolve.</summary>
    public ArticulationMap DuplicateAs(string name)
    {
        return new ArticulationMap
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            InstrumentName = InstrumentName,
            Description = Description,
            Version = Version,
            DefaultArticulationId = DefaultArticulationId,
            Groups = Groups.Select(g => new ArticulationGroup { Id = g.Id, Name = g.Name }).ToList(),
            Articulations = Articulations.Select(a => a.Clone()).ToList(),
            Metadata = new Dictionary<string, string>(Metadata),
        };
    }
}

public enum ImportSeverity { Info, Warning, Error }

public sealed record ImportMessage(ImportSeverity Severity, string Text);

/// <summary>What an importer did: the map when the file was usable, and everything it could not
/// carry over — nothing is dropped without a message.</summary>
public sealed class ArticulationImportResult
{
    public ArticulationMap? Map { get; set; }
    public List<ImportMessage> Messages { get; } = new();
    public bool Succeeded => Map is not null;
    public IEnumerable<ImportMessage> Warnings => Messages.Where(m => m.Severity != ImportSeverity.Info);

    public void Info(string text) => Messages.Add(new ImportMessage(ImportSeverity.Info, text));
    public void Warn(string text) => Messages.Add(new ImportMessage(ImportSeverity.Warning, text));
    public void Error(string text) => Messages.Add(new ImportMessage(ImportSeverity.Error, text));
}
