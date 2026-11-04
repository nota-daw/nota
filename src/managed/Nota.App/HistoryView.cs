// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The "History" browser tab: the open project's versions as a tree, newest first, drawn
// the way `git log --graph` draws branches (HistoryGraph lays it out). The current version
// carries a brass dot. Selecting a version shows its details in the footer with Switch and
// Open as copy; double-click switches; the context menu also renames, notes, stars and
// deletes. ⌘/Ctrl-click and Shift-click select several versions, which the footer and the
// context menu then delete together (#228). Rebuilds whenever the window reports the
// history changed.

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
using Nota.Application.Localization;

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

    private readonly IProjectHistory _history;
    private readonly IHistoryHost _host;
    // Multiple: a click selects one version, ⌘/Ctrl-click adds or removes one, Shift-click a run.
    private readonly ListBox _list = new() { Classes = { "browser" }, Padding = new Thickness(0, 0, 0, 6), SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _summary = new() { Classes = { "Mono" }, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _empty = new()
    {
        FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 220, Margin = new Thickness(20, 0),
    };
    private readonly Border _details = new() { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 8, 10, 10) };
    private readonly Button _options = new()
    {
        Classes = { "ghost" }, Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0),
        Content = new Glyph(GlyphKind.Options, 16),
    };
    // Starred only (#226): the same toggle the other browser tabs have for favourites.
    private readonly Button _starredOnly = new()
    {
        Classes = { "ghost" }, Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0),
        Content = new Glyph(GlyphKind.Star, 14),
    };
    private bool _onlyStarred;
    private ProjectHistoryState _state = ProjectHistoryState.Empty;
    private Dictionary<string, int> _numbers = new();   // version id -> 1-based number in save order
    private string? _dir;

    public HistoryView(IProjectHistory history, IHistoryHost host)
    {
        _history = history;
        _host = host;

        var caption = new TextBlock { Text = Loc.T("history.versions"), Classes = { "GroupLabel" }, VerticalAlignment = VerticalAlignment.Center };
        _summary.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Height = RowH, Margin = new Thickness(10, 8, 6, 4) };
        header.Children.Add(caption);
        Grid.SetColumn(_summary, 1);
        header.Children.Add(_summary);
        Grid.SetColumn(_starredOnly, 2);
        header.Children.Add(_starredOnly);
        Grid.SetColumn(_options, 3);
        header.Children.Add(_options);
        _starredOnly.Click += (_, _) => { _onlyStarred = !_onlyStarred; Refresh(); };
        PaintStarredOnly();
        _options.Click += (_, _) => BuildOptionsMenu().ShowAt(_options);
        ((Glyph)_options.Content!).BindResource(Glyph.ForegroundProperty, "Brush.TextTertiary");
        ToolTip.SetTip(_options, Loc.T("history.clean_up_the_history"));

        _empty.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        _details.BindResource(Border.BackgroundProperty, "Brush.Panel");
        _details.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        _details.IsVisible = false;

        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<HistoryRow>((row, _) => row is null ? new Control() : Row(row), supportsRecycling: false);
        _list.SelectionChanged += (_, _) => ShowSelection();
        _list.DoubleTapped += (_, _) => { if (Single() is { IsHead: false } r) _ = Switch(r.Version); };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Single() is { IsHead: false } r) { _ = Switch(r.Version); e.Handled = true; }
        };
        _list.ContextRequested += (_, e) =>
        {
            if ((e.Source as StyledElement)?.DataContext is not HistoryRow row) return;
            // A right-click inside a multiple selection acts on all of it; anywhere else it
            // selects the row under the pointer, as before.
            var selected = Selected();
            if (selected.Count > 1 && selected.Contains(row)) BuildManyMenu(selected).ShowAt(_list, showAtPointer: true);
            else
            {
                _list.SelectedItem = row;
                BuildMenu(row).ShowAt(_list, showAtPointer: true);
            }
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
        var keep = Selected().Select(r => r.Version.Id).ToHashSet();
        _dir = _host.ProjectPath;
        string? problem = null;
        _state = ProjectHistoryState.Empty;
        if (_dir is not null)
        {
            try { _state = _history.Read(_dir); }
            catch (ProjectHistoryException ex) { problem = ex.Message; }
        }

        _numbers = _state.Versions.Select((v, i) => (v.Id, i)).ToDictionary(x => x.Id, x => x.i + 1);
        var all = HistoryGraph.Layout(_state);
        // Starred only drops the tree: the versions between two starred ones are hidden, so
        // the lanes would join nothing. Each starred version is a plain dot, newest first.
        IReadOnlyList<HistoryRow> rows = _onlyStarred
            ? all.Where(r => r.Version.Starred)
                 .Select(r => r with { Lane = 0, Through = [], Joins = [], FromAbove = false, ToBelow = false, LaneCount = 1 })
                 .ToList()
            : all;
        _list.ItemsSource = rows;
        for (int i = 0; i < rows.Count; i++)
            if (keep.Contains(rows[i].Version.Id)) _list.Selection.Select(i);
        ShowSelection();

        _empty.Text = problem
            ?? (_dir is null ? Loc.T("history.save_the_project_to_start")
              : !_host.KeepVersionHistory && rows.Count == 0 ? Loc.T("history.version_history_is_turned_off")
              : all.Count == 0 ? Loc.T("history.each_save_adds_a_version")
              : rows.Count == 0 ? Loc.T("history.no_starred_versions_yet_right") : "");
        _empty.IsVisible = _empty.Text.Length > 0;
        ToolTip.SetTip(_empty, problem is null ? null : Loc.T("history.its_versions_cant_be_listed"));

        _options.IsVisible = all.Count > 1;
        _starredOnly.IsVisible = all.Count > 0;
        PaintStarredOnly();
        if (all.Count == 0 || _dir is null) _summary.Text = "";
        else
        {
            long size = 0;
            try { size = _history.Size(_dir).HistoryOnly; } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            _summary.Text = $"{all.Count} · {NotaNum.Bytes(size)}";
            ToolTip.SetTip(_summary, Loc.F("history.older_versions_take_on_disk", Loc.Plural("history.versions_n", all.Count), NotaNum.Bytes(size)));
        }
    }

    // Lit (accent star) while only starred versions are listed.
    private void PaintStarredOnly()
    {
        var g = (Glyph)_starredOnly.Content!;
        g.Kind = _onlyStarred ? GlyphKind.StarFilled : GlyphKind.Star;
        g.BindResource(Glyph.ForegroundProperty, _onlyStarred ? "Brush.Accent" : "Brush.TextTertiary");
        ToolTip.SetTip(_starredOnly, _onlyStarred ? Loc.T("history.show_all_versions") : Loc.T("history.show_starred_versions_only"));
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
            var star = new Glyph(GlyphKind.StarFilled, 12) { Margin = new Thickness(6, 0, 0, 0) };
            star.BindResource(Glyph.ForegroundProperty, "Brush.Accent");
            Grid.SetColumn(star, 3);
            grid.Children.Add(star);
        }
        Grid.SetColumn(when, 4);
        grid.Children.Add(when);
        var tip = new List<string> { Title(v) };
        if (Changes(v) is { Length: > 0 } changes && changes != Title(v)) tip.Add(changes);
        if (v.Note is { Length: > 0 } note && note != Title(v)) tip.Add(note);
        ToolTip.SetTip(grid, string.Join("\n", tip));
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

    // --- selection -----------------------------------------------------------

    /// <summary>The selected rows, newest first (list order).</summary>
    private List<HistoryRow> Selected()
    {
        var rows = _list.ItemsSource as IReadOnlyList<HistoryRow> ?? [];
        return _list.Selection.SelectedIndexes.Where(i => i >= 0 && i < rows.Count).Order().Select(i => rows[i]).ToList();
    }

    /// <summary>The selected row when exactly one is selected.</summary>
    private HistoryRow? Single()
    {
        var selected = Selected();
        return selected.Count == 1 ? selected[0] : null;
    }

    private void ShowSelection()
    {
        var selected = Selected();
        if (selected.Count > 1) ShowMany(selected);
        else ShowDetails(selected.Count == 1 ? selected[0] : null);
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
        if (v.AppVersion.Length > 0) meta.Add(Loc.F("history.nota", v.AppVersion));
        if (v.AddedBytes > 0) meta.Add("+" + NotaNum.Bytes(v.AddedBytes));
        var metaText = new TextBlock { Text = string.Join(" · ", meta), Classes = { "Value" }, TextWrapping = TextWrapping.Wrap };
        metaText.BindResource(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var stack = new StackPanel { Spacing = 4, Children = { name, metaText } };
        if (Changes(v) is { Length: > 0 } changes && changes != Title(v))
        {
            var what = new TextBlock { Text = changes, Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap, MaxHeight = 48 };
            what.BindResource(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            stack.Children.Add(what);
        }
        if (v.Note is { Length: > 0 } note && note != Title(v))   // the title already shows a one-line note
            stack.Children.Add(new TextBlock { Text = note, Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap, MaxHeight = 64 });
        if (!v.CanOpen)
            stack.Children.Add(new TextBlock { Text = Loc.T("history.saved_by_a_newer_nota"), Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap });

        var switchBtn = new Button
        {
            Content = ButtonText(row.IsHead ? Loc.T("history.current") : Loc.T("history.switch")),
            Classes = { "primary" },
            IsEnabled = !row.IsHead && v.CanOpen,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        if (row.IsHead) switchBtn.Classes.Remove("primary");
        ToolTip.SetTip(switchBtn, row.IsHead ? Loc.T("history.the_project_is_at_this") : Loc.T("history.switch_the_project_to_this"));
        switchBtn.Click += (_, _) => _ = Switch(v);
        var copyBtn = new Button
        {
            Content = ButtonText(Loc.T("history.open_copy")),
            IsEnabled = v.CanOpen,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(copyBtn, Loc.T("history.open_this_version_as_a"));
        copyBtn.Click += (_, _) => _ = _host.OpenVersionAsCopyAsync(v.Id);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*"), Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(switchBtn);
        Grid.SetColumn(copyBtn, 2);
        buttons.Children.Add(copyBtn);
        stack.Children.Add(buttons);
        _details.Child = stack;
    }

    // Several versions selected: how many, when they were saved, and one button that deletes
    // them. The current version is never deleted, so a selection holding it says it stays.
    private void ShowMany(IReadOnlyList<HistoryRow> rows)
    {
        _details.IsVisible = true;
        var deletable = Deletable(rows);

        var name = new TextBlock { Text = Loc.Plural("history.versions_n", rows.Count), Classes = { "Name" }, TextTrimming = TextTrimming.CharacterEllipsis };
        name.BindResource(TextBlock.ForegroundProperty, "Brush.TextPrimary");
        string first = LongTime(rows.Min(r => r.Version.CreatedAt)), last = LongTime(rows.Max(r => r.Version.CreatedAt));
        var metaText = new TextBlock { Text = first == last ? first : $"{first} – {last}", Classes = { "Value" }, TextWrapping = TextWrapping.Wrap };
        metaText.BindResource(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        var stack = new StackPanel { Spacing = 4, Children = { name, metaText } };
        if (deletable.Count < rows.Count)
            stack.Children.Add(new TextBlock { Text = Loc.T("history.the_current_version_stays_2"), Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap });

        var deleteBtn = new Button
        {
            Content = ButtonText(DeleteManyLabel(deletable.Count)),
            IsEnabled = deletable.Count > 0,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        ToolTip.SetTip(deleteBtn, Loc.T("history.delete_the_selected_versions_and"));
        deleteBtn.Click += (_, _) => _ = DeleteSelected(deletable);
        stack.Children.Add(deleteBtn);
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

        menu.Items.Add(MenuKit.Header(v.Label ?? LongTime(v.CreatedAt), null, row.IsHead ? Loc.T("history.current_version") : Loc.T("history.version")));
        menu.Items.Add(new Separator());
        Item(Loc.T("history.switch_to_this_version"), () => _ = Switch(v), !row.IsHead && v.CanOpen, GlyphKind.Arrow);
        Item(Loc.T("history.open_as_copy"), () => _ = _host.OpenVersionAsCopyAsync(v.Id), v.CanOpen, GlyphKind.Duplicate);
        menu.Items.Add(new Separator());
        Item(v.Label is null ? Loc.T("history.name") : Loc.T("common.rename"), () => _ = Prompt(Loc.T("history.name_version"), Loc.T("history.version_name"), v.Label, t => _history.SetLabel(_dir!, v.Id, t)), icon: GlyphKind.Edit);
        if (v.Label is not null) Item(Loc.T("history.remove_name"), () => Apply(() => _history.SetLabel(_dir!, v.Id, null)));
        Item(v.Note is null ? Loc.T("history.add_note") : Loc.T("history.edit_note"), () => _ = Prompt(Loc.T("history.version_note"), Loc.T("history.what_changed_in_this_version"), v.Note, t => _history.SetNote(_dir!, v.Id, t)), icon: GlyphKind.Note);
        if (v.Note is not null) Item(Loc.T("history.remove_note"), () => Apply(() => _history.SetNote(_dir!, v.Id, null)));
        Item(v.Starred ? Loc.T("history.remove_star") : Loc.T("history.star"), () => Apply(() => _history.SetStarred(_dir!, v.Id, !v.Starred)));
        menu.Items.Add(new Separator());
        var del = Item(Loc.T("history.delete_version_2"), () => _ = Delete(v), !row.IsHead, GlyphKind.Trash);
        del.Classes.Add("danger");
        if (row.IsHead) ToolTip.SetTip(del, Loc.T("history.switch_to_another_version_to"));
        return menu;
    }

    // The context menu of a multiple selection: delete them all (the current version stays).
    private MenuFlyout BuildManyMenu(IReadOnlyList<HistoryRow> rows)
    {
        var deletable = Deletable(rows);
        var menu = new MenuFlyout();
        menu.Items.Add(MenuKit.Header(Loc.Plural("history.versions_n", rows.Count), null, ""));
        menu.Items.Add(new Separator());
        var del = new MenuItem { Header = DeleteManyLabel(deletable.Count), Icon = MenuKit.Icon(GlyphKind.Trash), IsEnabled = deletable.Count > 0 };
        del.Classes.Add("danger");
        if (deletable.Count < rows.Count) ToolTip.SetTip(del, Loc.T("history.the_current_version_stays"));
        del.Click += (_, _) => _ = DeleteSelected(deletable);
        menu.Items.Add(del);
        return menu;
    }

    // Every selected version but the current one, which can't be deleted.
    private static List<ProjectVersion> Deletable(IEnumerable<HistoryRow> rows)
        => rows.Where(r => !r.IsHead).Select(r => r.Version).ToList();

    private static string DeleteManyLabel(int count) => count == 1 ? Loc.T("history.delete_version_2") : Loc.Plural("history.delete_versions_n", count);

    private MenuFlyout BuildOptionsMenu()
    {
        var menu = new MenuFlyout();
        var unnamed = UnnamedVersions();
        var clean = new MenuItem
        {
            Header = unnamed.Count == 0 ? Loc.T("history.delete_unnamed_versions_2") : Loc.Plural("history.delete_unnamed_versions_n", unnamed.Count),
            Icon = MenuKit.Icon(GlyphKind.Trash),
            IsEnabled = unnamed.Count > 0,
        };
        clean.Classes.Add("danger");
        clean.Click += (_, _) => _ = DeleteUnnamed(unnamed);
        menu.Items.Add(clean);
        return menu;
    }

    // Versions nobody marked as worth keeping: not current, unnamed, unstarred, without a note,
    // and not the latest version of a branch (that would lose the branch's last state).
    private List<ProjectVersion> UnnamedVersions()
    {
        var parents = _state.Versions.Select(v => v.Parent).OfType<string>().ToHashSet();
        return _state.Versions
            .Where(v => v.Id != _state.Head && v.Label is null && !v.Starred && v.Note is null && parents.Contains(v.Id))
            .ToList();
    }

    private async Task DeleteUnnamed(IReadOnlyList<ProjectVersion> versions)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || _dir is null || versions.Count == 0) return;
        bool ok = await new ConfirmWindow(Loc.T("history.delete_unnamed_versions"),
            Loc.Plural("history.delete_versions_and_the_audio", versions.Count),
            Loc.T("common.delete"), Loc.T("common.cancel")).ShowDialog<bool>(owner);
        if (!ok) return;
        long freed = 0;
        Apply(() => freed = _history.DeleteMany(_dir, versions.Select(v => v.Id).ToList()));
        _host.ReportStatus(Loc.F("history.freed", Loc.Plural("history.deleted_versions", versions.Count), NotaNum.Bytes(freed)));
    }

    private async Task DeleteSelected(IReadOnlyList<ProjectVersion> versions)
    {
        if (versions.Count == 1) { await Delete(versions[0]); return; }
        if (TopLevel.GetTopLevel(this) is not Window owner || _dir is null || versions.Count == 0) return;
        bool ok = await new ConfirmWindow(Loc.T("history.delete_versions"),
            Loc.Plural("history.delete_versions_audio_that_only", versions.Count),
            Loc.T("common.delete"), Loc.T("common.cancel")).ShowDialog<bool>(owner);
        if (!ok) return;
        long freed = 0;
        Apply(() => freed = _history.DeleteMany(_dir, versions.Select(v => v.Id).ToList()));
        _host.ReportStatus(Loc.F("history.freed", Loc.Plural("history.deleted_versions", versions.Count), NotaNum.Bytes(freed)));
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
        bool ok = await new ConfirmWindow(Loc.T("history.delete_version"),
            Loc.F("history.delete_audio_that_only_this", Title(v)),
            Loc.T("common.delete"), Loc.T("common.cancel")).ShowDialog<bool>(owner);
        if (ok) Apply(() => _history.Delete(_dir, v.Id));
    }

    // Runs a history edit, reporting a failure on the status line, then rebuilds.
    private void Apply(Action edit)
    {
        if (_dir is null) return;
        try { edit(); }
        catch (Exception ex) when (ex is ProjectHistoryException or System.IO.IOException or UnauthorizedAccessException)
        {
            _host.ReportStatus(Loc.F("history.couldnt_change_the_version", ex.Message));
        }
        Refresh();
    }

    // --- text ----------------------------------------------------------------

    // The row's name: the label, else the note's first line, else what changed, else its
    // number in save order.
    private string Title(ProjectVersion v)
        => v.Label ?? v.Note?.Split('\n')[0] ?? VersionSummary.Headline(v.Changes, NotaNum.Bpm)
           ?? (_numbers.TryGetValue(v.Id, out int n) ? Loc.F("common.version", n) : Loc.T("history.version"));

    // Everything the version changed, as one line ("" when unknown or nothing nameable).
    private static string Changes(ProjectVersion v)
        => v.Changes is null ? "" : string.Join(" · ", VersionSummary.Parts(v.Changes, NotaNum.Bpm));

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
