// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The "History" browser tab: the open project's versions as a tree, newest first, drawn
// the way `git log --graph` draws branches (HistoryGraph lays it out). The current version
// carries a brass dot. Selecting a version shows its details in the footer with Switch and
// Open as copy; double-click switches; the context menu also renames, notes, stars and
// deletes. Rebuilds whenever the window reports the history changed.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

/// <summary>What the History tab needs from the window that owns the project.</summary>
internal interface IHistoryHost
{
    /// <summary>The open bundle, or null while the project has never been saved.</summary>
    string? ProjectPath { get; }
    bool KeepVersionHistory { get; }
    Task<bool> SwitchToVersionAsync(string versionId);
    Task<bool> OpenVersionAsCopyAsync(string versionId);
    /// <summary>Show a one-line result on the window's status bar.</summary>
    void ReportStatus(string text);
}

internal sealed class HistoryView : UserControl
{
    private const double RowH = 26;
    private const double LaneW = 12;
    private const string IconStar = "M12 3 L14.5 9 L21 9.3 L16 13.5 L17.7 20 L12 16.2 L6.3 20 L8 13.5 L3 9.3 L9.5 9 Z";

    private readonly IProjectHistory _history;
    private readonly IHistoryHost _host;
    private readonly ListBox _list = new() { Classes = { "browser" }, Padding = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _summary = new() { Classes = { "Mono" }, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _empty = new()
    {
        FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 220, Margin = new Thickness(20, 0),
    };
    private readonly Border _details = new() { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 8, 10, 10) };
    private ProjectHistoryState _state = ProjectHistoryState.Empty;
    private Dictionary<string, int> _numbers = new();   // version id -> 1-based number in save order
    private string? _dir;

    public HistoryView(IProjectHistory history, IHistoryHost host)
    {
        _history = history;
        _host = host;

        var caption = new TextBlock { Text = "VERSIONS", Classes = { "GroupLabel" }, VerticalAlignment = VerticalAlignment.Center };
        _summary.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = RowH, Margin = new Thickness(10, 8, 10, 4) };
        header.Children.Add(caption);
        Grid.SetColumn(_summary, 1);
        header.Children.Add(_summary);

        _empty.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        _details.BindResource(Border.BackgroundProperty, "Brush.Panel");
        _details.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        _details.IsVisible = false;

        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<HistoryRow>((row, _) => row is null ? new Control() : Row(row), supportsRecycling: false);
        _list.SelectionChanged += (_, _) => ShowDetails(_list.SelectedItem as HistoryRow);
        _list.DoubleTapped += (_, _) => { if (_list.SelectedItem is HistoryRow r && !r.IsHead) _ = Switch(r.Version); };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _list.SelectedItem is HistoryRow { IsHead: false } r) { _ = Switch(r.Version); e.Handled = true; }
        };
        _list.ContextRequested += (_, e) =>
        {
            if ((e.Source as StyledElement)?.DataContext is not HistoryRow row) return;
            _list.SelectedItem = row;
            BuildMenu(row).ShowAt(_list, showAtPointer: true);
            e.Handled = true;
        };

        var body = new Grid();
        body.Children.Add(_list);
        body.Children.Add(_empty);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_details, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(_details);
        root.Children.Add(body);
        Content = root;
    }

    /// <summary>Re-reads the open project's history (after a save, switch, open or new).</summary>
    public void Refresh()
    {
        string? keep = (_list.SelectedItem as HistoryRow)?.Version.Id;
        _dir = _host.ProjectPath;
        string? problem = null;
        _state = ProjectHistoryState.Empty;
        if (_dir is not null)
        {
            try { _state = _history.Read(_dir); }
            catch (ProjectHistoryException ex) { problem = ex.Message; }
        }

        _numbers = _state.Versions.Select((v, i) => (v.Id, i)).ToDictionary(x => x.Id, x => x.i + 1);
        var rows = HistoryGraph.Layout(_state);
        _list.ItemsSource = rows;
        _list.SelectedItem = rows.FirstOrDefault(r => r.Version.Id == keep);
        ShowDetails(_list.SelectedItem as HistoryRow);

        _empty.Text = problem
            ?? (_dir is null ? "Save the project to start its history."
              : !_host.KeepVersionHistory && rows.Count == 0 ? "Version history is turned off in Settings."
              : rows.Count == 0 ? "Each save adds a version here." : "");
        _empty.IsVisible = _empty.Text.Length > 0;
        ToolTip.SetTip(_empty, problem is null ? null : "Its versions can't be listed until the file .history/versions.json is repaired.");

        if (rows.Count == 0 || _dir is null) _summary.Text = "";
        else
        {
            long size = 0;
            try { size = _history.Size(_dir).HistoryOnly; } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            _summary.Text = $"{rows.Count} · {NotaNum.Bytes(size)}";
            ToolTip.SetTip(_summary, $"{rows.Count} version{(rows.Count == 1 ? "" : "s")} · older versions take {NotaNum.Bytes(size)} on disk");
        }
    }

    // --- rows ----------------------------------------------------------------

    private Control Row(HistoryRow row)
    {
        var v = row.Version;
        var edge = new Border { Classes = { "RowEdge" } };
        var lanes = new LaneCell(row) { Width = 10 + row.LaneCount * LaneW, Height = RowH };

        var name = new TextBlock { Text = Title(v), Classes = { "RowName" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (v.Label is null) name.Classes.Add("child");   // an unnamed version reads quieter
        var when = new TextBlock { Text = ShortTime(v.CreatedAt), Classes = { "RowTag" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 9, 0) };

        var grid = new Grid { Height = RowH, ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
        grid.Children.Add(edge);
        Grid.SetColumn(lanes, 1);
        grid.Children.Add(lanes);
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        if (v.Starred)
        {
            var star = new Path { Data = Geometry.Parse(IconStar), Stretch = Stretch.Uniform, Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            star.BindResource(Shape.FillProperty, "Brush.Accent");
            Grid.SetColumn(star, 3);
            grid.Children.Add(star);
        }
        Grid.SetColumn(when, 4);
        grid.Children.Add(when);
        ToolTip.SetTip(grid, v.Note is { Length: > 0 } note ? $"{Title(v)} · {note}" : Title(v));
        return grid;
    }

    /// <summary>Draws one row's slice of the tree: lanes passing by, branches joining in,
    /// and the version's dot (brass for the current one).</summary>
    private sealed class LaneCell(HistoryRow row) : Control
    {
        public override void Render(DrawingContext ctx)
        {
            double h = Bounds.Height, cy = h / 2;
            double X(int lane) => 6 + lane * LaneW + LaneW / 2;
            var pen = new Pen(NotaPalette.TextDisabled, 1.5, lineCap: PenLineCap.Round);
            foreach (int l in row.Through) ctx.DrawLine(pen, new Point(X(l), 0), new Point(X(l), h));
            double x = X(row.Lane);
            if (row.FromAbove) ctx.DrawLine(pen, new Point(x, 0), new Point(x, cy));
            if (row.ToBelow) ctx.DrawLine(pen, new Point(x, cy), new Point(x, h));
            foreach (int j in row.Joins)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(X(j), 0), false);
                    c.QuadraticBezierTo(new Point(X(j), cy), new Point(x, cy));
                }
                ctx.DrawGeometry(null, pen, g);
            }
            if (row.IsHead) ctx.DrawEllipse(NotaPalette.Accent, null, new Point(x, cy), 4, 4);
            else ctx.DrawEllipse(NotaPalette.SurfaceCard, new Pen(NotaPalette.TextMuted, 1.5), new Point(x, cy), 3.25, 3.25);
        }
    }

    // --- details footer ------------------------------------------------------

    private void ShowDetails(HistoryRow? row)
    {
        _details.IsVisible = row is not null;
        if (row is null) { _details.Child = null; return; }
        var v = row.Version;

        var name = new TextBlock { Text = Title(v), Classes = { "Name" }, TextTrimming = TextTrimming.CharacterEllipsis };
        name.BindResource(TextBlock.ForegroundProperty, "Brush.TextPrimary");
        var meta = new List<string> { LongTime(v.CreatedAt) };
        if (v.AppVersion.Length > 0) meta.Add("Nota " + v.AppVersion);
        if (v.AddedBytes > 0) meta.Add("+" + NotaNum.Bytes(v.AddedBytes));
        var metaText = new TextBlock { Text = string.Join(" · ", meta), Classes = { "Value" }, TextWrapping = TextWrapping.Wrap };
        metaText.BindResource(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var stack = new StackPanel { Spacing = 4, Children = { name, metaText } };
        if (v.Note is { Length: > 0 } note && note != Title(v))   // the title already shows a one-line note
            stack.Children.Add(new TextBlock { Text = note, Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap, MaxHeight = 64 });
        if (!v.CanOpen)
            stack.Children.Add(new TextBlock { Text = "Saved by a newer Nota — update to open it.", Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap });

        var switchBtn = new Button
        {
            Content = ButtonText(row.IsHead ? "Current" : "Switch"),
            Classes = { "primary" },
            IsEnabled = !row.IsHead && v.CanOpen,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        if (row.IsHead) switchBtn.Classes.Remove("primary");
        ToolTip.SetTip(switchBtn, row.IsHead ? "The project is at this version" : "Switch the project to this version");
        switchBtn.Click += (_, _) => _ = Switch(v);
        var copyBtn = new Button
        {
            Content = ButtonText("Open copy"),
            IsEnabled = v.CanOpen,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(copyBtn, "Open this version as a new project, keeping this one as it is");
        copyBtn.Click += (_, _) => _ = _host.OpenVersionAsCopyAsync(v.Id);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*"), Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(switchBtn);
        Grid.SetColumn(copyBtn, 2);
        buttons.Children.Add(copyBtn);
        stack.Children.Add(buttons);
        _details.Child = stack;
    }

    // Button captions trim instead of the button clipping when the browser is narrow.
    private static TextBlock ButtonText(string text) => new() { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };

    // --- actions -------------------------------------------------------------

    private async Task Switch(ProjectVersion v)
    {
        if (!v.CanOpen) return;
        await _host.SwitchToVersionAsync(v.Id);
    }

    private MenuFlyout BuildMenu(HistoryRow row)
    {
        var v = row.Version;
        var menu = new MenuFlyout();
        MenuItem Item(string header, Action onClick, bool enabled = true, GlyphKind? icon = null)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            if (icon is { } k) mi.Icon = MenuKit.Icon(k);
            mi.Click += (_, _) => onClick();
            menu.Items.Add(mi);
            return mi;
        }

        Item("Switch to this version", () => _ = Switch(v), !row.IsHead && v.CanOpen, GlyphKind.Arrow);
        Item("Open as copy…", () => _ = _host.OpenVersionAsCopyAsync(v.Id), v.CanOpen, GlyphKind.Duplicate);
        menu.Items.Add(new Separator());
        Item(v.Label is null ? "Name…" : "Rename…", () => _ = Prompt("Name version", "Version name", v.Label, t => _history.SetLabel(_dir!, v.Id, t)), icon: GlyphKind.Edit);
        if (v.Label is not null) Item("Remove name", () => Apply(() => _history.SetLabel(_dir!, v.Id, null)));
        Item(v.Note is null ? "Add note…" : "Edit note…", () => _ = Prompt("Version note", "What changed in this version?", v.Note, t => _history.SetNote(_dir!, v.Id, t)), icon: GlyphKind.Note);
        if (v.Note is not null) Item("Remove note", () => Apply(() => _history.SetNote(_dir!, v.Id, null)));
        Item(v.Starred ? "Remove star" : "Star", () => Apply(() => _history.SetStarred(_dir!, v.Id, !v.Starred)));
        menu.Items.Add(new Separator());
        var del = Item("Delete version…", () => _ = Delete(v), !row.IsHead, GlyphKind.Trash);
        if (row.IsHead) ToolTip.SetTip(del, "Switch to another version to delete this one");
        return menu;
    }

    private async Task Prompt(string title, string prompt, string? initial, Action<string> apply)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var text = await new TextPromptWindow(title, prompt, initial ?? "").ShowDialog<string?>(owner);
        if (text is not null) Apply(() => apply(text));
    }

    private async Task Delete(ProjectVersion v)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || _dir is null) return;
        bool ok = await new ConfirmWindow("Delete version",
            $"Delete {Title(v)}? Audio that only this version uses is deleted from the project folder too. This can't be undone.",
            "Delete", "Cancel").ShowDialog<bool>(owner);
        if (ok) Apply(() => _history.Delete(_dir, v.Id));
    }

    // Runs a history edit, reporting a failure on the status line, then rebuilds.
    private void Apply(Action edit)
    {
        if (_dir is null) return;
        try { edit(); }
        catch (Exception ex) when (ex is ProjectHistoryException or System.IO.IOException or UnauthorizedAccessException)
        {
            _host.ReportStatus($"Couldn't change the version: {ex.Message}");
        }
        Refresh();
    }

    // --- text ----------------------------------------------------------------

    // The row's name: the label, else the note's first line, else its number in save order.
    private string Title(ProjectVersion v)
        => v.Label ?? v.Note?.Split('\n')[0] ?? (_numbers.TryGetValue(v.Id, out int n) ? $"Version {n}" : "Version");

    // "14:32" today, "1 Oct" this year, "1 Oct 2025" before.
    private static string ShortTime(DateTimeOffset t)
    {
        var local = t.LocalDateTime;
        var now = DateTime.Now;
        string f = local.Date == now.Date ? "HH:mm" : local.Year == now.Year ? "d MMM" : "d MMM yyyy";
        return local.ToString(f, NotaNum.Culture);
    }

    private static string LongTime(DateTimeOffset t)
    {
        var local = t.LocalDateTime;
        return local.ToString(local.Year == DateTime.Now.Year ? "d MMM, HH:mm" : "d MMM yyyy, HH:mm", NotaNum.Culture);
    }
}
