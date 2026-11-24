// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session view — which way the grid runs (specs/108, issue #968). Scenes down is the grid as it
// always was: a scene per row, a track per column, headers pinned at the top, Stop and the mixer
// pinned at the bottom. Scenes across is the same grid turned: a scene per column, a track per
// row (a "lane"), track headers pinned at the left, Stop and the mixer block pinned at the
// right. The three strips (header / cells / foot) and the two scroll viewers simply swap axes,
// so every cell keeps its behaviour and painting; this file holds the sizes that change.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Nota.Application;
using Nota.Application.Editing;

namespace Nota.App;

public sealed partial class SessionView
{
    /// <summary>Scenes across: the width of the Stop strip at the right of each lane.</summary>
    private const double StopColW = 40;

    // Built on Refresh() from Settings.SessionGrid; a change there redraws the view.
    private bool _across;

    // Scenes across: the height of every lane (track row) — a slot, or the tallest mixer section
    // the toolbar shows, whichever is taller, so the scene columns stay straight.
    private double _laneH = CellH;

    private static ISettingsService? SettingsSvc => App.Services?.GetService(typeof(ISettingsService)) as ISettingsService;

    private static bool AcrossSetting => SessionGridNav.IsAcross(SettingsSvc?.Current.SessionGrid);

    /// <summary>Turns the grid (the toolbar button; Preferences writes the same setting).</summary>
    private void SetAcross(bool across)
    {
        if (SettingsSvc is not { } s) return;
        s.Current.SessionGrid = across ? SessionGridNav.Across : SessionGridNav.Down;
        s.Save();   // Changed → OnSettingsChanged → Refresh
    }

    private void OnSettingsChanged()
    {
        if (AcrossSetting != _across) Refresh();
    }

    // ---- sizes ------------------------------------------------------------------------

    /// <summary>A slot (clip / group / blank cell) of column <paramref name="c"/>.</summary>
    private double SlotW(Column c) => _across ? TrackColW : c.Width;
    private double SlotH => _across ? _laneH : CellH;

    /// <summary>A scene's own cell: a row of the rail (down) or a column head (across).</summary>
    private double SceneW => _across ? TrackColW : SceneRailW;
    private double SceneH => _across ? HeaderH : CellH;

    /// <summary>A track's header: a column head (down) or a lane head (across).</summary>
    private double HeaderW(Column c) => _across ? SceneRailW : c.Width;
    private double HeaderHt => _across ? _laneH : HeaderH;

    /// <summary>The height of the tallest mixer section shown (0 when the toolbar hides them all).</summary>
    private double MixerBlockHeight()
    {
        double Rows(int n, double h) => n <= 0 ? 0 : n * h + (n - 1) * Gap;
        double tallest = 0;
        if (_showIO) tallest = Math.Max(tallest, Rows(3, IoRowH));
        if (_showSends && ReturnRows > 0) tallest = Math.Max(tallest, Rows(ReturnRows, FaderRowH));
        if (_showMixer) tallest = Math.Max(tallest, 2 * FaderRowH + MeterRowH + ButtonRowH + 3 * Gap);
        return tallest;
    }

    // ---- layout -----------------------------------------------------------------------

    /// <summary>Lays the three strips and the two scroll viewers out for the orientation.</summary>
    private void ApplyAxis()
    {
        _across = AcrossSetting;
        _laneH = _across ? Math.Max(CellH, MixerBlockHeight()) : CellH;

        var strip = _across ? Orientation.Vertical : Orientation.Horizontal;
        _headerRow.Orientation = _cellsRow.Orientation = _footerRow.Orientation = strip;
        if (_across)
        {
            _headerRow.Margin = new Thickness(GridPad, GridPad, 0, GridPad);
            _cellsRow.Margin = new Thickness(Gap, GridPad, 0, GridPad);
            _footerRow.Margin = new Thickness(Gap, GridPad, GridPad, GridPad);
        }
        else
        {
            _headerRow.Margin = new Thickness(GridPad, GridPad, GridPad, 0);
            _cellsRow.Margin = new Thickness(GridPad, Gap, GridPad, 0);
            _footerRow.Margin = new Thickness(GridPad, Gap, GridPad, GridPad);
        }

        // Header strip | cells (they scroll along the scenes' axis) | foot strip.
        _inner.RowDefinitions = new RowDefinitions(_across ? "*" : "Auto,*,Auto");
        _inner.ColumnDefinitions = new ColumnDefinitions(_across ? "Auto,*,Auto" : "*");
        void Place(Control c, int i)
        {
            Grid.SetRow(c, _across ? 0 : i);
            Grid.SetColumn(c, _across ? i : 0);
        }
        Place(_headerRow, 0);
        Place(_cellScroll, 1);
        Place(_footerRow, 2);

        _cellScroll.HorizontalScrollBarVisibility = _across ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _cellScroll.VerticalScrollBarVisibility = _across ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _outerScroll.HorizontalScrollBarVisibility = _across ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _outerScroll.VerticalScrollBarVisibility = _across ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    /// <summary>A strip's stack for one lane or column: along the scenes' axis.</summary>
    private StackPanel LaneStack(double crossSize) => _across
        ? new StackPanel { Orientation = Orientation.Horizontal, Height = crossSize, Spacing = Gap }
        : new StackPanel { Width = crossSize, Spacing = Gap };
}
