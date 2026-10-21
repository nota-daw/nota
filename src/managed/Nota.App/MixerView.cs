// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Mixer (mockup 1f): a dedicated strip mixer as a third Detail mode. One strip
// per track (+ returns + master): name with a 2px colour top, I/O rows, A/B
// sends, a pan knob, a 190px vertical fader with a stereo meter, a dB read-out
// and M/S/Arm. All strips ride the existing engine ops (vol/pan/mute/solo/send/
// meter); meters are pushed each UI tick by MainWindow. Crossfader is M7+. The footer
// also shows and switches the project pan law (specs/006).

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Editing;

namespace Nota.App;

/// <summary>The Mixer window's two views (specs/008).</summary>
public enum MixerViewMode { Strips, StereoMap }

public sealed class MixerView : UserControl
{
    private static readonly IBrush Card = NotaPalette.SurfaceCard;
    private static readonly IBrush MasterBg = NotaPalette.SurfaceCard;   // every strip is the panel tone
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush BorderInner = NotaPalette.Rule;       // the rule that divides inside a strip
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush Danger = NotaPalette.Danger;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush TextDisabled = NotaPalette.TextDisabled;
    private static readonly IBrush OnAccent = NotaPalette.TextOnAccent;

    private readonly IAudioEngine _engine;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(10) };
    private readonly List<(int trackId, MeterBar meter)> _meters = new();
    private readonly List<(int trackId, int bus, MiniFader fader)> _sendFaders = new();
    // Sends / I/O sections are collapsible across every strip (mockup 1f header
    // toggles); we keep the section borders so a toggle can hide them all at once.
    private readonly List<Control> _sendSections = new();
    private readonly List<Control> _ioSections = new();
    private bool _sendsVisible = true, _ioVisible = true;
    // The master volume lives in TransportViewModel.MasterVolume (saved with the project, shown
    // in the MST module and the arrangement's Master row); the master strip goes through these.
    public Func<float>? GetMasterVolume { get; set; }
    public Action<float>? SetMasterVolume { get; set; }
    private VFader? _masterFader;
    private DragNumber? _masterDb;
    // Project pan law (specs/006): the footer's Pan law control, its hint, the Older project
    // badge and a BAL badge on every group strip — all re-synced from the engine, so undo/redo
    // and project load show up without a rebuild.
    public Func<bool>? IsOlderProject { get; set; }
    private Action<int>? _setLawSegment;
    private TextBlock? _lawHint;
    private Control? _olderBadge;
    private readonly List<Control> _balBadges = new();
    private PanLaw? _shownLaw;
    private bool _shownOlder;

    // Strips or the Stereo Map beside a narrower strip column (specs/008). The strips scroller
    // is one instance moved between the two layouts.
    private readonly ScrollViewer _stripsScroller;
    private readonly ContentControl _main = new();
    private MixerViewMode _view = MixerViewMode.Strips;
    /// <summary>The Stereo Map, created the first time the view is shown.</summary>
    public StereoMapView? Map { get; private set; }
    /// <summary>Called once when the map is created, to wire its callbacks.</summary>
    public Action<StereoMapView>? MapCreated { get; set; }

    private int _builtTrackCount = -1;

    /// <summary>Raised after <see cref="Refresh"/> rebuilds the strips, so a host window can
    /// fit its width to them.</summary>
    public event Action? StripsChanged;

    /// <summary>The width the strip row wants (all strips + master + margin), unclipped.</summary>
    public double StripsWidth
    {
        get { _row.Measure(Size.Infinity); return _row.DesiredSize.Width; }
    }

    public MixerView(IAudioEngine engine)
    {
        ValueEntry.SetScope(this, true);   // Tab in a value box walks the values shown here
        _engine = engine;
        _stripsScroller = new ScrollViewer
        {
            Content = _row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var dock = new DockPanel();
        var footer = Footer();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(_main);
        Content = dock;
        ApplyView();
    }

    public MixerViewMode View
    {
        get => _view;
        set { if (_view == value) return; _view = value; ApplyView(); }
    }

    private void ApplyView()
    {
        // Detach the scroller from whichever layout held it before re-parenting.
        if (_stripsScroller.Parent is Panel p) p.Children.Remove(_stripsScroller);
        else if (_stripsScroller.Parent is ContentControl c) c.Content = null;
        if (Map?.Parent is Panel mp) mp.Children.Remove(Map);
        _main.Content = null;

        if (_view == MixerViewMode.Strips)
        {
            _stripsScroller.MaxWidth = double.PositiveInfinity;
            _main.Content = _stripsScroller;
            return;
        }
        if (Map is null) { Map = new StereoMapView(_engine) { MinWidth = 520 }; MapCreated?.Invoke(Map); }
        _stripsScroller.MaxWidth = 440;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(8) };
        grid.Children.Add(Map);
        Grid.SetColumn(_stripsScroller, 1);
        grid.Children.Add(_stripsScroller);
        _main.Content = grid;
    }

    public void Refresh()
    {
        _row.Children.Clear();
        _meters.Clear();
        _sendFaders.Clear();
        _sendSections.Clear();
        _ioSections.Clear();
        _balBadges.Clear();
        int returns = _engine.ReturnTrackCount;
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti)) continue;
            var c = ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(_engine, ti.Id));
            _row.Children.Add(Strip(ti, c, returns));
        }
        _row.Children.Add(MasterStrip());
        _shownLaw = null;   // the strips are new: repaint the law state onto them
        SyncPanLaw();
        _builtTrackCount = n;
        StripsChanged?.Invoke();
    }

    /// <summary>Pushes fresh meter readings to every strip (called at the UI clock).</summary>
    public void UpdateMeters()
    {
        // A track added or removed while the mixer is open gets (or loses) its strip.
        if (_builtTrackCount >= 0 && _engine.TrackCount != _builtTrackCount) Refresh();
        foreach (var (id, meter) in _meters)
        {
            if (id < 0) meter.Push(_engine.MasterMeter());
            else if (_engine.TryGetTrackMeter(id, out var m)) meter.Push(m);
        }
        // Follow master volume changes made elsewhere (MST, Master row, Remote, project load).
        if (GetMasterVolume is { } get && _masterFader is { Dragging: false } f && _masterDb is { } db)
        {
            double v = get();
            if (Math.Abs(f.Value - v) > 1e-3) { f.SetValueExternal(v); db.Value = LinToDb(v); }
        }
        // Sends follow the Devices panel's Sends card, the Session mixer and automation (#138).
        foreach (var (id, bus, send) in _sendFaders)
        {
            if (send.Dragging) continue;
            double v = _engine.GetTrackSend(id, bus);
            if (Math.Abs(send.Value - v) > 1e-3) send.Value = v;
        }
        SyncPanLaw();   // undo/redo or a load elsewhere may have changed it
    }

    // ---- strips -----------------------------------------------------------

    private Control Strip(NotaTrackInfo ti, IBrush color, int returns)
    {
        int id = ti.Id;
        bool isReturn = ti.IsReturn;
        var col = new StackPanel();

        col.Children.Add(Header(TrackNames.Of(_engine, ti), color, Card));

        string io1 = isReturn ? "Return in" : ti.IsGroup ? "Group in" : ti.IsInstrument ? "MIDI in" : "In 1";
        string io2 = isReturn ? "→ Master"
                   : ti.IsGroup ? "→ " + (ti.GroupId >= 0 ? TrackNames.Of(_engine, ti.GroupId) : "Master")
                   : ti.IsInstrument ? "Monitor auto" : "Monitor in";
        // Instrument tracks get a live "MIDI To" selector in place of the static I/O text.
        var ioRow = ti.IsInstrument ? MidiIoRow(id) : IoRow(io1, io2);
        ioRow.IsVisible = _ioVisible;
        _ioSections.Add(ioRow);
        col.Children.Add(ioRow);

        if (!isReturn)
        {
            var sends = SendsRow(id, returns);
            sends.IsVisible = _sendsVisible;
            _sendSections.Add(sends);
            col.Children.Add(sends);
        }

        col.Children.Add(PanRow(id, ti.Pan, ti.IsGroup));
        col.Children.Add(FaderRow(id, ti.Volume, color, out var db, out var meter));
        _meters.Add((id, meter));
        col.Children.Add(db);
        col.Children.Add(ButtonsRow(id, ti, isReturn));

        return new Border
        {
            Width = 154, Background = Card, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Panel, ClipToBounds = true, Child = col,
        };
    }

    private Control MasterStrip()
    {
        var col = new StackPanel();
        col.Children.Add(Header("Master", NotaPalette.Master, MasterBg));
        var ioRow = IoRow("Sum bus", "→ Output");
        ioRow.IsVisible = _ioVisible;
        _ioSections.Add(ioRow);
        col.Children.Add(ioRow);

        Control db;
        var faderRow = FaderRow(-1, GetMasterVolume?.Invoke() ?? 1f, NotaPalette.Master, out db, out var meter);
        _meters.Add((-1, meter));
        col.Children.Add(faderRow);
        col.Children.Add(db);
        // Master has no sends / pan / arm; keep the strip visually aligned with a
        // spacer where the buttons row sits.
        col.Children.Add(new Border { Height = 28, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0) });

        return new Border
        {
            Width = 154, Background = MasterBg, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Panel, ClipToBounds = true, Child = col,
        };
    }

    // ---- footer: section toggles + pan law + crossfader placeholder -------

    private Control Footer()
    {
        var sendsToggle = SectionToggle("Sends", _sendsVisible, on => { _sendsVisible = on; foreach (var s in _sendSections) s.IsVisible = on; });
        var ioToggle = SectionToggle("I/O", _ioVisible, on => { _ioVisible = on; foreach (var s in _ioSections) s.IsVisible = on; });
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "Show", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center }, sendsToggle, ioToggle },
        };

        var divider = new Border { Width = 1, Height = 20, Background = BorderDef, VerticalAlignment = VerticalAlignment.Center };
        var leftSide = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center,
            Children = { left, divider, PanLawGroup() },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(leftSide);
        var xf = Crossfader();
        Grid.SetColumn(xf, 2);
        grid.Children.Add(xf);
        return new Border { Height = 44, Background = MasterBg, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 0), Child = grid };
    }

    private Border SectionToggle(string label, bool initial, Action<bool> set)
    {
        bool on = initial;
        var t = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Height = 22, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Child = t };
        // Toggled on: a solid accent key with derived ink; off: a raised key.
        void Paint() { b.Background = on ? Brass : NotaPalette.SurfaceRaised; b.BorderBrush = on ? Brass : NotaPalette.Seam; t.Foreground = on ? OnAccent : TextSecondary; }
        b.PointerPressed += (_, e) => { e.Handled = true; on = !on; set(on); Paint(); };
        Paint();
        return b;
    }

    // Pan law: the project's pan law as shell segments, a one-line hint, and the Older project
    // badge for projects made before the law existed (they keep Balance until switched).
    private Control PanLawGroup()
    {
        var label = new TextBlock { Text = "Pan law", FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var seg = ClipEditorKit.Segments(new[] { "Stereo", "Balance" }, _engine.PanLaw == PanLaw.Stereo ? 0 : 1, i =>
        {
            var law = i == 0 ? PanLaw.Stereo : PanLaw.Balance;
            if (_engine.PanLaw != law) _engine.SetPanLaw(law);   // one undo step
            SyncPanLaw();
        }, out var set);
        seg.VerticalAlignment = VerticalAlignment.Center;
        _setLawSegment = set;
        if (seg.Child is Panel buttons)
        {
            string[] tips = { "Panning moves the whole stereo image; nothing is dropped", "Panning attenuates the opposite channel (legacy behaviour)" };
            for (int i = 0; i < buttons.Children.Count && i < tips.Length; i++) ToolTip.SetTip(buttons.Children[i], tips[i]);
        }
        _lawHint = new TextBlock { FontSize = 11, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        _olderBadge = Tag("Older project", 9, 0, 18, "Projects saved before stereo pan law open with Balance so existing mixes do not change");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { label, seg, _lawHint, _olderBadge },
        };
    }

    // Repaints the law state (segment, hint, badges) when the engine's law or the
    // older-project mark changed since the last call.
    private void SyncPanLaw()
    {
        var law = _engine.PanLaw;
        bool older = IsOlderProject?.Invoke() == true;
        if (law == _shownLaw && older == _shownOlder) return;
        _shownLaw = law;
        _shownOlder = older;
        bool stereo = law == PanLaw.Stereo;
        _setLawSegment?.Invoke(stereo ? 0 : 1);
        if (_lawHint is not null)
            _lawHint.Text = stereo ? "Pan moves the whole image of stereo tracks and returns"
                                   : "Pan attenuates the opposite channel, as in earlier versions";
        if (_olderBadge is not null) _olderBadge.IsVisible = older && !stereo;
        foreach (var b in _balBadges) b.IsVisible = stereo;
    }

    // A small mono tag in a hairline frame (Older project, BAL), with a tooltip.
    private static Border Tag(string text, double size, double tracking, double height, string tip)
    {
        var t = new TextBlock { Text = text, FontSize = size, LetterSpacing = tracking, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var b = new Border
        {
            Height = height, Padding = new Thickness(size >= 9 ? 6 : 3, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Badge, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = t,
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    // Crossfader is not in the engine yet — a disabled A/B track with an M7+ badge.
    private Control Crossfader()
    {
        var track = new Border { Width = 120, Height = 4, Background = Sunken, CornerRadius = NotaRadius.Clip, VerticalAlignment = VerticalAlignment.Center };
        var knob = new Border { Width = 12, Height = 14, Background = NotaPalette.SurfaceHover, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var host = new Panel { Width = 120, Children = { track, knob } };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, IsEnabled = false,
            Children =
            {
                new TextBlock { Text = "A", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
                host,
                new TextBlock { Text = "B", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { row, new NaBadge { Kind = NaBadgeKind.Future } },
        };
    }

    private Control Header(string name, IBrush topColor, IBrush bg)
    {
        // The strip header: a 3px bar in the track colour on top (muted grey for master).
        var grid = new Grid { RowDefinitions = new RowDefinitions("3,*") };
        grid.Children.Add(new Border { Background = topColor, Height = 3, VerticalAlignment = VerticalAlignment.Top });
        var label = new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(label, 1);
        grid.Children.Add(label);
        return new Border { Height = 24, Background = bg, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private Control IoRow(string io1, string io2) => new Border
    {
        Height = 34, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 4),
        Child = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = io1, FontSize = 8, Foreground = TextTertiary },
                new TextBlock { Text = io2, FontSize = 8, Foreground = TextTertiary },
            },
        },
    };

    // Instrument-track I/O row: a "MIDI In" selector (None, or another instrument track whose
    // MIDI this one receives) over the label. Writes/reads via the engine.
    private Control MidiIoRow(int id)
    {
        var cb = new ComboBox
        {
            FontSize = 8, Height = 16, MinHeight = 0, Padding = new Thickness(5, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var sources = new List<int>();
        void Add(string label, int src)
        {
            cb.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = label, FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis }, Padding = new Thickness(5, 1), MinHeight = 0 });
            sources.Add(src);
        }
        Add("None", -1);
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.IsInstrument && ti.Id != id)
                Add(TrackNames.Of(_engine, ti), ti.Id);
        int cur = _engine.GetTrackMidiSource(id);
        int sel = sources.IndexOf(cur);
        cb.SelectedIndex = sel >= 0 ? sel : 0;
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedIndex >= 0 && cb.SelectedIndex < sources.Count)
                _engine.SetTrackMidiSource(id, sources[cb.SelectedIndex]);
        };
        return new Border
        {
            Height = 34, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 4),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "MIDI in", FontSize = 8, Foreground = TextTertiary },
                    cb,
                },
            },
        };
    }

    private Control SendsRow(int id, int returns)
    {
        var body = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        for (int bus = 0; bus < 2; bus++)
        {
            char letter = (char)('A' + bus);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(new TextBlock { Text = letter.ToString(), FontSize = 8, Foreground = TextTertiary, Width = 8, VerticalAlignment = VerticalAlignment.Center });
            if (bus < returns)
            {
                var f = new MiniFader(_engine.GetTrackSend(id, bus), 1.0) { VerticalAlignment = VerticalAlignment.Center };
                int b = bus;
                f.ValueChanged += v => _engine.SetTrackSend(id, b, (float)v);
                ValueEntry.Attach(f, f.EntryField($"Send {(char)('A' + b)}", NotaNum.Pct));
                _sendFaders.Add((id, b, f));
                Grid.SetColumn(f, 1); grid.Children.Add(f);
            }
            else
            {
                var flat = new Border { Height = 3, Background = NotaPalette.TrackOff, CornerRadius = NotaRadius.Clip, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(flat, 1); grid.Children.Add(flat);
            }
            body.Children.Add(grid);
        }
        return new Border { Height = 42, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 5), Child = body };
    }

    private Control PanRow(int id, float pan, bool isGroup = false)
    {
        var knob = new PanKnob(pan);
        // One undo step per gesture (specs/009); opened before the automation write so a
        // recorded lane and the value undo together, closed after it.
        var undo = new MixGesture(_engine, MixTarget.Pan(id));
        knob.GestureBegin += undo.Begin;
        Action<double> setPan = p => _engine.SetTrackPan(id, (float)p);
        knob.PanChanged += p => undo.Change(p, setPan);
        ValueEntry.Attach(knob, knob.EntryField("Pan", NotaNum.Pan));
        if (id > 0)   // M9-C: record pan moves (master pan isn't an automation target)
        {
            knob.GestureBegin += () => _engine.BeginAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
            knob.GestureEnd   += () => _engine.EndAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
            MidiLearn.Bind(knob, MidiTarget.TrackPan(id), "Track Pan");
        }
        knob.GestureEnd += undo.End;
        Control label = Mono("Pan", TextTertiary);
        if (isGroup)   // groups keep their unity-centre balance whatever the project law (specs/006)
        {
            var bal = Tag("BAL", 7, 0.84, 11, "Groups keep a unity-centre balance so grouping stays level-transparent");
            bal.IsVisible = _engine.PanLaw == PanLaw.Stereo;
            _balBadges.Add(bal);
            label = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { label, bal } };
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { knob, label } };
        return new Border { Height = 38, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    // Fader travel is 0..1.5 linear (VFader.Max), i.e. up to ~+3.5 dB; the dB
    // read-out is drag-editable and double-click-typeable (HANDOFF §4), kept in
    // two-way sync with the fader.
    private const double MaxLin = 1.5;
    private static readonly double MaxDb = AudioMath.LinToDb(MaxLin);
    private const double MinDb = -60.0;
    private static double LinToDb(double v) => v <= 1e-4 ? MinDb : Math.Clamp(AudioMath.LinToDb(v), MinDb, MaxDb);
    private static double DbToLin(double db) => db <= MinDb + 1e-6 ? 0.0 : Math.Clamp(AudioMath.DbToLin(db), 0, MaxLin);

    private Control FaderRow(int id, float vol, IBrush color, out Control db, out MeterBar meter)
    {
        var fader = new VFader(vol, color);
        var dbNum = new DragNumber(LinToDb(vol), MinDb, MaxDb, 0.3, "+0.0;−0.0;0.0", 8)
        {
            Width = 52, HorizontalAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(dbNum, "Set the gain — drag, or double-click to type");
        void SetVolume(double v) { if (id < 0) SetMasterVolume?.Invoke((float)v); else _engine.SetTrackVolume(id, (float)v); }
        if (id < 0) { _masterFader = fader; _masterDb = dbNum; }
        // One undo step per drag of the fader or the dB number, and per reset / typed value
        // (specs/009). Opened before the automation write, closed after it.
        var undo = new MixGesture(_engine, id < 0 ? MixTarget.Master : MixTarget.Volume(id));
        fader.GestureBegin += undo.Begin;
        dbNum.GestureBegin += undo.Begin;
        Action<double> setVol = SetVolume;
        fader.ValueChanged += v => { undo.Change(v, setVol); dbNum.Value = LinToDb(v); };
        dbNum.ValueChanged += d => { double v = DbToLin(d); undo.Change(v, setVol); fader.SetValueExternal(v); };
        if (id > 0)   // M9-C: record volume moves (master volume isn't a track target)
        {
            fader.GestureBegin += () => _engine.BeginAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
            fader.GestureEnd   += () => _engine.EndAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
        }
        fader.GestureEnd += undo.End;
        dbNum.GestureEnd += undo.End;
        MidiLearn.Bind(fader, id > 0 ? MidiTarget.TrackVolume(id) : MidiTarget.MasterVolume, id > 0 ? "Track Volume" : "Master Volume");
        ValueEntry.Attach(fader, fader.EntryField("Volume", v => NotaNum.GainDb(v, signed: true), 1.0));
        db = dbNum;

        meter = new MeterBar();   // the almanac stereo meter: 11 + 5 + 11
        var scaleGrid = new Grid { Width = 16, RowDefinitions = new RowDefinitions("*,*,*,*") };
        string[] marks = { "+6", "0", "−12", "−48" };
        for (int i = 0; i < 4; i++)
        {
            var t = new TextBlock { Text = marks[i], FontSize = 7, Foreground = TextDisabled, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = i == 0 ? VerticalAlignment.Top : i == 3 ? VerticalAlignment.Bottom : VerticalAlignment.Center };
            t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            Grid.SetRow(t, i);
            scaleGrid.Children.Add(t);
        }

        var grid = new Grid { Height = 190, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(7, 8) };
        grid.Children.Add(scaleGrid);
        Grid.SetColumn(fader, 1); grid.Children.Add(fader);
        // Channels lettered under the meter (almanac § level meters), one letter per 11px column.
        var letters = new Grid { ColumnDefinitions = new ColumnDefinitions($"{MeterScale.ChannelW},{MeterScale.Step},{MeterScale.ChannelW}"), Margin = new Thickness(0, 3, 0, 0) };
        foreach (var (txt, col) in new[] { ("L", 0), ("R", 2) })
        {
            var t = new TextBlock { Text = txt, FontSize = NotaType.Axis, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextTertiary, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(t, col); letters.Children.Add(t);
        }
        DockPanel.SetDock(letters, Dock.Bottom);
        var meterCol = new DockPanel { Margin = new Thickness(4, 0, 0, 0), Children = { letters, meter } };
        Grid.SetColumn(meterCol, 2); grid.Children.Add(meterCol);
        return grid;
    }

    private Control ButtonsRow(int id, NotaTrackInfo ti, bool isReturn)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mute = Toggle("M", false, ti.Muted != 0, v => _engine.SetTrackMute(id, v));
        var solo = Toggle("S", false, ti.Soloed != 0, v => _engine.SetTrackSolo(id, v));
        MidiLearn.Bind(mute, MidiTarget.TrackMute(id), "Mute");
        MidiLearn.Bind(solo, MidiTarget.TrackSolo(id), "Solo");
        row.Children.Add(mute);
        row.Children.Add(solo);
        if (!isReturn) row.Children.Add(ArmToggle(ti.Armed != 0, v => _engine.SetTrackArmed(id, v)));
        return new Border { Height = 28, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
    }

    // Record-arm: neutral with a red disc at rest, solid record red with a pale disc when armed.
    private Border ArmToggle(bool initial, Action<bool> set)
    {
        bool on = initial;
        var disc = new Glyph(GlyphKind.Record, 7);
        var b = new Border { Width = 20, Height = 17, CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Child = disc };
        void Paint()
        {
            b.Background = on ? NotaPalette.Record : MasterBg;
            b.BorderBrush = on ? NotaPalette.Record : NotaPalette.Seam;
            disc.Foreground = on ? NotaPalette.RecordInk : NotaPalette.Record;
        }
        b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; e.Handled = true; on = !on; set(on); Paint(); };
        Paint();
        return b;
    }

    private Border Toggle(string label, bool danger, bool initial, Action<bool> set)
    {
        bool on = initial;
        var t = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = 20, Height = 17, CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Child = t };
        void Paint()
        {
            // On: mute amber, solo blue, with derived ink.
            var fill = danger ? NotaPalette.Danger : label == "S" ? NotaPalette.Solo : NotaPalette.Mute;
            b.Background = on ? fill : NotaPalette.SurfaceRaised;
            b.BorderBrush = on ? fill : NotaPalette.Seam;
            t.Foreground = on ? NotaPalette.InkOn(fill) : TextSecondary;
        }
        b.PointerPressed += (_, e) => { e.Handled = true; on = !on; set(on); Paint(); };
        Paint();
        return b;
    }

    private TextBlock Mono(string text, IBrush fg)
    {
        var t = new TextBlock { Text = text, FontSize = 8, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return t;
    }

}
