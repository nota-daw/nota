// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shell-header pieces shared by the cards that come in two sizes (Nota Arp, Nota Chord,
// Nota Synth …): the MIDI Learn button that mirrors the transport's Learn toggle, and the
// S / L size toggle bound to the device's "View" param (0 = L, 1 = S).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Localization;

namespace Nota.App;

internal static class MidiCardHeader
{
    public static Control LearnButton(DeviceCardContext ctx)
    {
        var tb = new TextBlock { Text = "MIDI learn", FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
        var learn = new Border { Height = 16, CornerRadius = NotaRadius.Cell, Padding = new Thickness(6, 0), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
        ToolTip.SetTip(learn, Loc.T("device.midi.map_a_hardware_control_arm"));
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
        => SizeToggle(ctx, isMini, mini => ctx.Engine.MidiEffectSetParam(ctx.TrackId, index, viewParam, mini ? 1f : 0f));

    /// <summary>The S / L toggle over any size store — a MIDI effect's View param, or an
    /// instrument's "view" param (InstrumentView). Flipping it rebuilds the chain.</summary>
    public static Control SizeToggle(DeviceCardContext ctx, Func<bool> isMini, Action<bool> setMini)
    {
        var size = DeviceCardKit.Segments(new[] { "S", "L" }, () => isMini() ? 0 : 1,
            i => { setMini(i == 0); ctx.RequestRebuild(); }, out _, padX: 0, minSegWidth: 16, fontSize: 8);
        ToolTip.SetTip(size, Loc.T("device.midi.card_size_s_mini_l"));
        return size;
    }

    /// <summary>The S / L / XL toggle of a device that opens in the XL panel (specs/085): the
    /// same segments with a third, XL. Picking one rebuilds the chain.</summary>
    public static Control SizeToggle(DeviceCardContext ctx, Func<Nota.Application.CardSize> get, Action<Nota.Application.CardSize> set)
    {
        static int Seg(Nota.Application.CardSize s) => s switch { Nota.Application.CardSize.S => 0, Nota.Application.CardSize.XL => 2, _ => 1 };
        var size = DeviceCardKit.Segments(new[] { "S", "L", "XL" }, () => Seg(get()),
            i => { set(i == 0 ? Nota.Application.CardSize.S : i == 2 ? Nota.Application.CardSize.XL : Nota.Application.CardSize.L); ctx.RequestRebuild(); },
            out _, padX: 0, minSegWidth: 16, fontSize: 8);
        ToolTip.SetTip(size, Loc.T("device.xl.card_size_s_l_xl"));
        return size;
    }
}
