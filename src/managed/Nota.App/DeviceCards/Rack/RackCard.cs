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
// partials. The card is rebuilt after every structural edit, so the view state (tabs, the
// chosen macro, mapping, the probe) lives in RackCardState, keyed by the rack, and survives
// rebuilds and theme switches. The chosen chain stays in the context, shared with the chain
// view. Colour only from NotaPalette slots: every theme recolours it live.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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

    public bool Mapping => MappingMacro >= 0;

    private static readonly Dictionary<(int Track, RackKind Kind, int Index), RackCardState> Store = new();

    /// <summary>The state of one rack: a track's instrument rack, or the rack at an audio or MIDI device index.</summary>
    public static RackCardState For(int trackId, RackKind kind, int index)
    {
        var key = (trackId, kind, index);
        if (!Store.TryGetValue(key, out var s)) Store[key] = s = new RackCardState();
        return s;
    }
}

internal sealed partial class RackCard(DeviceCardContext ctx, IRackAccess access, int rackIndex)
{
    public const double MacroColumnWidth = 116, RightPanelWidth = 160, StatusHeight = 18, TabHeight = 20, Gutter = 6;

    private readonly DeviceCardContext _ctx = ctx;
    private readonly IRackAccess _a = access;
    private readonly RackKindProfile _profile = RackKindProfile.For(access.Kind);
    private readonly RackCardState _st = RackCardState.For(ctx.TrackId, access.Kind, rackIndex);
    private readonly List<Action> _tick = new();

    private int ChainCount => _a.ChainCount();

    /// <summary>The chosen chain, clamped to the rack's chains (0 when it has none).</summary>
    private int Chosen
    {
        get
        {
            int n = ChainCount;
            return n > 0 ? Math.Clamp(_ctx.SelectedChain, 0, n - 1) : 0;
        }
        set => _ctx.SelectedChain = value;
    }

    // Rebuild on the next dispatcher cycle, never inside a pointer handler: tearing down the
    // clicked element mid-dispatch corrupts pointer routing.
    private void Rebuild() => Avalonia.Threading.Dispatcher.UIThread.Post(_ctx.RequestRebuild);

    public Control Build()
    {
        Chosen = Chosen;
        _st.SelectedMacro = Math.Clamp(_st.SelectedMacro, 0, 7);
        if (_st.Centre != RackCentreTab.Chains) _st.MappingMacro = -1;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{MacroColumnWidth},*,{RightPanelWidth}"),
            RowDefinitions = new RowDefinitions($"*,{StatusHeight}"),
            ColumnSpacing = Gutter, RowSpacing = Gutter / 2,
            Margin = new Thickness(NotaSpace.DeviceInset, NotaSpace.DeviceInset, NotaSpace.DeviceInset, 0),
        };
        Place(grid, Section(MacroColumn()), 0, 0);
        Place(grid, Section(Centre()), 0, 1);
        Place(grid, Section(RightPanel()), 0, 2);
        var status = Section(StatusLine(), SectionEdge.Top);
        Grid.SetColumnSpan(status, 3);
        Place(grid, status, 1, 0);

        if (_tick.Count > 0) _ctx.AddDeviceRefresher(() => { for (int i = 0; i < _tick.Count; i++) _tick[i](); });
        return grid;
    }

    private static void Place(Grid g, Control c, int row, int col)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        g.Children.Add(c);
    }

    // ---- centre: tabs over the chosen tab's body -------------------------------------

    private Control Centre()
    {
        var tabs = Segments(["Chains", _profile.ZoneTab, "Macros"], () => (int)_st.Centre, i =>
        {
            _st.Centre = (RackCentreTab)i;
            if (_st.Centre != RackCentreTab.Chains) _st.MappingMacro = -1; // mapping happens on the chain
            Rebuild();
        }, out _, fontSize: NotaType.RowLabel);
        var summary = Mono(CentreSummary(), NotaPalette.TextTertiary);
        summary.HorizontalAlignment = HorizontalAlignment.Right;
        var head = new Grid
        {
            Height = TabHeight, ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { tabs, summary },
        };
        Grid.SetColumn(summary, 1);

        Control body = _st.Centre switch
        {
            RackCentreTab.Zones => ZonesTab(),
            RackCentreTab.Macros => MacrosTab(),
            _ => ChainsTab(),
        };
        DockPanel.SetDock(head, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { head, body } };
    }

    private string CentreSummary()
    {
        int n = ChainCount;
        string chains = $"{n} chain{(n == 1 ? "" : "s")}";
        return _st.Centre switch
        {
            RackCentreTab.Zones => _profile.KeyZones ? "Keys C−1–G9 · vel 0–127" : "Select 0–127",
            RackCentreTab.Macros => $"{MappedMacroCount()} of 8 mapped",
            _ => _profile.Modes.Length > 0 ? $"{chains} · {_profile.ModeLabel(RackMode()).ToLowerInvariant()}" : chains,
        };
    }

    // ---- right panel: Chain · Rack out|Output -----------------------------------------

    private Control RightPanel()
    {
        var tabs = Segments(["Chain", _profile.OutputTab], () => _st.OutputTab ? 1 : 0, i =>
        {
            _st.OutputTab = i == 1;
            Rebuild();
        }, out _, fill: true, fontSize: NotaType.RowLabel);
        var head = new Border { Height = TabHeight, Child = tabs };
        DockPanel.SetDock(head, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { head, _st.OutputTab ? OutputPanel() : ChainPanel() } };
    }

    // ---- status line ------------------------------------------------------------------

    private Control StatusLine()
    {
        var text = new TextBlock
        {
            Text = StatusText(), FontSize = NotaType.KnobLabel, Foreground = NotaPalette.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return new Border { Height = StatusHeight, Padding = new Thickness(2, 0), Child = text };
    }

    // ---- shared helpers ---------------------------------------------------------------

    private int MappedMacroCount()
    {
        var seen = new bool[8];
        int n = 0;
        for (int i = 0; i < _a.MappingCount(); i++)
            if (_a.TryGetMapping(i, out var m) && m.Macro is >= 0 and < 8 && !seen[m.Macro]) { seen[m.Macro] = true; n++; }
        return n;
    }

    /// <summary>The rack's routing mode (0 Parallel when it has no choice). Filled in per rack kind.</summary>
    private int RackMode() => 0;

    private static TextBlock Mono(string text, IBrush ink, double size = NotaType.KnobValue) => new()
    {
        Text = text, FontFamily = NotaFonts.MonoFamily, FontSize = size, Foreground = ink,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A region a later step fills in: one Ink 5 line.</summary>
    private static Control Pending(string text) => new Border { Padding = new Thickness(4), Child = Hint(text) };
}
