// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's Projects tab rows and order (spec 092, #582): newest first by default, a
// Date | Name switch, and a two-line row — the name (with a dot on the open project), then
// the save date, tempo, key, length and track count in muted ink as they are read.

using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class BrowserView
{
    private const int ProjectsTab = 5;
    private Border _projectsBar = null!;
    private Border _projectsSortSeg = null!;

    private Control BuildProjectsBar()
    {
        _projectsSortSeg = SessionView.Segmented(new[] { Loc.T("browser.sort_date"), Loc.T("browser.sort_name") }, 0,
            i => { if (_vm is not null) _vm.ProjectsSort = i == 1 ? ProjectsSortOrder.Name : ProjectsSortOrder.Date; });
        _projectsSortSeg.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(_projectsSortSeg, Loc.T("browser.sort_projects"));
        _projectsBar = new Border { Margin = new Thickness(8, 0, 8, 6), Child = _projectsSortSeg, IsVisible = false };
        return _projectsBar;
    }

    private void HookProjects(BrowserViewModel vm)
    {
        if (_projectsSortSeg.Child is Avalonia.Controls.Primitives.UniformGrid g)
            for (int i = 0; i < g.Children.Count; i++)
                if (g.Children[i] is Avalonia.Controls.Primitives.ToggleButton tb) tb.IsChecked = (i == 1) == (vm.ProjectsSort == ProjectsSortOrder.Name);
    }

    private void UpdateProjectsBar()
    {
        bool on = _active == ProjectsTab && _vm is not null;
        _projectsBar.IsVisible = on;
        if (on) _vm!.RebuildProjects();   // saved since last time: newest first again
    }

    // Line 1: name (+ the open project's dot). Line 2: "11/12/2026 14:20 · 124 BPM · Am · 3:42 · 12 tracks".
    private Control ProjectRow(BrowserItem item)
    {
        var edge = new Border { Classes = { "RowEdge" } };
        var name = new TextBlock { Text = item.Name, Classes = { "RowName" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = item.IsOpen };
        dot.BindResource(Shape.FillProperty, "Brush.Accent");
        ToolTip.SetTip(dot, Loc.T("browser.project_open"));
        var line1 = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(dot, Dock.Right);
        line1.Children.Add(dot);
        line1.Children.Add(name);

        var details = new TextBlock { Classes = { "RowTag" }, TextTrimming = TextTrimming.CharacterEllipsis, Text = ProjectDetails(item) };
        var stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 9, 0), Children = { line1, details } };
        var grid = new Grid { Height = 40, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Background = Brushes.Transparent };
        grid.Children.Add(edge);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        void Changed(object? _, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BrowserItem.Summary)) details.Text = ProjectDetails(item);
            else if (e.PropertyName == nameof(BrowserItem.IsOpen)) dot.IsVisible = item.IsOpen;
        }
        grid.AttachedToVisualTree += (_, _) => { item.PropertyChanged -= Changed; item.PropertyChanged += Changed; details.Text = ProjectDetails(item); dot.IsVisible = item.IsOpen; };
        grid.DetachedFromVisualTree += (_, _) => item.PropertyChanged -= Changed;
        ToolTip.SetTip(grid, item.Path);
        return grid;
    }

    private static string ProjectDetails(BrowserItem item)
    {
        var parts = new List<string>();
        if (item.Sub.Length > 0) parts.Add(item.Sub);
        if (item.Summary is { } s)
        {
            parts.Add(Loc.F("browser.bpm", s.Bpm.ToString("0.##", NotaNum.Culture)));
            if (!string.IsNullOrEmpty(s.Key)) parts.Add(s.Key!);
            if (s.LengthBeats > 0) parts.Add(s.LengthText);
            parts.Add(Loc.Plural("browser.project_tracks", s.TrackCount));
        }
        return string.Join(" · ", parts);
    }
}
