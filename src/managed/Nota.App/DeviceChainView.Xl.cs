// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the XL panel (specs/085, #540). A device that opts in (IDeviceBody.SupportsXl:
// Nota EQ-8, Dynamic EQ-8) can be set to XL in its S / L / XL toggle. Its whole editor then moves
// into a panel above the chain — the shared card shell, as wide as the chain and taller than a
// card, with the device's L body stretched in it — while its card in the chain stays in place
// as a 260 × 260 placeholder. A track has one XL device (the toggle returns any other to L; a
// duplicate or a device moved in from another track loses to the one that already held the
// panel), and the panel follows the selected track: it shows that track's XL device, or hides.
// The size is the device's View param (DeviceView.CardSize), so it saves with the project.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    /// <summary>The XL panel's height when the Devices panel opens for it, and the least it
    /// shrinks to in a short window (the chain under it keeps its card height).</summary>
    internal const double XlHeight = 460, XlMinHeight = 180;

    private double _xlHeight = XlHeight;

    /// <summary>True while the shown track has a device in XL (the panel is up).</summary>
    public bool XlOpen => _xlIndex >= 0;

    /// <summary>The XL panel's height (the Devices panel's splitter sets it; at least
    /// <see cref="XlMinHeight"/>). Changing it refits <see cref="PreferredHeight"/>.</summary>
    public double XlPanelHeight
    {
        get => _xlHeight;
        set
        {
            double h = Math.Max(XlMinHeight, double.IsFinite(value) && value > 0 ? value : XlHeight);
            if (Math.Abs(h - _xlHeight) < 0.5) return;
            _xlHeight = h;
            UpdatePreferredHeight();
        }
    }

    /// <summary>The chain's own height under the XL panel: the card row and its insets, plus the gap above.</summary>
    internal const double XlChainPart = 3 * ChainInset + CardH;

    private readonly Border _xlHost = new() { IsVisible = false, Margin = new Thickness(ChainInset, ChainInset, ChainInset, 0) };
    private int _xlIndex = -1;   // the shown track's device in the panel, or -1
    private double _preferredHeight = 2 * ChainInset + CardH;

    /// <summary>The chain view's content height (the Detail toolbar and seam not included): the
    /// chain's card row, plus the XL panel while one is open.</summary>
    public double PreferredHeight => _preferredHeight;

    /// <summary>Raised when <see cref="PreferredHeight"/> changes (XL opened or closed, or a track
    /// with or without an XL device selected), so the host can size the Devices panel.</summary>
    public event Action? PreferredHeightChanged;

    // The track's XL device, or -1. With more than one (a duplicate, a device moved in, an older
    // edit), the one that held the panel last keeps it — or the first in the chain — and the rest
    // go back to L. One XL device writes nothing.
    private int ResolveXl()
    {
        if (_trackId <= 0) return -1;
        int n = _engine.TrackDeviceCount(_trackId);
        int owner = -1, first = -1, found = 0;
        for (int i = 0; i < n; i++)
        {
            if (!XlCapable(i) || DeviceView.Size(_engine, _trackId, i) != CardSize.XL) continue;
            found++;
            if (first < 0) first = i;
            if (owner < 0 && Extra(ChainKind.Effect, i).XlOwner) owner = i;
        }
        int pick = owner >= 0 ? owner : first;
        if (found > 1)
            for (int i = 0; i < n; i++)
                if (i != pick && XlCapable(i) && DeviceView.Size(_engine, _trackId, i) == CardSize.XL) DeviceView.SetSize(_engine, _trackId, i, CardSize.L);
        for (int i = 0; i < n; i++)
            if (XlCapable(i)) Extra(ChainKind.Effect, i).XlOwner = i == pick;
        return pick;
    }

    // A built-in effect whose body opens in XL, shown as a card (not folded) in the track's chain.
    private bool XlCapable(int index)
    {
        int kind = _engine.TrackDeviceBuiltinKind(_trackId, index);
        return kind >= 0 && kind != 5 && !_engine.DeviceFolded(_trackId, index) && _cardFactory.Resolve(kind).SupportsXl;
    }

    // Fills the panel row for the shown track and lays the view out for it.
    private void BuildXl(int count)
    {
        _xlHost.Child = _xlIndex >= 0 ? XlPanel(_xlIndex, count) : null;
        _xlHost.IsVisible = _xlIndex >= 0;
        // Without XL the chain fills the view exactly as before; with it the panel takes what the
        // chain's card row leaves.
        var rows = _layout.RowDefinitions;
        rows[0].Height = _xlIndex >= 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        rows[0].MinHeight = _xlIndex >= 0 ? XlMinHeight : 0;
        rows[1].Height = _xlIndex >= 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        FitLayoutHeight();
        UpdatePreferredHeight();
    }

    private void UpdatePreferredHeight()
    {
        double h = 2 * ChainInset + CardH + (_xlIndex >= 0 ? _xlHeight + ChainInset : 0);
        if (Math.Abs(h - _preferredHeight) > 0.5) { _preferredHeight = h; PreferredHeightChanged?.Invoke(); }
    }

    // The grid fills the view, but never below the panel's minimum plus the chain (then it scrolls).
    private void FitLayoutHeight()
    {
        double min = _xlIndex >= 0 ? XlMinHeight + ChainInset + 2 * ChainInset + CardH : 0;
        double vh = _outer.Bounds.Height;
        _layout.Height = vh > 0 ? Math.Max(vh, min) : double.NaN;
    }

    // The panel: the device's own body in the shared shell, stretched to the panel. The body is
    // built here only — its card in the chain is a placeholder — so its live views tick once.
    private Control XlPanel(int index, int count)
    {
        int kind = _engine.TrackDeviceBuiltinKind(_trackId, index);
        var strategy = _cardFactory.Resolve(kind);
        var ctx = NewCardContext();
        var body = strategy.Build(ctx, index);
        // Shrink closes the panel. The panel has no Remove ×: by the panel's edge it read as
        // "close" and removed the device; the card in the chain keeps it.
        var close = IconKey(GlyphKind.Shrink, Loc.T("device.xl.close_the_xl_panel_the"), () => CloseXl(index));
        var extras = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (strategy.HeaderAccessory(ctx, index) is { } toggle) extras.Children.Add(toggle);
        extras.Children.Add(PopOutKey(index));
        extras.Children.Add(close);
        var spec = new ShellSpec(
            Name: _engine.DeviceName(_trackId, index), Subtitle: strategy.Subtitle ?? "BUILT-IN", DeviceIndex: index, Count: count,
            Bypassed: _engine.DeviceBypassed(_trackId, index), Bypassable: true, CanMove: true, CanDelete: false, PresetKind: kind,
            IsInstrument: false, Width: double.NaN, Kind: ChainKind.Effect, HeaderExtra: extras, Height: double.NaN, Xl: true);
        var card = BuildCardShell(spec, strategy.FullBleed ? body : new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = body });
        // Esc in the panel (a text box that takes it keeps it) closes it.
        card.KeyDown += (_, e) =>
        {
            if (e.Handled || e.Key != Key.Escape) return;
            e.Handled = true;
            CloseXl(index);
        };
        return card;
    }

    // The device's card in the chain while it is in XL: 260 × 260, the same header (with the size
    // toggle to bring it back), and a body that says where the editor went.
    private Control XlPlaceholder(int index, int count, int kind, string name, bool bypassed)
    {
        var strategy = _cardFactory.Resolve(kind);
        var ctx = NewCardContext();
        var caption = new TextBlock
        {
            Text = Loc.T("device.xl.open_in_xl_above"), FontSize = NotaType.Caption, Foreground = TextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center,
        };
        var body = new Border { Background = Brushes.Transparent, Child = Section(caption), Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(body, Loc.T("device.xl.the_editor_is_open_in"));
        // A click selects the device, so its panel is the keyboard target.
        body.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(body).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            SelectDevice(ChainKind.Effect, index, e.KeyModifiers);
            _xlHost.BringIntoView();   // in a short window the panel may be scrolled away
        };
        var spec = new ShellSpec(
            Name: name, Subtitle: strategy.Subtitle ?? "BUILT-IN", DeviceIndex: index, Count: count, Bypassed: bypassed, Bypassable: true,
            CanMove: true, CanDelete: true, PresetKind: kind, IsInstrument: false, Width: CardLayout.MiniWidth, Kind: ChainKind.Effect,
            HeaderExtra: HeaderKeys(strategy.HeaderAccessory(ctx, index), PopOutKey(index)), Compact: true, Placeholder: true);
        return BuildCardShell(spec, new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = body });
    }

    // Close (the panel's ×, Esc): the device goes back to L.
    private void CloseXl(int index)
    {
        if (_trackId <= 0 || index >= _engine.TrackDeviceCount(_trackId)) return;
        DeviceView.SetSize(_engine, _trackId, index, CardSize.L);
        Rebuild();
    }
}
