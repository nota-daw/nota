// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A track's Cue key (specs/089): headphones, solid accent while the track is in Cue — the
// one key every surface shows (mixer strips, Session mixer, track headers, the inspector),
// MIDI-learnable like Mute. Surfaces show it only while a Cue output is set on this device
// or some track is already in Cue, so a stereo laptop's views look as they always did.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

internal static class CueKey
{
    /// <summary>A track went in or out of Cue (a key, a MIDI controller): every key on screen
    /// re-reads its track, so the mixer, headers, Session mixer and inspector agree.</summary>
    public static event Action? Changed;
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>A Cue output is set on this device, or a track is in Cue.</summary>
    public static bool Shown(IAudioEngine engine)
    {
        if (OutputSetupAccess.Current(engine).HasCue) return true;
        for (int i = 0; i < engine.TrackCount; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && engine.GetTrackCue(ti.Id)) return true;
        return false;
    }

    /// <summary>The key for <paramref name="trackId"/>; <paramref name="sync"/> repaints it when
    /// Cue changes elsewhere. <paramref name="changed"/> runs after a click.</summary>
    public static Border Create(IAudioEngine engine, int trackId, double width, double height, CornerRadius radius,
        out Action<bool> sync, Action? changed = null, Avalonia.Media.IBrush? offFill = null)
    {
        bool on = engine.GetTrackCue(trackId);
        var glyph = new Glyph(GlyphKind.Headphones, 9) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border
        {
            Height = height, CornerRadius = radius, BorderThickness = new Thickness(1), Child = glyph,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (width > 0) b.Width = width;
        ToolTip.SetTip(b, Loc.T("mixer.cue_tip"));
        Avalonia.Automation.AutomationProperties.SetName(b, Loc.T("mixer.cue"));
        void Paint()
        {
            b.Background = on ? NotaPalette.Accent : offFill ?? NotaPalette.SurfaceRaised;
            b.BorderBrush = on ? NotaPalette.Accent : offFill ?? NotaPalette.Seam;
            glyph.Foreground = on ? NotaPalette.TextOnAccent : NotaPalette.TextSecondary;
        }
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            on = !on;
            engine.SetTrackCue(trackId, on);
            Paint();
            changed?.Invoke();
            NotifyChanged();
        };
        sync = v => { if (v == on) return; on = v; Paint(); };
        var follow = sync;
        void Reread() => follow(engine.GetTrackCue(trackId));
        b.AttachedToVisualTree += (_, _) => { Changed += Reread; Reread(); };
        b.DetachedFromVisualTree += (_, _) => Changed -= Reread;
        MidiLearn.Bind(b, MidiTarget.TrackCue(trackId), Loc.T("mixer.cue"));
        Paint();
        return b;
    }
}
