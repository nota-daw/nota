// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the shared rack card (specs/040, nota-design "Nota Racks", frames
// 1a–1f): one body for the Instrument, Audio Effect and MIDI Effect racks, drawn inside
// the shared device shell (which owns the header: name, preset, badge, power switch).
//
//   macros (116) │ Chains · Zones|Select · Macros │ Chain · Rack out|Output (160)
//   ─────────────┴─────────────────────────────────┴──────────────────────────────
//   status line (18)
//
// The columns are card sections (a rule where they meet), never framed boxes. What differs
// between the racks comes from RackKindProfile; the regions live in the RackCard.*.cs
// partials of RackCardView, so they share its device popups, add menus and Browser drops.
// The card is rebuilt after every structural edit, so the view state (tabs, the chosen
// macro, mapping, the probe) lives in RackCardState, keyed by the rack, and survives
// rebuilds and theme switches; the chosen chain stays in the context. Colour only from
// NotaPalette slots: every theme recolours it live.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal enum RackCentreTab { Chains, Zones, Macros }

/// <summary>A rack card's view state. Not saved with the project.</summary>
internal sealed class RackCardState
{
    public RackCentreTab Centre = RackCentreTab.Chains;
    public bool OutputTab;        // right panel: false = Chain, true = Rack out / Output
    public int SelectedMacro;
    public int MappingMacro = -1; // the macro being mapped in place, or -1
    public int ProbePitch = -1;   // the key under the pointer on the Zones tab, or -1
    public int SelectedChain;     // the chain shown in the device lane and the Chain panel

    public bool Mapping => MappingMacro >= 0;

    private static readonly Dictionary<(int Track, RackKind Kind, int Index), RackCardState> Store = new();

    /// <summary>The state of one rack: a track's instrument rack, or the rack at an audio or MIDI device index.</summary>
    public static RackCardState For(int trackId, RackKind kind, int index)
    {
        var key = (trackId, kind, index);
        if (!Store.TryGetValue(key, out var s)) Store[key] = s = new RackCardState();
        return s;
    }

    /// <summary>Ends macro mapping on every rack card (Esc). True when one was mapping.</summary>
    public static bool EndAllMapping()
    {
        bool any = false;
        foreach (var s in Store.Values)
            if (s.Mapping) { s.MappingMacro = -1; any = true; }
        return any;
    }

    /// <summary>Whether any rack card is mapping a macro right now.</summary>
    public static bool AnyMapping()
    {
        foreach (var s in Store.Values) if (s.Mapping) return true;
        return false;
    }
}

internal sealed partial class RackCardView
{
    public const double RcMacroWidth = 116, RcRightWidth = 166, RcStatusHeight = 18, RcTabHeight = 20, RcGutter = 6;

    private IRackAccess _ra = null!;
    private RackKindProfile _rp = RackKindProfile.Instrument;
    private RackCardState _rs = new();

    /// <summary>The shared rack card body for <paramref name="a"/>. <paramref name="rackIndex"/> tells
    /// racks on one track apart (-1 = the track's instrument, else its device or MIDI effect index).</summary>
    internal Control BuildRackCard(IRackAccess a, int rackIndex)
    {
        _ra = a;
        _rp = RackKindProfile.For(a.Kind);
        _rs = RackCardState.For(T, a.Kind, rackIndex);
        _ownSel = true;
        _irTick.Clear();
        _rcRows.Clear();
        _rcCards.Clear();
        int n = a.ChainCount();
        Sel = n > 0 ? Math.Clamp(Sel, 0, n - 1) : 0;
        _rs.SelectedMacro = Math.Clamp(_rs.SelectedMacro, 0, 7);
        if (_rs.Centre != RackCentreTab.Chains) _rs.MappingMacro = -1;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{RcMacroWidth},*,{RcRightWidth}"),
            RowDefinitions = new RowDefinitions($"*,{RcStatusHeight}"),
            ColumnSpacing = RcGutter, RowSpacing = 3,
            Margin = new Thickness(NotaSpace.DeviceInset, NotaSpace.DeviceInset, NotaSpace.DeviceInset, 0),
        };
        // A section's rule is on its left edge: pad the content off it by a gutter, so the rule
        // sits halfway between the columns.
        RcPlace(grid, Section(RcMacroColumn()), 0, 0);
        RcPlace(grid, new RuleSection { Padding = new Thickness(RcGutter, 0, 0, 0), Child = RcCentre() }, 0, 1);
        RcPlace(grid, new RuleSection { Padding = new Thickness(RcGutter, 0, 0, 0), Child = RcRightPanel() }, 0, 2);
        var status = Section(RcStatusLine(), SectionEdge.Top);
        Grid.SetColumnSpan(status, 3);
        RcPlace(grid, status, 1, 0);

