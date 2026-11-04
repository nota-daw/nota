// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — fallback instrument card: an "Open GUI" stub for a built-in with no
// editor. Hosted plug-ins have their own card (PluginCard).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using static Nota.App.DeviceCardKit;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class GenericInstrumentCard : IInstrumentCard
{
    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;

        var gui = TextButton(Loc.T("device.generic.open_gui"));
        gui.PointerPressed += (_, _) => { try { engine.OpenPluginEditor(track, -1); } catch { /* no-op */ } };
        var body = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { gui } };
        return new Border { Padding = new Thickness(10), Child = body };
    }

    // Wrapped by the shared shell like every other instrument (spec 012): name, presets, power slot.
    public bool BodyOnly => true;
    public double CardWidth => 170;
}
