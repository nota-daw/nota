// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The controller mapping editor (specs/110, #973): for a controller that already has a profile,
// Set up opens on that profile instead of the step-by-step wizard. Every function the wizard knows
// is listed by group with the control it is on; click one (or Enter), press or move a control and
// it is learned the wizard's way. A control another function has is named and moved only when the
// user says so; a function can be cleared or put back; any palette command can go on a spare button
// (+ Command…); moving a control while nothing listens shows what it does. Finish writes only the
// edited entries back into the profile — a shipped one becomes the user's own copy with the same id,
// which Back to shipped profile removes again — and Learn everything again runs the wizard
// pre-filled with the current controls. The port is captured while the window is open (as in the
// wizard), so nothing plays or fires.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.Application.Controllers;
using Nota.Application.Localization;

namespace Nota.App.Controllers;

internal sealed class ControllerMappingWindow : NotaWindow
{
    // One line of the list: a wizard function, a command put on a button, or a read-only control
    // the profile has that is none of those ("Other").
    private sealed class Row
    {
        public SetupFunction? F { get; init; }
        public MappingEntry? Other { get; init; }
        public bool Added { get; init; }   // a + Command… row added in this window
        public bool Custom { get; init; }  // a command row the wizard doesn't list (added here or in the profile)
        public SetupGroup? Group => F?.Group;
        public IEnumerable<FunctionKey> Keys => F?.Keys ?? Array.Empty<FunctionKey>();
        public Border? View { get; set; }
    }

    private sealed record Conflict(FunctionKey Key, FunctionKey? Left, ClassifiedControl Control, IReadOnlyList<MappingEntry> Entries);

    private readonly ControllerService _service;
    private readonly ConnectedController _port;
    private ControllerProfile _profile;
    private ProfileMapping _map;
    private readonly List<Row> _rows = new();
    private Row? _selected, _listening;
    private int? _halfOnly;      // a pair row listening for one half (0 left, 1 right)
    private int _half;           // a pair learned as buttons: 1 = waiting for the second
    private Conflict? _conflict;
    private ClassifiedControl? _ambiguous;
    private bool _unassignedOnly, _closing, _gone;
    private readonly ControlCatcher _catcher = new();
    private readonly DispatcherTimer _poll;
    private long _activityMs, _foundMs;
    private (int, int, int)? _lastFound;
    private IReadOnlyList<MappingEntry>? _beforeListen;
    private readonly Dictionary<int, (int Msb, int Lsb)> _nrpn = new();   // find by touch: the NRPN number each channel selected

