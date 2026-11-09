// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The hardware entries of an audio track's input picker (specs/086): every switched-on input
// of the capture device alone (a mono source, recorded in the middle — #340), then the
// odd-aligned stereo pairs whose two inputs are on, under the user's names (Audio I/O setup),
// else the device's, else "Input N". The track's own pick always shows, marked when the
// device lacks it or it is switched off, so the picker says what the track is set to.

using System.Collections.Generic;
using Nota.Application.Localization;

namespace Nota.Application;

public enum InputPickState { Ok, NotOnDevice, SwitchedOff }

public readonly record struct InputPickEntry(InputPick Pick, string Label, string Short, InputPickState State);

public static class InputPickList
{
    /// <summary>Past this many mono inputs a picker groups them under submenus.</summary>
    public const int SubmenuThreshold = 16;

    /// <param name="count">The device's input channels; 0 when unknown (then 1, 2 and 1/2).</param>
    /// <param name="deviceName">The name the device gives a channel ("" for none); may be null.</param>
    /// <param name="current">The track's pick, listed even when it isn't otherwise.</param>
    public static List<InputPickEntry> Build(int count, System.Func<int, string>? deviceName, InputSetup? setup, InputPick? current)
    {
        bool known = count > 0;
        if (!known) count = 2;
        var list = new List<InputPickEntry>();
        for (int c = 0; c < count; c++)
        {
            var p = InputPick.Mono(c);
            if (InputSetups.IsOn(setup, p) || p == current) list.Add(Entry(p, deviceName, setup, count, known));
        }
        for (int c = 0; c + 1 < count; c += 2)
        {
            var p = InputPick.Pair(c);
            if (InputSetups.IsOn(setup, p) || p == current) list.Add(Entry(p, deviceName, setup, count, known));
        }
        if (current is { } cur && cur.First + cur.Width > count) list.Add(Entry(cur, deviceName, setup, count, known));
        return list;
    }

    public static InputPickEntry Entry(InputPick p, System.Func<int, string>? deviceName, InputSetup? setup, int count, bool known)
    {
        var state = known && p.First + p.Width > count ? InputPickState.NotOnDevice
            : !InputSetups.IsOn(setup, p) ? InputPickState.SwitchedOff : InputPickState.Ok;
        string label = MenuLabel(p, deviceName, setup);
        string shortLabel = ShortLabel(p, setup);
        label = state switch
        {
            InputPickState.NotOnDevice => Loc.F("record_input_pick.not_available", label),
            InputPickState.SwitchedOff => Loc.F("record_input_pick.switched_off", label),
            _ => label,
        };
        return new InputPickEntry(p, label, shortLabel, state);
    }

    /// <summary>"Vox (input 3, mono)" / "Audio input 3 (mono)" / "Gtr (inputs 3/4)" / "Audio inputs 3/4 (stereo)".</summary>
    public static string MenuLabel(InputPick p, System.Func<int, string>? deviceName, InputSetup? setup)
    {
        int n = p.First + 1;
        string? user = InputSetups.NameOf(setup, p);
        if (p.IsStereo)
            return user is null ? Loc.F("record_input_pick.audio_inputs_stereo", n, n + 1) : Loc.F("record_input_pick.named_inputs_stereo", user, n, n + 1);
        string name = user ?? DeviceName(deviceName, p.First);
        return name.Length > 0 ? Loc.F("record_input_pick.named_input_mono", name, n) : Loc.F("record_input_pick.audio_input_mono", n);
    }

    /// <summary>The short label: the user's name, else "In 3" / "In 3/4".</summary>
    public static string ShortLabel(InputPick p, InputSetup? setup = null)
        => InputSetups.NameOf(setup, p)
           ?? (p.IsStereo ? Loc.F("record_input_pick.in_pair", p.First + 1, p.First + 2) : Loc.F("record_input_pick.in_n", p.First + 1));

    public static string DeviceName(System.Func<int, string>? deviceName, int channel)
    {
        string name = deviceName?.Invoke(channel)?.Trim() ?? "";
        // Drivers that name inputs only by number add nothing the label doesn't already say.
        return name.Length == 0 || name == (channel + 1).ToString() ? "" : name;
    }
}
