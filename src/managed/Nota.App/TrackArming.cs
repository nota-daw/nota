// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The arm buttons' side of exclusive record-arm (#332, rule in Nota.Presentation.TrackArm):
// every arm button (Arrangement header, Session mixer, Mixer window) goes through here, and
// the views repaint when an arm disarmed other tracks.

using System;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

internal static class TrackArming
{
    /// <summary>Raised after an exclusive arm disarmed other tracks, so every view repaints them.</summary>
    public static event Action? OthersChanged;

    /// <summary>Arms or disarms a track, exclusively unless <paramref name="additive"/> (see TrackArm).</summary>
    public static void Set(IAudioEngine engine, int trackId, bool on, bool additive)
    {
        if (TrackArm.Set(engine, trackId, on, additive)) OthersChanged?.Invoke();
    }

    /// <summary>The arm buttons' tooltip, with the platform's modifier spelled out.</summary>
    public static string Tip => MenuKit.Keys("Arm for recording · ⌘-click to arm several tracks");

    /// <summary>⌘ on macOS, Ctrl elsewhere: the "add to the armed set" modifier.</summary>
    public static bool IsAdditive(Avalonia.Input.KeyModifiers m)
        => (m & (OperatingSystem.IsMacOS() ? Avalonia.Input.KeyModifiers.Meta : Avalonia.Input.KeyModifiers.Control)) != 0;
}
