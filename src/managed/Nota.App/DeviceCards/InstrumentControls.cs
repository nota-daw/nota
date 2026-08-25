// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — the shared built-in-instrument knob, used by every built-in synth
// editor (Synth / Physical / Aurora / Volt). It drives a plugin-param on the track's
// instrument (deviceIndex -1), records automation-write gestures, and registers itself
// for live follow through the context.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal static class InstrumentControls
{
    internal static Control InstKnob(DeviceCardContext ctx, Dictionary<string, int> idx, string id, string name, Action sync, double knobSize = 38, double cellW = 58, IBrush? arc = null)
        => InstKnob(ctx, idx, id, name, sync, null, knobSize, cellW, arc);

    // Same as InstKnob, but the value readout is formatted with <paramref name="fmt"/>
    // (real units — ms / dB / kHz — instead of a bare percentage). The formatter is also
    // handed to the live-follow tick so automation moves keep the units.
    internal static Control InstKnob(DeviceCardContext ctx, Dictionary<string, int> idx, string id, string name, Action sync, Func<float, string>? fmt, double knobSize = 38, double cellW = 58, IBrush? arc = null)
    {
        if (!idx.TryGetValue(id, out var i)) return new Panel();
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        Func<float, string> f = fmt ?? Pct;
        var value = new TextBlock { Text = f(engine.PluginParamGet(track, -1, i)), FontSize = 8, Foreground = TextPrimary };
        value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var knob = new Knob(engine.PluginParamGet(track, -1, i), 1.0) { Accent = true, ArcColor = arc, Default = engine.InstrumentParamDefault(track, i), Width = knobSize, Height = knobSize };
        int pi = i; string pid = id;
        knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = f((float)v); sync(); };
        knob.GestureBegin += () => engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, pid);
        knob.GestureEnd += () => engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, pid);
        ctx.AddInstFader(i, knob, value, fmt);
        MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), name);
        return KnobCell(name, knob, value, cellW);
    }
}
