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

namespace Nota.App;

internal static class MidiCardHeader
{
    public static Control LearnButton(DeviceCardContext ctx)
    {
        // A raised 22px device button (the kit's TextButton); armed, it takes the soft tint with
        // orange text.
        var learn = DeviceCardKit.TextButton("MIDI learn");
        learn.Height = NotaSize.Dropdown; learn.Padding = new Thickness(10, 0); learn.VerticalAlignment = VerticalAlignment.Center;
        var tb = (TextBlock)learn.Child!;
        tb.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(learn, "Map a hardware control: arm, touch a control on the card, move a knob or key");
        void Sync()
        {
            bool on = TopLevel.GetTopLevel(learn) is MainWindow w && w.MidiLearnArmed;
            learn.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
            tb.Foreground = on ? NotaPalette.AccentBright : NotaPalette.TextPrimary;
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
            i => { setMini(i == 0); ctx.RequestRebuild(); }, out _, padX: 5, minSegWidth: 18);
        ToolTip.SetTip(size, "Card size: S (mini) / L (full)");
        return size;
    }
}
