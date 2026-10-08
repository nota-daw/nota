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

namespace Nota.App;

public sealed partial class SessionView
{
    private static readonly string[] ModeLabels = { "Trigger", "Gate", "Toggle", "Repeat" };
    private static readonly (SessionFollowAction Action, string Label)[] FollowActions =
    {
        (SessionFollowAction.None, "None"), (SessionFollowAction.Next, "Next"), (SessionFollowAction.Again, "Again"),
        (SessionFollowAction.Previous, "Previous"), (SessionFollowAction.First, "First"), (SessionFollowAction.Last, "Last"),
        (SessionFollowAction.Any, "Any"), (SessionFollowAction.Other, "Other"), (SessionFollowAction.Stop, "Stop"),
        (SessionFollowAction.Jump, "Jump"),
    };
    private static readonly (double Beats, string Label)[] LengthSteps =
    {
        (1, "1 beat"), (2, "2 beats"), (4, "1 bar"), (8, "2 bars"), (16, "4 bars"), (32, "8 bars"), (64, "16 bars"),
    };
    private static readonly (double Beats, string Label)[] FollowTimes =
    {
        (0, "One pass"), (1, "1 beat"), (2, "2 beats"), (4, "1 bar"), (8, "2 bars"), (16, "4 bars"), (32, "8 bars"), (64, "16 bars"),
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
            else if (SlotFilled(c.TrackId, _sel.Scene) && !(_recTrack == c.TrackId && _recScene == _sel.Scene)) BuildClipInspector(stack, c, _sel.Scene);
            else BuildEmptyInspector(stack, c, _sel.Scene);
        }