    private readonly TextBlock _profileLine = new() { Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _backToShipped = new() { Classes = { "ghost" } };
    private readonly WrapPanel _strip = new() { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
    private readonly StackPanel _list = new() { Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _hearing = new() { Classes = { "Mono" }, FontSize = NotaType.Caption, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
    private readonly Avalonia.Controls.Shapes.Ellipse _activity = new() { Width = 6, Height = 6, Fill = NotaPalette.TextDisabled, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Classes = { "Caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0) };
    private readonly ComboBox _encoder = new() { MinWidth = 200 };

    public ControllerMappingWindow(ControllerService service, ConnectedController port)
    {
        _service = service;
        _port = port;
        _profile = port.Profile!;
        _map = new ProfileMapping(_profile, service.ProfileJson(_profile) ?? "{}");
        Title = Loc.F("controllers.edit.title", port.Name);
        Width = 720;
        Height = 660;
        MinHeight = 420;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        foreach (var e in new[] { EncoderMode.Absolute, EncoderMode.RelativeTwos, EncoderMode.RelativeSignBit, EncoderMode.RelativeOffset })
            _encoder.Items.Add(ControlCatcher.EncoderName(e));

        // Header: what is being edited and where it will be saved; the way back to the shipped
        // profile and to the full walk.
        var name = new TextBlock { Text = port.Name, Classes = { "Heading" }, Foreground = NotaPalette.TextPrimary };
        _backToShipped.Content = Loc.T("controllers.edit.back_to_shipped");
        _backToShipped.Click += async (_, _) => await BackToShipped();
        var again = new Button { Content = Loc.T("controllers.edit.learn_again"), Classes = { "ghost" } };
        ToolTip.SetTip(again, Loc.T("controllers.edit.learn_again_tip"));
        again.Click += async (_, _) => await LearnAgain();
        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Top, Children = { _backToShipped, again } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        header.Children.Add(new StackPanel { Spacing = 4, Children = { name, _profileLine } });
        Grid.SetColumn(headerButtons, 1);
        header.Children.Add(headerButtons);

        // The strip: jump to a group, the "unassigned only" filter, and what the controller sends.
        var filter = new ToggleSwitch(false) { VerticalAlignment = VerticalAlignment.Center };
        filter.Changed += on => { _unassignedOnly = on; StopListening(); Render(); };
        var filterLabel = new TextBlock { Text = Loc.T("controllers.edit.unassigned_only"), Classes = { "Caption" }, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
        filterLabel.PointerPressed += (_, _) => filter.Toggle();
        var hearing = new Border
        {
            Background = NotaPalette.BgSunken, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _activity, _hearing } },
        };
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(filterLabel, 1); Grid.SetColumn(hearing, 3);
        tools.Children.Add(filter); tools.Children.Add(filterLabel); tools.Children.Add(hearing);

        _scroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(12, 4, 12, 12), Child = _list },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var well = new Border { Background = NotaPalette.Well, CornerRadius = NotaRadius.Card, ClipToBounds = true, Child = _scroll };

        var cancel = new Button { Content = Loc.T("common.cancel"), Classes = { "ghost" } };
        cancel.Click += (_, _) => Close();
        var finish = new Button { Content = Loc.T("controllers.setup.finish"), Classes = { "primary" } };
        finish.Click += (_, _) => Finish();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(_status, 1); Grid.SetColumn(finish, 2);
        footer.Children.Add(cancel); footer.Children.Add(_status); footer.Children.Add(finish);

        var top = new StackPanel { Margin = new Thickness(20, 16, 20, 12), Spacing = 12, Children = { header, _strip, tools } };
        var footerBar = new Border { BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 10), Child = footer };
        var body = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(footerBar, Dock.Bottom);
        body.Children.Add(top); body.Children.Add(footerBar);
        body.Children.Add(new Border { Margin = new Thickness(20, 0, 20, 12), Child = well });
        SetBody(body);

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Closing += OnClosing;

        BuildRows();
        Render();
        Hint();

        _hearing.Text = Loc.T("controllers.setup.hearing_none");
        _service.Capture(port.Uid);
        _service.TakeCaptured();
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
        Closed += (_, _) => { _poll.Stop(); _service.Capture(null); };
    }

    // ---- rows -------------------------------------------------------------------------------

    private void BuildRows()
    {
        // Command rows survive a rebuild even once their control is gone, so a cleared one stays "—".
        var added = _rows.Where(r => r.Custom).ToList();
        _rows.Clear();
        var known = new HashSet<FunctionKey>();
        foreach (var f in SetupFunctions.Build())
        {
            _rows.Add(new Row { F = f });
            foreach (var k in f.Keys) known.Add(k);
        }
        // A command the profile puts on a button that the wizard doesn't ask for: a row of its own,
        // in Extra buttons with those added here.
        int extraEnd = _rows.FindLastIndex(r => r.Group == SetupGroup.Extra) + 1;
        var commands = _map.Entries.Where(e => e.Decl?.Role is { Kind: ControlRoleKind.Command, TwoWay: false } && e.Right is { } k && !known.Contains(k))
            .Select(e => e.Right!.Value.Command!).Distinct().ToList();
        foreach (var id in commands.Concat(added.Select(a => a.F!.Command!)).Distinct())
        {
            _rows.Insert(extraEnd++, new Row { F = SetupFunctions.ForCommand(id), Added = added.Any(a => a.Added && a.F!.Command == id), Custom = true });
            known.Add(FunctionKey.OfCommand(id));
        }
        // Everything else the profile has: shown, not edited (pads, extra buttons, more knobs).
        foreach (var e in _map.Entries.Where(e => e.Decl is not null && !(e.Right is { } r && known.Contains(r)) && !(e.Left is { } l && known.Contains(l))))
            _rows.Add(new Row { Other = e });
        if (_selected is not null) _selected = _rows.FirstOrDefault(r => SameRow(r, _selected));
        if (_listening is not null) _listening = _rows.FirstOrDefault(r => SameRow(r, _listening));
    }

    private static bool SameRow(Row a, Row b) => a.F is not null && b.F is not null ? a.F.Keys.SequenceEqual(b.F.Keys) : ReferenceEquals(a.Other, b.Other);

    // Complete: every half has a control (what "Unassigned only" and the strip count); any: something to clear.
    private bool Assigned(Row r) => r.Other is not null || r.Keys.All(k => _map.ControlOf(k) is not null);
    private bool AnyAssigned(Row r) => r.Other is not null || r.Keys.Any(k => _map.ControlOf(k) is not null);
    private bool Changed(Row r) => r.Keys.Any(k => _map.IsChanged(k));

    private bool Visible(Row r) => !_unassignedOnly || r.Other is null && (!Assigned(r) || ReferenceEquals(r, _listening) || Changed(r));

    private string NameOfKey(FunctionKey k)
    {
        foreach (var r in _rows)
        {
            if (r.F is not { } f) continue;
            if (f.IsPair && f.Left!.Key == k) return f.Left.Name;
            if (f.IsPair && f.Right!.Key == k) return f.Right.Name;
            if (!f.IsPair && f.Key == k) return f.Name;
        }
        return k.Command ?? k.Kind.ToString();
    }

    private string NameOf(MappingEntry e)
    {
        var row = _rows.FirstOrDefault(r => r.F is not null && (e.Right is { } k && r.Keys.Contains(k) || e.Left is { } l && r.Keys.Contains(l)));
        return row?.F?.Name ?? ControllerService.ControlLabel(e.Decl!);
    }

    // ---- drawing ----------------------------------------------------------------------------

    private void Render()
    {
        ShowHeader();
        ShowStrip();
        _list.Children.Clear();
        SetupGroup? group = null;
        bool otherShown = false;
        foreach (var r in _rows)
        {
            if (!Visible(r)) continue;
            if (r.F is not null && r.Group != group)
            {
                group = r.Group;
                _list.Children.Add(GroupHeader(SetupFunctions.GroupTitle(group!.Value), group.ToString()!));
            }
            if (r.Other is not null && !otherShown)
            {
                otherShown = true;
                _list.Children.Add(GroupHeader(Loc.T("controllers.edit.group_other"), nameof(Row.Other)));
            }
            _list.Children.Add(RowView(r));
            if (r.F?.Group == SetupGroup.Extra && !_rows.Skip(_rows.IndexOf(r) + 1).Any(x => x.Group == SetupGroup.Extra && Visible(x)))
                _list.Children.Add(AddCommandRow());
        }
        if (_list.Children.Count == 0)
            _list.Children.Add(new TextBlock { Text = Loc.T("controllers.edit.all_assigned"), Classes = { "Caption" }, Margin = new Thickness(8, 16) });
    }

    private void ShowHeader()
    {
        bool shippedId = _service.ShippedFileName(_profile.Id) is not null;
        _profileLine.Text = _profile.Origin == ProfileOrigin.Shipped ? Loc.F("controllers.edit.origin_shipped", _profile.Name)
            : shippedId ? Loc.F("controllers.edit.origin_copy", _profile.Name)
            : Loc.F("controllers.edit.origin_user", _profile.Name);
        _backToShipped.IsVisible = _profile.Origin == ProfileOrigin.User && shippedId;
    }

    private static Control GroupHeader(string text, string tag)
        => new TextBlock { Text = text, Classes = { "SectionLabel" }, Margin = new Thickness(8, 14, 8, 4), Tag = tag };

    // The groups as chips with their assigned counts; a click scrolls there.
    private void ShowStrip()
    {
        _strip.Children.Clear();
        foreach (var g in Enum.GetValues<SetupGroup>())
        {
            var rows = _rows.Where(r => r.Group == g).ToList();
            int on = rows.Count(Assigned);
            _strip.Children.Add(StripChip(SetupFunctions.GroupTitle(g), on, rows.Count, g.ToString()));
        }
        int others = _rows.Count(r => r.Other is not null);
        if (others > 0) _strip.Children.Add(StripChip(Loc.T("controllers.edit.group_other"), others, others, nameof(Row.Other)));
    }

    private Control StripChip(string title, int on, int of, string tag)
    {
        var label = new TextBlock
        {
            Inlines = new Avalonia.Controls.Documents.InlineCollection
            {
                new Avalonia.Controls.Documents.Run(title) { Foreground = NotaPalette.TextPrimary },
                new Avalonia.Controls.Documents.Run("  " + Loc.F("controllers.edit.count", on, of)) { Foreground = NotaPalette.TextTertiary },
            },
            FontSize = NotaType.Caption, VerticalAlignment = VerticalAlignment.Center,
        };
        var chip = new Border
        {
            Height = 24, Padding = new Thickness(10, 0), CornerRadius = NotaRadius.Group, Background = NotaPalette.Well,
            Cursor = new Cursor(StandardCursorType.Hand), Child = label,
        };
        chip.PointerPressed += (_, _) =>
        {
            var h = _list.Children.OfType<TextBlock>().FirstOrDefault(t => (string?)t.Tag == tag);
            if (h is null) return;
            // Scroll so the group's header sits at the top of the list.
            var p = h.TranslatePoint(new Point(0, 0), _list);
            if (p is { } pt) _scroll.Offset = new Vector(0, Math.Max(0, pt.Y - 4));
        };
        return chip;
    }

    private Control RowView(Row r)
    {
        bool listening = ReferenceEquals(r, _listening), selected = ReferenceEquals(r, _selected), changed = Changed(r);

        Control icon = r.F is { } f ? FunctionIcon(f) : new Glyph(GlyphKind.MidiPort, 12) { Foreground = NotaPalette.TextTertiary };
        var iconBox = new Border { Width = 26, Child = icon, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = r.F?.Name ?? ControllerService.ControlLabel(r.Other!.Decl!), VerticalAlignment = VerticalAlignment.Center, FontSize = NotaType.TrackName,
            Foreground = r.Other is null ? NotaPalette.TextPrimary : NotaPalette.TextSecondary, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = listening ? FontWeight.SemiBold : FontWeight.Normal,
        };
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = NotaPalette.Accent, VerticalAlignment = VerticalAlignment.Center, IsVisible = changed };
        ToolTip.SetTip(dot, Loc.T("controllers.edit.changed_tip"));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,230,12,*,Auto"), Height = 34 };
        Grid.SetColumn(name, 1); Grid.SetColumn(dot, 2);
        var controls = Controls(r, listening);
        Grid.SetColumn(controls, 3);
        var actions = Actions(r, selected || listening);
        Grid.SetColumn(actions, 4);
        grid.Children.Add(iconBox); grid.Children.Add(name); grid.Children.Add(dot); grid.Children.Add(controls); grid.Children.Add(actions);

        var stack = new StackPanel { Children = { grid } };
        if (listening && _conflict is { } c) stack.Children.Add(ConflictLine(c));
        else if (listening && _ambiguous is { } a) stack.Children.Add(AmbiguousLine(a));

        // Listening: the live orange — a solid bar on the left over a soft tint; picked: the tint.
        var bar = new Border { Width = 3, CornerRadius = NotaRadius.Fine, Background = NotaPalette.Accent, IsVisible = listening, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-9, 6, 0, 6) };
        var view = new Border
        {
            Padding = new Thickness(9, 0, 6, 0), CornerRadius = NotaRadius.Field,
            Background = listening || selected ? NotaPalette.AccentSoft : Brushes.Transparent,
            Cursor = r.Other is null ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            Child = new Panel { Children = { stack, bar } },
        };
        if (r.Other is null)
        {
            view.PointerEntered += (_, _) => { if (!ReferenceEquals(r, _listening) && !ReferenceEquals(r, _selected)) view.Background = NotaPalette.SurfaceHover; };
            view.PointerExited += (_, _) => { if (!ReferenceEquals(r, _listening) && !ReferenceEquals(r, _selected)) view.Background = Brushes.Transparent; };
            view.PointerPressed += (_, e) =>
            {
                if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null) return;   // a chip or an action
                if (ReferenceEquals(r, _listening) && _halfOnly is null) { StopListening(); Hint(); }
                else Listen(r);
                Render();
            };
        }
        else
        {
            ToolTip.SetTip(view, Loc.T("controllers.edit.other_tip"));
            view.PointerPressed += (_, _) => { _selected = r; Render(); };
        }
        r.View = view;
        return view;
    }

