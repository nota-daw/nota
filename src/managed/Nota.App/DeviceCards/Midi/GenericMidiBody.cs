// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — fallback body for a MIDI effect without a bespoke editor:
// generic faders + readouts (up to 10 params).

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class GenericMidiBody : IMidiDeviceBody
{
    public double Width => 190;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        var panel = new StackPanel { Spacing = 7 };
        int pc = Math.Min(10, engine.MidiEffectParamCount(track, index));
        for (int i = 0; i < pc; i++)
        {
            int pi = i;
            float min = engine.MidiEffectParamMin(track, index, i), max = engine.MidiEffectParamMax(track, index, i);
            float v = engine.MidiEffectGetParam(track, index, i);
            bool integral = max - min >= 2 && (max <= 24.5f);   // small integer ranges show as whole numbers
            var value = new TextBlock { Text = FmtParam(v, integral), FontSize = 9, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Right };
            value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            top.Children.Add(new TextBlock { Text = engine.MidiEffectParamName(track, index, i).ToUpperInvariant(), FontSize = 9, Foreground = TextTertiary });
            Grid.SetColumn(value, 1); top.Children.Add(value);
            var bar = new Knob((v - min) / Math.Max(1e-6f, max - min), 1.0) { Accent = true, HorizontalAlignment = HorizontalAlignment.Center,
                Default = (engine.MidiEffectParamDefault(track, index, pi) - min) / Math.Max(1e-6f, max - min) };
            bar.ValueChanged += nv => { float nvv = (float)(min + nv * (max - min)); engine.MidiEffectSetParam(track, index, pi, nvv); value.Text = FmtParam(nvv, integral); };
            MidiLearn.Bind(bar, MidiTarget.MidiDeviceParam(track, index, pi), engine.MidiEffectParamName(track, index, pi));
            panel.Children.Add(new StackPanel { Spacing = 2, Children = { top, bar } });
        }
        return panel;
    }

    private static string FmtParam(float v, bool integral) => integral ? Math.Round(v).ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.00", CultureInfo.InvariantCulture);
}
