// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The output setup (specs/089, #483): per playback device, which output pair carries Main
// (the audience mix) and which carries the private Cue mix, and where the metronome click
// goes. Like the input setup (specs/086) it belongs to the rig, not the project, so it lives
// in the app settings keyed by audio system and device — a project opened on a laptop with
// two outputs never routes to an output that isn't there.

using System;

namespace Nota.Application;

/// <summary>Where the metronome and count-in clicks go.</summary>
public enum ClickDestination { Main = 0, Cue = 1, MainAndCue = 2 }

/// <summary>The engine's routing against the open device (nota_audio_output_routing_state).</summary>
[Flags]
public enum OutputRoutingState
{
    Ok = 0,
    /// <summary>Main's pair is not on this device: Main plays on outputs 1/2.</summary>
    MainFellBack = 1,
    /// <summary>A Cue pair is set but not on this device: Cue is silent, nothing moves to Main.</summary>
    CueUnavailable = 2,
}

/// <summary>One device's output setup. Channels are 1-based, as the user sees them.</summary>
public sealed class OutputSetup
{
    public int MainFirst { get; set; } = 1;
    public int MainWidth { get; set; } = 2;
    /// <summary>First channel of the Cue pair; null = no Cue output.</summary>
    public int? CueFirst { get; set; }
    public int CueWidth { get; set; } = 2;
    public ClickDestination Click { get; set; } = ClickDestination.Main;
    /// <summary>Click level in dB (−60 and below = silent, up to +6).</summary>
    public double ClickDb { get; set; }

    public bool HasCue => CueFirst is not null;

    public OutputSetup Copy() => new()
    {
        MainFirst = MainFirst, MainWidth = MainWidth, CueFirst = CueFirst, CueWidth = CueWidth,
        Click = Click, ClickDb = ClickDb,
    };
}

public static class OutputSetups
{
    public const double ClickDbMin = -60.0;
    public const double ClickDbMax = 6.0;

    /// <summary>Raised after a setup changed, so the mixer's Cue strip and pickers refresh.</summary>
    public static event Action? Changed;
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>The settings key of a playback device (same scheme as inputs).</summary>
    public static string KeyOf(AudioSystem system, string outputUid, string asioDriver)
        => InputSetups.KeyOf(system, outputUid, asioDriver);

    /// <summary>Two picks (1-based first, width) share a channel.</summary>
    public static bool Overlaps(int aFirst, int aWidth, int bFirst, int bWidth)
        => aFirst < bFirst + bWidth && bFirst < aFirst + aWidth;

    /// <summary>A valid pick: width 1, or width 2 starting on an odd channel (1/2, 3/4 …).</summary>
    public static bool IsValidPick(int first, int width)
        => first >= 1 && (width == 1 || (width == 2 && first % 2 == 1));

    /// <summary>Main moved; a Cue on the same channels is dropped (FR-004).</summary>
    public static OutputSetup WithMain(OutputSetup s, int first, int width)
    {
        var r = s.Copy();
        r.MainFirst = first;
        r.MainWidth = width;
        if (r.CueFirst is int c && Overlaps(first, width, c, r.CueWidth)) r.CueFirst = null;
        return r;
    }

    /// <summary>Cue moved (null = no Cue output). Refused (s returned) when it hits Main.
    /// Without a Cue output the click can only go to Main.</summary>
    public static OutputSetup WithCue(OutputSetup s, int? first, int width)
    {
        if (first is int f && Overlaps(s.MainFirst, s.MainWidth, f, width)) return s;
        var r = s.Copy();
        r.CueFirst = first;
        r.CueWidth = width;
        if (first is null) r.Click = ClickDestination.Main;
        return r;
    }

    public static OutputSetup WithClick(OutputSetup s, ClickDestination click, double db)
    {
        var r = s.Copy();
        r.Click = r.HasCue ? click : ClickDestination.Main;
        r.ClickDb = Math.Clamp(db, ClickDbMin, ClickDbMax);
        return r;
    }

    /// <summary>The click level as a linear gain (silent at the bottom of the range).</summary>
    public static float ClickGain(OutputSetup s)
        => s.ClickDb <= ClickDbMin ? 0f : (float)Math.Pow(10.0, s.ClickDb / 20.0);

    /// <summary>A device never set up: Main 1/2, no Cue, click to Main. Under ASIO Main starts
    /// on the pair chosen before this setting existed (FR-005, 0-based <paramref name="asioPair"/>).</summary>
    public static OutputSetup Default(int asioPair = 0)
        => new() { MainFirst = Math.Max(0, asioPair) / 2 * 2 + 1 };
}
