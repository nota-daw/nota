// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Splash screen — a borderless, centred window shown the instant the app launches
// while the audio engine and services spin up (see App.OnFrameworkInitializationCompleted).
// It is dismissed the moment the main window is ready. No chrome, no taskbar entry.
// The card carries the release's own art (specs/074): SplashArtComposer turns the version
// into a composition, so every release opens with a picture of its own. The brand mark,
// version and an indeterminate progress bar (so the launch never looks frozen) sit on the
// plain ground beside it. If the art fails for any reason, the plain card from before is
// shown instead — the art never stands between the user and the app.

using System;
using Avalonia;
using Avalonia.Controls;
using AvDecorations = Avalonia.Controls.WindowDecorations;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Nota.Application.Localization;

namespace Nota.App;

public sealed class SplashWindow : Window
{
    public SplashWindow()
    {
        WindowDecorations = AvDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = SplashScene.Width;
        Height = SplashScene.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        try { Content = ArtCard(SplashArtComposer.Compose(AppInfo.Version)); }
        catch { Content = PlainCard(); }
    }

    /// <summary>The release's art with the brand block on the plain ground of its text region.</summary>
    private static Control ArtCard(SplashScene scene)
    {
        var t = scene.TextRect;
        bool row = scene.Layout == SplashLayout.TextBottom;
        double barW = row ? 160 : t.W - 44;

        var words = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = Loc.T("common.nota"),
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary,
        });
        words.Children.Add(new TextBlock
        {
            Text = Loc.F("common.version", AppInfo.Version),
            FontSize = 12,
            Foreground = NotaPalette.TextTertiary,
        });
        words.Children.Add(Bar(barW, new Thickness(0, 8, 0, 0)));

        var brand = new StackPanel
        {
            Orientation = row ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        brand.Children.Add(Logo(row ? 44 : 40));
        brand.Children.Add(words);

        var text = new Border
        {
            Width = t.W,
            Height = t.H,
            Padding = new Thickness(22, 0),
            Child = brand,
        };
        Canvas.SetLeft(text, t.X);
        Canvas.SetTop(text, t.Y);

        var canvas = new Canvas { Width = SplashScene.Width, Height = SplashScene.Height };
        canvas.Children.Add(new SplashArtView(scene));
        canvas.Children.Add(text);

        // The 1px card edge, as before; the art itself is clipped to the same corner.
        return new Border
        {
            BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Body,
            ClipToBounds = true,
            Child = canvas,
        };
    }

    /// <summary>The splash before specs/074: logo, name, version and progress on a plain card.</summary>
    private static Control PlainCard()
    {
        var stack = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var logo = Logo(84);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(logo);
        stack.Children.Add(new TextBlock
        {
            Text = Loc.T("common.nota"),
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = Loc.F("common.version", AppInfo.Version),
            FontSize = 12,
            Foreground = NotaPalette.TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var bar = Bar(220, new Thickness(0, 10, 0, 0));
        bar.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(bar);

        // Rounded card on the transparent window, structured with a 1px border to match
        // the rest of the chrome (no drop shadow — this is a solid launch panel).
        return new Border
        {
            Background = NotaPalette.BgSunken,
            BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Body,
            Child = stack,
        };
    }

    private static Image Logo(double size)
    {
        var logo = new Image { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left };
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://Nota.App/Assets/logo.png"));
            logo.Source = new Bitmap(s);
        }
        catch { /* logo is decorative — the splash still reads without it */ }
        return logo;
    }

    private static ProgressBar Bar(double width, Thickness margin) => new()
    {
        IsIndeterminate = true,
        Width = width,
        Height = 3,
        MinWidth = 0,
        Margin = margin,
        Foreground = NotaPalette.Accent,
        Background = NotaPalette.BorderDefault,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
}
