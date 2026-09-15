// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Pattern — the Drum Rack step sequencer. It borrows the look and the feel of
// the Nota Rhythm card (beat-grouped step buttons, shift-click accents, a velocity lane,
// a teal playhead) but lists one row per loaded pad instead of stepping a single voice.
//
// It owns no pattern state: the grid IS the MIDI clip. A lit cell is a note at the pad's
// trigger note, so an edit here rewrites the clip (one undo entry per click, and one per
// drag) and a note drawn in the piano roll lights up here on the next Reload — the two
// views are the same data seen two ways. Only the cells the user actually touches are
// added or removed, so a note that doesn't sit on the grid is drawn in its nearest cell
// and otherwise left exactly as the roll left it.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

public sealed class DrumPatternView : UserControl
{
    private const int BeatsPerBar = 4;     // as everywhere else in the app (transport, props rails)
    private const int MaxCells = 64;       // steps drawn at once; a longer clip pages in windows
    private const double NameW = 156;
    private const double RowH = 20;
    private const double StepW = 28;       // target step width — the grid shrinks below it on a narrow panel
    private const double VelH = 26;
    private const float DefaultVel = 0.78f;
    private const float AccentVel = 1.0f;
    private const float AccentFloor = 0.95f;   // at/above this a hit reads as an accent

    private static readonly int[] Resolutions = { 1, 2, 4, 8 };            // steps per beat
    private static readonly string[] ResNames = { "1/4", "1/8", "1/16", "1/32" };
    // The chosen grid outlives a single clip — switching clips shouldn't reset it.
    private static int _resPersist = 4;

    private readonly IAudioEngine _e;
    private readonly Func<NotaNote[]> _getNotes;
    private readonly Action<NotaNote[]> _setNotes;
    private readonly Action<NotaNote[]>? _setNotesLive;
    private readonly Func<double> _getLength;
    private readonly string _title;

    private List<Pad> _pads = new();
    private readonly List<NotaNote> _notes = new();
    private readonly Dictionary<int, int> _rowOfPitch = new();

    private int _res = _resPersist;
    private int _steps, _winStart, _win;
    private int _selRow = -1;
    private int _paint;                    // 0 = idle, 1 = painting hits in, 2 = painting them out
    private bool _dragging;                // a paint / velocity gesture owns the grid
    private int _playStep = -1;

    private Border[,] _cells = new Border[0, 0];
    private int[,] _cellNote = new int[0, 0];   // index into _notes of the hit in a cell, -1 = empty
    private int[,] _shown = new int[0, 0];      // last painted cell signature (-1 = empty)
    private bool[,] _shownPlay = new bool[0, 0];
    private Border[] _velFills = Array.Empty<Border>();
    private Panel[] _velSlots = Array.Empty<Panel>();
    private Border[] _nameCells = Array.Empty<Border>();
    private TextBlock[] _nameLabels = Array.Empty<TextBlock>();
    private TextBlock? _velLabel;
    private readonly List<Grid> _stepGrids = new();   // ruler + rows + velocity lane, all one width

    /// <summary>The clip was rewritten — refresh the arrangement and any open piano roll.</summary>
    public event Action? Changed;

    public int TrackId { get; }

    private readonly record struct Pad(int Chain, int Note, string Name);

    /// <param name="setNotesLive">Optional no-undo write used for every frame of a drag
    /// after its first edit, so a painted run or a velocity sweep is one undo step and not
    /// one per cell. Falls back to <paramref name="setNotes"/> when the source has none.</param>
    public DrumPatternView(IAudioEngine engine, int trackId, string title,
                           Func<NotaNote[]> getNotes, Action<NotaNote[]> setNotes, Func<double> getLength,
                           Action<NotaNote[]>? setNotesLive = null)
    {
        _e = engine;
        TrackId = trackId;
        _title = title;
        _getNotes = getNotes;
        _setNotes = setNotes;
        _setNotesLive = setNotesLive;
        _getLength = getLength;
        PointerReleased += (_, _) => { _paint = 0; _dragging = false; };
        SizeChanged += (_, _) => ApplyGridWidth();
        Rebuild();
    }

    // ---- outside world ----------------------------------------------------

