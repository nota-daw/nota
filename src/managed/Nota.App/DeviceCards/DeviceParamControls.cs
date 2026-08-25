// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — reusable parameter-fader fragments shared by the device-body
// strategies: the generic param-bar grid, a single automatable param knob, and the
// sidechain source/tap/gain/mix panel. All are state-free builders that take a
// DeviceCardContext; the param knob registers its live-follow into the context.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal static class DeviceParamControls
{
    internal static string Fmt(float v) => Math.Abs(v) >= 100 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.0", CultureInfo.InvariantCulture);

    internal static Control ParamBars(DeviceCardContext ctx, int index)
    {
        int pc = ctx.Engine.DeviceParamCount(ctx.TrackId, index);
        var body = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        for (int p = 0; p < pc; p++) body.Children.Add(ParamRow(ctx, index, p));
        return body;
    }

    internal static Control ParamRow(DeviceCardContext ctx, int device, int param)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        string name = engine.DeviceParamName(track, device, param);
        float min = engine.DeviceParamMin(track, device, param);
        float max = engine.DeviceParamMax(track, device, param);
        float val = engine.DeviceGetParam(track, device, param);
        double span = Math.Max(1e-6, max - min);

        var value = new TextBlock { Text = Fmt(val), FontSize = 8, Foreground = TextPrimary };
        value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var knob = new Knob((val - min) / span, 1.0) { Accent = true, Default = (engine.DeviceParamDefault(track, device, param) - min) / span };
        int d = device, pp = param;
        knob.ValueChanged += frac =>
        {
            double v = min + frac * span;
            engine.DeviceSetParam(track, d, pp, (float)v);
            value.Text = Fmt((float)v);
        };
        // M9-C: record built-in device-param moves while playing.
        knob.GestureBegin += () => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, d, pp, "");
        knob.GestureEnd   += () => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, d, pp, "");
        MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, d, pp), name);
        // Follow automation / external changes live on the UI tick (skip during a drag).
        ctx.AddDeviceRefresher(() =>
        {
            if (knob.Dragging) return;
            float cur = engine.DeviceGetParam(track, d, pp);
            double frac = (cur - min) / span;
            if (Math.Abs(frac - knob.Value) > 1e-3) { knob.Value = frac; value.Text = Fmt(cur); }
        });
        return KnobCell(name, knob, value);
    }

    /// <summary>Sidechain controls: source track picker + tap point (pre/post FX), detector
    /// gain and dry/wet mix (Phase B–D). "None" = the device's own input.</summary>
    internal static Control SidechainSelector(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int di = index;
        var label = new TextBlock { Text = "SIDECHAIN", FontSize = 9, Foreground = TextTertiary };

        // Source: "None" first, then every track except this one.
        var ids = new List<int> { -1 };
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 10 };
        combo.Items.Add("None");
        int n = engine.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue;
            string kind = ti.IsReturn ? "Return" : ti.IsInstrument ? "Instrument" : "Audio";
            ids.Add(ti.Id);
            combo.Items.Add($"{i + 1} · {kind}");
        }
        combo.SelectedIndex = Math.Max(0, ids.IndexOf(engine.DeviceSidechainSource(track, di)));
        combo.SelectionChanged += (_, _) =>
        {
            int sel = combo.SelectedIndex;
            if (sel < 0 || sel >= ids.Count) return;
            engine.SetDeviceSidechainSource(track, di, ids[sel]);
            ctx.NotifyChanged();
        };

        // Tap point: a small two-state toggle (Post FX ⇄ Pre FX).
        var tapText = new TextBlock { FontSize = 9, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center };
        void SyncTap() => tapText.Text = engine.DeviceSidechainTapPre(track, di) ? "Pre FX" : "Post FX";
        SyncTap();
        var tap = new Border
        {
            Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 3), Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Stretch, Child = tapText,
        };
        tap.PointerPressed += (_, _) =>
        {
            engine.SetDeviceSidechainTapPre(track, di, !engine.DeviceSidechainTapPre(track, di));
            SyncTap();
        };

        // Gain (−24..+24 dB) and Mix (0..100%) sliders.
        var gain = ScFaderRow("GAIN", (engine.DeviceSidechainGain(track, di) + 24f) / 48f,
            g => $"{g * 48f - 24f:+0.0;-0.0;0.0} dB",
            g => engine.SetDeviceSidechainGain(track, di, (float)(g * 48.0 - 24.0)));
        var mix = ScFaderRow("MIX", engine.DeviceSidechainMix(track, di),
            m => $"{m * 100f:0} %",
            m => engine.SetDeviceSidechainMix(track, di, (float)m));

        return new StackPanel { Spacing = 5, Children = { label, combo, tap, gain, mix } };
    }

    // A labeled rotary knob for a raw 0..1 sidechain control.
    private static Control ScFaderRow(string name, double norm, Func<double, string> fmt, Action<double> apply)
    {
        var value = new TextBlock { Text = fmt(norm), FontSize = 8, Foreground = TextPrimary };
        value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var knob = new Knob(Math.Clamp(norm, 0, 1), 1.0) { Accent = true };
        knob.ValueChanged += frac => { apply(frac); value.Text = fmt(frac); };
        return KnobCell(name, knob, value);
    }
}
