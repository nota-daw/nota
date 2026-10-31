// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The adaptive frame of the clip editor's side panels (specs/055): SectionColumns stacks the
// sections in one or two columns, split by hairlines, and ColumnFlow decides how many from the
// height it is given and the width the editor can spare (PanelColumns does the arithmetic),
// with a foot (the inspector's hint, the tools' Apply row) pinned under the scrolling sections.
// The second column has exactly the first one's width, so a section never reflows: the panel,
// border included, just doubles (2 × 232 for the inspector), and the column gap is the side
// padding plus the border width.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Nota.App;

/// <summary>Sections in one or two columns (set by <see cref="ColumnFlow"/>), a hairline
/// centred in the gap between neighbours of a column.</summary>
public sealed class SectionColumns : Control
{
    private readonly double _spacing;
    private readonly Control[] _sections;
    private Rect[] _rules = Array.Empty<Rect>();

    /// <param name="spacing">Space either side of the hairline between two sections.</param>
    public SectionColumns(double spacing, params Control[] sections)
    {
        _spacing = spacing;
        _sections = sections;
        foreach (var s in sections) { LogicalChildren.Add(s); VisualChildren.Add(s); }
    }

    public double Gap => 2 * _spacing + 1;
    public int Columns { get; private set; } = 1;
    public int Split { get; private set; } = int.MaxValue;
    public double ColumnGap { get; set; }

    internal void SetLayout(int columns, int split)
    {
        if (columns == Columns && split == Split) return;
        Columns = columns; Split = split;
        InvalidateMeasure();
    }

    private double ColumnWidth(double width) => Columns == 2 ? Math.Max(0, (width - ColumnGap) / 2) : width;

    /// <summary>Each visible section's height at <paramref name="columnWidth"/>.</summary>
    internal double[] MeasureSections(double columnWidth)
    {
        var h = new System.Collections.Generic.List<double>(_sections.Length);
        foreach (var c in _sections)
        {
            if (!c.IsVisible) continue;
            c.Measure(new Size(columnWidth, double.PositiveInfinity));
            h.Add(c.DesiredSize.Height);
        }
        return h.ToArray();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = MeasureSections(ColumnWidth(availableSize.Width));
        int k = Math.Min(Split, h.Length);
        double height = Math.Max(PanelColumns.Stack(h, 0, k, Gap), PanelColumns.Stack(h, k, h.Length, Gap));
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double colW = ColumnWidth(finalSize.Width);
        var rules = new System.Collections.Generic.List<Rect>();
        int i = 0;
        double y = 0, x = 0;
        foreach (var c in _sections)
        {
            if (!c.IsVisible) continue;
            if (i == Split) { x = colW + ColumnGap; y = 0; }
            else if (i > 0) { rules.Add(new Rect(x, y + _spacing, colW, 1)); y += Gap; }
            c.Arrange(new Rect(x, y, colW, c.DesiredSize.Height));
            y += c.DesiredSize.Height;
            i++;
        }
        _rules = rules.ToArray();
        InvalidateVisual();
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        foreach (var r in _rules) context.FillRectangle(NotaPalette.GraphBorder, r);
    }
}

/// <summary>A side panel's body: <see cref="SectionColumns"/> in a vertical scroller with an
/// optional foot pinned below. One column is <see cref="ColumnWidth"/> wide, inside the panel's
/// border (<c>edge</c> wide); it takes a second only when one column would scroll and the width
/// it is offered, less <see cref="Reserve"/> (what the editor canvas keeps), still holds two.</summary>
public sealed class ColumnFlow : Control
{
    private readonly ScrollViewer _scroll;
    private readonly Border _pad;
    private readonly Control? _foot;
    private SectionColumns _body = null!;

    private readonly double _edge;

    public ColumnFlow(SectionColumns body, Control? foot, Thickness padding, double columnWidth, double edge = 1)
    {
        ColumnWidth = columnWidth;
        _edge = edge;
        _pad = new Border { Padding = padding };
        _scroll = new ScrollViewer
        {
            Content = _pad,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _foot = foot;
        LogicalChildren.Add(_scroll); VisualChildren.Add(_scroll);
        if (foot is not null) { LogicalChildren.Add(foot); VisualChildren.Add(foot); }
        Body = body;
    }

    public double ColumnWidth { get; }

    /// <summary>Width the editor's header needs beside the clip cell, which follows this panel's
    /// width, so a second column must leave it. Read at each measure.</summary>
    public Func<double>? Beside { get; set; }

    private double _reserve;
    public double Reserve
    {
        get => _reserve;
        set { if (_reserve != value) { _reserve = value; InvalidateMeasure(); } }
    }

    public SectionColumns Body
    {
        get => _body;
        set
        {
            _body = value;
            _body.ColumnGap = _pad.Padding.Left + _pad.Padding.Right + _edge;
            _pad.Child = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var pad = _pad.Padding;
        double one = ColumnWidth, two = 2 * ColumnWidth + _edge;
        double footH = 0;
        if (_foot is not null) { _foot.Measure(new Size(one, availableSize.Height)); footH = _foot.DesiredSize.Height; }
        var heights = _body.MeasureSections(Math.Max(0, one - pad.Left - pad.Right));
        double room = availableSize.Height - footH - pad.Top - pad.Bottom;
        bool widthForTwo = PanelColumns.RoomForTwo(availableSize.Width, two, Reserve, Beside?.Invoke() ?? 0);
        var plan = PanelColumns.Choose(heights, _body.Gap, room, widthForTwo);
        _body.SetLayout(plan.Columns, plan.Columns == 2 ? plan.Split : int.MaxValue);

        double w = plan.Columns == 2 ? two : one;
        if (_foot is not null) { _foot.Measure(new Size(w, availableSize.Height)); footH = _foot.DesiredSize.Height; }
        _scroll.Measure(new Size(w, Math.Max(0, availableSize.Height - footH)));
        double h = _scroll.DesiredSize.Height + footH;
        return new Size(w, double.IsInfinity(availableSize.Height) ? h : Math.Min(h, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double footH = _foot?.DesiredSize.Height ?? 0;
        double scrollH = Math.Max(0, finalSize.Height - footH);
        _scroll.Arrange(new Rect(0, 0, finalSize.Width, scrollH));
        _foot?.Arrange(new Rect(0, scrollH, finalSize.Width, footH));
        return finalSize;
    }
}