    /// <summary>Re-read the clip (a piano-roll edit, recording, undo, a project load) and
    /// repaint. Cheap when nothing moved — only cells whose content changed are touched —
    /// and falls back to a full rebuild when the pad set or the clip length changed.</summary>
    public void Reload()
    {
        if (_dragging) return;   // mid drag: our own writes are already on screen
        var pads = ScanPads();
        if (StepCount() != _steps || !SamePads(pads)) { Rebuild(); return; }
        ReadNotes();
        MapCells();
        StyleAll();
    }

    /// <summary>Clip-local playback position, in beats (negative / past the end = silent).</summary>
    public void SetPlayhead(double localBeats, bool playing)
    {
        int step = playing && localBeats >= 0 && localBeats < _getLength() ? (int)Math.Floor(localBeats * _res) : -1;
        if (step == _playStep) return;
        int prev = _playStep;
        _playStep = step;
        RestyleColumn(prev);
        RestyleColumn(step);
    }

    // ---- pads -------------------------------------------------------------

    private List<Pad> ScanPads()
    {
        var pads = new List<Pad>();
        int n = _e.RackChainCount(TrackId);
        for (int c = 0; c < n; c++)
        {
            int note = _e.RackChainTriggerNote(TrackId, c);
            // Prefer the pad's own name (kit voice / dropped sample) — every Sampler pad
            // reports the same instrument name, which makes the grid unreadable.
            if (note >= 0)
            {
                string nm = _e.RackChainName(TrackId, c);
                pads.Add(new Pad(c, note, nm.Length > 0 ? nm : _e.RackChainInstrumentName(TrackId, c)));
            }
        }
        pads.Sort((x, y) => x.Note.CompareTo(y.Note));   // kick (lowest pad) on top, as on the pad grid
        return pads;
    }

    private bool SamePads(List<Pad> pads)
    {
        if (pads.Count != _pads.Count) return false;
        for (int i = 0; i < pads.Count; i++) if (pads[i] != _pads[i]) return false;
        return true;
    }

    // ---- model ------------------------------------------------------------

    private int StepCount() => Math.Max(1, (int)Math.Round(_getLength() * _res));
    private int StepOf(double beat) => (int)Math.Round(beat * _res);
    private double BeatOf(int step) => step / (double)_res;

    private void ReadNotes()
    {
        _notes.Clear();
        _notes.AddRange(_getNotes());
    }

    // (row, step) → the first note sitting in that cell. Notes outside the window, past the
    // clip end or on a pitch no pad claims are simply not mapped — they stay untouched.
    private void MapCells()
    {
        for (int r = 0; r < _cellNote.GetLength(0); r++)
            for (int s = 0; s < _cellNote.GetLength(1); s++) _cellNote[r, s] = -1;
        for (int i = 0; i < _notes.Count; i++)
        {
            var n = _notes[i];
            if (!_rowOfPitch.TryGetValue(n.Pitch, out int r)) continue;
            int s = StepOf(n.StartBeat);
            if (s < _winStart || s >= _winStart + _win || s >= _steps) continue;
            if (_cellNote[r, s - _winStart] < 0) _cellNote[r, s - _winStart] = i;
        }
    }

    // live: a continuing drag — write without an undo checkpoint, the way the piano roll
    // streams a note drag (the gesture's first edit already seeded one).
    private void Commit(bool live = false)
    {
        var arr = _notes.ToArray();
        if (live && _setNotesLive is not null) _setNotesLive(arr); else _setNotes(arr);
        MapCells();
        StyleAll();
        Changed?.Invoke();
    }

    private void AddHit(int r, int step, float velocity)
        => _notes.Add(new NotaNote(_pads[r].Note, BeatOf(step), 1.0 / _res, velocity));

    // Clears the cell completely: a step the user switches off must go quiet even when two
    // notes landed in it (a doubled hit drawn in the piano roll, say).
    private void ClearCell(int r, int step)
    {
        int pitch = _pads[r].Note;
        for (int i = _notes.Count - 1; i >= 0; i--)
            if (_notes[i].Pitch == pitch && StepOf(_notes[i].StartBeat) == step) _notes.RemoveAt(i);
    }

    private void PaintCell(int r, int step, bool on, bool live = false)
    {
        bool isOn = _cellNote[r, step - _winStart] >= 0;
        if (on == isOn) return;
        if (on) AddHit(r, step, DefaultVel); else ClearCell(r, step);
        Commit(live);
    }

