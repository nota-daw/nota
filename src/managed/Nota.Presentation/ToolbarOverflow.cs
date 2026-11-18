// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The transport bar's overflow (specs/097, #520). On a narrow window the bar sheds its
// least-needed parts in a fixed order (#303); the ⋯ key at its end lists what was shed, so
// nothing on the bar is ever out of reach. Which parts that is for a given fit — and in what
// order the menu lists them — is decided here, as data, so the rules can be checked without
// a window. The view maps each part to its controls and builds the menu.

namespace Nota.Presentation;

/// <summary>A part of the transport bar that can be shed for lack of room.</summary>
public enum ToolbarPart
{
    CpuBar,         // the DSP meter's bar; its number stays
    RemoteLabel,    // the word "Remote"; the glyph, count and dot stay
    Tap,
    Key,
    Remote,         // the Remote chip (Connect Phone)
    Follow,
    Automation,
    Mixer,          // the Mixer show / hide key (#516)
    Assistant,      // the Assistant panel key (specs/098)
    TimeSignature,
    MidiLearn,      // the MIDI learn chip — the panel goes with its last chip
    Grid,
    Snap,
    LoopRecord,
    Loop,
    Master,
    DspMeter,
}

public static class ToolbarOverflow
{
    /// <summary>The order the bar sheds in, one part per step; parts come back in reverse.
    /// First what is shown elsewhere or merely informative, then what has a menu entry or a
    /// shortcut, last the readouts you set while playing (#303).</summary>
    public static IReadOnlyList<ToolbarPart> ShedOrder { get; } =
    [
        ToolbarPart.CpuBar,
        ToolbarPart.RemoteLabel,
        ToolbarPart.Tap,
        ToolbarPart.Key,
        ToolbarPart.Remote,
        ToolbarPart.Follow,
        ToolbarPart.Automation,
        ToolbarPart.Mixer,
        ToolbarPart.Assistant,
        ToolbarPart.TimeSignature,
        ToolbarPart.MidiLearn,
        ToolbarPart.Grid,
        ToolbarPart.Snap,
        ToolbarPart.LoopRecord,
        ToolbarPart.Loop,
        ToolbarPart.Master,
        ToolbarPart.DspMeter,
    ];

    /// <summary>Left to right as the parts sit in the bar — the order the menu lists them in.</summary>
    public static IReadOnlyList<ToolbarPart> BarOrder { get; } =
    [
        ToolbarPart.Loop,
        ToolbarPart.LoopRecord,
        ToolbarPart.Tap,
        ToolbarPart.TimeSignature,
        ToolbarPart.Key,
        ToolbarPart.Grid,
        ToolbarPart.Follow,
        ToolbarPart.Snap,
        ToolbarPart.Automation,
        ToolbarPart.Mixer,
        ToolbarPart.Assistant,
        ToolbarPart.MidiLearn,
        ToolbarPart.Remote,
        ToolbarPart.RemoteLabel,
        ToolbarPart.CpuBar,
        ToolbarPart.DspMeter,
        ToolbarPart.Master,
    ];

    /// <summary>A part whose information stays in the bar when it goes (the DSP number beside
    /// the bar, the Remote glyph and count beside the word): it needs no menu entry.</summary>
    public static bool IsInfoOnly(ToolbarPart part) => part is ToolbarPart.CpuBar or ToolbarPart.RemoteLabel;

    /// <summary>The parts the menu lists when the first <paramref name="shedSteps"/> steps of
    /// <see cref="ShedOrder"/> are shed (clamped to 0 … count), in bar order.</summary>
    public static IReadOnlyList<ToolbarPart> Entries(int shedSteps)
    {
        int n = Math.Clamp(shedSteps, 0, ShedOrder.Count);
        var shed = new HashSet<ToolbarPart>();
        for (int i = 0; i < n; i++) shed.Add(ShedOrder[i]);
        return BarOrder.Where(p => shed.Contains(p) && !IsInfoOnly(p)).ToList();
    }
}
