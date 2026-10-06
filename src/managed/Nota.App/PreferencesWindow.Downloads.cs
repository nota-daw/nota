// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → Downloads: one page for Plug-ins, Sample Packs and AI Models, picked with a
// switch beside the title (PreferencesWindow.Store.cs / .Samples.cs / .Models.cs build each).
// The title, switch and intro scroll away; the filter / search / Refresh toolbar sticks to the
// top of the pane. It lives in an overlay above the ScrollViewer, held level with a spacer in
// the scrolled content until the spacer passes the top edge, and gets a hairline underneath
// once rows slide below it.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private const int DownloadsIndex = 5;
    private static readonly string[] DownloadSources = { "Plug-ins", "Sample Packs", "AI Models" };
    private int _downloadSource;   // 0 = plug-ins, 1 = sample packs, 2 = AI models; kept for the window's life

    private Control DownloadsPane(Control header)
    {
        var (toolbar, body) = _downloadSource switch { 0 => StoreParts(), 1 => PackParts(), _ => ModelParts() };
        var intro = Caption(_downloadSource switch { 0 => StoreIntro, 1 => PacksIntro, _ => ModelsIntro }, muted: true);
        intro.MaxWidth = 600;
        intro.HorizontalAlignment = HorizontalAlignment.Left;
        intro.Margin = new Thickness(0, 0, 0, 4);

        // Title row: the page header left, the registry switch right.
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var segs = new ToggleButton[DownloadSources.Length];
        for (int i = 0; i < DownloadSources.Length; i++)
        {
            int idx = i;
            segs[i] = new ToggleButton { Classes = { "seg" }, FontSize = 12, IsChecked = i == _downloadSource, Content = DownloadSources[i] };
            segs[i].Click += (_, _) =>
            {
                segs[idx].IsChecked = idx == _downloadSource;   // a toggle never clears itself
                if (idx == _downloadSource) return;
                _downloadSource = idx;
                Select(DownloadsIndex);
            };
            strip.Children.Add(segs[i]);
        }
        header.Margin = new Thickness(0);
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 16) };
        titleRow.Children.Add(header);
        var sw = new Border { Classes = { "segmented" }, Child = strip, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(sw, 1);
        titleRow.Children.Add(sw);

        // The toolbar sits in the overlay; the spacer keeps its place in the flow.
        var spacer = new Border();
        var pinned = new Border
        {
            Background = Ground, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(32, 10, 32 - ScrollBarWidth, 10), Margin = new Thickness(0, 0, ScrollBarWidth, 0),
            VerticalAlignment = VerticalAlignment.Top, Child = toolbar,
            RenderTransform = new TranslateTransform(),
        };
        if (toolbar is null) intro.Margin = new Thickness(0, 0, 0, 18);   // no toolbar: the list follows the intro
        var content = new StackPanel { Children = { titleRow, intro, spacer, body } };
        var scroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(32, 26, 32, 40), Child = content },
        };

        void Place()
        {
            spacer.Height = pinned.Bounds.Height;
            // The spacer's top in the viewport: its place in the content (under the 26 px top
            // padding) less how far the content has scrolled.
            double top = spacer.Bounds.Y + 26 - scroll.Offset.Y;
            ((TranslateTransform)pinned.RenderTransform!).Y = Math.Max(0, top);
            pinned.BorderBrush = top < 0 ? Hairline : Brushes.Transparent;
        }
        if (toolbar is null) return scroll;
        scroll.ScrollChanged += (_, _) => Place();
        pinned.SizeChanged += (_, _) => Place();
        content.LayoutUpdated += (_, _) => Place();

        return new Panel { Children = { scroll, pinned } };
    }

    // ScrollBar:vertical width in NotaTheme.axaml: the overlay stops short of it.
    private const double ScrollBarWidth = 9;
}
