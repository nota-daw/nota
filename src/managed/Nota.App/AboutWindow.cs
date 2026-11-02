// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed class AboutWindow : NotaWindow
{
    public AboutWindow()
    {
        var build = App.Services.GetRequiredService<EngineBuildInfo>();
        Title = Loc.T("About Nota");
        Width = 380;
        Height = 348;   // + title-bar band
        CanResize = false;
        Background = Brush("Brush.BgApp");

        var panel = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Brand header — logo beside name + versions, same as the welcome screen.
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var logo = new Image { Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Center };
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://Nota.App/Assets/logo.png"));
            logo.Source = new Bitmap(s);
        }
        catch { /* decorative */ }
        Grid.SetColumn(logo, 0);
        header.Children.Add(logo);

        var brand = new StackPanel
        {
            Margin = new Thickness(16, 0, 0, 0),
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        brand.Children.Add(new TextBlock
        {
            Text = Loc.T("Nota"), FontSize = 26, FontWeight = FontWeight.SemiBold,
            Foreground = Brush("Brush.TextPrimary"),
        });
        brand.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = Loc.F("Version {0}", AppInfo.Version),
        });
        brand.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = Loc.F("Engine {0}", build.Version),
        });
        Grid.SetColumn(brand, 1);
        header.Children.Add(brand);
        panel.Children.Add(header);
        panel.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = Loc.T("Desktop DAW · AGPLv3"),
        });
        panel.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Margin = new Thickness(0, 8, 0, 0),
            Text = Loc.T("© 2026 Egor Khindikaynen (Ambertape)"),
        });
        panel.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = Loc.T("Free software under the GNU AGPLv3."),
        });
        panel.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.T("Includes third-party software (JUCE, Avalonia, miniaudio, RtMidi, dr_libs, Signalsmith, HIIR). See LICENSES/THIRD-PARTY-NOTICES.md for copyright and attribution notices."),
        });
        SetBody(panel);
    }

    private IBrush Brush(string key)
        => (IBrush?)NotaPalette.ByKey(key) ?? NotaPalette.TextPrimary;
}