    private static Control FunctionIcon(SetupFunction f)
    {
        GlyphKind? glyph = f.Icon ?? f.Left?.Icon ?? (f.Left?.Command ?? f.Command) switch
        {
            "edit.undo" => (GlyphKind?)GlyphKind.History,
            "transport.tempoDown" or "transport.tapTempo" => GlyphKind.Metronome,
            "device.focusPrevious" => GlyphKind.Device,
            "session.stopAll" => GlyphKind.Stop,
            null => null,
            _ => GlyphKind.Action,
        } ?? f.Role switch
        {
            ControlRoleKind.Fader or ControlRoleKind.MasterFader => (GlyphKind?)GlyphKind.Sliders,
            ControlRoleKind.BankNext => GlyphKind.Mixer,
            ControlRoleKind.PageNext => GlyphKind.Doc,
            ControlRoleKind.Jog => GlyphKind.Cycle,
            _ => null,
        };
        if (glyph is { } g) return new Glyph(g, 12) { Foreground = NotaPalette.TextSecondary };
        var pic = new ControlPictogram { Width = 16, Height = 16 };
        pic.Show(PictogramKind.Knob);
        return pic;
    }

    // The control(s) a row is on, or what it waits for.
    private Control Controls(Row r, bool listening)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (r.Other is { Decl: { } od }) { panel.Children.Add(Chip(ControlCatcher.Chip(od), null)); return panel; }
        var f = r.F!;
        if (listening && _conflict is null && _ambiguous is null)
        {
            string prompt = _halfOnly is { } h ? Loc.F("controllers.edit.listen_half", h == 0 ? f.Left!.Name : f.Right!.Name)
                : f.IsPair && _half == 1 ? Loc.F("controllers.edit.listen_second", f.Right!.Name)
                : f.IsPair ? Loc.T("controllers.edit.listen_pair")
                : f.Picture is PictogramKind.Knob or PictogramKind.Fader or PictogramKind.Jog ? Loc.T("controllers.edit.listen_move")
                : Loc.T("controllers.edit.listen_press");
            panel.Children.Add(new TextBlock { Text = prompt, Foreground = NotaPalette.AccentBright, FontSize = NotaType.TrackName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (f.IsPair && _half == 1 && _map.ControlOf(f.Left!.Key) is { } first) panel.Children.Insert(0, Chip(ControlCatcher.Chip(first), f.Left.Name));
            return panel;
        }
        if (!f.IsPair)
        {
            panel.Children.Add(_map.ControlOf(f.Key) is { } d ? Chip(ControlCatcher.Chip(d), null) : Dash());
            return panel;
        }
        var left = _map.EntryFor(f.Left!.Key);
        var right = _map.EntryFor(f.Right!.Key);
        // One encoder doing both halves shows on both; a click on a half still learns that half alone.
        panel.Children.Add(HalfChip(r, 0, f.Left.Name, left));
        panel.Children.Add(HalfChip(r, 1, f.Right.Name, right));
        return panel;
    }

    // One half of a pair: its name and control; a click learns that half alone.
    private Control HalfChip(Row r, int half, string name, MappingEntry? e)
    {
        var b = new Button
        {
            Classes = { "chip" }, Padding = new Thickness(8, 2),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    new TextBlock { Text = name, FontSize = NotaType.Body, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = e?.Decl is { } d ? ControlCatcher.Chip(d) : Loc.T("controllers.edit.none"), Classes = { "Mono" }, FontSize = NotaType.Body, Foreground = e is null ? NotaPalette.TextTertiary : NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        ToolTip.SetTip(b, Loc.F("controllers.edit.half_tip", name));
        b.Click += (_, _) => { Listen(r, half); Render(); };
        return b;
    }

    private static Control Chip(string text, string? label)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (label is not null) p.Children.Add(new TextBlock { Text = label, FontSize = NotaType.Body, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(new TextBlock { Text = text, Classes = { "Mono" }, FontSize = NotaType.Body, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center });
        return new Border { Background = NotaPalette.SurfaceInset, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 2), Child = p, VerticalAlignment = VerticalAlignment.Center };
    }

    private static Control Dash() => new TextBlock { Text = Loc.T("controllers.edit.none"), FontSize = NotaType.TrackName, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };

    // Revert / clear / remove — on the picked or listening row.
    private Control Actions(Row r, bool show)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, IsVisible = show && r.Other is null };
        if (r.F is null) return panel;
        if (Changed(r))
            panel.Children.Add(Action(GlyphKind.History, Loc.T("controllers.edit.revert_tip"), () => Revert(r)));
        if (AnyAssigned(r))
            panel.Children.Add(Action(GlyphKind.Close, Loc.T("controllers.edit.clear_tip"), () => Clear(r)));
        if (r.Added)
            panel.Children.Add(Action(GlyphKind.Trash, Loc.T("controllers.edit.remove_tip"), () => { if (ReferenceEquals(r, _listening)) StopListening(); foreach (var k in r.Keys) _map.Clear(k); _rows.Remove(r); if (ReferenceEquals(_selected, r)) _selected = null; BuildRows(); Render(); }));
        return panel;
    }

    private static Button Action(GlyphKind g, string tip, Action click)
    {
        var b = new Button { Content = new Glyph(g, 11), Classes = { "ghost" }, Width = 26, Height = 26, Padding = new Thickness(0), Foreground = NotaPalette.TextSecondary };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private Control ConflictLine(Conflict c)
    {
        string names = string.Join(Loc.T("controllers.edit.list_sep"), c.Entries.Select(NameOf).Distinct());
        var move = new Button { Content = Loc.T("controllers.edit.move_here"), Classes = { "primary" } };
        move.Click += (_, _) => Commit(c.Key, c.Left, c.Control);
        var cancel = new Button { Content = Loc.T("common.cancel"), Classes = { "ghost" } };
        cancel.Click += (_, _) => { _conflict = null; _catcher.Reset(); Render(); };
        var text = new TextBlock
        {
            Text = Loc.F("controllers.edit.conflict", ControlCatcher.Short(c.Control), names), Foreground = NotaPalette.Warning,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6, Margin = new Thickness(22, 0, 0, 8) };
        Grid.SetColumn(move, 1); Grid.SetColumn(cancel, 2);
        g.Children.Add(text); g.Children.Add(move); g.Children.Add(cancel);
        return g;
    }

    private Control AmbiguousLine(ClassifiedControl a)
    {
        _encoder.SelectedIndex = _encoder.SelectedIndex <= 0 ? 1 : _encoder.SelectedIndex;
        (_encoder.Parent as Panel)?.Children.Remove(_encoder);
        var use = new Button { Content = Loc.T("controllers.edit.use"), Classes = { "primary" } };
        use.Click += (_, _) => UseEncoding();
        var text = new TextBlock { Text = Loc.F("controllers.setup.ambiguous", a.Number, a.Channel + 1), Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6, Margin = new Thickness(22, 0, 0, 8) };
        Grid.SetColumn(_encoder, 1); Grid.SetColumn(use, 2);
        g.Children.Add(text); g.Children.Add(_encoder); g.Children.Add(use);
        return g;
    }

    // "+ Command…": any palette command on a spare button.
    private Control AddCommandRow()
    {
        var b = new Button
        {
            Classes = { "ghost" }, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new Glyph(GlyphKind.Plus, 10) { Foreground = NotaPalette.TextSecondary }, new TextBlock { Text = Loc.T("controllers.edit.add_command"), VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        ToolTip.SetTip(b, Loc.T("controllers.edit.add_command_tip"));
        b.Click += (_, _) => ShowCommandPicker(b);
        return b;
    }

    private void ShowCommandPicker(Control anchor)
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var (content, search) = BuildCommandPicker(id => { flyout.Hide(); AddCommand(id); });
        flyout.Content = content;
        flyout.Opened += (_, _) => Dispatcher.UIThread.Post(() => search.Focus());
        flyout.ShowAt(anchor);
    }

    // The picker: a search box over the palette's commands (title, category), the ones that
    // already have a row left out; Enter or a double click picks.
    private (Control Content, TextBox Search) BuildCommandPicker(Action<string> pick)
    {
        var have = _rows.SelectMany(r => r.Keys).Where(k => k.Command is not null).Select(k => k.Command!).ToHashSet();
        var all = SetupFunctions.Commands.Values.Where(c => !have.Contains(c.Id) && c.Id != Nota.Application.Palette.CommandCatalog.Palette)
            .OrderBy(c => c.Category, StringComparer.CurrentCulture).ThenBy(c => c.Title, StringComparer.CurrentCulture).ToList();
        var search = new TextBox { PlaceholderText = Loc.T("controllers.edit.search_commands"), Classes = { "search" }, MinWidth = 300 };
        var list = new ListBox { Height = 280, Width = 340 };
        void Fill()
        {
            string q = search.Text?.Trim() ?? "";
            list.Items.Clear();
            foreach (var c in all.Where(c => q.Length == 0 || c.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                         || c.Category.Contains(q, StringComparison.CurrentCultureIgnoreCase) || c.Id.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                var title = new TextBlock { Text = c.Title, Foreground = NotaPalette.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis };
                var category = new TextBlock { Text = c.Category, Classes = { "Caption" }, Margin = new Thickness(12, 0, 0, 0) };
                Grid.SetColumn(category, 1);
                list.Items.Add(new ListBoxItem { Tag = c.Id, Content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { title, category } } });
            }
            if (list.ItemCount > 0) list.SelectedIndex = 0;
        }
        void Pick() { if (list.SelectedItem is ListBoxItem { Tag: string id }) pick(id); }
        search.TextChanged += (_, _) => Fill();
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && list.ItemCount > 0) { list.SelectedIndex = Math.Min(list.ItemCount - 1, list.SelectedIndex + 1); e.Handled = true; }
            else if (e.Key == Key.Up && list.ItemCount > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
            else if (e.Key == Key.Enter) { Pick(); e.Handled = true; }
        };
        list.DoubleTapped += (_, _) => Pick();
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Pick(); e.Handled = true; } };
        Fill();
        // The search as the browser's: a well with the glass icon, an orange edge while typing.
        search.BorderThickness = new Thickness(0);
        search.Background = Brushes.Transparent;
        search.VerticalContentAlignment = VerticalAlignment.Center;
        var field = new Border
        {
            Height = 30, CornerRadius = NotaRadius.Field, Background = NotaPalette.BgSunken, Padding = new Thickness(8, 0),
            Child = new DockPanel { Children = { new Glyph(GlyphKind.Search, 12) { Foreground = NotaPalette.TextTertiary, Margin = new Thickness(0, 0, 6, 0), [DockPanel.DockProperty] = Dock.Left }, search } },
        };
        return (new StackPanel { Spacing = 8, Margin = new Thickness(4), Children = { field, list } }, search);
    }

