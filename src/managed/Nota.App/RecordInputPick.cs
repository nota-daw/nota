// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One entry of an audio track's record-input pickers (track menu, header box, Session I/O
// row, inspector): a source plus, for the hardware input, which device channel(s) the track
// takes. The hardware input lists every input of the capture device (specs/086) — each one
// alone as a mono source recorded in the middle (#340), then the odd-aligned stereo pairs —
// so any input of a multi-input interface can be recorded.

using System.Collections.Generic;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

internal readonly record struct RecordInputPick(int Source, InputPick Input)
{
    /// <summary>The hardware-input entries for a track's picker (see <see cref="InputPickList"/>),
    /// with their menu and short labels. A picker the user opened (<paramref name="probe"/>)
    /// asks the engine to look at the selected device when it hasn't been open yet; views
    /// built on their own don't (opening an input device is slow and may ask for microphone
    /// access).</summary>
    public static List<(RecordInputPick Pick, string Label, string Short)> HardwareEntries(IAudioEngine? engine, int trackId = 0, bool probe = false)
    {
        int count = engine?.AudioInputChannelCount() ?? 0;
        if (count <= 0 && probe && engine is not null) count = engine.ProbeAudioInput();
        InputPick? cur = engine is not null && trackId > 0 && engine.GetTrackRecordInput(trackId) == 0 ? engine.GetTrackInput(trackId) : null;
        var setup = engine is null ? null : InputSetupAccess.Current(engine);
        var list = new List<(RecordInputPick, string, string)>();
        foreach (var e in InputPickList.Build(count, engine is null ? null : engine.AudioInputChannelName, setup, cur))
            list.Add((new(0, e.Pick), e.Label, e.Short));
        return list;
    }

    /// <summary>Whether a picker should group the hardware entries under submenus.</summary>
    public static bool NeedsSubmenus(List<(RecordInputPick Pick, string Label, string Short)> hardware)
        => hardware.FindAll(h => !h.Pick.Input.IsStereo).Count > InputPickList.SubmenuThreshold;

    /// <summary>The short label of the track's hardware pick, with the user's name for it.</summary>
    public static string ShortLabel(IAudioEngine engine, InputPick p) => InputPickList.ShortLabel(p, InputSetupAccess.Current(engine));

    /// <summary>A track's current pick. The input only matters for the hardware source.</summary>
    public static RecordInputPick Of(IAudioEngine engine, int trackId)
    {
        int src = engine.GetTrackRecordInput(trackId);
        return new(src, src == 0 ? engine.GetTrackInput(trackId) : InputPick.Stereo12);
    }

    /// <summary>A track or return as a source (any input setting: only hardware has one).</summary>
    public static RecordInputPick Track(int source) => new(source, InputPick.Stereo12);

    /// <summary>Whether this entry is the track's current pick.</summary>
    public bool Matches(RecordInputPick current)
        => Source == current.Source && (Source != 0 || Input == current.Input);

    public void ApplyTo(IAudioEngine engine, int trackId)
    {
        if (Source == 0) engine.SetTrackInput(trackId, Input);
        engine.SetTrackRecordInput(trackId, Source);
    }
}
