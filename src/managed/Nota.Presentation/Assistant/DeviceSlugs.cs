// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Every built-in device's page in the user manual, by engine kind: the device header menu's
// Documentation item opens it (NotaDocs), and the Assistant's Show me anchors name device cards
// by it ("ui:device.audio-effects.compressor", specs/098). The page slugs mirror nota-docs'
// src/content/docs/devices tree — keep them in step when a device is added there.

namespace Nota.Presentation.Assistant;

public enum DeviceGroup { Instrument, AudioEffect, MidiEffect }

/// <summary>A built-in device and its manual page ("devices/audio-effects/compressor").</summary>
public sealed record DevicePage(DeviceGroup Group, int EngineKind, string Page)
{
    /// <summary>The Show me anchor of its card: "ui:device." + the page under devices/, dotted.</summary>
    public string Anchor => "ui:device." + Page["devices/".Length..].Replace('/', '.');
}

public static class DeviceSlugs
{
    // Engine kind ids → page under devices/ (index = kind; "" = no page of its own).
    private static readonly string[] Instruments =
    [
        "instruments/synth", "instruments/sampler", "instruments/physical", "racks/instrument-rack",
        "racks/drum-rack", "instruments/aurora", "instruments/volt", "instruments/bass",
        "instruments/pendulum", "instruments/operator", "instruments/grain", "instruments/flux",
        "instruments/rhythm", "instruments/monolith", "instruments/pentad", "instruments/consort",
        "instruments/keys", "instruments/mosaic", "instruments/twin",
    ];
    private static readonly string[] AudioEffects =
    [
        "audio-effects/eq-8", "audio-effects/compressor", "audio-effects/reverb", "audio-effects/delay",
        "audio-effects/utility", "racks/audio-effect-rack", "audio-effects/valve", "audio-effects/auto-filter",
        "audio-effects/vintage", "audio-effects/orbit", "audio-effects/auto-shift", "audio-effects/beat-repeat",
        "audio-effects/crush", "audio-effects/dynamic-eq", "audio-effects/ceiling", "audio-effects/strata",
        "audio-effects/eq-3", "audio-effects/forge", "audio-effects/level", "audio-effects/shutter",
        "audio-effects/chamber", "audio-effects/prism", "audio-effects/lens", "audio-effects/flanger",
        "audio-effects/phaser", "audio-effects/chorus", "audio-effects/vocoder",
    ];
    private static readonly string[] MidiEffects =
    [
        "midi-effects/arp", "midi-effects/chord", "midi-effects/scale", "midi-effects/length",
        "midi-effects/velocity", "midi-effects/random", "racks/midi-effect-rack",
    ];

    /// <summary>The page for a device of this group and engine kind, or "" when it has none.</summary>
    public static string PageOf(DeviceGroup group, int kind)
    {
        var map = group switch { DeviceGroup.Instrument => Instruments, DeviceGroup.MidiEffect => MidiEffects, _ => AudioEffects };
        return kind >= 0 && kind < map.Length && map[kind].Length > 0 ? "devices/" + map[kind] : "";
    }

    public static IEnumerable<DevicePage> All()
    {
        for (int k = 0; k < Instruments.Length; k++) yield return new(DeviceGroup.Instrument, k, "devices/" + Instruments[k]);
        for (int k = 0; k < AudioEffects.Length; k++) yield return new(DeviceGroup.AudioEffect, k, "devices/" + AudioEffects[k]);
        for (int k = 0; k < MidiEffects.Length; k++) yield return new(DeviceGroup.MidiEffect, k, "devices/" + MidiEffects[k]);
    }

    public static DevicePage? ByAnchor(string anchor) => All().FirstOrDefault(d => d.Anchor == anchor);
}