    // A picked command: its row in Extra buttons, listening at once.
    private void AddCommand(string id)
    {
        var row = new Row { F = SetupFunctions.ForCommand(id), Added = true, Custom = true };
        int at = _rows.FindLastIndex(r => r.Group == SetupGroup.Extra) + 1;
        _rows.Insert(at, row);
        Listen(row);
        Render();
        Dispatcher.UIThread.Post(() => row.View?.BringIntoView());
    }

    // ---- listening --------------------------------------------------------------------------

    private void Listen(Row r, int? half = null)
    {
        if (r.Other is not null) return;
        StopListening();   // a pair left half-way goes back first — and the rows are rebuilt
        r = _rows.FirstOrDefault(x => SameRow(x, r)) ?? r;
        if (_gone) { _selected = r; return; }
        _beforeListen = _map.Snapshot();
        _listening = _selected = r;
        _halfOnly = half;
        _half = 0;
        _conflict = null;
        _ambiguous = null;
        _catcher.Reset();
        _service.TakeCaptured();
        _status.Text = Loc.T("controllers.edit.status_listening");
    }

    // Stopped before a pair got its second button: the first one goes back too, as if never
    // learned (nothing of a cancelled learn stays).
    private void StopListening()
    {
        if (_half == 1 && _beforeListen is { } before)
        {
            _map.Restore(before);
            BuildRows();
        }
        _beforeListen = null;
        _listening = null;
        _halfOnly = null;
        _half = 0;
        _conflict = null;
        _ambiguous = null;
        _catcher.Reset();
    }

