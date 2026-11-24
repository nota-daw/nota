// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which device a controller's knobs follow (specs/051, FR-016): a track, the section of its chain
// and the index there — and, for a device inside a rack, the chain and slot. Plus the device's
// type key, the name its pages are stored under (built-in kind, rack, or plug-in identity), so
// edited pages apply to every instance of that type (FR-018).

namespace Nota.Application.Controllers;

/// <summary>Where a device sits in a track's chain (the instrument is index -1).</summary>
public enum FocusSection { Midi = 0, Instrument = 1, Audio = 2 }

/// <summary>A device inside a rack: the chain and its slot there; slot -1 is the chain's
/// instrument. <see cref="Midi"/> = a MIDI effect of an instrument rack's chain.</summary>
public sealed record RackPath(int Chain, int Slot, bool Midi = false);

/// <summary>A focused device. <see cref="Index"/> is the rack itself when <see cref="Rack"/> is set.</summary>
public sealed record DeviceAddress(int TrackId, FocusSection Section, int Index, RackPath? Rack = null)
{
    public static DeviceAddress Instrument(int trackId) => new(trackId, FocusSection.Instrument, -1);
}

/// <summary>The keys device pages are stored under (contracts/device-pages.md).</summary>
public static class DeviceTypeKey
{
    public const string RackAudio = "rack:audio", RackInstrument = "rack:inst", RackMidi = "rack:midi";

    public static string Builtin(FocusSection section, int kind)
        => DevicePageCatalog.RackKey(section, kind) ?? section switch
        {
            FocusSection.Instrument => "builtin:inst:",
            FocusSection.Audio => "builtin:fx:",
            _ => "builtin:midi:",
        } + DevicePageCatalog.Slug(section, kind);

    public static string Plugin(string pluginId) => "plugin:" + pluginId;

    public static bool IsPlugin(string key) => key.StartsWith("plugin:", StringComparison.Ordinal);
    public static bool IsRack(string key) => key.StartsWith("rack:", StringComparison.Ordinal);
}
