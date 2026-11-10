// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Audio I/O setup (specs/086, #353 option 2): per capture device, which inputs are
// switched on and what the user calls them. It belongs to the machine, not the project — a
// project stores input numbers only — so it lives in the app settings, keyed by the audio
// system and device. Track input pickers list only switched-on inputs, under these names; a
// track on a switched-off input keeps its pick and records nothing until it is on again.

using System;
using System.Collections.Generic;

namespace Nota.Application;

/// <summary>One device's input setup. Absent entries mean the default: on, the device's name.</summary>
public sealed class InputSetup
{
    /// <summary>Switched-off inputs (0-based).</summary>
    public List<int> Off { get; set; } = new();
    /// <summary>User names of single inputs (0-based channel → name).</summary>
    public Dictionary<int, string> Names { get; set; } = new();
    /// <summary>User names of stereo pairs (0-based first channel, even → name).</summary>
    public Dictionary<int, string> PairNames { get; set; } = new();

    public bool IsEmpty => Off.Count == 0 && Names.Count == 0 && PairNames.Count == 0;
}

public static class InputSetups
{
    /// <summary>Raised after a setup changed, so open pickers and labels refresh (FR-020).</summary>
    public static event Action? Changed;
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>The settings key of a capture device: its audio system and uid ("" = the
    /// system default input). Under ASIO the driver is the device.</summary>
    public static string KeyOf(AudioSystem system, string inputUid, string asioDriver)
        => system == AudioSystem.Asio ? $"asio:{asioDriver}" : $"device:{inputUid}";

    public static bool IsOn(InputSetup? s, int channel) => s is null || !s.Off.Contains(channel);

    /// <summary>A pick is listed when every input it takes is on.</summary>
    public static bool IsOn(InputSetup? s, InputPick p)
    {
        for (int c = p.First; c < p.First + p.Width; c++)
            if (!IsOn(s, c)) return false;
        return true;
    }

    public static string? NameOf(InputSetup? s, InputPick p)
    {
        if (s is null) return null;
        var names = p.IsStereo ? s.PairNames : s.Names;
        return names.TryGetValue(p.First, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : null;
    }

    /// <summary>Switch an input on/off; returns the setup to store (null when it is all defaults).</summary>
    public static InputSetup? WithOn(InputSetup? s, int channel, bool on)
    {
        var r = Copy(s);
        r.Off.Remove(channel);
        if (!on) { r.Off.Add(channel); r.Off.Sort(); }
        return r.IsEmpty ? null : r;
    }

    /// <summary>Name an input or a pair ("" or whitespace clears it back to the device's name).</summary>
    public static InputSetup? WithName(InputSetup? s, InputPick p, string? name)
    {
        var r = Copy(s);
        var names = p.IsStereo ? r.PairNames : r.Names;
        if (string.IsNullOrWhiteSpace(name)) names.Remove(p.First);
        else names[p.First] = name.Trim();
        return r.IsEmpty ? null : r;
    }

    /// <summary>The switched-off inputs the engine should treat as unavailable at take start.</summary>
    public static int[] OffChannels(InputSetup? s) => s is null ? Array.Empty<int>() : s.Off.ToArray();

    private static InputSetup Copy(InputSetup? s) => s is null ? new InputSetup() : new InputSetup
    {
        Off = new List<int>(s.Off),
        Names = new Dictionary<int, string>(s.Names),
        PairNames = new Dictionary<int, string>(s.PairNames),
    };
}