    private void Hint() => _status.Text = Loc.T("controllers.edit.status_hint");

    private void Poll()
    {
        long now = Environment.TickCount64;
        if (_activityMs > 0 && now - _activityMs > 160) { _activity.Fill = NotaPalette.TextDisabled; _activityMs = 0; }
        var got = _service.TakeCaptured();
        if (got.Count > 0)
        {
            _hearing.Text = ControlCatcher.Raw(got[^1]);
            _activity.Fill = NotaPalette.Success;
            _activityMs = now;
        }
        // Unplugged: nothing more can be learned; what was changed can still be cleared, put back
        // and saved (spec edge case). Plugged back in, it captures again.
        bool gone = _port.Stage == ControllerStage.Gone;
        if (gone != _gone)
        {
            _gone = gone;
            if (gone) { StopListening(); Render(); _status.Text = Loc.F("controllers.edit.status_gone", _port.Name); }
            else { _service.Capture(_port.Uid); Hint(); }
        }
        if (gone) return;
        if (_listening is null) { FindByTouch(got, now); return; }
        if (_conflict is not null || _ambiguous is not null) return;   // the user decides first
        foreach (var ev in got) _catcher.Add(ev, now);
        if (_catcher.Empty) return;
        var f = _listening.F!;
        bool slow = _halfOnly is null && (f.IsPair || f.Picture is PictogramKind.Knob or PictogramKind.Encoder or PictogramKind.Jog);
        bool button = f.Button || f.IsPair;
        if (_catcher.Read(now, button, slow) is not { } c) return;
        if (c.Ambiguous) { _ambiguous = c; Render(); return; }
        Learned(c);
    }

