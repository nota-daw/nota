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
    /// <summary>The hardware-input entries for a track's picker, with their menu and short
    /// labels: every device input alone, then the stereo pairs. A picker the user opened
    /// (<paramref name="probe"/>) asks the engine to look at the selected device when it
    /// hasn't been open yet; views built on their own don't (opening an input device is slow
    /// and may ask for microphone access). Unknown: inputs 1, 2 and 1/2 as before. A pick
    /// the device lacks stays listed (marked) so the menu shows what the track is set to.</summary>
    public static List<(RecordInputPick Pick, string Label, string Short)> HardwareEntries(IAudioEngine? engine, int trackId = 0, bool probe = false)
    {
        int count = engine?.AudioInputChannelCount() ?? 0;
        if (count <= 0 && probe && engine is not null) count = engine.ProbeAudioInput();
        bool known = count > 0;
        if (!known) count = 2;
        var list = new List<(RecordInputPick, string, string)>();
        for (int c = 0; c < count; c++)
        {
            var p = InputPick.Mono(c);
            list.Add((new(0, p), MenuLabel(engine, p), ShortLabel(p)));
        }
        for (int c = 0; c + 1 < count; c += 2)
        {
            var p = InputPick.Pair(c);
            list.Add((new(0, p), MenuLabel(engine, p), ShortLabel(p)));
        }
        if (engine is not null && trackId > 0 && engine.GetTrackRecordInput(trackId) == 0)
        {
            var cur = engine.GetTrackInput(trackId);
            if (cur.First + cur.Width > count)
                list.Add((new(0, cur), known ? Loc.F("record_input_pick.not_available", MenuLabel(engine, cur)) : MenuLabel(engine, cur), ShortLabel(cur)));
        }
        return list;
    }

    /// <summary>"Vocal mic (input 3, mono)" / "Audio input 3 (mono)" / "Audio inputs 3/4 (stereo)".</summary>
    public static string MenuLabel(IAudioEngine? engine, InputPick p)
    {
        int n = p.First + 1;
        if (p.IsStereo) return Loc.F("record_input_pick.audio_inputs_stereo", n, n + 1);
        string name = DeviceName(engine, p.First);
        return name.Length > 0 ? Loc.F("record_input_pick.named_input_mono", name, n) : Loc.F("record_input_pick.audio_input_mono", n);
    }

    /// <summary>The short label of a hardware pick: "In 3", "In 3/4".</summary>
    public static string ShortLabel(InputPick p)
        => p.IsStereo ? Loc.F("record_input_pick.in_pair", p.First + 1, p.First + 2) : Loc.F("record_input_pick.in_n", p.First + 1);

    private static string DeviceName(IAudioEngine? engine, int channel)
    {
        if (engine is null || channel >= engine.AudioInputChannelCount()) return "";
        string name = engine.AudioInputChannelName(channel).Trim();
        // Drivers that name inputs only by number add nothing the label doesn't already say.
        return name.Length == 0 || name == (channel + 1).ToString() ? "" : name;
    }

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
