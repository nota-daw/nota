// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · Sections — the song-structure lane between the Overview and the ruler
// (design 1a, "structure first"). A bar of numbers says nothing about a song; a row of named
// spans — Intro, Verse, Drop — says where you are. Sections are authoring metadata, not
// audio: they live in the view and ride in the bundle as a sidecar, so a project written by
// an older build still opens and simply has none.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Nota.App;

/// <summary>A named span of arrangement time (Intro / Verse A / Drop …).</summary>
public sealed class ArrangementSection
{
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; }
    public string Name { get; set; } = "";
}

public sealed partial class ArrangementView
{
    private readonly List<ArrangementSection> _sections = new();
    private readonly SectionsControl _sectionsLane;
    private Border _sectionsRow = null!;

    /// <summary>Show/hide the Sections lane (View ▸ Toggle sections).</summary>
    public bool ShowSections
    {
        get => _sectionsRow.IsVisible;
        set
        {
            if (_sectionsRow.IsVisible == value) return;
            _sectionsRow.IsVisible = value;
            if (value) _sectionsLane.InvalidateVisual();
        }
    }

    /// <summary>Raised whenever the section list changes, so the host can mark the project dirty.</summary>
    public event Action? SectionsChanged;

    // The lane row: the same header-column chrome as the ruler, then the spans themselves.
    private Border BuildSectionsRow()
    {
        var label = new TextBlock
        {
            Text = "SECTIONS", Classes = { "SectionLabel" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var grid = new Grid { Height = SectionsH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(HeaderPanel(label));
        Grid.SetColumn(_sectionsLane, 1);
        grid.Children.Add(_sectionsLane);

        var row = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        row.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        return row;
    }

    // ---- model ------------------------------------------------------------

    /// <summary>The sections, in start order (what the bundle sidecar persists).</summary>
    public IReadOnlyList<ArrangementSection> SectionList
    {
        get { _sections.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat)); return _sections; }
    }

    /// <summary>Replace the whole list (project load).</summary>
    public void SetSections(IEnumerable<ArrangementSection> sections)
    {
        _sections.Clear();
        foreach (var s in sections)
            if (s.LengthBeats > 0) _sections.Add(new ArrangementSection { StartBeat = Math.Max(0, s.StartBeat), LengthBeats = s.LengthBeats, Name = s.Name });
        _sections.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
        _sectionsLane.InvalidateVisual();
    }

    /// <summary>Drop every section (File ▸ New).</summary>
    public void ClearSections()
    {
        if (_sections.Count == 0) return;
        _sections.Clear();
        _sectionsLane.InvalidateVisual();
    }

    private void SectionsEdited()
    {
        _sections.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
        _sectionsLane.InvalidateVisual();
        SectionsChanged?.Invoke();
    }

    // A new section is named for the slot it lands in, so a session gets Section 1..n without
    // a dialog in the way; renaming is one double-click.
    private string NextSectionName()
    {
        for (int i = 1; ; i++)
        {
            string candidate = "Section " + i.ToString(CultureInfo.InvariantCulture);
            if (!_sections.Any(s => string.Equals(s.Name, candidate, StringComparison.Ordinal))) return candidate;
        }
    }

    private ArrangementSection AddSection(double startBeat, double lengthBeats)
    {
        var s = new ArrangementSection
        {
            StartBeat = Math.Max(0, startBeat),
            LengthBeats = Math.Max(_beatsPerBar, lengthBeats),
            Name = NextSectionName(),
        };
        _sections.Add(s);
        SectionsEdited();
        return s;
    }

    private void RemoveSection(ArrangementSection s)
    {
        if (!_sections.Remove(s)) return;
        SectionsEdited();
    }

    // Snap a section edge to whole bars — song structure is bar-aligned, always, so this
    // ignores the clip snap grid (which is often finer than a bar).
    private double SnapBar(double beat)
    {
        double bar = Math.Max(1, _beatsPerBar);
        return Math.Max(0, Math.Round(beat / bar) * bar);
    }

    private void RenameSection(Control anchor, ArrangementSection s)
    {
        var box = new TextBox { Text = s.Name, Width = 160, FontSize = 12 };
        var flyout = new Flyout { Content = box, Placement = PlacementMode.Bottom };
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) { s.Name = box.Text ?? ""; SectionsEdited(); flyout.Hide(); ke.Handled = true; }
            else if (ke.Key == Key.Escape) { flyout.Hide(); ke.Handled = true; }
        };
        flyout.ShowAt(anchor);
        Dispatcher.UIThread.Post(() => { box.SelectAll(); box.Focus(); }, DispatcherPriority.Input);
    }

    // ---- the lane ---------------------------------------------------------

    private sealed class SectionsControl : Control
    {
        private static readonly IBrush LaneBg = NotaPalette.Wash(NotaPalette.BgSunken, 0xFF);
        // Spans alternate between a brass-marked and a neutral treatment, so a long song
        // reads as a rhythm of blocks rather than one continuous ribbon of labels.
        private static readonly IBrush WarmFill = NotaPalette.Wash(NotaPalette.Accent, 0x33);
        private static readonly IBrush ColdFill = NotaPalette.Wash(NotaPalette.SurfaceRaised, 0xFF);
        private static readonly IBrush WarmEdge = NotaPalette.AccentBright;
        private static readonly IBrush ColdEdge = NotaPalette.BorderStrong;
        private static readonly IBrush WarmInk = NotaPalette.AccentBright;
        private static readonly IBrush ColdInk = NotaPalette.TextSecondary;
        private static readonly IBrush HintInk = NotaPalette.TextDisabled;
        private static readonly IBrush DraftFill = NotaPalette.Wash(NotaPalette.AccentBright, 0x28);
        private static readonly IPen DraftPen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xC0), 1);
        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
        private static readonly Cursor EdgeCursor = new(StandardCursorType.SizeWestEast);

        private const double EdgeGrab = 4;   // px around a span edge that grabs it

        private enum Mode { None, Draft, Move, EdgeLeft, EdgeRight }

        private readonly ArrangementView _o;
        private Mode _mode;
        private ArrangementSection? _active;
        private double _pressBeat, _grabOffset, _fixedBeat;
        private bool _moved;

        public SectionsControl(ArrangementView o)
        {
            _o = o;
            ClipToBounds = true;
            Cursor = HandCursor;
        }

        private double BeatAt(double x) => _o._scrollBeats + x / _o._pixelsPerBeat;

        private ArrangementSection? HitTest(double x, out bool onLeft, out bool onRight)
        {
            onLeft = onRight = false;
            foreach (var s in _o._sections)
            {
                double x0 = _o.BeatToX(s.StartBeat), x1 = _o.BeatToX(s.StartBeat + s.LengthBeats);
                if (x < x0 - EdgeGrab || x > x1 + EdgeGrab) continue;
                bool wide = x1 - x0 > 3 * EdgeGrab;
                onLeft = wide && Math.Abs(x - x0) <= EdgeGrab;
                onRight = wide && Math.Abs(x - x1) <= EdgeGrab;
                return s;
            }
            return null;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            double x = pt.Position.X;
            var hit = HitTest(x, out bool onLeft, out bool onRight);

            if (pt.Properties.IsRightButtonPressed)
            {
                ShowMenu(hit, BeatAt(x));
                e.Handled = true;
                return;
            }
            if (!pt.Properties.IsLeftButtonPressed) return;

            if (hit is not null && e.ClickCount == 2) { _o.RenameSection(this, hit); e.Handled = true; return; }

            _pressBeat = BeatAt(x);
            _moved = false;
            _active = hit;
            if (hit is null) _mode = Mode.Draft;
            else if (onLeft) { _mode = Mode.EdgeLeft; _fixedBeat = hit.StartBeat + hit.LengthBeats; }
            else if (onRight) { _mode = Mode.EdgeRight; _fixedBeat = hit.StartBeat; }
            else { _mode = Mode.Move; _grabOffset = _pressBeat - hit.StartBeat; }
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var pos = e.GetPosition(this);
            if (_mode == Mode.None)
            {
                HitTest(pos.X, out bool l, out bool r);
                Cursor = l || r ? EdgeCursor : HandCursor;
                return;
            }

            double beat = BeatAt(pos.X);
            if (!_moved && Math.Abs(beat - _pressBeat) * _o._pixelsPerBeat > 3) _moved = true;
            if (!_moved) return;

            switch (_mode)
            {
                case Mode.Draft:
                    _draftEnd = beat;     // the draft band is drawn from _pressBeat to here
                    InvalidateVisual();
                    break;
                case Mode.Move when _active is not null:
                    _active.StartBeat = _o.SnapBar(beat - _grabOffset);
                    InvalidateVisual();
                    break;
                case Mode.EdgeLeft when _active is not null:
                {
                    double start = Math.Min(_o.SnapBar(beat), _fixedBeat - _o._beatsPerBar);
                    _active.LengthBeats = _fixedBeat - start;
                    _active.StartBeat = start;
                    InvalidateVisual();
                    break;
                }
                case Mode.EdgeRight when _active is not null:
                    _active.LengthBeats = Math.Max(_o._beatsPerBar, _o.SnapBar(beat) - _fixedBeat);
                    InvalidateVisual();
                    break;
            }
        }

        private double _draftEnd;

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            var mode = _mode;
            var active = _active;
            bool moved = _moved;
            _mode = Mode.None; _active = null; _moved = false;
            e.Pointer.Capture(null);

            if (mode == Mode.Draft)
            {
                if (moved)
                {
                    double a = _o.SnapBar(Math.Min(_pressBeat, _draftEnd));
                    double b = _o.SnapBar(Math.Max(_pressBeat, _draftEnd));
                    _o.AddSection(a, Math.Max(_o._beatsPerBar, b - a));
                }
                else _o.SeekTo(_o.SnapBar(_pressBeat));   // a plain click on empty lane scrubs
                InvalidateVisual();
                return;
            }
            if (active is null) return;
            // A click (no drag) on a span jumps the playhead to its start — the gesture the
            // lane exists for. A drag committed geometry instead.
            if (!moved) _o.SeekTo(active.StartBeat);
            else _o.SectionsEdited();
            InvalidateVisual();
        }

        // Wheel over the lane drives the timeline exactly as it does over the ruler and lanes.
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            _o.HandleLaneWheel(e, e.GetPosition(this).X);
            if (!e.Handled) { _o.ScrollByBeats(-WheelInput.Pixels(e.Delta.Y) / _o._pixelsPerBeat); e.Handled = true; }
        }

        private void ShowMenu(ArrangementSection? s, double beat)
        {
            var flyout = new MenuFlyout();
            if (s is not null)
            {
                var rename = new MenuItem { Header = "Rename…" };
                rename.Click += (_, _) => _o.RenameSection(this, s);
                var loop = new MenuItem { Header = "Loop section" };
                loop.Click += (_, _) => _o.SetLoopRegion(s.StartBeat, s.StartBeat + s.LengthBeats);   // raises LoopChanged itself
                var select = new MenuItem { Header = "Select section" };
                select.Click += (_, _) => _o.SelectSectionRange(s);
                var dup = new MenuItem { Header = "Duplicate" };
                dup.Click += (_, _) =>
                {
                    var copy = _o.AddSection(s.StartBeat + s.LengthBeats, s.LengthBeats);
                    copy.Name = s.Name;
                    _o.SectionsEdited();
                };
                var del = new MenuItem { Header = "Delete" };
                del.Click += (_, _) => _o.RemoveSection(s);
                flyout.Items.Add(rename);
                flyout.Items.Add(loop);
                flyout.Items.Add(select);
                flyout.Items.Add(new Separator());
                flyout.Items.Add(dup);
                flyout.Items.Add(del);
            }
            else
            {
                var add = new MenuItem { Header = "Add section here" };
                add.Click += (_, _) => _o.AddSection(_o.SnapBar(beat), _o._beatsPerBar * 8);
                flyout.Items.Add(add);
                if (_o._sections.Count > 0)
                {
                    var clear = new MenuItem { Header = "Delete all sections" };
                    clear.Click += (_, _) => { _o.ClearSections(); _o.SectionsChangedRaise(); };
                    flyout.Items.Add(clear);
                }
            }
            flyout.ShowAt(this, showAtPointer: true);
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            ctx.FillRectangle(LaneBg, new Rect(0, 0, w, h));

            var list = _o._sections;
            if (list.Count == 0 && _mode != Mode.Draft)
            {
                var hint = new FormattedText("drag here to mark a section", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9, HintInk);
                ctx.DrawText(hint, new Point(8, (h - hint.Height) / 2));
            }

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                double x0 = _o.BeatToX(s.StartBeat), x1 = _o.BeatToX(s.StartBeat + s.LengthBeats);
                if (x1 < 0 || x0 > w) continue;
                bool warm = (i & 1) == 0;
                var r = new Rect(x0, 2, Math.Max(2, x1 - x0), h - 4);
                using (ctx.PushClip(new Rect(0, 0, w, h)))
                {
                    ctx.DrawRectangle(warm ? WarmFill : ColdFill, null, r, 3, 3);
                    ctx.FillRectangle(warm ? WarmEdge : ColdEdge, new Rect(r.X, r.Y, 2, r.Height));
                    if (r.Width > 22)
                    {
                        var ft = new FormattedText(s.Name, CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, Typeface.Default, 9, warm ? WarmInk : ColdInk)
                        { MaxTextWidth = Math.Max(8, r.Width - 10), Trimming = TextTrimming.CharacterEllipsis };
                        ctx.DrawText(ft, new Point(r.X + 7, r.Y + (r.Height - ft.Height) / 2));
                    }
                }
            }

            // The span being drawn right now (drag on empty lane).
            if (_mode == Mode.Draft && _moved)
            {
                double a = _o.BeatToX(_o.SnapBar(Math.Min(_pressBeat, _draftEnd)));
                double b = _o.BeatToX(_o.SnapBar(Math.Max(_pressBeat, _draftEnd)));
                var r = new Rect(a, 2, Math.Max(2, b - a), h - 4);
                ctx.DrawRectangle(DraftFill, DraftPen, r, 3, 3);
            }

            // Playhead tick, so the lane and the ruler read as one instrument.
            double px = _o.BeatToX(_o._playheadBeats);
            if (px >= 0 && px <= w) ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
        }
    }

    // ---- commands the lane's menu reaches ---------------------------------

    internal void SectionsChangedRaise() => SectionsChanged?.Invoke();

    /// <summary>Make a section's span the arrangement's time selection, across every row —
    /// so Delete / Duplicate / Consolidate act on the whole section.</summary>
    internal void SelectSectionRange(ArrangementSection s)
    {
        if (_tracks.Count == 0) return;
        SetTimeSelection(s.StartBeat, s.StartBeat + s.LengthBeats, 0, _tracks.Count - 1);
    }

    // ---- sidecar persistence ---------------------------------------------

    private const string SectionsSidecar = "sections.json";

    private sealed class SectionDto
    {
        public double Start { get; set; }
        public double Length { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>Write the sections into a .nota bundle. Cosmetic metadata — a failure here
    /// must never fail the save.</summary>
    public void SaveSections(string bundleDir)
    {
        var path = System.IO.Path.Combine(bundleDir, SectionsSidecar);
        try
        {
            if (_sections.Count == 0) { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); return; }
            var dto = SectionList.Select(s => new SectionDto { Start = s.StartBeat, Length = s.LengthBeats, Name = s.Name }).ToList();
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(dto));
        }
        catch { /* sections are cosmetic — never block a save */ }
    }

    /// <summary>Read the sections back out of a bundle (absent / corrupt → none).</summary>
    public void LoadSections(string bundleDir)
    {
        ClearSections();
        var path = System.IO.Path.Combine(bundleDir, SectionsSidecar);
        if (!System.IO.File.Exists(path)) return;
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<List<SectionDto>>(System.IO.File.ReadAllText(path));
            if (dto is null) return;
            SetSections(dto.Select(d => new ArrangementSection { StartBeat = d.Start, LengthBeats = d.Length, Name = d.Name }));
        }
        catch { /* a corrupt sidecar just means no sections */ }
    }
}
