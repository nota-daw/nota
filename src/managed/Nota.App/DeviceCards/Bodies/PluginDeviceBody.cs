// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — hosted plug-in (kind -1) body: an "open editor" stub, latency
// readout, and (for plugins that expose a sidechain bus) the shared source picker.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class PluginDeviceBody : IDeviceBody
{
    public double Width => 230;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;

        var open = new Border
        {
            Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 4), HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "Open editor ↗", FontSize = 11, Foreground = TextPrimary },
        };
        open.PointerPressed += (_, _) => { try { engine.OpenPluginEditor(track, index); } catch { /* no-op */ } };

        double ms = engine.SampleRate > 0 ? engine.TrackLatencySamples(track) / engine.SampleRate * 1000.0 : 0;
        var latency = new TextBlock { Text = $"latency {ms:0.0} ms — compensated", FontSize = 9, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center };
        latency.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");

        var body = new StackPanel
        {
            Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Hosted plug-in — native GUI in own window", FontSize = 10, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                open, latency,
            },
        };
        // Phase C: plugins that expose a sidechain input bus get the same source picker.
        if (engine.DeviceAcceptsSidechain(track, index))
        {
            body.Children.Add(new Border { Height = 1, Background = BorderDef, Margin = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Stretch });
            body.Children.Add(DeviceParamControls.SidechainSelector(ctx, index));
        }
        return body;
    }
}
