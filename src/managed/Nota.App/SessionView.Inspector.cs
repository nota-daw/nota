// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session view — the inspector: properties of the selected clip (length, loop, launch mode,
// quantize, legato, velocity, follow actions, colour), scene (tempo, signature, follow,
// colour) or empty slot (stop button). Every edit goes straight to the engine, so it is one
// undo step and shows on the grid at once.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Input;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class SessionView
{
    private static readonly string[] ModeLabels = { Loc.T("session.inspector.trigger"), Loc.T("common.gate"), Loc.T("session.inspector.toggle"), Loc.T("session.inspector.repeat") };
    private static readonly (SessionFollowAction Action, string Label)[] FollowActions =
    {
        (SessionFollowAction.None, Loc.T("common.none")), (SessionFollowAction.Next, Loc.T("session.inspector.next")), (SessionFollowAction.Again, Loc.T("session.inspector.again")),
        (SessionFollowAction.Previous, Loc.T("session.inspector.previous")), (SessionFollowAction.First, Loc.T("session.inspector.first")), (SessionFollowAction.Last, Loc.T("session.inspector.last")),
        (SessionFollowAction.Any, Loc.T("session.inspector.any")), (SessionFollowAction.Other, Loc.T("common.other")), (SessionFollowAction.Stop, Loc.T("common.stop")),
        (SessionFollowAction.Jump, Loc.T("session.inspector.jump")),
    };
    // The bar choices count the project's bars (#405), so "1 bar" in 3/4 is three beats.
    private static double Bars(double n) => n * Meter.BeatsPerBar;
    private static (double Beats, string Label)[] LengthSteps => new[]
    {
        (1.0, Loc.T("session.inspector.1_beat")), (2, Loc.T("session.inspector.2_beats")), (Bars(1), Loc.T("common.1_bar")), (Bars(2), Loc.T("session.inspector.2_bars")), (Bars(4), Loc.T("session.inspector.4_bars")), (Bars(8), Loc.T("session.inspector.8_bars")), (Bars(16), Loc.T("session.inspector.16_bars")),
    };
    private static (double Beats, string Label)[] FollowTimes => new[]
    {
        (0.0, Loc.T("session.inspector.one_pass")), (1, Loc.T("session.inspector.1_beat")), (2, Loc.T("session.inspector.2_beats")), (Bars(1), Loc.T("common.1_bar")), (Bars(2), Loc.T("session.inspector.2_bars")), (Bars(4), Loc.T("session.inspector.4_bars")), (Bars(8), Loc.T("session.inspector.8_bars")), (Bars(16), Loc.T("session.inspector.16_bars")),
    };
    private static readonly (int Num, int Den)[] Signatures = { (0, 0), (2, 4), (3, 4), (4, 4), (5, 4), (6, 8), (7, 8), (12, 8) };

    private InspectorPreview? _inspPreview;

    private void RebuildInspector()
    {
        _inspPreview = null;
        var stack = new StackPanel { Spacing = 18, Margin = new Thickness(16) };
        if (_sel.Kind == SelKind.Scene) BuildSceneInspector(stack, _sel.Scene);
        else if (ColumnFor(_sel.TrackId) is { } c)
        {
            if (c.Kind == ColKind.Group) BuildGroupInspector(stack, c, _sel.Scene);
            else if (SlotFilled(c.TrackId, _sel.Scene) && !(_recTrack == c.TrackId && _recScene == _sel.Scene) && _engine.SessionSlotState(c.TrackId, _sel.Scene) != 4) BuildClipInspector(stack, c, _sel.Scene);
            else BuildEmptyInspector(stack, c, _sel.Scene);
        }
        else if (_sel.Kind == SelKind.Bus && BusColumn(_sel.TrackId) is { } bus) BuildBusInspector(stack, bus);

        var hint = new TextBlock
        {
            Text = MenuKit.Keys(Loc.T("session.inspector.click_selects_the_triangle_launches")),
            FontSize = 8.5, LineHeight = 15, Foreground = NotaPalette.TextDisabled, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16),
        };
        hint.BindResource(TextBlock.FontFamilyProperty, "Font.UI");
        var dock = new DockPanel();
        DockPanel.SetDock(hint, Dock.Bottom);
        dock.Children.Add(hint);
        dock.Children.Add(new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _inspectorHost.Child = dock;
    }

    private void UpdateInspectorLive() => _inspPreview?.InvalidateVisual();

    // ---- pieces -------------------------------------------------------------------------

    private static Control Title(IBrush color, string title, string sub)
    {
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                new Border { Width = 10, Height = 10, CornerRadius = NotaRadius.Field, Background = color, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = InspectorW - 60 },
            },
        };
        var s = Mono(sub, 10, NotaPalette.TextTertiary);
        s.TextTrimming = TextTrimming.CharacterEllipsis;
        return new StackPanel { Spacing = 4, Children = { head, s } };
    }

    internal static TextBlock SectionHead(string text) => new()
    {
        Text = text, FontSize = 8.5, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextMuted,
    };

    // A labelled value well (LENGTH, LOOP, START…): a click opens its choices when it has any.
    private static Control Field(string label, string value, IBrush ink, Action<Control>? click)
    {
        var text = Mono(value, 11, ink);
        Control well;
        if (click is null)
        {
            well = new Border
            {
                Height = NotaSize.Shell, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Cell,
                Background = NotaPalette.BgSunken, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1), Child = text,
            };
        }
        else
        {
            var b = new Button
            {
                Height = NotaSize.Shell, Padding = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Background = NotaPalette.BgSunken, Content = text,
            };
            b.Click += (_, _) => click(b);
            well = b;
        }
        return new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = label, FontSize = 8.5, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextTertiary }, well },
        };
    }

    private static Grid TwoUp(params Control[] items)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10, RowSpacing = 10 };
        for (int i = 0; i < items.Length; i++)
        {
            if (i % 2 == 0) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(items[i], i % 2);
            Grid.SetRow(items[i], i / 2);
            g.Children.Add(items[i]);
        }
        return g;
    }

    // A label on the left and a small value chip on the right (Quantize, Legato…).
    internal static Control Row(string label, Control right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 22 };
        g.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(right, 1);
        right.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(right);
        return g;
    }

    internal static Button Chip(string value, bool lit, Action<Button> click)
    {
        var b = new Button { Classes = { "chip" }, Height = 22, Padding = new Thickness(8, 0), MinWidth = 0, Content = Mono(value, 10, lit ? NotaPalette.TextOnAccent : NotaPalette.TextPrimary) };
        b.Classes.Set("lit", lit);
        b.Click += (_, _) => click(b);
        return b;
    }

    internal static void Pick<T>(Control anchor, IEnumerable<(T Value, string Label)> items, Func<T, bool> isCurrent, Action<T> choose)
    {
        var f = new MenuFlyout();
        foreach (var (v, label) in items)
        {
            var value = v;
            var mi = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = isCurrent(value) };
            mi.Click += (_, _) => choose(value);
            f.Items.Add(mi);
        }
        f.ShowAt(anchor);
    }

    private static Control Actions(params (string Label, string Key, Action Click)[] actions)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, key, click) in actions)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeight.Medium } } };
            if (key.Length > 0) content.Children.Add(Mono(MenuKit.Keys(key), 9, NotaPalette.TextDisabled));
            var b = new Button { Height = NotaSize.Shell, Padding = new Thickness(10, 0), Margin = new Thickness(0, 0, 6, 6), Content = content };
            b.Click += (_, _) => click();
            wrap.Children.Add(b);
        }
        return wrap;
    }

    // Colour swatches: the track palette's bases in two shades. Clicking the current one
    // again goes back to the inherited colour (the track's, or none for a scene).
    private static Control Swatches(int current, Action<int> pick)
    {
        int bases = ArrangementView.PaletteBases;
        var grid = new UniformGrid { Columns = bases };
        for (int shade = 0; shade < 2; shade++)
            for (int b = 0; b < bases; b++)
            {
                int idx = b * ArrangementView.PaletteShades + shade;
                bool on = idx == current;
                var sw = new Border
                {
                    Height = 22, Margin = new Thickness(3), CornerRadius = NotaRadius.Cell, Background = ArrangementView.TrackBrush(idx),
                    BorderBrush = on ? NotaPalette.TextPrimary : Brushes.Transparent, BorderThickness = new Thickness(on ? 1.5 : 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                ToolTip.SetTip(sw, on ? Loc.T("session.inspector.click_again_to_use_the") : null);
                sw.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(sw).Properties.IsLeftButtonPressed) return;
                    e.Handled = true;
                    pick(on ? -1 : idx);
                };
                grid.Children.Add(sw);
            }
        return new StackPanel { Spacing = 7, Children = { SectionHead(Loc.T("session.inspector.color")), grid } };
    }

    // ---- clip ---------------------------------------------------------------------------

    private void BuildClipInspector(StackPanel s, Column c, int scene)
    {
        if (!_engine.TryGetSessionClipProps(c.TrackId, scene, out var p)) return;
        int id = c.TrackId;
        void Set(Func<NotaSessionClipProps, NotaSessionClipProps> edit)
        {
            if (!_engine.TryGetSessionClipProps(id, scene, out var cur)) return;
            _engine.SetSessionClipProps(id, scene, edit(cur));
            RefreshSlot(id, scene);
        }
        var brush = ClipBrush(c, scene);
        double len = _engine.SessionSlotLength(id, scene);
        string kind = c.IsInstrument ? "MIDI" : Loc.T("common.audio");
        s.Children.Add(Title(brush, ClipName(c, scene), $"{c.Name} · {SceneName(scene)} · {NotaText.Label(kind)}"));

        _inspPreview = new InspectorPreview(this, c, scene, brush);
        s.Children.Add(new Border
        {
            Height = 68, CornerRadius = NotaRadius.Field, Background = NotaPalette.Wash(brush, 0x1F),
            BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1), ClipToBounds = true, Child = _inspPreview,
        });

        bool loop = p.Loop != 0;
        s.Children.Add(TwoUp(
            Field(Loc.T("common.length"), FormatLength(len), NotaPalette.TextPrimary, a => Pick(a, LengthSteps, b => Math.Abs(b - len) < 1e-6,
                b => { _engine.SetSessionSlotLength(id, scene, b); RefreshSlot(id, scene); })),
            Field(Loc.T("common.loop"), loop ? "On" : Loc.T("session.inspector.off_one_shot"), loop ? NotaPalette.TextPrimary : NotaPalette.Warning, _ => Set(x => { x.Loop = loop ? 0 : 1; return x; })),
            Field(Loc.T("common.start"), "1.1.1", NotaPalette.TextSecondary, null),
            Field(Loc.T("session.inspector.loop_end"), FormatPosition(len), NotaPalette.TextSecondary, null)));

        var launch = new StackPanel { Spacing = 10 };
        launch.Children.Add(SectionHead(Loc.T("session.launch")));
        var modes = Segmented(ModeLabels, Math.Clamp(p.LaunchMode, 0, 3), i => Set(x => { x.LaunchMode = i; return x; }));
        ToolTip.SetTip(modes, Loc.T("session.inspector.trigger_starts_gate_plays_while"));
        launch.Children.Add(modes);
        string quant = p.QuantBeats < 0 ? Loc.T("session.inspector.global") : QuantLabel(p.QuantBeats);
        var quantItems = new List<(double, string)> { (-1.0, Loc.T("session.inspector.global")) };
        foreach (var q in QuantSteps) quantItems.Add(q);
        launch.Children.Add(Row(Loc.T("common.quantize"), Chip(quant, false, a => Pick(a, quantItems, q => Math.Abs(q - p.QuantBeats) < 1e-6,
            q => Set(x => { x.QuantBeats = q; return x; })))));
        launch.Children.Add(Row(Loc.T("session.inspector.legato"), Chip(p.Legato != 0 ? "On" : Loc.T("common.off"), p.Legato != 0, _ => Set(x => { x.Legato = x.Legato != 0 ? 0 : 1; return x; }))));
        var vel = new DragNumber(p.VelocityAmount * 100, 0, 100, 0.5, "0", 10) { Width = 44, Height = 22 };
        OneUndoStep(vel);
        vel.ValueChanged += v => { if (_engine.TryGetSessionClipProps(id, scene, out var cur)) { cur.VelocityAmount = (float)(v / 100); _engine.SetSessionClipProps(id, scene, cur); } };
        ToolTip.SetTip(vel, Loc.T("session.inspector.velocity_amount_how_far_the"));
        launch.Children.Add(Row(Loc.T("session.inspector.velocity"), new Border { CornerRadius = NotaRadius.Cell, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1), Child = vel }));
        s.Children.Add(launch);

        // Follow actions.
        bool follow = _engine.SessionFollow;
        var fa = new StackPanel { Spacing = 10 };
        var faHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        faHead.Children.Add(SectionHead(Loc.T("session.inspector.follow_action")));
        if (!follow) { var off = Mono(Loc.T("session.inspector.off_globally"), 9, NotaPalette.TextTertiary); Grid.SetColumn(off, 1); faHead.Children.Add(off); }
        fa.Children.Add(faHead);
        string ActionLabel(int a) => Array.Find(FollowActions, x => (int)x.Action == a).Label ?? Loc.T("common.none");
        Control ActionRow(string slot, int action, Action<int> setAction, Control chance)
        {
            var label = Mono(slot, 10, NotaPalette.TextTertiary);
            label.Width = 14;
            var pick = new Button
            {
                Height = 24, Padding = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = NotaPalette.BgSunken,
                Content = new DockPanel
                {
                    Children = { new Glyph(GlyphKind.ChevronDown, 7) { Foreground = NotaPalette.TextDisabled, [DockPanel.DockProperty] = Dock.Right }, new TextBlock { Text = ActionLabel(action), FontSize = 10, Foreground = NotaPalette.TextPrimary } },
                },
            };
            pick.Click += (_, _) => Pick(pick, Array.ConvertAll(FollowActions, x => ((int)x.Action, x.Label)), a => a == action, setAction);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Height = 24 };
            g.Children.Add(label);
            Grid.SetColumn(pick, 1); g.Children.Add(pick);
            chance.Width = 40;
            Grid.SetColumn(chance, 2); g.Children.Add(chance);
            return g;
        }
        int total = Math.Max(1, p.ChanceA + p.ChanceB);
        int pctA = (int)Math.Round(p.ChanceA * 100.0 / total);
        var chanceA = new DragNumber(pctA, 0, 100, 0.5, "0", 10) { Height = 24 };
        OneUndoStep(chanceA);
        var chanceB = Mono($"{100 - pctA}", 10, NotaPalette.TextSecondary);
        chanceB.TextAlignment = TextAlignment.Center;
        chanceA.ValueChanged += v =>
        {
            int a = (int)Math.Round(v);
            chanceB.Text = $"{100 - a}";
            if (_engine.TryGetSessionClipProps(id, scene, out var cur)) { cur.ChanceA = a; cur.ChanceB = 100 - a; _engine.SetSessionClipProps(id, scene, cur); }
        };
        ToolTip.SetTip(chanceA, Loc.T("session.inspector.chance_of_a_in_percent"));
        fa.Children.Add(ActionRow("A", p.FollowA, a => Set(x => { x.FollowA = a; return x; }), chanceA));
        fa.Children.Add(ActionRow("B", p.FollowB, a => Set(x => { x.FollowB = a; return x; }), chanceB));
        if (p.FollowA == (int)SessionFollowAction.Jump || p.FollowB == (int)SessionFollowAction.Jump)
        {
            var scenes = new List<(int, string)>();
            for (int i = 0; i < _sceneCount; i++) scenes.Add((i, SceneName(i)));
            fa.Children.Add(Row(Loc.T("session.inspector.jump_to"), Chip(SceneName(Math.Clamp(p.JumpScene, 0, Math.Max(0, _sceneCount - 1))), false,
                a => Pick(a, scenes, i => i == p.JumpScene, i => Set(x => { x.JumpScene = i; return x; })))));
        }
        double after = p.FollowBeats > 0 ? p.FollowBeats : len;
        fa.Children.Add(Row(Loc.T("session.inspector.after"), Chip(FormatDuration(after), false, a => Pick(a, FollowTimes, b => Math.Abs(b - p.FollowBeats) < 1e-6,
            b => Set(x => { x.FollowBeats = b; return x; })))));
        s.Children.Add(fa);
        if (!follow) Inactive.Set(fa, true, interactive: true);

        s.Children.Add(Swatches(p.Color, idx => Set(x => { x.Color = idx; return x; })));
        s.Children.Add(Actions(
            (Loc.T("common.duplicate"), Shortcut.Cmd('D'), () => DuplicateSelection()),
            (Loc.T("session.inspector.rename"), "F2", () => RenameSelection()),
            (Loc.T("session.inspector.to_arrangement"), Shortcut.CmdShift('C'), () => CopySelectionToArrangement()),
            (Loc.T("common.edit"), "", () => SlotEditRequested?.Invoke(id, scene)),
            (Loc.T("common.delete"), Shortcut.None(KeyGlyphs.Backspace), () => DeleteSelection())));
    }

    // A drag on a number is one undo step, however many values it passes through.
    private void OneUndoStep(DragNumber dn)
    {
        dn.GestureBegin += _engine.BeginUndoGroup;
        dn.GestureEnd += _engine.EndUndoGroup;
    }

    private static string QuantLabel(double beats)
    {
        foreach (var (b, label) in QuantSteps) if (Math.Abs(b - beats) < 1e-6) return label;
        return BeatsText(beats);
    }

    // ---- scene --------------------------------------------------------------------------

    private void BuildSceneInspector(StackPanel s, int scene)
    {
        _engine.TryGetSceneProps(scene, out var p);
        void Set(Func<NotaSceneProps, NotaSceneProps> edit)
        {
            if (!_engine.TryGetSceneProps(scene, out var cur)) return;
            _engine.SetSceneProps(scene, edit(cur));
            RefreshScene(scene);
        }
        int clips = 0;
        foreach (var c in LaunchCols) if (SlotFilled(c.TrackId, scene)) clips++;
        var color = p.Color >= 0 ? ArrangementView.TrackBrush(p.Color) : NotaPalette.BorderStrong;
        s.Children.Add(Title(color, SceneName(scene), Loc.F("session.inspector.scene_caps", scene + 1, Loc.Plural("session.inspector.clips", clips))));

        Control tempo;
        if (p.Tempo > 0)
        {
            var dn = new DragNumber(p.Tempo, 20, 999, 0.25, "0.00", 11) { Height = NotaSize.Shell - 2 };
            OneUndoStep(dn);
            dn.ValueChanged += v => { if (_engine.TryGetSceneProps(scene, out var cur)) { cur.Tempo = v; _engine.SetSceneProps(scene, cur); } };
            var clear = new Button { Classes = { "ghost" }, Width = 22, Height = 22, Padding = new Thickness(0), Content = new Glyph(GlyphKind.Close, 8), HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(clear, Loc.T("session.inspector.launch_without_a_tempo_change"));
            clear.Click += (_, _) => Set(x => { x.Tempo = 0; return x; });
            var row = new DockPanel { Children = { clear, dn } };
            DockPanel.SetDock(clear, Dock.Right);
            tempo = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = Loc.T("session.inspector.tempo"), FontSize = 8.5, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextTertiary },
                    new Border { Height = NotaSize.Shell, CornerRadius = NotaRadius.Cell, Background = NotaPalette.BgSunken, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1), Child = row },
                },
            };
            ToolTip.SetTip(tempo, Loc.T("session.inspector.tempo_set_when_the_scene"));
        }
        else
            tempo = Field(Loc.T("session.inspector.tempo"), "—", NotaPalette.TextDisabled, _ =>
                Set(x => { x.Tempo = GetTempo?.Invoke().Bpm ?? 120; return x; }));
        string sig = p.SigNum > 0 && p.SigDen > 0 ? $"{p.SigNum}/{p.SigDen}" : "—";
        var sigField = Field(Loc.T("session.inspector.signature"), sig, p.SigNum > 0 ? NotaPalette.TextPrimary : NotaPalette.TextDisabled, a =>
            Pick(a, Array.ConvertAll(Signatures, x => ((x.Num, x.Den), x.Num == 0 ? Loc.T("common.none") : $"{x.Num}/{x.Den}")),
                v => v.Num == p.SigNum && v.Den == p.SigDen, v => Set(x => { x.SigNum = v.Num; x.SigDen = v.Den; return x; })));
        s.Children.Add(TwoUp(tempo, sigField));

        var rows = new StackPanel { Spacing = 8 };
        string follow = p.Follow != 0 ? Loc.F("session.inspector.next_n", FormatLength(p.FollowBeats)) : Loc.T("common.off");
        var followItems = new List<(double, string)> { (0, Loc.T("common.off")) };
        foreach (double bars in new[] { 1.0, 2, 4, 8, 16, 32 }) followItems.Add((Bars(bars), Loc.F("session.inspector.next_after", FormatLength(Bars(bars)))));
        rows.Children.Add(Row(Loc.T("session.inspector.follow_action"), Chip(follow, p.Follow != 0, a => Pick(a, followItems,
            b => p.Follow == 0 ? b == 0 : Math.Abs(b - p.FollowBeats) < 1e-6,
            b => Set(x => { x.Follow = b > 0 ? 1 : 0; if (b > 0) x.FollowBeats = b; return x; })))));
        rows.Children.Add(Row(Loc.T("session.inspector.select_next_scene_on_launch"), Chip(_selectNextOnLaunch ? "On" : Loc.T("common.off"), _selectNextOnLaunch, _ => { _selectNextOnLaunch = !_selectNextOnLaunch; RebuildInspector(); })));
        s.Children.Add(rows);

        s.Children.Add(Swatches(p.Color, idx => Set(x => { x.Color = idx; return x; })));
        s.Children.Add(Actions(
            (Loc.T("session.launch"), "", () => LaunchSceneRow(scene)),
            (Loc.T("session.inspector.insert_below"), Shortcut.Cmd('I'), () => InsertSceneAtSelection()),
            (Loc.T("common.duplicate"), Shortcut.Cmd('D'), () => DuplicateSelection()),
            (Loc.T("session.inspector.rename"), "F2", () => RenameSelection()),
            (Loc.T("session.inspector.to_arrangement"), Shortcut.CmdShift('C'), () => CopySelectionToArrangement()),
            (Loc.T("common.delete"), Shortcut.None(KeyGlyphs.Backspace), () => DeleteSelection())));
    }

    // ---- empty slot + group slot ------------------------------------------------------------

    private void BuildEmptyInspector(StackPanel s, Column c, int scene)
    {
        bool rec = (_recTrack == c.TrackId && _recScene == scene) || (_recScene >= 0 && _engine.SessionSlotState(c.TrackId, scene) == 4);
        s.Children.Add(Title(NotaPalette.BorderStrong, rec ? Loc.T("session.recording") : Loc.T("session.inspector.empty_slot"), $"{c.Name} · {SceneName(scene)}"));
        bool stop = _engine.GetSessionSlotStopButton(c.TrackId, scene);
        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(Row(Loc.T("session.inspector.stop_button"), Chip(stop ? "On" : Loc.T("session.inspector.removed"), false, _ => { _engine.SetSessionSlotStopButton(c.TrackId, scene, !stop); RefreshSlot(c.TrackId, scene); })));
        rows.Children.Add(new TextBlock
        {
            Text = stop ? Loc.F("session.inspector.launching_this_scene_stops", c.Name) : Loc.F("session.inspector.launching_this_scene_keeps_playing", c.Name),
            FontSize = 10, LineHeight = 16, Foreground = NotaPalette.TextTertiary, TextWrapping = TextWrapping.Wrap,
        });
        s.Children.Add(rows);
        var actions = new List<(string, string, Action)>();
        if (c.IsInstrument) actions.Add((Loc.T("session.insert_midi_clip"), "", () => InsertMidiClip(c.TrackId, scene)));
        actions.Add(rec ? (Loc.T("session.inspector.stop_recording"), "", () => { _engine.StopSessionRecordSlot(c.TrackId); Refresh(); })
                        : (Loc.T("session.record_here"), "", () => { _engine.RecordSessionSlot(c.TrackId, scene); Refresh(); }));
        if (CanPaste) actions.Add((Loc.T("common.paste"), Shortcut.Cmd('V'), () => PasteSelection()));
        s.Children.Add(Actions(actions.ToArray()));
    }

    // A Return / Master column (#314): no slots to launch; its devices are in the panel below.
    private void BuildBusInspector(StackPanel s, Column c)
    {
        s.Children.Add(Title(c.Kind == ColKind.Master ? NotaPalette.Master : c.Brush, c.Name, c.Kind == ColKind.Master ? "MASTER" : "RETURN"));
        s.Children.Add(new TextBlock
        {
            Text = c.Kind == ColKind.Master
                ? Loc.T("session.inspector.every_track_ends_up_here")
                : Loc.T("session.inspector.tracks_reach_it_through_their"),
            FontSize = 11, LineHeight = 16, Foreground = NotaPalette.TextTertiary, TextWrapping = TextWrapping.Wrap,
        });
    }

    private void BuildGroupInspector(StackPanel s, Column g, int scene)
    {
        s.Children.Add(Title(g.Brush, $"{g.Name} · {SceneName(scene)}", Loc.F("session.inspector.group_slot_tracks_caps", g.Children.Count)));
        s.Children.Add(new TextBlock
        {
            Text = Loc.T("session.inspector.launches_every_tracks_slot_in"),
            FontSize = 10, LineHeight = 16, Foreground = NotaPalette.TextTertiary, TextWrapping = TextWrapping.Wrap,
        });
        s.Children.Add(Actions((Loc.T("session.inspector.launch_group_slot"), Shortcut.None(KeyGlyphs.Return), () => LaunchGroup(g, scene)), (Loc.T("session.inspector.stop_group"), "", () => StopColumn(g)),
            (Loc.T("session.inspector.to_arrangement"), Shortcut.CmdShift('C'), () => CopySelectionToArrangement())));
    }

    /// <summary>The selected clip's notes / waveform with a live playhead.</summary>
    private sealed class InspectorPreview : Control
    {
        private readonly SessionView _o;
        private readonly Column _c;
        private readonly int _scene;
        private readonly IBrush _brush;
        private readonly NotaNote[] _notes = Array.Empty<NotaNote>();
        private readonly float[] _peaks = new float[96 * 2];
        private readonly int _peakCount;
        private readonly double _len;

        public InspectorPreview(SessionView o, Column c, int scene, IBrush brush)
        {
            _o = o; _c = c; _scene = scene; _brush = brush;
            _len = o._engine.SessionSlotLength(c.TrackId, scene);
            if (c.IsInstrument) _notes = o._engine.GetSessionNotes(c.TrackId, scene);
            else _peakCount = o._engine.GetSessionSlotPeaks(c.TrackId, scene, _peaks, 96);
        }

        public override void Render(DrawingContext ctx)
        {
            var area = new Rect(10, 10, Math.Max(0, Bounds.Width - 20), Math.Max(0, Bounds.Height - 20));
            if (_len <= 0) return;
            if (_c.IsInstrument && _notes.Length > 0)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                foreach (var n in _notes) { lo = Math.Min(lo, n.Pitch); hi = Math.Max(hi, n.Pitch); }
                int range = Math.Max(1, hi - lo);
                foreach (var n in _notes)
                {
                    if (n.StartBeat >= _len) continue;
                    double x = area.X + n.StartBeat / _len * area.Width;
                    double w = Math.Max(2, Math.Min(n.LengthBeats, _len - n.StartBeat) / _len * area.Width);
                    double y = area.Y + (hi - n.Pitch) / (double)range * (area.Height - 5);
                    ctx.DrawRectangle(_brush, null, new Rect(x, y, w, 5), NotaSkin.MapRadius(1), NotaSkin.MapRadius(1));
                }
            }
            else if (!_c.IsInstrument) DrawPeaks(ctx, area, _peaks, _peakCount, _brush);

            if (_o._engine.SessionSlotState(_c.TrackId, _scene) == 3)
            {
                double x = area.X + Math.Clamp(_o._engine.SessionSlotPosition(_c.TrackId) / _len, 0, 1) * area.Width;
                ctx.DrawLine(PlayheadPen, new Point(Math.Round(x) + 0.5, area.Y - 4), new Point(Math.Round(x) + 0.5, area.Bottom + 4));
            }
        }
    }
}