    private void AccentCell(int r, int step)
    {
        int idx = _cellNote[r, step - _winStart];
        if (idx < 0) AddHit(r, step, AccentVel);
        else
        {
            var n = _notes[idx];
            n.Velocity = n.Velocity >= AccentFloor ? DefaultVel : AccentVel;
            _notes[idx] = n;
        }
        Commit();
    }

    private void SetCellVelocity(int r, int step, float velocity, bool live = false)
    {
        int idx = _cellNote[r, step - _winStart];
        if (idx < 0) AddHit(r, step, velocity);
        else { var n = _notes[idx]; n.Velocity = velocity; _notes[idx] = n; }
        Commit(live);
    }

    private void ClearPattern()
    {
        int before = _notes.Count;
        for (int i = _notes.Count - 1; i >= 0; i--)
            if (_rowOfPitch.ContainsKey(_notes[i].Pitch)) _notes.RemoveAt(i);
        if (_notes.Count != before) Commit();
    }

    // ---- build ------------------------------------------------------------

    private void Rebuild()
    {
        _pads = ScanPads();
        _steps = StepCount();
        int pages = Math.Max(1, (_steps + MaxCells - 1) / MaxCells);
        _winStart = Math.Clamp(_winStart / MaxCells, 0, pages - 1) * MaxCells;
        _win = Math.Max(1, Math.Min(MaxCells, _steps - _winStart));
        _selRow = _pads.Count == 0 ? -1 : Math.Clamp(_selRow, 0, _pads.Count - 1);
        _playStep = -1;   // the cells are about to be rebuilt; the next tick relights it

        _rowOfPitch.Clear();
        for (int r = 0; r < _pads.Count; r++) _rowOfPitch.TryAdd(_pads[r].Note, r);

        int rows = Math.Max(1, _pads.Count);
        _cells = new Border[rows, _win];
        _cellNote = new int[rows, _win];
        _shown = new int[rows, _win];
        _shownPlay = new bool[rows, _win];
        _nameCells = new Border[rows];
        _nameLabels = new TextBlock[rows];
        _velFills = new Border[_win];
        _velSlots = new Panel[_win];

        ReadNotes();
        _stepGrids.Clear();
        Content = _pads.Count == 0 ? EmptyState() : Body();
        ApplyGridWidth();
        MapCells();
        StyleAll(force: true);
    }