    private void UseEncoding()
    {
        if (_ambiguous is not { } a) return;
        var c = a with
        {
            Ambiguous = false,
            Encoder = _encoder.SelectedIndex switch { 1 => EncoderMode.RelativeTwos, 2 => EncoderMode.RelativeSignBit, 3 => EncoderMode.RelativeOffset, _ => EncoderMode.Absolute },
        };
        _ambiguous = null;
        Learned(c);
    }

    // What was caught, for the function listening: the wizard's rules for a pair.
    private void Learned(ClassifiedControl c)
    {
        var f = _listening!.F!;
        if (_halfOnly is { } h) Propose(h == 0 ? f.Left!.Key : f.Right!.Key, null, c);
        else if (!f.IsPair) Propose(f.Key, null, c);
        else if (_half == 0 && _catcher.LooksLikeEncoder(c)) Propose(f.Right!.Key, f.Left!.Key, c);
        else Propose(_half == 0 ? f.Left!.Key : f.Right!.Key, null, c);
    }

    private void Propose(FunctionKey key, FunctionKey? left, ClassifiedControl c)
    {
        var inWay = _map.ConflictsFor(c, key, left);
        // A pair learned as two buttons: its own first button isn't a conflict for the second, it
        // is the same button pressed twice — ask for another one.
        if (_listening?.F is { IsPair: true } pf && _half == 1 && inWay.Any(e => e.Covers(pf.Left!.Key)))
        {
            _status.Text = Loc.F("controllers.edit.status_same_button", pf.Left!.Name);
            _catcher.Reset();
            return;
        }
        if (inWay.Count > 0) { _conflict = new Conflict(key, left, c, inWay); Render(); return; }
        Commit(key, left, c);
    }

