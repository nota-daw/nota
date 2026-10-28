// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One entry of an audio track's record-input pickers (track menu, header box, Session I/O
// row): a source plus, for the hardware input, which of its channels the track takes (#340).
// The hardware input shows up as three entries — inputs 1/2 as a stereo pair, input 1 alone,
// input 2 alone — so a mono mic on one input of a stereo device can be recorded centred.

using System;
using Nota.Application;

namespace Nota.App;

internal readonly record struct RecordInputPick(int Source, InputChannels Channels)
{
    /// <summary>The hardware-input entries, in picker order, with their menu and short labels.</summary>
    public static readonly (RecordInputPick Pick, string Label, string Short)[] Hardware =
    {
        (new(0, InputChannels.Mono1), "Audio input 1 (mono)", "Ext 1"),
        (new(0, InputChannels.Mono2), "Audio input 2 (mono)", "Ext 2"),
        (new(0, InputChannels.Stereo), "Audio inputs 1/2 (stereo)", "Ext 1/2"),
    };

    /// <summary>A track's current pick. Channels only matter for the hardware input.</summary>
    public static RecordInputPick Of(IAudioEngine engine, int trackId)
    {
        int src = engine.GetTrackRecordInput(trackId);
        return new(src, src == 0 ? engine.GetTrackInputChannels(trackId) : InputChannels.Stereo);
    }

    /// <summary>The short label of a hardware pick ("Ext 1", "Ext 2", "Ext 1/2").</summary>
    public static string HardwareShort(InputChannels channels)
        => Array.Find(Hardware, h => h.Pick.Channels == channels).Short ?? "Ext";

    /// <summary>A track or return as a source (any channel setting: only hardware has one).</summary>
    public static RecordInputPick Track(int source) => new(source, InputChannels.Stereo);

    /// <summary>Whether this entry is the track's current pick.</summary>
    public bool Matches(RecordInputPick current)
        => Source == current.Source && (Source != 0 || Channels == current.Channels);

    public void ApplyTo(IAudioEngine engine, int trackId)
    {
        if (Source == 0) engine.SetTrackInputChannels(trackId, Channels);
        engine.SetTrackRecordInput(trackId, Source);
    }
}
