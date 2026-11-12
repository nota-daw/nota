// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Headers only (specs/095, #592): Modular shows the arrangement's own track-header column
// beside its canvas — this same view with the timeline put away — so every card, control,
// gesture, menu, meter, fold and look is the arrangement's by construction, and a header
// click selects the track as it always has (the host turns that into the Modular graph).
// The mode only hides bands and narrows the view to its column; nothing else changes.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // Modular's own chrome heights, so the column lines up with its toolbar and status line.
    private const double ModularToolbarH = 34, ModularStatusH = 22;

    private bool _headersOnly;
    private Control _zoomBar = null!;
    private Grid _topGrid = null!;
    private Grid _bottomGrid = null!;
    private readonly TextBlock _tracksCaption = new()
    {
        Classes = { "SectionLabel" }, IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NameGap, 0, 0, 0),
    };

    /// <summary>The header column alone (Modular view): no overview, sections, ruler, lanes or
    /// scrollbar; the view is as wide as its column and docks on the header side.</summary>
    public bool HeadersOnly
    {
        get => _headersOnly;
        set
        {
            if (_headersOnly == value) return;
            _headersOnly = value;
            // Overview and Sections keep their own (user) visibility; collapse them instead.
            _overviewRow.MaxHeight = value ? 0 : double.PositiveInfinity;
            _sectionsRow.MaxHeight = value ? 0 : double.PositiveInfinity;
            _ruler.IsVisible = !value;
            _zoomBar.IsVisible = !value;
            _tracksCaption.IsVisible = value;
            _topGrid.Height = value ? ModularToolbarH - 1 : RulerH;   // + the band's 1 px rule
            _lanes.IsVisible = !value;
            _overlay.IsVisible = !value;
            _footerLanes.IsVisible = !value;
            _hScroll.IsVisible = !value;
            _bottomGrid.Height = value ? ModularStatusH - 1 : double.NaN;
            if (!value) SetColumnHint(false);
            ApplyHeaderWidth(final: true);   // the window-width clamp differs between the modes
            FitHeadersOnlyWidth();
        }
    }

    /// <summary>The header column's width as drawn.</summary>
    public double HeaderColumnWidth => _headerW;

    /// <summary>The column changed width or side — a host laying something beside it re-fits.</summary>
    public event Action? HeaderColumnWidthChanged;

    /// <summary>The track Modular shows (-1 none), asked when a menu opens: its Show in
    /// Modular is disabled there.</summary>
    public Func<int>? ModularTrackId { get; set; }

    // Headers only: the view is exactly its column, on the header side. Otherwise it fills.
    private void FitHeadersOnlyWidth()
    {
        if (_headersOnly)
        {
            Width = _headerW;
            HorizontalAlignment = _headersRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        else
        {
            Width = double.NaN;
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        HeaderColumnWidthChanged?.Invoke();
    }

    /// <summary>The track (or group, or return) whose header is under a point given in the
    /// window's coordinates, with its card to anchor a menu on; null over the master, empty
    /// space or outside the column. Modular's cross-track CV drop asks this.</summary>
    public (int TrackId, Control Anchor)? TrackAtHeaderPoint(Point windowPoint)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || !IsEffectivelyVisible) return null;
        if (top.TranslatePoint(windowPoint, _headers) is { } hp && hp.X >= 0 && hp.X <= _headerW
            && _scroller.TranslatePoint(new Point(0, 0), top) is { } so
            && windowPoint.Y >= so.Y && windowPoint.Y <= so.Y + _scroller.Bounds.Height)
        {
            int row = RowAtY(hp.Y);
            if (row >= 0)
            {
                int id = _tracks[row].Id;
                Control anchor = _headerCards.TryGetValue(id, out var hc) ? hc.card : _headers;
                return (id, anchor);
            }
        }
        // The pinned footer scrolls when returns are tall: only its visible part counts.
        if (_footerScroller.TranslatePoint(new Point(0, 0), top) is { } fo
            && windowPoint.Y >= fo.Y && windowPoint.Y <= fo.Y + _footerScroller.Bounds.Height
            && top.TranslatePoint(windowPoint, _footerHeaders) is { } fp && fp.X >= 0 && fp.X <= _headerW)
            for (int i = 0; i < _returns.Count && i < _footerCards.Count; i++)
            {
                var card = _footerCards[i].card;
                if (card.Bounds.Contains(fp)) return (_returns[i].Id, card);
            }
        return null;
    }

    /// <summary>A track menu's Show in Modular (specs/095): the host opens Modular on the track.</summary>
    public event Action<int>? ShowInModularRequested;

    // Show in Modular, for a track, group or return menu (never the master). In Modular it is
    // disabled on the track already shown.
    private MenuItem ShowInModularItem(int trackId)
    {
        var mi = MenuKit.Item(Loc.T("common.show_in_modular"), GlyphKind.Modular, () => ShowInModularRequested?.Invoke(trackId));
        mi.IsEnabled = !(_headersOnly && trackId == (ModularTrackId?.Invoke() ?? -1));
        return mi;
    }

    /// <summary>A CV cable is being dragged in Modular: the column's divider turns orange
    /// to say a header is a drop target (another track's parameter).</summary>
    public void SetColumnHint(bool on)
    {
        IBrush brush = on ? NotaPalette.Accent : Brush("Brush.BorderDefault");
        foreach (var b in _columnDividers) b.BorderBrush = brush;
        if (on) foreach (var b in _columnDividers) b.BorderThickness = _headersRight ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 2, 0);
        else foreach (var b in _columnDividers) b.BorderThickness = ColumnDivider;
    }
}