    private Control EmptyState()
        => new Border
        {
            Background = NotaPalette.BgApp,
            Child = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "No pads loaded", FontSize = 12, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Drop samples on the Drum Rack pads in Devices, then step them here.", FontSize = 10, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };

    private Control Body()
    {
        var rows = new StackPanel { Spacing = 2 };
        for (int r = 0; r < _pads.Count; r++) rows.Children.Add(PadRow(r));
        var scroll = new ScrollViewer
        {
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 5, 10, 5),
        };

        var header = Header();
        var ruler = RulerRow();
        var velocity = VelocityRow();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(ruler, Dock.Top);
        DockPanel.SetDock(velocity, Dock.Bottom);
        return new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { header, ruler, velocity, scroll } };
    }

    // ---- header: title · pad count · grid · page · clear ----
    private Control Header()
    {
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Cap("PATTERN"),
                new TextBlock { Text = _title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center },
                Mono($"{_pads.Count} pad{(_pads.Count == 1 ? "" : "s")} · {_steps} steps", TextTertiary),
                Mono("click to place · shift-click accents · drag to paint", NotaPalette.TextDisabled),
            },
        };

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right };
        right.Children.Add(Cap("GRID"));
        right.Children.Add(Seg(ResNames, Array.IndexOf(Resolutions, _res), i =>
        {
            _res = _resPersist = Resolutions[i];
            _winStart = 0;
            Rebuild();
        }));
        if (_steps > MaxCells)
        {
            int pages = (_steps + MaxCells - 1) / MaxCells;
            var names = new string[pages];
            double barSteps = _res * BeatsPerBar;
            for (int p = 0; p < pages; p++)
            {
                int firstBar = (int)(p * MaxCells / barSteps) + 1;
                int lastBar = (int)Math.Ceiling(Math.Min(_steps, (p + 1) * MaxCells) / barSteps);
                names[p] = firstBar == lastBar ? $"{firstBar}" : $"{firstBar}–{lastBar}";
            }
            right.Children.Add(Cap("BARS"));
            right.Children.Add(Seg(names, _winStart / MaxCells, p => { _winStart = p * MaxCells; Rebuild(); }));
        }
        right.Children.Add(Chip("Clear", ClearPattern));

        var dp = new DockPanel { LastChildFill = false, Margin = new Thickness(12, 0), Children = { left, right } };
        return new Border { Height = 30, Background = NotaPalette.SurfaceCard, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(0, 0, 0, 1), Child = dp };
    }

    // ---- the beat ruler above the grid ----
    private Control RulerRow()
    {
        int groups = (_win + _res - 1) / _res;
        var outer = new Grid { ColumnSpacing = 5, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        for (int g = 0; g < groups; g++) outer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int g = 0; g < groups; g++)
        {
            int beat = (_winStart + g * _res) / _res;
            bool bar = beat % BeatsPerBar == 0;
            var tb = Mono(bar ? $"{beat / BeatsPerBar + 1}" : $"{beat / BeatsPerBar + 1}.{beat % BeatsPerBar + 1}",
                          bar ? NotaPalette.AccentBright : NotaPalette.TextDisabled, 7);
            tb.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(tb, g);
            outer.Children.Add(tb);
        }
        SizeGrid(outer);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{NameW},*"), ColumnSpacing = 8, Margin = new Thickness(10, 4, 10, 0) };
        Grid.SetColumn(outer, 1);
        grid.Children.Add(outer);
        return grid;
    }

    // ---- one pad row: name cell + its steps ----
    private Control PadRow(int r)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{NameW},*"), ColumnSpacing = 8, Height = RowH };
        var name = PadName(r);
        var steps = BeatGrid(s => StepCell(r, s));
        Grid.SetColumn(steps, 1);
        grid.Children.Add(name);
        grid.Children.Add(steps);
        return grid;
    }

    private Control PadName(int r)
    {
        var pad = _pads[r];
        var swatch = new Border { Width = 4, Height = 11, CornerRadius = new CornerRadius(1), Background = Hue(pad.Chain), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = pad.Name, FontSize = 9, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var note = Mono(NoteName(pad.Note), TextTertiary, 7);
        note.HorizontalAlignment = HorizontalAlignment.Right;
        var inner = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Margin = new Thickness(6, 0) };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(note, 2);
        inner.Children.Add(swatch);
        inner.Children.Add(name);
        inner.Children.Add(note);

        var cell = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = inner };
        ToolTip.SetTip(cell, $"{pad.Name} · {NoteName(pad.Note)} — click to play it and pick its velocity lane");
        cell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            SelectRow(r);
            _e.SetAuditionTrack(TrackId);   // audition without arming, as the pad grid does
            _e.NoteOn(pad.Note, 1.0f);
            e.Pointer.Capture(cell);
        };
        cell.PointerReleased += (_, e) => { _e.NoteOff(pad.Note); e.Pointer.Capture(null); };
        _nameCells[r] = cell;
        _nameLabels[r] = name;
        return cell;
    }

    private Control StepCell(int r, int step)
    {
        var cell = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) };
        _cells[r, step - _winStart] = cell;
        cell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            SelectRow(r);
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0) { AccentCell(r, step); return; }
            bool wasOn = _cellNote[r, step - _winStart] >= 0;
            _paint = wasOn ? 2 : 1;                 // the first cell decides what the drag does
            _dragging = true;
            PaintCell(r, step, !wasOn);
        };
        cell.PointerEntered += (_, e) =>
        {
            if (_paint == 0) return;
            // A release outside the grid never reaches us, so re-check the button here.
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _paint = 0; _dragging = false; return; }
            PaintCell(r, step, _paint == 1, live: true);
        };
        return cell;
    }

    // ---- velocity lane for the selected row ----
    private Control VelocityRow()
    {
        _velLabel = Mono("", TextTertiary);
        var label = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { Cap("VELOCITY"), _velLabel },
        };
        var lane = BeatGrid(VelocityCell);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{NameW},*"), ColumnSpacing = 8, Height = VelH, Margin = new Thickness(10, 0, 10, 0) };
        Grid.SetColumn(lane, 1);
        grid.Children.Add(label);
        grid.Children.Add(lane);
        return new Border
        {
            Height = VelH + 12, Background = NotaPalette.SurfaceDeep, BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6), Child = grid,
        };
    }

    private Control VelocityCell(int step)
    {
        int local = step - _winStart;
        var fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = NotaPalette.Accent, CornerRadius = new CornerRadius(2) };
        var slot = new Panel { Background = NotaPalette.SurfaceAbyss, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), ClipToBounds = true, Children = { fill } };
        _velFills[local] = fill;
        _velSlots[local] = slot;
        void SetFromY(PointerEventArgs pe, bool live)
        {
            if (_selRow < 0) return;
            double h = Math.Max(1, slot.Bounds.Height);
            float v = (float)Math.Clamp(1.0 - pe.GetPosition(slot).Y / h, 0.02, 1.0);
            SetCellVelocity(_selRow, step, v, live);
        }
        slot.PointerPressed += (_, e) => { e.Handled = true; e.Pointer.Capture(slot); _dragging = true; SetFromY(e, live: false); };
        slot.PointerMoved += (_, e) => { if (e.GetCurrentPoint(slot).Properties.IsLeftButtonPressed) SetFromY(e, live: true); };
        slot.PointerReleased += (_, e) => { e.Pointer.Capture(null); _dragging = false; };
        return new Border { CornerRadius = new CornerRadius(2), ClipToBounds = true, Child = slot };
    }

    // Beat-grouped columns: one flexible column per beat, each holding its steps, so any
    // grid resolution reads as beats at a glance and the whole row stretches to the panel.
    private Control BeatGrid(Func<int, Control> makeCell)
    {
        int groups = (_win + _res - 1) / _res;
        var outer = new Grid { ColumnSpacing = 5 };
        for (int g = 0; g < groups; g++) outer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int g = 0; g < groups; g++)
        {
            int count = Math.Min(_res, _win - g * _res);
            var inner = new Grid { ColumnSpacing = 2 };
            for (int k = 0; k < count; k++) inner.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int k = 0; k < count; k++)
            {
                var c = makeCell(_winStart + g * _res + k);
                Grid.SetColumn(c, k);
                inner.Children.Add(c);
            }
            Grid.SetColumn(inner, g);
            outer.Children.Add(inner);
        }
        SizeGrid(outer);
        return outer;
    }

    // Steps stay drum-machine sized instead of stretching a 16-step bar across a 1500px
    // panel. Star columns inside a left-aligned grid would collapse to nothing (nothing in
    // them has a width of its own), so every step grid — the ruler, the rows, the velocity
    // lane — is given the same explicit width and divides it between its beats.
    private void SizeGrid(Grid outer)
    {
        outer.HorizontalAlignment = HorizontalAlignment.Left;
        _stepGrids.Add(outer);
    }

    private void ApplyGridWidth()
    {
        if (_stepGrids.Count == 0) return;
        int groups = (_win + _res - 1) / _res;
        double natural = (groups - 1) * 5;
        for (int g = 0; g < groups; g++)
        {
            int count = Math.Min(_res, _win - g * _res);
            natural += count * StepW + (count - 1) * 2;
        }
        double avail = Bounds.Width - NameW - 28;   // name column + its gap + the row margins
        double w = avail > 60 ? Math.Min(natural, avail) : natural;
        // An unset Width is NaN, and every comparison against it is false — check for it.
        foreach (var g in _stepGrids) if (double.IsNaN(g.Width) || Math.Abs(g.Width - w) > 0.5) g.Width = w;
    }

    // ---- painting ---------------------------------------------------------

    private void SelectRow(int r)
    {
        if (r == _selRow || r < 0 || r >= _pads.Count) return;
        _selRow = r;
        for (int i = 0; i < _pads.Count; i++) StyleName(i);
        StyleVelocityLane();
    }

    private void StyleAll(bool force = false)
    {
        for (int r = 0; r < _pads.Count; r++)
        {
            StyleName(r);
            for (int s = _winStart; s < _winStart + _win; s++) StyleCell(r, s, force);
        }
        StyleVelocityLane();
    }

    private void StyleName(int r)
    {
        var cell = _nameCells[r];
        if (cell is null) return;
        bool sel = r == _selRow;
        cell.Background = sel ? NotaPalette.AccentSubtle : NotaPalette.SurfaceInset;
        cell.BorderBrush = sel ? NotaPalette.AccentTint : NotaPalette.BorderDefault;
        _nameLabels[r].Foreground = sel ? NotaPalette.AccentBright : NotaPalette.TextPrimary;
    }

    private void StyleCell(int r, int step, bool force = false)
    {
        int local = step - _winStart;
        var cell = _cells[r, local];
        if (cell is null) return;
        int idx = _cellNote[r, local];
        float vel = idx >= 0 ? Math.Clamp(_notes[idx].Velocity, 0f, 1f) : 0f;
        bool on = idx >= 0;
        bool accent = on && vel >= AccentFloor;
        // Velocity is drawn in 8 steps of fill so the washes stay a small cached set —
        // NotaPalette.Wash registers every (slot, alpha) pair for the lifetime of the app.
        int level = on ? Math.Clamp((int)Math.Round(vel * 7), 0, 7) : -1;
        int sig = on ? level * 2 + (accent ? 1 : 0) : -1;
        bool play = step == _playStep;
        if (!force && sig == _shown[r, local] && play == _shownPlay[r, local]) return;
        _shown[r, local] = sig;
        _shownPlay[r, local] = play;

        cell.Background = on
            ? NotaPalette.Wash(Hue(_pads[r].Chain), (byte)(0x50 + level * 0x18))
            : step % (_res * BeatsPerBar) == 0 ? NotaPalette.SurfaceInset
            : step % _res == 0 ? NotaPalette.SurfaceDeep
            : NotaPalette.SurfaceAbyss;
        cell.BorderBrush = play ? NotaPalette.TealBright
            : accent ? NotaPalette.AccentBright
            : on ? Hue(_pads[r].Chain)
            : step % (_res * BeatsPerBar) == 0 ? NotaPalette.BorderDefault
            : NotaPalette.GraphBorder;
        cell.BorderThickness = new Thickness(play ? 2 : 1);
    }

    private void RestyleColumn(int step)
    {
        if (step < _winStart || step >= _winStart + _win) return;
        for (int r = 0; r < _pads.Count; r++) StyleCell(r, step);
    }

    private void StyleVelocityLane()
    {
        if (_velLabel is not null)
            _velLabel.Text = _selRow >= 0 && _selRow < _pads.Count ? _pads[_selRow].Name : "—";
        for (int s = 0; s < _win; s++)
        {
            var fill = _velFills[s];
            var slot = _velSlots[s];
            if (fill is null || slot is null) continue;
            int idx = _selRow >= 0 ? _cellNote[_selRow, s] : -1;
            float vel = idx >= 0 ? Math.Clamp(_notes[idx].Velocity, 0f, 1f) : 0f;
            fill.Height = idx >= 0 ? Math.Max(2, VelH * vel) : 0;
            fill.Background = vel >= AccentFloor ? NotaPalette.AccentBright : NotaPalette.Accent;
            int step = _winStart + s;
            slot.Background = step % (_res * BeatsPerBar) == 0 ? NotaPalette.SurfaceInset : NotaPalette.SurfaceAbyss;
        }
    }

    // ---- small shared bits ------------------------------------------------

    // The palette slot itself, so a theme change repaints the pad hues (a copy would freeze them).
    private static SolidColorBrush Hue(int chain) => NotaPalette.TrackBrushes[((chain % 8) + 8) % 8];

    private static TextBlock Cap(string text)
        => new() { Text = text, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Mono(string text, IBrush colour, double size = 8)
    {
        var tb = new TextBlock { Text = text, FontSize = size, Foreground = colour, VerticalAlignment = VerticalAlignment.Center };
        tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return tb;
    }

    private static Control Seg(string[] names, int sel, Action<int> onSelect)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        for (int i = 0; i < names.Length; i++)
        {
            int iv = i;
            bool on = i == sel;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 1), Cursor = new Cursor(StandardCursorType.Hand),
                Background = on ? NotaPalette.Accent : Brushes.Transparent,
                Child = new TextBlock { Text = names[i], FontSize = 8, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, Foreground = on ? NotaPalette.TextOnAccent : NotaPalette.TextTertiary },
            };
            chip.PointerPressed += (_, e) => { e.Handled = true; onSelect(iv); };
            row.Children.Add(chip);
        }
        return new Border
        {
            Background = NotaPalette.BgSunken, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row,
        };
    }

    private static Control Chip(string text, Action onClick)
    {
        var b = new Border
        {
            BorderThickness = new Thickness(1), BorderBrush = NotaPalette.BorderStrong, Background = NotaPalette.SurfaceRaised,
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 9, Foreground = NotaPalette.TextSecondary },
        };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }
}
