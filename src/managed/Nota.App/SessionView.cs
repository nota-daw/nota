// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session view (mockup 1c): a clip-launch grid of tracks × scenes. A scene rail
// launches/stops whole rows; each track column is a stack of clip slots over a
// mini-mixer (pan / send A / vol / M-S-arm / meter) so you can jam without
// leaving the grid; a master column launches scenes. Slot colour reflects live
// engine state (empty / filled / queued / playing / recording), polled at the
// UI clock by MainWindow via UpdateStates. See ARCHITECTURE.md § UI.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed class SessionView : UserControl
{
    private const double SceneRailW = 168;
    private const double ColW = 126;
    private const double SlotH = 62;
    private const double HeaderH = 38;
    private const double StopNubH = 24;
    private const double Gap = 2;
    private const double Radius = NotaRadius.TileValue;

    // Ember Graphite palette (static so cells can repaint without resource lookups).
    private static readonly IBrush Lane = NotaPalette.SurfaceInset;
    private static readonly IBrush Card = NotaPalette.SurfaceCard;
    private static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Success = NotaPalette.Success;
    private static readonly IBrush Warning = NotaPalette.Warning;
    private static readonly IBrush Warning40 = NotaPalette.Wash(NotaPalette.Warning, 0x66); // queued blink dim (HANDOFF §4)
    private static readonly IBrush Danger = NotaPalette.Danger;
    private static readonly IBrush Record = NotaPalette.Record;   // red belongs to recording
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush TextDisabled = NotaPalette.TextDisabled;
    private static readonly IBrush OnAccent = NotaPalette.TextOnAccent;
    private static readonly IBrush GreenFill = NotaPalette.Wash(NotaPalette.Success, 0x21);
    private static readonly IBrush RedFill = NotaPalette.Wash(NotaPalette.Record, 0x28);
    private static readonly IBrush AmberFill = NotaPalette.Wash(NotaPalette.Warning, 0x1F);

    // Track palette — mirrors ArrangementView.TrackBase (Brush.Track1..8 + Return A/B).
    private static Color[] TrackBase => NotaPalette.TrackColors;

    private readonly IAudioEngine _engine;
    // Three horizontally-aligned strips (same column widths + spacing) so a column's
    // header, slots and mixer line up: headers pin to the top, the slot grid scrolls
    // vertically in the middle, mixers + stop nubs pin to the bottom.
    private readonly StackPanel _headerRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(12, 12, 12, 0) };
    private readonly StackPanel _slotsRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(12, Gap, 12, Gap) };
    private readonly StackPanel _footerRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(12, 0, 12, 12) };
    private readonly List<SlotCell> _cells = new();
    private readonly List<TrackStrip> _strips = new();

    private readonly HashSet<int> _armed = new();   // tracks armed for slot-click recording
    private int _blinkFrame;   // drives the 2Hz queued blink at the UI clock
    private Border? _backToArr; // "Back to Arrangement" — lit while session overrides the timeline

    /// <summary>Raised to edit a filled slot's notes (trackId, scene).</summary>
    public event Action<int, int>? SlotEditRequested;

    /// <summary>Raised after a slot is copied into the arrangement (M5-6).</summary>
    public event Action? ArrangementChanged;

    /// <summary>Raised when a browser item is dropped on a slot (M7-5): item, track id,
    /// scene, and whether the slot's track is an instrument track.</summary>
    public event Action<BrowserItem, int, int, bool>? ItemDropped;
    private void RaiseDrop(BrowserItem item, int trackId, int scene, bool instrument)
        => ItemDropped?.Invoke(item, trackId, scene, instrument);

    public SessionView(IAudioEngine engine)
    {
        _engine = engine;

        // Middle: only the slot grid scrolls vertically (headers/mixers stay pinned).
        var vScroll = new ScrollViewer
        {
            Content = _slotsRow,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var inner = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        inner.Children.Add(_headerRow);                       // row 0 — pinned headers
        Grid.SetRow(vScroll, 1); inner.Children.Add(vScroll); // row 1 — scrolling slots
        Grid.SetRow(_footerRow, 2); inner.Children.Add(_footerRow); // row 2 — pinned mixers

        // Outer: everything scrolls horizontally together so columns stay aligned.
        var hScroll = new ScrollViewer
        {
            Content = inner,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var stack = new DockPanel();
        var bar = BuildActionBar();
        DockPanel.SetDock(bar, Dock.Top);
        stack.Children.Add(bar);
        stack.Children.Add(hScroll);
        Content = stack;
    }

    // Session toolbar row (mockup 1c): + Scene (no engine support yet → N/A),
    // Stop All (live), and a right-aligned follow-actions placeholder (M7+).
    private Control BuildActionBar()
    {
        var addScene = new Border
        {
            Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius), Padding = new Thickness(9, 3),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "+ Scene", FontSize = 11, Foreground = TextSecondary },
        };
        ToolTip.SetTip(addScene, "Add a scene");
        addScene.PointerPressed += (_, _) => { _engine.AddScene(); Refresh(); };
        AddHoverPress(addScene);

        var stopAll = new Border
        {
            Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius), Padding = new Thickness(9, 3),
            Child = new TextBlock { Text = "Stop All", FontSize = 11, Foreground = TextSecondary },
        };
        stopAll.PointerPressed += (_, _) => { _engine.StopAllSession(); _engine.StopSessionRecord(); UpdateStates(); };
        AddHoverPress(stopAll);

        // Lit only while session clips override the Arrangement; click returns every track to
        // the timeline ("Back to Arrangement").
        var backToArr = new Border
        {
            Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius), Padding = new Thickness(9, 3),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = "Back to Arrangement", FontSize = 11, Foreground = NotaPalette.TextDisabled },
        };
        ToolTip.SetTip(backToArr, "Stop session clips and return all tracks to the Arrangement");
        backToArr.PointerPressed += (_, _) => { _engine.BackToArrangement(); UpdateStates(); };
        backToArr.Cursor = new Cursor(StandardCursorType.Hand);   // press dim via its own accent highlight
        _backToArr = backToArr;

        var follow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                new TextBlock { Text = "Follow actions", FontSize = 11, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center },
                new NaBadge { Kind = NaBadgeKind.Future },
            },
        };

        var grid = new Grid { Height = 34, ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(12, 8, 12, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { addScene, stopAll, backToArr } };
        grid.Children.Add(left);
        Grid.SetColumn(follow, 3);
        follow.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(follow);
        return grid;
    }

    public void Refresh()
    {
        _headerRow.Children.Clear();
        _slotsRow.Children.Clear();
        _footerRow.Children.Clear();
        _cells.Clear();
        _strips.Clear();
        int scenes = _engine.SceneCount;

        AddSceneRail(scenes);

        int n = _engine.TrackCount;
        int colorCounter = 0;
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti) || ti.IsReturn || ti.IsGroup) continue;   // groups have no clip slots
            AddTrackColumn(ti, scenes, colorCounter++ % TrackBase.Length);
        }

        AddMasterColumn(scenes);
        UpdateStates();
    }

    /// <summary>Re-reads live slot state + meters and recolours (no rebuild).</summary>
    public void UpdateStates()
    {
        _blinkFrame++;
        bool blinkOn = (_blinkFrame / 8) % 2 == 0;   // ~2Hz at a 30Hz clock
        double pos = _engine.PositionBeats;
        bool sessionOverride = false;
        foreach (var c in _cells)
        {
            int st = _engine.SessionSlotState(c.TrackId, c.Scene);
            if (st >= 2) sessionOverride = true;   // queued / playing / recording → session has the track
            c.ApplyState(st, blinkOn, pos);
        }
        foreach (var s in _strips)
            s.UpdateMeter(_engine);

        // "Back to Arrangement" lights up (and becomes clickable) only while session overrides.
        if (_backToArr is not null)
        {
            _backToArr.IsHitTestVisible = sessionOverride;
            _backToArr.BorderBrush = sessionOverride ? Brass : BorderStrong;
            ((TextBlock)_backToArr.Child!).Foreground = sessionOverride ? AccentBright : NotaPalette.TextDisabled;
        }
    }

    // ---- scene rail -------------------------------------------------------

    private void AddSceneRail(int scenes)
    {
        _headerRow.Children.Add(new Border
        {
            Width = SceneRailW, Height = HeaderH,
            Child = new TextBlock
            {
                Text = "SCENES", FontSize = 10, FontWeight = FontWeight.Bold,
                Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            },
        });

        var mid = new StackPanel { Width = SceneRailW, Spacing = Gap };
        for (int s = 0; s < scenes; s++) mid.Children.Add(SceneRow(s));
        _slotsRow.Children.Add(mid);

        // No bottom controls on the scene rail — a spacer keeps the columns aligned.
        _footerRow.Children.Add(new Border { Width = SceneRailW });
    }

    private Control SceneRow(int scene)
    {
        var launch = IconButton(GlyphKind.Play, AccentBright, 22, 22, 9);
        launch.PointerPressed += (_, _) => { _engine.LaunchScene(scene); UpdateStates(); };
        ToolTip.SetTip(launch, $"Launch scene {scene + 1}");

        var texts = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = $"Scene {scene + 1}", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary },
                new MonoText($"{scene + 1:00}", 9, TextTertiary),
            },
        };

        var stop = IconButton(GlyphKind.Stop, TextTertiary, 14, 14, 6);
        stop.PointerPressed += (_, _) => { _engine.StopScene(scene); UpdateStates(); };
        ToolTip.SetTip(stop, $"Stop scene {scene + 1}");

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { launch, texts } };
        row.Children.Add(content);
        Grid.SetColumn(stop, 2);
        stop.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(stop);

        var cell = new Border
        {
            Height = SlotH, Background = Card, BorderBrush = BorderDef,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Radius),
            Padding = new Thickness(8, 0), Child = row,
        };
        // Right-click a scene row → delete that scene (keeps at least one).
        cell.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(cell).Properties.IsRightButtonPressed) return;
            e.Handled = true;
            var flyout = new MenuFlyout();
            var del = new MenuItem { Header = $"Delete scene {scene + 1}", IsEnabled = _engine.SceneCount > 1 };
            del.Click += (_, _) => { _engine.RemoveScene(scene); Refresh(); };
            flyout.Items.Add(del);
            flyout.ShowAt(cell, showAtPointer: true);
        };
        return cell;
    }

    // ---- track column -----------------------------------------------------

    private void AddTrackColumn(NotaTrackInfo ti, int scenes, int colorIndex)
    {
        var color = NotaPalette.TrackBrushes[colorIndex];
        _headerRow.Children.Add(ColumnHeader((ti.IsInstrument ? "Inst " : "Audio ") + ti.Id, color));

        var mid = new StackPanel { Width = ColW, Spacing = Gap };
        for (int s = 0; s < scenes; s++)
        {
            var slot = new SlotCell(this, ti.Id, s, ti.IsInstrument, color);
            _cells.Add(slot);
            mid.Children.Add(slot.Border);
        }
        _slotsRow.Children.Add(mid);

        // Per-track stop nub + mini-mixer, pinned to the bottom.
        var stop = new Border
        {
            Height = StopNubH, Background = Lane, BorderBrush = Raised,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Radius),
            Child = new Glyph(GlyphKind.Stop, 7) { Foreground = TextTertiary },
        };
        ToolTip.SetTip(stop, "Stop this track");
        stop.PointerPressed += (_, _) => { _engine.StopSlot(ti.Id); UpdateStates(); };
        AddHoverPress(stop);

        _footerRow.Children.Add(new StackPanel { Width = ColW, Spacing = Gap, Children = { stop, BuildMiniMixer(ti, color) } });
    }

    private Control ColumnHeader(string name, IBrush topColor)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("2,*") };
        var spine = new Rectangle { Fill = topColor, Height = 2, Margin = new Thickness(Radius, 0, Radius, 0), VerticalAlignment = VerticalAlignment.Top };
        var label = new TextBlock
        {
            Text = name, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        grid.Children.Add(spine);
        Grid.SetRow(label, 1);
        grid.Children.Add(label);
        return new Border
        {
            Width = ColW, Height = HeaderH, Background = Card, BorderBrush = BorderDef,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Radius), Child = grid,
        };
    }

    private Control BuildMiniMixer(NotaTrackInfo ti, IBrush color)
    {
        int id = ti.Id;
        var vol = new MiniFader(ti.Volume, 1.5);
        vol.ValueChanged += v => _engine.SetTrackVolume(id, (float)v);
        var pan = new MiniFader((ti.Pan + 1) / 2, 1.0);
        pan.ValueChanged += v => _engine.SetTrackPan(id, (float)(v * 2 - 1));

        var db = new MonoText("", 8, TextTertiary);
        void RefreshDb() { double d = AudioMath.LinToDb(Math.Max(1e-4, vol.Value)); db.Text = vol.Value <= 1e-4 ? "−∞" : $"{d:+0.0;−0.0}"; }
        vol.ValueChanged += _ => RefreshDb();
        RefreshDb();

        var meter = new MiniMeter { Height = 4 };
        var strip = new TrackStrip { TrackId = id, Meter = meter };
        _strips.Add(strip);

        var mute = MixToggle("M", false, ti.Muted != 0, v => _engine.SetTrackMute(id, v));
        var solo = MixToggle("S", false, ti.Soloed != 0, v => _engine.SetTrackSolo(id, v));
        if (ti.Armed != 0) _armed.Add(id);
        var arm = ArmToggle(ti.Armed != 0, v =>
        {
            _engine.SetTrackArmed(id, v);
            if (v) _armed.Add(id); else _armed.Remove(id);
            UpdateStates();   // empty audio slots show/hide their record affordance on arm
        });

        var btnRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 3 };
        btnRow.Children.Add(mute);
        Grid.SetColumn(solo, 1); btnRow.Children.Add(solo);
        Grid.SetColumn(arm, 2); btnRow.Children.Add(arm);
        var meterWrap = new Border { Height = 4, Background = Sunken, CornerRadius = NotaRadius.Clip, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Child = meter };
        Grid.SetColumn(meterWrap, 3); meterWrap.Margin = new Thickness(2, 0, 0, 0);
        btnRow.Children.Add(meterWrap);

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(FaderRow("PAN", pan, null));
        // One send fader per existing return bus (A, B, …) — nothing when there are no returns.
        int returns = Math.Min(_engine.ReturnTrackCount, 4);
        for (int b = 0; b < returns; b++)
        {
            int bus = b;
            var send = new MiniFader(_engine.GetTrackSend(id, bus), 1.0);
            send.ValueChanged += v => _engine.SetTrackSend(id, bus, (float)v);
            body.Children.Add(FaderRow(((char)('A' + bus)).ToString(), send, null));
        }
        body.Children.Add(FaderRow("VOLUME", vol, db));
        body.Children.Add(btnRow);
        return new Border
        {
            Background = Card, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius), Padding = new Thickness(7), Child = body,
        };
    }

    private Control FaderRow(string label, MiniFader fader, TextBlock? trailing)
    {
        fader.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(new TextBlock { Text = label, FontSize = 8, Foreground = TextTertiary, Width = 32, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(fader, 1);
        grid.Children.Add(fader);
        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 2);
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.Margin = new Thickness(4, 0, 0, 0);
            grid.Children.Add(trailing);
        }
        return grid;
    }

    // Record-arm: neutral with a red disc at rest, solid record red with a pale disc when armed.
    private Border ArmToggle(bool initial, Action<bool> set)
    {
        bool on = initial;
        var disc = new Glyph(GlyphKind.Record, 6);
        var b = new Border { Width = 17, Height = 15, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Child = disc };
        void Paint()
        {
            b.Background = on ? Record : Raised;
            b.BorderBrush = on ? Record : BorderStrong;
            disc.Foreground = on ? NotaPalette.RecordInk : Record;
        }
        b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; e.Handled = true; on = !on; set(on); Paint(); };
        Paint();
        return b;
    }

    private Border MixToggle(string label, bool danger, bool initial, Action<bool> set)
    {
        bool on = initial;
        var t = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = 17, Height = 15, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Child = t };
        void Paint()
        {
            var accent = danger ? Danger : Brass;
            b.Background = on ? accent : Raised;
            b.BorderBrush = on ? accent : BorderStrong;
            t.Foreground = on ? OnAccent : TextSecondary;
        }
        b.PointerPressed += (_, e) => { e.Handled = true; on = !on; set(on); Paint(); };
        Paint();
        return b;
    }

    // ---- master column ----------------------------------------------------

    private void AddMasterColumn(int scenes)
    {
        var header = ColumnHeader("Master", Brass);
        ((Border)header).Background = Raised;
        _headerRow.Children.Add(header);

        var mid = new StackPanel { Width = ColW, Spacing = Gap };
        for (int s = 0; s < scenes; s++)
        {
            int scene = s;
            var launch = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Glyph(GlyphKind.Play, 9) { Foreground = AccentBright },
                    new TextBlock { Text = $"Scene {scene + 1}", FontSize = 10, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center },
                },
            };
            var row = new Border
            {
                Height = SlotH, Background = Raised, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Radius), Padding = new Thickness(8, 0), Child = launch,
            };
            row.PointerPressed += (_, _) => { _engine.LaunchScene(scene); UpdateStates(); };
            AddHoverPress(row);
            mid.Children.Add(row);
        }
        _slotsRow.Children.Add(mid);

        var stopAll = new Border
        {
            Height = StopNubH, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Glyph(GlyphKind.Stop, 7) { Foreground = TextSecondary },
                    new TextBlock { Text = "Stop All", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        stopAll.PointerPressed += (_, _) => { _engine.StopAllSession(); _engine.StopSessionRecord(); UpdateStates(); };
        AddHoverPress(stopAll);
        // Pin Stop All to the very bottom so it aligns with the track mixers' base.
        _footerRow.Children.Add(new StackPanel { Width = ColW, VerticalAlignment = VerticalAlignment.Bottom, Children = { stopAll } });
    }

    // ---- helpers ----------------------------------------------------------

    private static Border IconButton(GlyphKind glyph, IBrush fg, double w, double h, double glyphSize = 9)
    {
        var b = new Border
        {
            Width = w, Height = h, Background = Raised, BorderBrush = BorderStrong,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Radius),
            Child = new Glyph(glyph, glyphSize) { Foreground = fg },
        };
        AddHoverPress(b);
        return b;
    }

    // Manual hover/press feedback for the hand-built Border "buttons" (these aren't themed
    // Buttons, so they'd otherwise be visually inert): brass outline on hover, recess on press.
    private static void AddHoverPress(Border b)
    {
        var restBorder = b.BorderBrush;
        b.Cursor = new Cursor(StandardCursorType.Hand);
        b.PointerEntered  += (_, _) => { b.BorderBrush = Brass; };
        var restBg = b.Background;
        b.PointerExited   += (_, _) => { b.BorderBrush = restBorder; b.Background = restBg; };
        // Pressed goes into the recess (almanac § States) — the ground changes, nothing fades.
        b.PointerPressed  += (_, _) => { b.Background = NotaPalette.BgSunken; };
        b.PointerReleased += (_, _) => { b.Background = restBg; };
    }

    private sealed class MonoText : TextBlock
    {
        public MonoText(string text, double size, IBrush fg)
        {
            Text = text; FontSize = size; Foreground = fg;
            this.BindResource(FontFamilyProperty, "Font.Mono");
        }
    }

    // A live mini-mixer strip: keeps a handle to its meter for per-frame updates.
    private sealed class TrackStrip
    {
        public int TrackId;
        public MiniMeter Meter = null!;
        public void UpdateMeter(IAudioEngine eng)
            => Meter.SetLevel(eng.TryGetTrackMeter(TrackId, out var m) ? m.Peak : 0);
    }

    // A thin horizontal peak meter (green), width-agnostic.
    private sealed class MiniMeter : Control
    {
        private double _level;
        public void SetLevel(double v) { v = Math.Clamp(v, 0, 1); if (Math.Abs(v - _level) < 0.005) return; _level = v; InvalidateVisual(); }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || _level <= 0) return;
            ctx.FillRectangle(Success, new Rect(0, 0, w * _level, h));
        }
    }

    // A single clip slot: owns its Border + repaints on state changes.
    private sealed class SlotCell
    {
        public int TrackId { get; }
        public int Scene { get; }
        public Border Border { get; }

        private readonly SessionView _owner;
        private readonly bool _instrument;
        private readonly IBrush _trackColor;
        private readonly IBrush _trackBorder;   // track colour @ ~45%
        private readonly Glyph _icon;
        private readonly TextBlock _label;
        private readonly MonoText _badge;
        private readonly Rectangle _progress;
        private int _state = -2;
        private bool _lastBlink;
        private bool _lastArmed;   // empty audio slots repaint their record affordance on arm changes

        public SlotCell(SessionView owner, int trackId, int scene, bool instrument, ISolidColorBrush trackColor)
        {
            _owner = owner;
            TrackId = trackId;
            Scene = scene;
            _instrument = instrument;
            _trackColor = trackColor;
            _trackBorder = trackColor is SolidColorBrush slot
                ? NotaPalette.Wash(slot, 0x73)
                : new SolidColorBrush(Color.FromArgb(0x73, trackColor.Color.R, trackColor.Color.G, trackColor.Color.B));

            _icon = new Glyph(GlyphKind.Play, 9);
            _label = new TextBlock { FontSize = 10, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _badge = new MonoText("", 8, TextTertiary) { VerticalAlignment = VerticalAlignment.Center };

            var topRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Top };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { _icon, _label } };
            topRow.Children.Add(head);
            Grid.SetColumn(_badge, 2);
            topRow.Children.Add(_badge);

            _progress = new Rectangle { Height = 2, Fill = Success, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Width = 0 };

            Border = new Border
            {
                Width = ColW, Height = SlotH, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Radius),
                Padding = new Thickness(7, 6), ClipToBounds = true,
                Child = new Panel { Children = { topRow, _progress } },
            };
            Border.PointerPressed += OnPressed;

            // Accept both browser rows and external audio files (Finder / other apps) — same
            // path the arrangement uses, so a slot fills from either source.
            IBrush? savedBorder = null;
            DragDrop.SetAllowDrop(Border, true);
            DragDrop.AddDragOverHandler(Border, (_, e) =>
                e.DragEffects = BrowserView.IsAcceptableDrag(e) ? DragDropEffects.Copy : DragDropEffects.None);
            DragDrop.AddDragEnterHandler(Border, (_, e) =>
            {
                if (!BrowserView.IsAcceptableDrag(e)) return;
                savedBorder = Border.BorderBrush; Border.BorderBrush = Brass;   // highlight the target
            });
            DragDrop.AddDragLeaveHandler(Border, (_, _) =>
            { if (savedBorder is not null) { Border.BorderBrush = savedBorder; savedBorder = null; } });
            DragDrop.AddDropHandler(Border, (_, e) =>
            {
                if (savedBorder is not null) { Border.BorderBrush = savedBorder; savedBorder = null; }
                var items = BrowserView.DroppedItems(e);
                if (items.Count > 0)
                { _owner.RaiseDrop(items[0], TrackId, Scene, _instrument); e.Handled = true; }
            });
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            // Right-click a filled slot (any type) → its context menu (delete, + more for MIDI).
            if (_state >= 1 && e.GetCurrentPoint(Border).Properties.IsRightButtonPressed)
            { ShowSlotMenu(); return; }

            // Click a recording slot → stop recording (the take materialises and loops back).
            if (_state == 4)
            { _owner._engine.StopSessionRecord(); _owner.Refresh(); return; }

            // Armed track: click a slot to record into it (overdub MIDI / capture
            // audio input, M5-4). Per-track arm from the mini-mixer.
            if (_owner._armed.Contains(TrackId))
            { _owner._engine.RecordSessionSlot(TrackId, Scene); _owner.Refresh(); return; }

            bool filled = _state >= 1;
            if (filled)
            {
                // Double-click edits (piano roll for MIDI, audio-slot editor for audio); single
                // click launches.
                if (e.ClickCount == 2) _owner.SlotEditRequested?.Invoke(TrackId, Scene);
                else _owner._engine.LaunchSlot(TrackId, Scene);
                _owner.UpdateStates();
            }
            else if (_instrument)
            {
                _owner._engine.AddSessionMidiClip(TrackId, Scene, 4.0);
                _owner.Refresh();
            }
            else if (e.GetCurrentPoint(Border).Properties.IsLeftButtonPressed)
            {
                // Empty audio slot: one-click record — arm the track first if it isn't already.
                if (!_owner._armed.Contains(TrackId))
                { _owner._engine.SetTrackArmed(TrackId, true); _owner._armed.Add(TrackId); }
                _owner._engine.RecordSessionSlot(TrackId, Scene);
                _owner.Refresh();
            }
        }

        private void ShowSlotMenu()
        {
            var flyout = new MenuFlyout();
            if (_instrument)
            {
                var lengths = new MenuItem { Header = "Loop length" };
                foreach (double beats in new[] { 1.0, 2.0, 4.0, 8.0, 16.0 })
                {
                    double b = beats;
                    var item = new MenuItem { Header = $"{b:0.#} beats" };
                    item.Click += (_, _) => { _owner._engine.SetSessionSlotLength(TrackId, Scene, b); _owner.UpdateStates(); };
                    lengths.Items.Add(item);
                }
                flyout.Items.Add(lengths);
                var toArr = new MenuItem { Header = "Copy to arrangement (at playhead)" };
                toArr.Click += (_, _) =>
                {
                    _owner._engine.SessionSlotToArrangement(TrackId, Scene, _owner._engine.PositionBeats);
                    _owner.ArrangementChanged?.Invoke();
                };
                flyout.Items.Add(toArr);
                flyout.Items.Add(new Separator());
            }
            var del = new MenuItem { Header = "Delete clip" };
            del.Click += (_, _) => { _owner._engine.ClearSessionSlot(TrackId, Scene); _owner.Refresh(); };
            flyout.Items.Add(del);
            flyout.ShowAt(Border, showAtPointer: true);
        }

        // 0 empty, 1 filled, 2 queued, 3 playing, 4 recording.
        public void ApplyState(int state, bool blinkOn, double posBeats)
        {
            // Loop-length badge (M5-5) + progress bar update every call (live).
            double len = state >= 1 ? _owner._engine.SessionSlotLength(TrackId, Scene) : 0;
            _badge.Text = len > 0 ? $"{len:0.#}b" : "";

            if (state == 3 && len > 0)
            {
                double frac = (posBeats % len) / len;
                _progress.Width = Math.Clamp(frac, 0, 1) * (ColW - 2);
            }
            else _progress.Width = 0;

            // An empty audio slot's affordance depends on the track's arm state, so it must
            // also repaint when arming toggles (not just on a slot-state change).
            bool armed = _owner._armed.Contains(TrackId);
            bool armAffordance = state == 0 && !_instrument;

            // Queued cells blink at ~2Hz; repaint on state / blink-phase / arm change.
            if (state == _state && (state != 2 || blinkOn == _lastBlink)
                && (!armAffordance || armed == _lastArmed)) return;
            _state = state;
            _lastBlink = blinkOn;
            _lastArmed = armed;
            ToolTip.SetTip(Border, state == 4 ? "Click to stop recording" : null);

            switch (state)
            {
                case 4: // recording
                    Border.Background = RedFill; Border.BorderBrush = Record;
                    _icon.Kind = GlyphKind.Record; _icon.IsVisible = true; _icon.Foreground = Record;
                    _label.Text = "rec"; _label.Foreground = Record;
                    break;
                case 3: // playing
                    Border.Background = GreenFill; Border.BorderBrush = Success;
                    _icon.Kind = GlyphKind.Play; _icon.IsVisible = true; _icon.Foreground = Success;
                    _label.Text = "Clip"; _label.Foreground = TextPrimary;
                    break;
                case 2: // queued — border blinks 100%↔40% at ~2Hz (HANDOFF §4)
                    Border.Background = AmberFill;
                    Border.BorderBrush = blinkOn ? Warning : Warning40;
                    _icon.Kind = GlyphKind.Play; _icon.IsVisible = true; _icon.Foreground = Warning;
                    _label.Text = "Clip"; _label.Foreground = Warning;
                    break;
                case 1: // filled
                    Border.Background = Raised; Border.BorderBrush = _trackBorder;
                    _icon.Kind = GlyphKind.Play; _icon.IsVisible = true; _icon.Foreground = _trackColor;
                    _label.Text = "Clip"; _label.Foreground = TextPrimary;
                    break;
                default: // empty
                    Border.Background = Lane; Border.BorderBrush = BorderDef;
                    if (_instrument)
                    {
                        // Instrument: "+" to create a MIDI clip.
                        _icon.IsVisible = false;
                        _label.Text = "+"; _label.Foreground = TextDisabled;
                    }
                    else if (armed)
                    {
                        // Armed audio track: a red record dot — click to capture input here.
                        _icon.Kind = GlyphKind.Record; _icon.IsVisible = true; _icon.Foreground = Record;
                        _label.Text = "Rec"; _label.Foreground = Record;
                    }
                    else
                    {
                        // Idle audio slot: a faint hollow ring hints it's a record / drop target.
                        _icon.Kind = GlyphKind.RecordRing; _icon.IsVisible = true; _icon.Foreground = TextDisabled;
                        _label.Text = ""; _label.Foreground = TextDisabled;
                    }
                    break;
            }
        }
    }
}