        if (_irTick.Count > 0) _ctx.AddDeviceRefresher(() => { for (int i = 0; i < _irTick.Count; i++) _irTick[i](); });
        return grid;
    }

    private static void RcPlace(Grid g, Control c, int row, int col)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        g.Children.Add(c);
    }

    // ---- centre: tabs over the chosen tab's body -------------------------------------

    private Control RcCentre()
    {
        var tabs = Segments(["Chains", _rp.ZoneTab, "Macros"], () => (int)_rs.Centre, i =>
        {
            _rs.Centre = (RackCentreTab)i;
            if (_rs.Centre != RackCentreTab.Chains) _rs.MappingMacro = -1; // mapping happens on the chain
            DeferRebuild();
        }, out _, fontSize: NotaType.RowLabel);
        var summary = RcMono(RcCentreSummary(), NotaPalette.TextTertiary);
        summary.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(summary, 1);
        var head = new Grid { Height = RcTabHeight, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { tabs, summary } };

        Control body = _rs.Centre switch
        {
            RackCentreTab.Zones => RcZonesTab(),
            RackCentreTab.Macros => RcMacrosTab(),
            _ => RcChainsTab(),
        };
        body.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(head, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { head, body } };
    }

    private string RcCentreSummary()
    {
        int n = _ra.ChainCount();
        string chains = n == 1 ? "1 chain" : $"{n} chains";
        return _rs.Centre switch
        {
            RackCentreTab.Zones => _rp.KeyZones ? "Keys C−1–G9 · vel 0–127" : "Select 0–127",
            RackCentreTab.Macros => $"{RcMappedMacros()} of 8 mapped",
            _ => _rp.Modes.Length > 0 ? $"{chains} · {RcModeWord()}" : chains,
        };
    }

    private string RcModeWord() => _ra.RackMode() switch
    {
        RackKindProfile.Serial => "serial",
        RackKindProfile.Select => "select",
        _ => _rp.Kind == RackKind.MidiEffect ? "parallel · merged" : "parallel · summed",
    };

    // ---- right panel: Chain · Rack out|Output -----------------------------------------

    private Control RcRightPanel()
    {
        var tabs = Segments(["Chain", _rp.OutputTab], () => _rs.OutputTab ? 1 : 0, i =>
        {
            _rs.OutputTab = i == 1;
            DeferRebuild();
        }, out _, fill: true, fontSize: NotaType.RowLabel);
        var head = new Border { Height = RcTabHeight, Child = tabs };
        DockPanel.SetDock(head, Dock.Top);
        var body = _rs.OutputTab ? RcOutputPanel() : RcChainPanel();
        body.Margin = new Thickness(0, 6, 0, 0);
        return new DockPanel { LastChildFill = true, Children = { head, body } };
    }

    // ---- status line ------------------------------------------------------------------

    private Control RcStatusLine()
    {
        var text = new TextBlock
        {
            Text = RcStatusText(), FontSize = NotaType.KnobLabel, Foreground = NotaPalette.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return new Border { Height = RcStatusHeight, Padding = new Thickness(2, 0), Child = text };
    }

    // ---- shared helpers ---------------------------------------------------------------

    private int RcMappedMacros()
    {
        var seen = new bool[8];
        int n = 0;
        for (int i = 0; i < _ra.MappingCount(); i++)
            if (_ra.TryGetMapping(i, out var m) && m.Macro is >= 0 and < 8 && !seen[m.Macro]) { seen[m.Macro] = true; n++; }
        return n;
    }

    private int RcTargetCount(int macro)
    {
        int n = 0;
        for (int i = 0; i < _ra.MappingCount(); i++)
            if (_ra.TryGetMapping(i, out var m) && m.Macro == macro) n++;
        return n;
    }

    /// <summary>The chain's display name: its own name, else its first device's.</summary>
    private string RcChainName(int c)
    {
        string n = _ra.ChainName(c);
        if (n.Length > 0) return n;
        if (_rp.TakesInstrument) { n = _ra.ChainInstrumentName(c); if (n.Length > 0) return n; }
        if (_rp.Kind == RackKind.MidiEffect && _ra.ChainMidiCount(c) > 0) return _ra.ChainMidiName(c, 0);
        if (_rp.Kind == RackKind.AudioEffect && _ra.ChainDeviceCount(c) > 0) return _ra.ChainDeviceName(c, 0);
        return $"Chain {c + 1}";
    }

    /// <summary>Whether a chain sounds: not muted, and soloed when any chain is.</summary>
    private bool RcAudible(int c)
    {
        if (_ra.ChainMute(c)) return false;
        for (int i = 0; i < _ra.ChainCount(); i++)
            if (_ra.ChainSolo(i)) return _ra.ChainSolo(c);
        return true;
    }

    private static TextBlock RcMono(string text, IBrush ink, double size = NotaType.KnobValue) => new()
    {
        Text = text, FontFamily = NotaFonts.UiFamily, FontSize = size, Foreground = ink,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock RcText(string text, IBrush ink, double size = NotaType.KnobLabel, FontWeight? weight = null) => new()
    {
        Text = NotaText.Label(text), FontSize = size, Foreground = ink, FontWeight = weight ?? FontWeight.Medium,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A name the user typed (chain, macro): never re-cased.</summary>
    private static TextBlock RcName(string text, IBrush ink, double size = NotaType.KnobLabel) => new()
    {
        Text = text, FontSize = size, Foreground = ink, FontWeight = FontWeight.SemiBold,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A small raised key (Map, Duplicate, …): neutral, or a solid fill with derived ink while on.</summary>
    private static Border RcKey(string text, Action click, bool on = false, SolidColorBrush? onFill = null, bool enabled = true, string? tip = null)
    {
        var fill = onFill ?? NotaPalette.Accent;
        var tb = new TextBlock
        {
            Text = NotaText.Label(text), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium,
            Foreground = on ? NotaPalette.InkOn(fill) : enabled ? NotaPalette.TextSecondary : NotaPalette.TextDisabled,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var b = new Border
        {
            Height = 16, MinWidth = 26, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell,
            Background = on ? fill : NotaPalette.SurfaceRaised, BorderBrush = on ? fill : NotaPalette.BorderDefault,
            BorderThickness = new Thickness(1), Cursor = enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            VerticalAlignment = VerticalAlignment.Center, Child = tb,
        };
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.PointerPressed += (_, e) =>
        {
            if (!enabled || !e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            click();
        };
        return b;
    }

    /// <summary>A one-line rename field under <paramref name="anchor"/>.</summary>
    private void RcPromptRename(Control anchor, string current, Action<string> apply)
    {
        var box = new TextBox { Text = current, FontSize = 9, Width = 110, Height = 20, Padding = new Thickness(4, 0) };
        var fly = new Flyout { Content = box };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { apply(box.Text ?? ""); fly.Hide(); DeferRebuild(); }
            else if (e.Key == Key.Escape) fly.Hide();
        };
        fly.ShowAt(anchor);
        box.Focus();
        box.SelectAll();
    }
}
