// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shell-header pieces shared by the MIDI-effect cards that come in two sizes (Nota Arp,
// Nota Chord): the MIDI Learn button that mirrors the transport's Learn toggle, and the
// S / L size toggle bound to the device's "View" param (0 = L, 1 = S).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal static class MidiCardHeader
{
    public static Control LearnButton(DeviceCardContext ctx)
    {
        var tb = new TextBlock { Text = "MIDI Learn", FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
        var learn = new Border { Height = 16, CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 0), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
        ToolTip.SetTip(learn, "Map a hardware control: arm, touch a control on the card, move a knob or key");
        void Sync()
        {
            bool on = TopLevel.GetTopLevel(learn) is MainWindow w && w.MidiLearnArmed;
            learn.Background = on ? NotaPalette.AccentSubtle : NotaPalette.TrackOff;
            learn.BorderBrush = on ? NotaPalette.BorderBrass : NotaPalette.TrackOff;
            tb.Foreground = on ? NotaPalette.AccentHover : NotaPalette.TextPrimary;
        }
        learn.PointerPressed += (_, ev) =>
        {
            if (!ev.GetCurrentPoint(learn).Properties.IsLeftButtonPressed) return;
            ev.Handled = true;
            if (TopLevel.GetTopLevel(learn) is MainWindow w) w.ToggleMidiLearn();
            Sync();
        };
        ctx.AddDeviceRefresher(Sync);
        learn.AttachedToVisualTree += (_, _) => Sync();
        return learn;
    }

    public static Control SizeToggle(DeviceCardContext ctx, int index, int viewParam, Func<bool> isMini)
    {
        var e = ctx.Engine; int t = ctx.TrackId;
        var size = DeviceCardKit.Segments(new[] { "S", "L" }, () => isMini() ? 0 : 1,
            i => { e.MidiEffectSetParam(t, index, viewParam, i == 0 ? 1f : 0f); ctx.RequestRebuild(); }, out _, padX: 0, minSegWidth: 16, fontSize: 8);
        ToolTip.SetTip(size, "Card size: S (mini) / L (full)");
        return size;
    }
}