    private void Commit(FunctionKey key, FunctionKey? left, ClassifiedControl c)
    {
        // An encoder that did both halves of a pair leaves both when one half moves (it can't turn
        // one way only): say which function that left without a control.
        var shared = left is null && _map.EntryFor(key) is { Left: not null } se ? se : null;
        _map.Assign(key, c, left);
        _conflict = null;
        _catcher.Reset();
        BuildRows();   // a moved control may have been a row of its own (Other controls)
        var row = _listening!;
        if (row.F!.IsPair && _halfOnly is null && left is null && _half == 0)
        {
            _half = 1;   // now the pair's second button
            _service.TakeCaptured();
            Render();
            return;
        }
        _status.Text = Loc.F("controllers.edit.status_learned", row.F.Name, ControlCatcher.Describe(c));
        if (shared is { Right: { } sr, Left: { } sl } && (sr == key ? sl : sr) is var other && _map.ControlOf(other) is null)
            _status.Text = Loc.F("controllers.edit.status_encoder_left", NameOfKey(other));
        _half = 0;   // complete: nothing to put back
        StopListening();
        _selected = row;
        // FR-006a: with "unassigned only" on, straight on to the next one without a control.
        if (_unassignedOnly && _rows.Skip(_rows.IndexOf(row) + 1).Concat(_rows.Take(_rows.IndexOf(row)))
                .FirstOrDefault(r => r.F is not null && !Assigned(r)) is { } next)
            Listen(next);
        else if (_unassignedOnly)
            _status.Text = Loc.T("controllers.edit.status_all_assigned");
        Render();
        Dispatcher.UIThread.Post(() => (_listening ?? _selected)?.View?.BringIntoView());
    }

    // Not listening: moving a control shows what it does (FR-010).
    private void FindByTouch(List<(int Status, int Channel, int D1, int D2)> got, long now)
    {
        foreach (var ev in got)
        {
            if (ev.Status is 0x80 || ev.Status == 0x90 && ev.D2 == 0) continue;   // a release says nothing new
            // NRPN: 99 / 98 pick the parameter on the channel, 6 / 38 / 96 / 97 move it.
            // 99 / 98 always keep the channel's NRPN number up to date. They are a plain CC control
            // of their own only when one is declared there and no NRPN control shares the channel.
            if (ev.Status == 0xB0 && ev.D1 is 99 or 98)
            {
                var n = _nrpn.TryGetValue(ev.Channel, out var had) ? had : (Msb: -1, Lsb: -1);
                _nrpn[ev.Channel] = ev.D1 == 99 ? (ev.D2, n.Lsb) : (n.Msb, ev.D2);
                bool nrpnHere = _map.Entries.Any(x => x.Decl is { Message: ControlMessage.Nrpn } d && d.Channel == ev.Channel);
                if (nrpnHere || _map.Find(ev.Status, ev.Channel, ev.D1) is null) continue;
            }
            MappingEntry? nrpn = null;
            if (ev.Status == 0xB0 && ev.D1 is 6 or 38 or 96 or 97 && _nrpn.TryGetValue(ev.Channel, out var sel) && sel.Msb >= 0 && sel.Lsb >= 0)
                nrpn = _map.FindNrpn(ev.Channel, (sel.Msb << 7) | sel.Lsb);
            var key = nrpn is not null ? (0x1000, ev.Channel, nrpn.Decl!.Number) : (ev.Status, ev.Channel, ev.D1);
            if (_lastFound == key && now - _foundMs < 400) continue;
            _lastFound = key; _foundMs = now;
            if ((nrpn ?? _map.Find(ev.Status, ev.Channel, ev.D1)) is { } e)
            {
                var row = _rows.FirstOrDefault(r => r.F is not null && (e.Right is { } k && r.Keys.Contains(k) || e.Left is { } l && r.Keys.Contains(l)))
                          ?? _rows.FirstOrDefault(r => ReferenceEquals(r.Other, e));
                if (row is null) continue;
                if (!Visible(row)) continue;
                _selected = row;
                _status.Text = Loc.F("controllers.edit.status_found", ControlCatcher.Chip(e.Decl!), row.F?.Name ?? ControllerService.ControlLabel(e.Decl!));
                Render();
                Dispatcher.UIThread.Post(() => row.View?.BringIntoView());
            }
            else
            {
                var c = ControlClassifier.Classify(new[] { ev });
                _status.Text = Loc.F("controllers.edit.status_unmapped", c is { } cc ? ControlCatcher.Short(cc) : ControlCatcher.Raw(ev));
            }
        }
    }

