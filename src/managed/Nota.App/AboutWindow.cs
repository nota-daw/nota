// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public sealed class AboutWindow : NotaWindow
{
    public AboutWindow()
    {
        var build = App.Services.GetRequiredService<EngineBuildInfo>();
        Title = "About Nota";
        Width = 380;
        Height = 276;   // + title-bar band
        CanResize = false;
        Background = Brush("Brush.BgApp");

        var panel = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Nota", FontSize = 26, FontWeight = FontWeight.SemiBold,
            Foreground = Brush("Brush.TextPrimary"),
        });
        panel.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = $"Version {AppInfo.Version} · {build.Edition} edition",
        });
        panel.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = $"Engine {build.Version}",
        });
        panel.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = "Desktop DAW · dual-license (AGPLv3 / commercial)",
        });
        SetBody(panel);
    }

    private IBrush Brush(string key)
        => this.TryFindResource(key, out var v) && v is IBrush b ? b : Brushes.Magenta;
}
