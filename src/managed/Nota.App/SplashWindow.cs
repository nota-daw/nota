// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Splash screen — a borderless, centred window shown the instant the app launches
// while the audio engine and services spin up (see App.OnFrameworkInitializationCompleted).
// It is dismissed the moment the main window is ready. No chrome, no taskbar entry:
// just the brand mark, version and an indeterminate progress bar so the launch never
// looks frozen.

using System;
using Avalonia;
using Avalonia.Controls;
using AvDecorations = Avalonia.Controls.WindowDecorations;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Nota.App;

public sealed class SplashWindow : Window
{
    public SplashWindow()
    {
        WindowDecorations = AvDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 420;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        var logo = new Image
        {
            Width = 84,
            Height = 84,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://Nota.App/Assets/logo.png"));
            logo.Source = new Bitmap(s);
        }
        catch { /* logo is decorative — the splash still reads without it */ }

        var stack = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(logo);
        stack.Children.Add(new TextBlock
        {
            Text = "Nota",
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"Version {AppInfo.Version}",
            FontSize = 12,
            Foreground = NotaPalette.TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var bar = new ProgressBar
        {
            IsIndeterminate = true,
            Width = 220,
            Height = 3,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = NotaPalette.Accent,
            Background = NotaPalette.BorderDefault,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(bar);

        // Rounded card on the transparent window, structured with a 1px border to match
        // the rest of the chrome (no drop shadow — this is a solid launch panel).
        Content = new Border
        {
            Background = NotaPalette.BgSunken,
            BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Body,
            Child = stack,
        };
    }
}