    // ---- row actions ------------------------------------------------------------------------

    private void Clear(Row r)
    {
        if (ReferenceEquals(r, _listening)) StopListening();
        foreach (var k in r.Keys) _map.Clear(k);
        _status.Text = Loc.F("controllers.edit.status_cleared", r.F!.Name);
        BuildRows();
        Render();
    }

    private void Revert(Row r)
    {
        if (ReferenceEquals(r, _listening)) StopListening();
        foreach (var k in r.Keys)
        {
            var inWay = _map.Revert(k);
            if (inWay.Count > 0)
            {
                _status.Text = Loc.F("controllers.edit.status_revert_blocked", r.F!.Name, string.Join(Loc.T("controllers.edit.list_sep"), inWay.Select(NameOf).Distinct()));
                Render();
                return;
            }
        }
        _status.Text = Loc.F("controllers.edit.status_reverted", r.F!.Name);
        BuildRows();
        Render();
    }

    // ---- keys -------------------------------------------------------------------------------

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        var cmd = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        // A focused button, menu or list keeps Enter / Space / arrows (Finish stays Finish, the
        // encoding menu keeps its arrows); the window's shortcuts apply everywhere else.
        bool onControl = e.Source is Visual sv && (sv.FindAncestorOfType<Button>(true) is not null
                         || sv.FindAncestorOfType<ComboBox>(true) is not null || sv.FindAncestorOfType<ListBox>(true) is not null);
        if (onControl && e.Key is Key.Enter or Key.Space or Key.Up or Key.Down && !e.KeyModifiers.HasFlag(cmd)) return;
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(cmd):
                Finish();
                break;
            case Key.Enter when _conflict is { } c:
                Commit(c.Key, c.Left, c.Control);
                break;
            case Key.Enter when _ambiguous is not null:
                UseEncoding();
                break;
            case Key.Enter when _selected is { Other: null } s && _listening is null:
                Listen(s);
                Render();
                break;
            case Key.Escape when _conflict is not null:
                _conflict = null; _catcher.Reset(); Render();
                break;
            case Key.Escape when _listening is not null:
                StopListening(); Hint(); Render();
                break;
            case Key.Escape:
                Close();
                break;
            case Key.Up or Key.Down:
                Move(e.Key == Key.Down ? 1 : -1);
                break;
            case Key.Delete or Key.Back when _selected is { F: not null } s && _listening is null:
                Clear(s);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Move(int by)
    {
        StopListening();   // may rebuild the rows: count on the new ones
        var rows = _rows.Where(Visible).ToList();
        if (rows.Count == 0) return;
        int at = _selected is null ? (by > 0 ? -1 : rows.Count) : rows.IndexOf(_selected);
        at = Math.Clamp(at + by, 0, rows.Count - 1);
        _selected = rows[at];
        Render();
        Dispatcher.UIThread.Post(() => _selected?.View?.BringIntoView());
    }

    // ---- saving -----------------------------------------------------------------------------

    private void Finish()
    {
        if (_map.HasChanges)
        {
            // A user profile is updated in place; a shipped one becomes the user's own copy under
            // the same file name and id, which replaces it (FR-013).
            _service.SaveUserProfile(_profile.FileName, _map.ToJson());
        }
        _closing = true;
        Close();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing || !_map.HasChanges) return;
        e.Cancel = true;
        bool discard = await new ConfirmWindow(Loc.T("controllers.edit.discard_title"), Loc.T("controllers.edit.discard_message"),
            Loc.T("controllers.edit.discard"), Loc.T("controllers.edit.keep_editing")).ShowDialog<bool>(this);
        if (!discard) return;
        _closing = true;
        Close();
    }

    private async System.Threading.Tasks.Task BackToShipped()
    {
        if (!await new ConfirmWindow(Loc.T("controllers.edit.back_to_shipped"), Loc.F("controllers.edit.back_to_shipped_message", _profile.Name),
                Loc.T("controllers.edit.back_to_shipped_confirm"), Loc.T("common.cancel")).ShowDialog<bool>(this))
            return;
        StopListening();   // a pair left half-way goes back into the mapping it came from, not the shipped one
        string id = _profile.Id;
        _service.RemoveUserProfile(_profile.FileName);
        if (_service.ProfileById(id) is not { } shipped || _service.ProfileJson(shipped) is not { } json) { _closing = true; Close(); return; }
        _profile = shipped;
        _map = new ProfileMapping(shipped, json);
        BuildRows();
        _status.Text = Loc.T("controllers.edit.status_shipped_back");
        Render();
    }

    private async System.Threading.Tasks.Task LearnAgain()
    {
        StopListening();
        _poll.Stop();
        var wizard = new ControllerSetupWindow(_service, _port, _map);
        await wizard.ShowDialog(this);
        _service.Capture(_port.Uid);   // the wizard let the port go when it closed
        _service.TakeCaptured();
        _poll.Start();
        if (wizard.Result is { } learned)
        {
            _map.ReplaceAll(learned, wizard.Scope);
            BuildRows();
            _status.Text = Loc.T("controllers.edit.status_learned_again");
        }
        Render();
    }
}
