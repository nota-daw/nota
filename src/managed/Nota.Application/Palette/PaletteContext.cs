// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-2): the snapshot taken the moment the palette opens — where it was
// called from, which track and chain position a device would go to, what is selected. It
// does not change while the palette is open, so the target line always tells the truth.

namespace Nota.Application.Palette;

/// <summary>Where the palette was called from.</summary>
public enum PaletteOrigin { Arrangement, Session, Modular, Devices, Browser, Mixer, PianoRoll, DeviceWindow }

/// <summary>The chain section a device goes into.</summary>
public enum ChainSection { None, Midi, Instrument, Audio }

/// <summary>For Devices / Modular / Device window: where in the chain a new device lands.
/// <see cref="Index"/> is the selected device's index inside <see cref="Section"/> (-1 =
/// nothing selected → the end of the device's own section).</summary>
public readonly record struct InsertPoint(ChainSection Section, int Index, string DeviceName = "")
{
    public static readonly InsertPoint None = new(ChainSection.None, -1);
    public bool HasSelection => Section != ChainSection.None && (Index >= 0 || Section == ChainSection.Instrument);
}

/// <summary>The kind of track the target is.</summary>
public enum PaletteTrackType { None, Audio, Instrument, Return, Group, Master }

public sealed record PaletteContext
{
    public PaletteOrigin Origin { get; init; }
    /// <summary>The main view on screen ("Arrangement" / "Session" / "Modular") — actions of it rank higher.</summary>
    public string View { get; init; } = "Arrangement";
    /// <summary>The target track (CP-3), or -1.</summary>
    public int TrackId { get; init; } = -1;
    public string TrackName { get; init; } = "";
    public PaletteTrackType TrackType { get; init; }
    /// <summary>The target track's instrument kind (engine), -1 without one; 3 Instrument Rack, 4 Drum Rack.</summary>
    public int InstrumentKind { get; init; } = -1;
    public string InstrumentName { get; init; } = "";
    public InsertPoint Insert { get; init; } = InsertPoint.None;

    // ---- selection --------------------------------------------------------------------
    public int SelectedClips { get; init; }
    public bool HasTimeSelection { get; init; }
    /// <summary>A clip open in the piano roll (Quantize & co. need one).</summary>
    public bool HasOpenMidiClip { get; init; }
    public int SessionScene { get; init; } = -1;
    public int SelectedTrackCount { get; init; }

    /// <summary>The window the palette opened over — focus returns there on close. Opaque
    /// to this layer.</summary>
    public object? Window { get; init; }

    public bool HasTrack => TrackId > 0;
    public bool IsRack => InstrumentKind is 3 or 4;
}

/// <summary>Whether an item can be applied in the current context, and why not (CP-8).</summary>
public readonly record struct Availability(bool Ok, string Reason = "")
{
    public static readonly Availability Yes = new(true);
    public static Availability No(string reason) => new(false, reason);
}

/// <summary>How Enter was pressed (CP-5).</summary>
[Flags]
public enum ApplyMode
{
    Default = 0,
    /// <summary>⌘/Ctrl+Enter: an instrument or instrument preset on a new track; a track opens in Devices.</summary>
    NewTrack = 1,
    /// <summary>⌥/Alt+Enter: replace the selected device instead of inserting after it.</summary>
    Replace = 2,
    /// <summary>⇧Enter: apply and keep the palette open; the insert point moves past the new device.</summary>
    KeepOpen = 4,
}