        var hint = new TextBlock
        {
            Text = MenuKit.Keys("Click selects · the triangle launches · Enter launches the selection\n←↑↓→ move · ⌘C ⌘V ⌘D · ⌫ delete · ⌘I insert scene\n⌘⇧C copy to arrangement · ⌘V pastes clips copied there"),
            FontSize = 9, LineHeight = 15, Foreground = NotaPalette.TextDisabled, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16),
        };
        hint.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
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
                new Border { Width = 10, Height = 10, CornerRadius = NotaRadius.Clip, Background = color, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = InspectorW - 60 },
            },
        };
        var s = Mono(sub, 10, NotaPalette.TextTertiary);
        s.TextTrimming = TextTrimming.CharacterEllipsis;
        return new StackPanel { Spacing = 4, Children = { head, s } };
    }

    private static TextBlock SectionHead(string text) => new()
    {
        Text = text, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = NotaPalette.TextMuted,
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
                Height = NotaSize.Shell, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Badge,
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
            Children = { new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = NotaPalette.TextTertiary }, well },
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
    private static Control Row(string label, Control right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 22 };
        g.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(right, 1);
        right.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(right);
        return g;
    }

    private static Button Chip(string value, bool lit, Action<Button> click)
    {
        var b = new Button { Classes = { "chip" }, Height = 22, Padding = new Thickness(8, 0), MinWidth = 0, Content = Mono(value, 10, lit ? NotaPalette.TextOnAccent : NotaPalette.TextPrimary) };
        b.Classes.Set("lit", lit);
        b.Click += (_, _) => click(b);
        return b;
    }

    private static void Pick<T>(Control anchor, IEnumerable<(T Value, string Label)> items, Func<T, bool> isCurrent, Action<T> choose)
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
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.Medium } } };
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
                    Height = 22, Margin = new Thickness(3), CornerRadius = NotaRadius.Badge, Background = ArrangementView.TrackBrush(idx),
                    BorderBrush = on ? NotaPalette.TextPrimary : Brushes.Transparent, BorderThickness = new Thickness(on ? 1.5 : 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                ToolTip.SetTip(sw, on ? "Click again to use the default colour" : null);
                sw.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(sw).Properties.IsLeftButtonPressed) return;
                    e.Handled = true;
                    pick(on ? -1 : idx);
                };
                grid.Children.Add(sw);
            }
        return new StackPanel { Spacing = 7, Children = { SectionHead("COLOR"), grid } };
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
        string kind = c.IsInstrument ? "MIDI" : "AUDIO";
        s.Children.Add(Title(brush, ClipName(c, scene), $"{c.Name.ToUpperInvariant()} · {SceneName(scene).ToUpperInvariant()} · {kind}"));

        _inspPreview = new InspectorPreview(this, c, scene, brush);
        s.Children.Add(new Border
        {
            Height = 68, CornerRadius = NotaRadius.Control, Background = NotaPalette.Wash(brush, 0x1F),
            BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1), ClipToBounds = true, Child = _inspPreview,
        });

        bool loop = p.Loop != 0;
        s.Children.Add(TwoUp(
            Field("LENGTH", FormatLength(len), NotaPalette.TextPrimary, a => Pick(a, LengthSteps, b => Math.Abs(b - len) < 1e-6,
                b => { _engine.SetSessionSlotLength(id, scene, b); RefreshSlot(id, scene); })),
            Field("LOOP", loop ? "On" : "Off · one-shot", loop ? NotaPalette.TextPrimary : NotaPalette.Warning, _ => Set(x => { x.Loop = loop ? 0 : 1; return x; })),
            Field("START", "1.1.1", NotaPalette.TextSecondary, null),
            Field("LOOP END", FormatPosition(len), NotaPalette.TextSecondary, null)));

        var launch = new StackPanel { Spacing = 10 };
        launch.Children.Add(SectionHead("LAUNCH"));
        var modes = Segmented(ModeLabels, Math.Clamp(p.LaunchMode, 0, 3), i => Set(x => { x.LaunchMode = i; return x; }));
        ToolTip.SetTip(modes, "Trigger starts · Gate plays while held · Toggle starts and stops · Repeat retriggers while held");
        launch.Children.Add(modes);
        string quant = p.QuantBeats < 0 ? "Global" : QuantLabel(p.QuantBeats);
        var quantItems = new List<(double, string)> { (-1.0, "Global") };
        foreach (var q in QuantSteps) quantItems.Add(q);
        launch.Children.Add(Row("Quantize", Chip(quant, false, a => Pick(a, quantItems, q => Math.Abs(q - p.QuantBeats) < 1e-6,
            q => Set(x => { x.QuantBeats = q; return x; })))));
        launch.Children.Add(Row("Legato", Chip(p.Legato != 0 ? "On" : "Off", p.Legato != 0, _ => Set(x => { x.Legato = x.Legato != 0 ? 0 : 1; return x; }))));
        var vel = new DragNumber(p.VelocityAmount * 100, 0, 100, 0.5, "0", 10) { Width = 44, Height = 22 };
        OneUndoStep(vel);
        vel.ValueChanged += v => { if (_engine.TryGetSessionClipProps(id, scene, out var cur)) { cur.VelocityAmount = (float)(v / 100); _engine.SetSessionClipProps(id, scene, cur); } };
        ToolTip.SetTip(vel, "Velocity amount: how far the launch velocity scales the clip · drag");
        launch.Children.Add(Row("Velocity %", new Border { CornerRadius = NotaRadius.Badge, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1), Child = vel }));
        s.Children.Add(launch);

        // Follow actions.
        bool follow = _engine.SessionFollow;
        var fa = new StackPanel { Spacing = 10 };
        var faHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        faHead.Children.Add(SectionHead("FOLLOW ACTION"));
        if (!follow) { var off = Mono("OFF GLOBALLY", 9, NotaPalette.TextTertiary); Grid.SetColumn(off, 1); faHead.Children.Add(off); }
        fa.Children.Add(faHead);
        string ActionLabel(int a) => Array.Find(FollowActions, x => (int)x.Action == a).Label ?? "None";
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
                    Children = { new Glyph(GlyphKind.ChevronDown, 7) { Foreground = NotaPalette.TextDisabled, [DockPanel.DockProperty] = Dock.Right }, new TextBlock { Text = ActionLabel(action), FontSize = 11, Foreground = NotaPalette.TextPrimary } },
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
        ToolTip.SetTip(chanceA, "Chance of A in percent · B gets the rest · drag");
        fa.Children.Add(ActionRow("A", p.FollowA, a => Set(x => { x.FollowA = a; return x; }), chanceA));
        fa.Children.Add(ActionRow("B", p.FollowB, a => Set(x => { x.FollowB = a; return x; }), chanceB));
        if (p.FollowA == (int)SessionFollowAction.Jump || p.FollowB == (int)SessionFollowAction.Jump)
        {
            var scenes = new List<(int, string)>();
            for (int i = 0; i < _sceneCount; i++) scenes.Add((i, SceneName(i)));
            fa.Children.Add(Row("Jump to", Chip(SceneName(Math.Clamp(p.JumpScene, 0, Math.Max(0, _sceneCount - 1))), false,
                a => Pick(a, scenes, i => i == p.JumpScene, i => Set(x => { x.JumpScene = i; return x; })))));
        }
        double after = p.FollowBeats > 0 ? p.FollowBeats : len;
        fa.Children.Add(Row("After", Chip(FormatDuration(after), false, a => Pick(a, FollowTimes, b => Math.Abs(b - p.FollowBeats) < 1e-6,
            b => Set(x => { x.FollowBeats = b; return x; })))));
        s.Children.Add(fa);
        if (!follow) Inactive.Set(fa, true, interactive: true);

        s.Children.Add(Swatches(p.Color, idx => Set(x => { x.Color = idx; return x; })));
        s.Children.Add(Actions(
            ("Duplicate", "⌘D", () => DuplicateSelection()),
            ("Rename", "F2", () => RenameSelection()),
            ("To Arrangement", "⌘⇧C", () => CopySelectionToArrangement()),
            ("Edit", "", () => SlotEditRequested?.Invoke(id, scene)),
            ("Delete", "⌫", () => DeleteSelection())));
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
        return NotaNum.Str(beats, "0.##") + " beats";
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
        s.Children.Add(Title(color, SceneName(scene), $"SCENE {scene + 1:00} · {clips} {(clips == 1 ? "clip" : "clips")}"));

        Control tempo;
        if (p.Tempo > 0)
        {
            var dn = new DragNumber(p.Tempo, 20, 999, 0.25, "0.00", 11) { Height = NotaSize.Shell - 2 };
            OneUndoStep(dn);
            dn.ValueChanged += v => { if (_engine.TryGetSceneProps(scene, out var cur)) { cur.Tempo = v; _engine.SetSceneProps(scene, cur); } };
            var clear = new Button { Classes = { "ghost" }, Width = 22, Height = 22, Padding = new Thickness(0), Content = new Glyph(GlyphKind.Close, 8), HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(clear, "Launch without a tempo change");
            clear.Click += (_, _) => Set(x => { x.Tempo = 0; return x; });
            var row = new DockPanel { Children = { clear, dn } };
            DockPanel.SetDock(clear, Dock.Right);
            tempo = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "TEMPO", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = NotaPalette.TextTertiary },
                    new Border { Height = NotaSize.Shell, CornerRadius = NotaRadius.Badge, Background = NotaPalette.BgSunken, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1), Child = row },
                },
            };
            ToolTip.SetTip(tempo, "Tempo set when the scene launches · drag to change");
        }
        else
            tempo = Field("TEMPO", "—", NotaPalette.TextDisabled, _ =>
                Set(x => { x.Tempo = GetTempo?.Invoke().Bpm ?? 120; return x; }));
        string sig = p.SigNum > 0 && p.SigDen > 0 ? $"{p.SigNum}/{p.SigDen}" : "—";
        var sigField = Field("SIGNATURE", sig, p.SigNum > 0 ? NotaPalette.TextPrimary : NotaPalette.TextDisabled, a =>
            Pick(a, Array.ConvertAll(Signatures, x => ((x.Num, x.Den), x.Num == 0 ? "None" : $"{x.Num}/{x.Den}")),
                v => v.Num == p.SigNum && v.Den == p.SigDen, v => Set(x => { x.SigNum = v.Num; x.SigDen = v.Den; return x; })));
        s.Children.Add(TwoUp(tempo, sigField));

        var rows = new StackPanel { Spacing = 8 };
        string follow = p.Follow != 0 ? $"Next · {FormatLength(p.FollowBeats)}" : "Off";
        var followItems = new List<(double, string)> { (0, "Off") };
        foreach (double bars in new[] { 1.0, 2, 4, 8, 16, 32 }) followItems.Add((bars * 4, $"Next after {FormatLength(bars * 4)}"));
        rows.Children.Add(Row("Follow action", Chip(follow, p.Follow != 0, a => Pick(a, followItems,
            b => p.Follow == 0 ? b == 0 : Math.Abs(b - p.FollowBeats) < 1e-6,
            b => Set(x => { x.Follow = b > 0 ? 1 : 0; if (b > 0) x.FollowBeats = b; return x; })))));
        rows.Children.Add(Row("Select next scene on launch", Chip(_selectNextOnLaunch ? "On" : "Off", _selectNextOnLaunch, _ => { _selectNextOnLaunch = !_selectNextOnLaunch; RebuildInspector(); })));
        s.Children.Add(rows);

        s.Children.Add(Swatches(p.Color, idx => Set(x => { x.Color = idx; return x; })));
        s.Children.Add(Actions(
            ("Launch", "", () => LaunchSceneRow(scene)),
            ("Insert below", "⌘I", () => InsertSceneAtSelection()),
            ("Duplicate", "⌘D", () => DuplicateSelection()),
            ("Rename", "F2", () => RenameSelection()),
            ("To Arrangement", "⌘⇧C", () => CopySelectionToArrangement()),
            ("Delete", "⌫", () => DeleteSelection())));
    }

    // ---- empty slot + group slot ------------------------------------------------------------

    private void BuildEmptyInspector(StackPanel s, Column c, int scene)
    {
        bool rec = _recTrack == c.TrackId && _recScene == scene;
        s.Children.Add(Title(NotaPalette.BorderStrong, rec ? "Recording" : "Empty slot", $"{c.Name.ToUpperInvariant()} · {SceneName(scene).ToUpperInvariant()}"));
        bool stop = _engine.GetSessionSlotStopButton(c.TrackId, scene);
        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(Row("Stop button", Chip(stop ? "On" : "Removed", false, _ => { _engine.SetSessionSlotStopButton(c.TrackId, scene, !stop); RefreshSlot(c.TrackId, scene); })));
        rows.Children.Add(new TextBlock
        {
            Text = stop ? $"Launching this scene stops {c.Name}." : $"Launching this scene keeps {c.Name} playing.",
            FontSize = 11, LineHeight = 16, Foreground = NotaPalette.TextTertiary, TextWrapping = TextWrapping.Wrap,
        });
        s.Children.Add(rows);
        var actions = new List<(string, string, Action)>();
        if (c.IsInstrument) actions.Add(("Insert MIDI clip", "", () => InsertMidiClip(c.TrackId, scene)));
        actions.Add(rec ? ("Stop recording", "", () => { _engine.StopSessionRecord(); Refresh(); })
                        : ("Record here", "", () => { _engine.RecordSessionSlot(c.TrackId, scene); Refresh(); }));
        if (CanPaste) actions.Add(("Paste", "⌘V", () => PasteSelection()));
        s.Children.Add(Actions(actions.ToArray()));
    }

    private void BuildGroupInspector(StackPanel s, Column g, int scene)
    {
        s.Children.Add(Title(g.Brush, $"{g.Name} · {SceneName(scene)}", $"GROUP SLOT · {g.Children.Count} TRACKS"));
        s.Children.Add(new TextBlock
        {
            Text = "Launches every track's slot in this scene. The stop button stops them all.",
            FontSize = 11, LineHeight = 16, Foreground = NotaPalette.TextTertiary, TextWrapping = TextWrapping.Wrap,
        });
        s.Children.Add(Actions(("Launch group slot", "↵", () => LaunchGroup(g, scene)), ("Stop group", "", () => StopColumn(g)),
            ("To Arrangement", "⌘⇧C", () => CopySelectionToArrangement())));
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
                    ctx.DrawRectangle(_brush, null, new Rect(x, y, w, 5), 1, 1);
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
