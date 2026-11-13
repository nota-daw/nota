// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · Sections — the song-structure lane between the Overview and the ruler
// (design 1a, "structure first"). A bar of numbers says nothing about a song; a row of named
// spans — Intro, Verse, Drop — says where you are. Sections are authoring metadata, not
// audio, but they belong to the project (specs/016): the engine holds them next to scenes,
// so every edit is one undo step and project.json stores them. The view keeps a working
// copy that a drag reshapes live and commits to the engine on release. Dragging a section
// rearranges the song (specs/073): it and everything in its span move to a boundary between
// sections, which the engine does as one edit; ⌥-drag moves the marker alone.

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
using Nota.Application;
using Nota.Application.Localization;

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

    // The lane row: the header column names the lane, then the spans themselves.
    private Border BuildSectionsRow()
    {
        var label = new TextBlock
        {
            Text = Loc.T("arrangement.sections.sections"), Classes = { "SectionLabel" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(NameGap, 0, 0, 0),
        };
        var grid = new Grid { Height = SectionsH };
        var header = HeaderPanel(label);
        grid.Children.Add(header);
        grid.Children.Add(_sectionsLane);
        AddSideRow(grid, header, _sectionsLane);

        // A hairline, lighter than the lines above and below: the sections belong with the
        // ruler under them, while Overview and the tracks are separate areas.
        var row = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        row.BindResource(Border.BorderBrushProperty, "Brush.Hairline");
        return row;
    }

    // ---- model ------------------------------------------------------------

    /// <summary>The sections, in start order.</summary>
    public IReadOnlyList<ArrangementSection> SectionList
    {
        get { _sections.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat)); return _sections; }
    }

    /// <summary>Re-read the sections from the engine (load, new, undo / redo, an MCP edit).
    /// Skipped while a drag is reshaping the working copy.</summary>
    public void ReloadSections()
    {
        if (_engine is not { } eng || _sectionsLane.IsDragging) return;
        _sections.Clear();
        foreach (var s in eng.Sections)
            _sections.Add(new ArrangementSection { StartBeat = s.StartBeat, LengthBeats = s.LengthBeats, Name = s.Name });
        _sectionsLane.InvalidateVisual();
    }

    // Commit the working copy as one edit: one undo step, none if nothing changed. The
    // engine normalises the list (sorted, empty spans dropped), so read it back.
    private void SectionsEdited()
    {
        _sections.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
        if (_engine is { } eng)
        {
            eng.SetSections(_sections.Select(s => new SectionInfo(s.StartBeat, s.LengthBeats, s.Name)).ToList());
            ReloadSections();
        }
        _sectionsLane.InvalidateVisual();
        SectionsChanged?.Invoke();
    }

    // Position in start order, ties kept in list order — what an automatic name counts
    // (specs/058). Computed, not sorted in place, so it also holds mid-drag.
    private int SectionIndex(ArrangementSection s)
    {
        int self = _sections.IndexOf(s), n = 0;
        for (int i = 0; i < _sections.Count; i++)
        {
            var x = _sections[i];
            if (i != self && (x.StartBeat < s.StartBeat || (x.StartBeat == s.StartBeat && i < self))) n++;
        }
        return n;
    }

    /// <summary>The name a section shows: its own, or "Section N" for its position when automatic.</summary>
    public string SectionDisplayName(ArrangementSection s) => SectionNames.Display(s.Name, SectionIndex(s));

    // A new section is automatic ("Section N", renumbering with its position), so a session
    // gets Section 1..n without a dialog in the way; renaming is one double-click.
    private ArrangementSection AddSection(double startBeat, double lengthBeats)
    {
        var s = new ArrangementSection
        {
            StartBeat = Math.Max(0, startBeat),
            LengthBeats = Math.Max(_beatsPerBar, lengthBeats),
        };
        _sections.Add(s);
        return s;
    }

    private void RemoveSection(ArrangementSection s)
    {
        if (Live(s) is not { } live || !_sections.Remove(live)) return;
        SectionsEdited();
    }

    // A reload (undo, an MCP edit) replaces the working copy, so a menu or rename flyout
    // opened before it may hold a stale span: find the one it still means.
    private ArrangementSection? Live(ArrangementSection s)
        => _sections.Contains(s) ? s
         : _sections.FirstOrDefault(x => x.StartBeat == s.StartBeat && x.LengthBeats == s.LengthBeats && x.Name == s.Name);

    // Snap a section edge to whole bars — song structure is bar-aligned, always, so this
    // ignores the clip snap grid (which is often finer than a bar).
    private double SnapBar(double beat)
    {
        double bar = Math.Max(1, _beatsPerBar);
        return Math.Max(0, Math.Round(beat / bar) * bar);
    }

    // Enter commits; Escape or a click elsewhere drops the edit. An automatic section opens
    // empty with its automatic name as the placeholder, so Enter on an empty box keeps (or
    // returns to) automatic naming, and the caption says which kind of name Enter will store.
    private void RenameSection(Control anchor, ArrangementSection s)
    {
        bool automatic = SectionNames.IsAutomatic(s.Name);
        string automaticName = SectionNames.Automatic(SectionIndex(s));
        var box = new TextBox { Text = automatic ? "" : s.Name, PlaceholderText = automaticName, Width = 180, FontSize = 11 };
        var caption = new TextBlock { Classes = { "Caption" }, VerticalAlignment = VerticalAlignment.Center };
        void Caption() => caption.Text = SectionNames.IsAutomatic(box.Text) ? Loc.T("arrangement.sections.automatic_name") : Loc.T("arrangement.sections.custom_name");
        Caption();
        box.TextChanged += (_, _) => Caption();

        var reset = new Button { Content = Loc.T("arrangement.sections.reset_to_default"), Classes = { "ghost" }, FontSize = 11, Height = 22, Padding = new Thickness(6, 0), IsEnabled = !automatic };
        ToolTip.SetTip(reset, Loc.F("arrangement.sections.back_to_the_automatic_name", automaticName));
        var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(reset, Dock.Right);
        footer.Children.Add(reset);
        footer.Children.Add(caption);

        var flyout = new Flyout { Content = new StackPanel { Children = { box, footer } }, Placement = PlacementMode.Bottom };
        reset.Click += (_, _) => { ResetSectionName(s); flyout.Hide(); };
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                string name = SectionNames.Normalize(box.Text);
                if (Live(s) is { } live && live.Name != name) { live.Name = name; SectionsEdited(); }
                flyout.Hide(); ke.Handled = true;
            }
            else if (ke.Key == Key.Escape) { flyout.Hide(); ke.Handled = true; }
        };
        flyout.ShowAt(anchor);
        Dispatcher.UIThread.Post(() => { box.SelectAll(); box.Focus(); }, DispatcherPriority.Input);
    }

    /// <summary>Return a section to automatic naming — one undo step, none if it already is.</summary>
    private void ResetSectionName(ArrangementSection s)
    {
        if (Live(s) is not { } live || SectionNames.IsAutomatic(live.Name)) return;
        live.Name = "";
        SectionsEdited();
    }

    /// <summary>Reset the name of the section under the playhead (command palette). Returns
    /// what happened, for the status bar.</summary>
    public string ResetSectionNameAtPlayhead()
    {
        var s = SectionList.LastOrDefault(x => x.StartBeat <= _playheadBeats && _playheadBeats < x.StartBeat + x.LengthBeats);
        if (s is null) return Loc.T("arrangement.sections.no_section_at_the_playhead");
        if (SectionNames.IsAutomatic(s.Name)) return Loc.F("arrangement.sections.already_has_its_automatic_name", SectionDisplayName(s));
        string was = s.Name, now = SectionNames.Automatic(SectionIndex(s));
        ResetSectionName(s);
        return Loc.F("arrangement.sections.is_again", was, now);
    }

    // ---- edits that carry the arrangement (specs/073) ---------------------

    private ArrangementSection? SectionAtPlayhead()
        => SectionList.LastOrDefault(x => x.StartBeat <= _playheadBeats && _playheadBeats < x.StartBeat + x.LengthBeats);

    // A section that overlaps another can't take its content anywhere: whose is the shared part?
    private bool SectionOverlapsAt(int index) => _engine is { } eng && index >= 0 && eng.SectionOverlaps(index);

    private string OverlapNote(ArrangementSection s)
        => Loc.F("arrangement.sections.overlaps_another_section_separate_them", SectionDisplayName(s));

    // Run an edit that carries content with a section. Afterwards a time selection no longer
    // covers the music it did, and cuts and removals re-index the clips, so a clip selection
    // would point at other clips: both go, and open clip editors re-sync as after a split.
    // Clips and sections are re-read. Clips only moved, so the track headers stay — rebuilding
    // them is what makes a big song slow (SC-004) — unless the song has take lanes: their
    // headers hold each take's place (its menu comps by it), so they are rebuilt.
    private bool SectionContentEdit(Func<IAudioEngine, bool> edit)
    {
        if (_engine is not { } eng) return false;
        int Takes() { int n = 0; foreach (var t in _tracks) n += eng.TakeCount(t.Id); return n; }
        bool takes = Takes() > 0;
        if (!edit(eng)) return false;
        takes |= Takes() > 0;
        ClearTimeSelection();
        Select(-1, -1);
        if (takes) Refresh(); else RefreshMovedClips();
        ReloadSections();
        ClipsSplit?.Invoke();
        SectionsChanged?.Invoke();
        return true;
    }

    /// <summary>Move section <paramref name="index"/> (start order) and everything in its span
    /// to boundary <paramref name="toBoundary"/> — one undo step. False when refused or a no-op.</summary>
    internal bool MoveSectionWithContent(int index, int toBoundary)
        => SectionContentEdit(eng => eng.MoveSection(index, toBoundary));

    private bool DuplicateSectionWithContent(ArrangementSection s)
        => Live(s) is { } live && SectionContentEdit(eng => eng.DuplicateSectionWithContent(SectionIndex(live)));

    private bool DeleteSectionWithContent(ArrangementSection s)
        => Live(s) is { } live && SectionContentEdit(eng => eng.DeleteSectionWithContent(SectionIndex(live)));

    /// <summary>Command palette: swap the section under the playhead with its neighbour, content
    /// and all. Returns what happened, for the status bar.</summary>
    public string MoveSectionAtPlayhead(bool later)
    {
        if (SectionAtPlayhead() is not { } s) return Loc.T("arrangement.sections.no_section_at_the_playhead");
        int i = SectionIndex(s);
        string name = SectionDisplayName(s);
        if (SectionOverlapsAt(i)) return OverlapNote(s);
        if (later && i >= _sections.Count - 1) return Loc.F("arrangement.sections.is_already_the_last_section", name);
        if (!later && i == 0) return Loc.F("arrangement.sections.is_already_the_first_section", name);
        if (!MoveSectionWithContent(i, later ? i + 2 : i - 1)) return Loc.F("arrangement.sections.couldnt_move_there", name);
        return later ? Loc.F("arrangement.sections.moved_later", name) : Loc.F("arrangement.sections.moved_earlier", name);
    }

    /// <summary>Command palette: duplicate the section under the playhead with its content.</summary>
    public string DuplicateSectionWithContentAtPlayhead()
    {
        if (SectionAtPlayhead() is not { } s) return Loc.T("arrangement.sections.no_section_at_the_playhead");
        if (SectionOverlapsAt(SectionIndex(s))) return OverlapNote(s);
        string name = SectionDisplayName(s);
        return DuplicateSectionWithContent(s) ? Loc.F("arrangement.sections.duplicated_with_its_content", name) : Loc.F("arrangement.sections.couldnt_duplicate", name);
    }

    /// <summary>Command palette: delete the section under the playhead with its content.</summary>
    public string DeleteSectionWithContentAtPlayhead()
    {
        if (SectionAtPlayhead() is not { } s) return Loc.T("arrangement.sections.no_section_at_the_playhead");
        if (SectionOverlapsAt(SectionIndex(s))) return OverlapNote(s);
        string name = SectionDisplayName(s);
        return DeleteSectionWithContent(s) ? Loc.F("arrangement.sections.deleted_with_its_content", name) : Loc.F("arrangement.sections.couldnt_delete", name);
    }

    /// <summary>Esc / lost focus: abandon a drag in the Sections lane. True when one was active.</summary>
    internal bool CancelSectionsGesture() => _sectionsLane.CancelGesture();

    // ---- the lane ---------------------------------------------------------

    private sealed class SectionsControl : Control
    {
        private static readonly IBrush LaneBg = NotaPalette.Wash(NotaPalette.BgSunken, 0xFF);
        // Spans alternate between two neutral tone steps, so a long song reads as a rhythm of
        // blocks rather than one continuous ribbon of labels. Neither is the accent: a section
        // at rest is neither live nor chosen. (A span being drawn — Draft — is.)
        private static readonly IBrush WarmFill = NotaPalette.Wash(NotaPalette.SurfaceHover, 0xFF);
        private static readonly IBrush ColdFill = NotaPalette.Wash(NotaPalette.SurfaceRaised, 0xFF);
        private static readonly IBrush WarmEdge = NotaPalette.TextFaint;
        private static readonly IBrush ColdEdge = NotaPalette.BorderStrong;
        private static readonly IBrush WarmInk = NotaPalette.TextPrimary;
        private static readonly IBrush ColdInk = NotaPalette.TextSecondary;
        private static readonly IBrush HintInk = NotaPalette.TextDisabled;
        private static readonly IBrush DraftFill = NotaPalette.Wash(NotaPalette.AccentBright, 0x28);
        private static readonly IPen DraftPen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xC0), 1);
        // Where a rearranging drag will land: the accent marks the chosen boundary.
        private static readonly IBrush InsertBar = NotaPalette.AccentBright;
        // The span riding under the pointer is not the choice — where it lands is — so its outline stays neutral.
        private static readonly IPen GhostPen = new Pen(NotaPalette.BorderStrong, 1);
        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
        private static readonly Cursor EdgeCursor = new(StandardCursorType.SizeWestEast);

        private const double EdgeGrab = 4;   // px around a span edge that grabs it

        // Reorder (specs/073): a plain drag of a span carries the song with it to a boundary;
        // Move (⌥, or an overlapping span) slides the marker alone.
        private enum Mode { None, Draft, Move, Reorder, EdgeLeft, EdgeRight }

        private readonly ArrangementView _o;
        private Mode _mode;
        private ArrangementSection? _active;
        private double _pressBeat, _grabOffset, _fixedBeat;
        private bool _moved;
        private int _from, _to;          // Reorder: the span's start-order index → target boundary
        private double _dragBeat;        // Reorder: the pointer, for the ghost
        private bool _overlapping;       // the grabbed span overlaps another: marker only
        private double _lastX;
        private DispatcherTimer? _autoScroll;

        /// <summary>A span is being drawn, moved or resized: the working copy is ahead of the engine.</summary>
        public bool IsDragging => _mode != Mode.None;

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
            else
            {
                _grabOffset = _pressBeat - hit.StartBeat;
                _from = _to = _o.SectionIndex(hit);
                _dragBeat = _pressBeat;
                bool markerOnly = (e.KeyModifiers & KeyModifiers.Alt) != 0;
                _overlapping = !markerOnly && _o.SectionOverlapsAt(_from);
                // A lone section has nowhere else in the order to go: its drag moves the marker.
                _mode = markerOnly || _overlapping || _o._sections.Count < 2 ? Mode.Move : Mode.Reorder;
            }
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
            if (!_moved && Math.Abs(beat - _pressBeat) * _o._pixelsPerBeat > 3)
            {
                _moved = true;
                if (_overlapping && _active is not null) _o.StatusMessage?.Invoke(_o.OverlapNote(_active));
            }
            if (!_moved) return;
            _lastX = pos.X;
            if (_mode is Mode.Move or Mode.Reorder) EnsureAutoScroll();
            DragTo(beat);
        }

        private void DragTo(double beat)
        {
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
                case Mode.Reorder:
                    _dragBeat = beat;
                    _to = TargetBoundary(beat - _grabOffset);
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
            _autoScroll?.Stop();
            e.Pointer.Capture(null);

            if (mode == Mode.Draft)
            {
                if (moved)
                {
                    double a = _o.SnapBar(Math.Min(_pressBeat, _draftEnd));
                    double b = _o.SnapBar(Math.Max(_pressBeat, _draftEnd));
                    _o.AddSection(a, Math.Max(_o._beatsPerBar, b - a));
                    _o.SectionsEdited();
                }
                else _o.SeekTo(_o.SnapBar(_pressBeat));   // a plain click on empty lane scrubs
                InvalidateVisual();
                return;
            }
            if (active is null) return;
            // A click (no drag) on a span jumps the playhead to its start — the gesture the
            // lane exists for. A drag committed geometry instead; a rearranging drag dropped
            // where it started changes nothing.
            if (!moved) _o.SeekTo(active.StartBeat);
            else if (mode == Mode.Reorder) { if (_to != _from && _to != _from + 1) _o.MoveSectionWithContent(_from, _to); }
            else _o.SectionsEdited();
            InvalidateVisual();
        }

        /// <summary>Esc / lost window focus: drop the drag with no change — a marker drag's
        /// working copy goes back to the engine's.</summary>
        internal bool CancelGesture()
        {
            if (_mode == Mode.None) return false;
            _mode = Mode.None; _active = null; _moved = false;
            _autoScroll?.Stop();
            _o.ReloadSections();
            InvalidateVisual();
            return true;
        }

        // The boundary a rearranging drag lands on: where the ghost's middle sits among the other
        // spans' middles (0..count, before section k or after the last). A boundary inside an
        // overlap can't take it, so the last good one holds.
        private int TargetBoundary(double ghostStart)
        {
            var list = _o.SectionList;
            int n = list.Count;
            if (_from < 0 || _from >= n) return _from;
            double centre = ghostStart + list[_from].LengthBeats / 2;
            int k = 0;
            for (int i = 0; i < n; i++)
                if (i != _from && list[i].StartBeat + list[i].LengthBeats / 2 < centre) k++;
            int to = k <= _from ? k : k + 1;
            double d = BoundaryBeat(list, to);
            foreach (var s in list)
                if (d > s.StartBeat + 1e-6 && d < s.StartBeat + s.LengthBeats - 1e-6) return _to;
            return to;
        }

        private static double BoundaryBeat(IReadOnlyList<ArrangementSection> list, int k)
            => k < list.Count ? list[k].StartBeat : list.Max(s => s.StartBeat + s.LengthBeats);

        // Where span i would start if the drag ended now — the engine's own remap (specs/073
        // data-model): the grabbed span lands on the boundary, the ones it passes shift by its length.
        private double PreviewStart(IReadOnlyList<ArrangementSection> list, int i)
        {
            double t = list[i].StartBeat;
            if (_mode != Mode.Reorder || !_moved || _to == _from || _to == _from + 1) return t;
            var me = list[_from];
            double a = me.StartBeat, len = me.LengthBeats, b = a + len, d = BoundaryBeat(list, _to);
            if (i == _from) return d <= a ? d : d - len;
            if (d <= a && t >= d - 1e-6 && t < a - 1e-6) return t + len;
            if (d >= b && t >= b - 1e-6 && t < d - 1e-6) return t - len;
            return t;
        }

        // Near an edge a drag scrolls the view, as clip drags do (ArrangementView.LaneControl).
        private void EnsureAutoScroll()
        {
            if (_autoScroll is not null) { if (!_autoScroll.IsEnabled) _autoScroll.Start(); return; }
            _autoScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _autoScroll.Tick += (_, _) => AutoScrollTick();
            _autoScroll.Start();
        }

        private void AutoScrollTick()
        {
            if (_mode is not (Mode.Move or Mode.Reorder)) { _autoScroll?.Stop(); return; }
            const double zone = 40;
            double w = Bounds.Width;
            double depth = _lastX < zone ? -(zone - _lastX) : _lastX > w - zone ? _lastX - (w - zone) : 0;
            if (depth == 0) return;
            _o.ScrollByBeats(Math.Sign(depth) * Math.Clamp(Math.Abs(depth) / zone, 0.15, 1.0) * 2.0);
            DragTo(BeatAt(_lastX));
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
                int bars = (int)Math.Round(s.LengthBeats / Math.Max(1, _o._beatsPerBar));
                var dup = MenuKit.Item(Loc.T("common.duplicate"), GlyphKind.Duplicate, () =>
                {
                    if (_o.Live(s) is null) return;
                    var copy = _o.AddSection(s.StartBeat + s.LengthBeats, s.LengthBeats);
                    copy.Name = s.Name;
                    _o.SectionsEdited();   // one step: the copy arrives already named
                });
                int idx = _o.SectionIndex(s), n = _o._sections.Count;
                bool free = !_o.SectionOverlapsAt(idx);   // overlapping spans keep their content where it is
                var earlier = MenuKit.Item(Loc.T("arrangement.sections.move_earlier"), GlyphKind.ChevronLeft,
                    () => { if (_o.Live(s) is { } l) _o.MoveSectionWithContent(_o.SectionIndex(l), _o.SectionIndex(l) - 1); },
                    enabled: free && idx > 0);
                var later = MenuKit.Item(Loc.T("arrangement.sections.move_later"), GlyphKind.ChevronRight,
                    () => { if (_o.Live(s) is { } l) _o.MoveSectionWithContent(_o.SectionIndex(l), _o.SectionIndex(l) + 2); },
                    enabled: free && idx < n - 1);
                var dupContent = MenuKit.Item(Loc.T("arrangement.sections.duplicate_with_content"), GlyphKind.Duplicate,
                    () => _o.DuplicateSectionWithContent(s), enabled: free);
                MenuKit.AddGroups(flyout.Items,
                    new Control[] { MenuKit.Header(_o.SectionDisplayName(s), null, Loc.Plural("arrangement.sections.section_bars", bars)) },
                    new Control[]
                    {
                        MenuKit.Item(Loc.T("common.rename"), GlyphKind.Edit, () => _o.RenameSection(this, s)),
                        MenuKit.Item(Loc.T("arrangement.sections.reset_to_default_name"), GlyphKind.Cycle, () => _o.ResetSectionName(s), enabled: !SectionNames.IsAutomatic(s.Name)),
                    },
                    new Control[]
                    {
                        MenuKit.Item(Loc.T("arrangement.sections.loop_section"), GlyphKind.Cycle, () => _o.SetLoopRegion(s.StartBeat, s.StartBeat + s.LengthBeats)),   // raises LoopChanged itself
                        MenuKit.Item(Loc.T("arrangement.sections.select_section"), GlyphKind.Select, () => _o.SelectSectionRange(s)),
                    },
                    new Control[] { earlier, later },
                    new Control[] { dup, dupContent },
                    new Control[]
                    {
                        MenuKit.Danger(Loc.T("common.delete"), GlyphKind.Trash, () => _o.RemoveSection(s)),
                        MenuKit.Danger(Loc.T("arrangement.sections.delete_with_content"), GlyphKind.Trash, () => _o.DeleteSectionWithContent(s), enabled: free),
                    });
            }
            else
            {
                flyout.Items.Add(MenuKit.Item(Loc.T("arrangement.sections.add_section_here"), GlyphKind.Plus,
                    () => { _o.AddSection(_o.SnapBar(beat), _o._beatsPerBar * 8); _o.SectionsEdited(); }));
                if (_o._sections.Count > 0)
                {
                    flyout.Items.Add(new Separator());
                    flyout.Items.Add(MenuKit.Danger(Loc.T("arrangement.sections.delete_all_sections"), GlyphKind.Trash,
                        () => { _o._sections.Clear(); _o.SectionsEdited(); }));
                }
            }
            flyout.ShowAt(this, showAtPointer: true);
        }

        private void DrawSpan(DrawingContext ctx, ArrangementSection s, Rect r, bool warm)
        {
            ctx.DrawRectangle(warm ? WarmFill : ColdFill, null, r, NotaRadius.SwitchValue, NotaRadius.SwitchValue);
            ctx.FillRectangle(warm ? WarmEdge : ColdEdge, new Rect(r.X, r.Y, 2, r.Height));
            if (r.Width <= 22) return;
            var ft = new NotaFormattedText(_o.SectionDisplayName(s), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 9, warm ? WarmInk : ColdInk)
            { MaxTextWidth = Math.Max(8, r.Width - 10), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(ft, new Point(r.X + 7, r.Y + (r.Height - ft.Height) / 2));
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            ctx.FillRectangle(LaneBg, new Rect(0, 0, w, h));

            // Start order while rearranging: the drag's indices are the engine's (sorted) ones.
            IReadOnlyList<ArrangementSection> list = _mode == Mode.Reorder ? _o.SectionList : _o._sections;
            if (list.Count == 0 && _mode != Mode.Draft)
            {
                var hint = new NotaFormattedText(Loc.T("arrangement.sections.drag_here_to_mark_a"), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9, HintInk);
                ctx.DrawText(hint, new Point(8, (h - hint.Height) / 2));
            }

            // While rearranging, every span is drawn where it will be (the grabbed one as its
            // landing slot plus a ghost under the pointer); tones alternate in that new order.
            bool reorder = _mode == Mode.Reorder && _moved && _from >= 0 && _from < list.Count;
            var starts = new double[list.Count];
            for (int i = 0; i < list.Count; i++) starts[i] = PreviewStart(list, i);
            var order = Enumerable.Range(0, list.Count).OrderBy(i => starts[i]).ToArray();
            var rank = new int[list.Count];
            for (int k = 0; k < order.Length; k++) rank[order[k]] = k;

            using (ctx.PushClip(new Rect(0, 0, w, h)))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    double x0 = _o.BeatToX(starts[i]), x1 = _o.BeatToX(starts[i] + s.LengthBeats);
                    if (x1 < 0 || x0 > w) continue;
                    var r = new Rect(x0, 2, Math.Max(2, x1 - x0), h - 4);
                    if (reorder && i == _from)
                    {
                        ctx.DrawRectangle(DraftFill, DraftPen, r, NotaRadius.SwitchValue, NotaRadius.SwitchValue);   // the landing slot
                        ctx.FillRectangle(InsertBar, new Rect(r.X - 1, 0, 2, h));
                        continue;
                    }
                    DrawSpan(ctx, s, r, (rank[i] & 1) == 0);
                }
                if (reorder)
                {
                    var me = list[_from];
                    double gx = _o.BeatToX(_dragBeat - _grabOffset);
                    var g = new Rect(gx, 2, Math.Max(2, me.LengthBeats * _o._pixelsPerBeat), h - 4);
                    using (ctx.PushOpacity(0.8))
                    {
                        DrawSpan(ctx, me, g, true);
                        ctx.DrawRectangle(null, GhostPen, g, NotaRadius.SwitchValue, NotaRadius.SwitchValue);
                    }
                }
            }

            // The span being drawn right now (drag on empty lane).
            if (_mode == Mode.Draft && _moved)
            {
                double a = _o.BeatToX(_o.SnapBar(Math.Min(_pressBeat, _draftEnd)));
                double b = _o.BeatToX(_o.SnapBar(Math.Max(_pressBeat, _draftEnd)));
                var r = new Rect(a, 2, Math.Max(2, b - a), h - 4);
                ctx.DrawRectangle(DraftFill, DraftPen, r, NotaRadius.SwitchValue, NotaRadius.SwitchValue);
            }

            // Playhead tick, so the lane and the ruler read as one instrument.
            double px = _o.BeatToX(_o._displayPlayheadBeats);
            if (px >= 0 && px <= w) ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
        }
    }

    // ---- commands the lane's menu reaches ---------------------------------

    /// <summary>Make a section's span the arrangement's time selection, across every row —
    /// so Delete / Duplicate / Consolidate act on the whole section.</summary>
    internal void SelectSectionRange(ArrangementSection s)
    {
        if (_tracks.Count == 0) return;
        SetTimeSelection(s.StartBeat, s.StartBeat + s.LengthBeats, 0, _tracks.Count - 1);
    }
}
