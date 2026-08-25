// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — fallback instrument card: an "Open GUI" stub for a hosted plugin
// instrument (or a built-in with no editor), plus a "Save preset" action for plugins.

using Avalonia.Controls;
using Avalonia.Layout;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class GenericInstrumentCard : IInstrumentCard
{
    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;

        var gui = TextButton("Open GUI ↗");
        gui.PointerPressed += (_, _) => { try { engine.OpenPluginEditor(track, -1); } catch { /* no-op */ } };
        var body = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { gui } };
        if (engine.TrackInstrumentKind(track) == -1)
        {
            var save = TextButton("Save preset");
            save.PointerPressed += (_, _) => ctx.RequestPresetSave(-1);
            body.Children.Add(save);
        }
        return SimpleCard("Instrument", 170, body);
    }
}
