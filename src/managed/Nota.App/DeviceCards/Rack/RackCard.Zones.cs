// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's Zones / Select tab (specs/040). Instrument and MIDI
// racks: each chain's key and velocity zone, dragged by their edges, and a probe — hover a
// key and every row shows whether that chain would play it (at velocity 100), the footer
// names them. Audio Effect and MIDI racks: each chain's select range and the selector, which
// only counts in Select mode (the rows go faint otherwise). Edges move the engine live; the
// card rebuilds when a drag ends so the chain list's summaries follow.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed partial class RackCardView
{
    private const double RcZoneNameWidth = 80, RcVelWidth = 64;
    private const int RcProbeVelocity = 100;

    private Control RcZonesTab()
    {
        var a = _ra;
        int n = a.ChainCount();
        bool keys = _rp.KeyZones, vel = _rp.VelocityZones, select = _rp.SelectRanges;
        bool selectOn = a.RackMode() == RackKindProfile.Select;
        var keyBars = new List<RackRangeBar>();
        var rowHosts = new List<(Border Row, TextBlock Name, int Chain)>();
        TextBlock result = RcMono("", NotaPalette.TextTertiary, 7.5);

        var rows = new StackPanel { Spacing = 3 };

        // Column titles + ruler.
        var ruler = new RackRuler
        {
            Ticks = keys ? [(12, "C0"), (36, "C2"), (60, "C4"), (84, "C6"), (108, "C8")] : [(0, "0"), (32, "32"), (64, "64"), (96, "96"), (127, "127")],
        };
        var titles = new Grid { ColumnDefinitions = new ColumnDefinitions($"{RcZoneNameWidth},*{(vel ? $",{RcVelWidth}" : "")}"), ColumnSpacing = 8, Height = 12 };
        titles.Children.Add(RcText("Chain", NotaPalette.TextTertiary, 7.5));
        Grid.SetColumn(ruler, 1); titles.Children.Add(ruler);
        if (vel) { var vt = RcText("Velocity", NotaPalette.TextTertiary, 7.5); Grid.SetColumn(vt, 2); titles.Children.Add(vt); }
        rows.Children.Add(titles);

        // The selector (Select mode picks the chains whose select range holds it).
        if (select)
        {
            bool follows = a.SelectorFollows();
            int sel = (int)Math.Round(a.LiveSelector() * 127);
            var bar = new RackRangeBar { Single = true, Lo = sel, Hi = sel, Dim = !selectOn || follows, Height = 10, IsHitTestVisible = !follows };
            ToolTip.SetTip(bar, follows
                ? "The selector follows the input level · turn Follow input off on the Rack out tab to set it by hand"
                : "Drag to choose which chains play · right-click to map it to a macro");
            bar.Changed += (lo, _) => { a.SetSelector(lo / 127f); RcProbeUpdate(); };
            bar.Committed += DeferRebuild;
            bar.ContextRequested += (_, e) => { e.Handled = true; RcSelectorMenu(bar); };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{RcZoneNameWidth},*{(vel ? $",{RcVelWidth}" : "")}"), ColumnSpacing = 8, Height = 14 };
            row.Children.Add(RcText("Selector", NotaPalette.TextSecondary, NotaType.KnobLabel));
            Grid.SetColumn(bar, 1); row.Children.Add(bar);
            rows.Children.Add(row);
            _irTick.Add(() =>
            {
                int live = (int)Math.Round(a.LiveSelector() * 127);
                if (live != bar.Lo) { bar.Lo = bar.Hi = live; bar.InvalidateVisual(); RcProbeUpdate(); }
            });
        }

        for (int c = 0; c < n; c++)
        {
            int cc = c;
            var hue = ChainHue(c);
            var stripe = new Border { Width = 3, Height = 9, CornerRadius = NotaRadius.Bar, Background = hue, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            var name = RcName(RcChainName(c), c == Sel ? NotaPalette.AccentBright : NotaPalette.TextPrimary, NotaType.KnobLabel);
            var nameCell = new DockPanel { LastChildFill = true, Children = { RcDock(stripe, Dock.Left), name } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{RcZoneNameWidth},*{(vel ? $",{RcVelWidth}" : "")}"), ColumnSpacing = 8, Height = 22, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(nameCell);

            if (keys)
            {
                a.ChainZone(c, out int kl, out int kh, out int vl, out int vh);
                var kb = new RackRangeBar { Lo = kl, Hi = kh, Hue = hue, Label = (lo, hi) => $"{NoteName(lo)}–{NoteName(hi)}" };
                kb.Changed += (lo, hi) => { a.ChainZone(cc, out _, out _, out int l, out int h); a.SetChainZone(cc, lo, hi, l, h); RcProbeUpdate(); };
                kb.Committed += () => { Sel = cc; DeferRebuild(); };
                kb.Hover += v => { _rs.ProbePitch = v; RcProbeUpdate(); };
                ToolTip.SetTip(kb, "Drag the edges to set the key zone · hover to test a note");
                Grid.SetColumn(kb, 1); grid.Children.Add(kb);
                keyBars.Add(kb);
                if (vel)
                {
                    var vb = new RackRangeBar { Lo = vl, Hi = vh, Hue = hue, Label = (lo, hi) => $"{lo}–{hi}" };
                    vb.Changed += (lo, hi) => { a.ChainZone(cc, out int k0, out int k1, out _, out _); a.SetChainZone(cc, k0, k1, lo, hi); RcProbeUpdate(); };
                    vb.Committed += () => { Sel = cc; DeferRebuild(); };
                    ToolTip.SetTip(vb, "Drag the edges to set the velocity zone");
                    Grid.SetColumn(vb, 2); grid.Children.Add(vb);
                }
                if (select)
                {
                    // MIDI rack: the select range sits under the key zone, faint outside Select mode.
                    a.SelectRange(c, out int sl, out int sh);
                    var sb = new RackRangeBar { Lo = sl, Hi = sh, Hue = hue, Dim = !selectOn, Height = 6, Margin = new Thickness(0, 14, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                    sb.Changed += (lo, hi) => { a.SetSelectRange(cc, lo, hi); RcProbeUpdate(); };
                    sb.Committed += () => { Sel = cc; DeferRebuild(); };
                    ToolTip.SetTip(sb, "Select range: in Select mode this chain plays while the selector is inside it");
                    kb.VerticalAlignment = VerticalAlignment.Top;
                    kb.Height = 12;
                    var stack = new Panel { Children = { kb, sb } };
                    grid.Children.Remove(kb);
                    Grid.SetColumn(stack, 1); grid.Children.Add(stack);
                    grid.Height = 24;
                }
            }
            else
            {
                a.SelectRange(c, out int sl, out int sh);
                var sb = new RackRangeBar { Lo = sl, Hi = sh, Hue = hue, Dim = !selectOn, Label = (lo, hi) => $"{lo}–{hi}" };
                sb.Changed += (lo, hi) => { a.SetSelectRange(cc, lo, hi); RcProbeUpdate(); };
                sb.Committed += () => { Sel = cc; DeferRebuild(); };
                ToolTip.SetTip(sb, "Drag the edges to set the select range");
                Grid.SetColumn(sb, 1); grid.Children.Add(sb);
            }
            var rowBorder = new Border { CornerRadius = NotaRadius.Badge, Padding = new Thickness(4, 0, 0, 0), Child = grid };
            rows.Children.Add(rowBorder);
            rowHosts.Add((rowBorder, name, c));
        }

        string hint = keys ? "Drag zone edges · hover to test a note"
            : selectOn ? "Drag the selector or map it to a macro" : "Ranges apply when Chains run is set to Select";
        var foot = new DockPanel
        {
            LastChildFill = false, Height = 16,
            Children = { RcDock(RcText(hint, NotaPalette.TextTertiary, 7.5, FontWeight.Normal), Dock.Left), RcDock(result, Dock.Right) },
        };
        var footer = Section(foot, SectionEdge.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // Probe: which chains play the hovered key (or the selector, for an audio rack).
        _rcProbe = () =>
        {
            int probe = _rs.ProbePitch;
            var hits = RcProbeHits(probe);
            foreach (var kb in keyBars) { kb.Probe = keys ? probe : -1; kb.InvalidateVisual(); }
            foreach (var (row, nm, ch) in rowHosts)
            {
                bool hit = hits.Contains(ch);
                row.Background = hit ? NotaPalette.Wash(NotaPalette.Teal, 0x22) : Brushes.Transparent;
                nm.Foreground = hit ? NotaPalette.TealBright : ch == Sel ? NotaPalette.AccentBright : NotaPalette.TextPrimary;
            }
            string names = string.Join(keys ? " + " : ", ", hits.Select(RcChainName));
            if (keys)
                result.Text = probe < 0 ? "Hover the zones" : $"{NoteName(probe)} · vel {RcProbeVelocity} → {(names.Length > 0 ? names : "nothing")}";
            else
                result.Text = $"selector {(int)Math.Round(a.LiveSelector() * 127)} → {(names.Length > 0 ? names : "silence")}{(selectOn ? "" : " · inactive")}";
            result.Foreground = hits.Count > 0 ? NotaPalette.TealBright : NotaPalette.TextTertiary;
        };
        RcProbeUpdate();
        return new DockPanel { LastChildFill = true, Children = { footer, scroll } };
    }

    // Map the selector to a macro (the selector is a rack control, not a chain parameter).
    private void RcSelectorMenu(Control anchor)
    {
        var f = new MenuFlyout();
        f.Items.Add(MenuKit.Header("Selector", null, "Map to a macro"));
        f.Items.Add(new Separator());
        for (int m = 0; m < 8; m++)
        {
            int mm = m;
            var mi = new MenuItem { Header = $"Macro {m + 1} · {_ra.MacroName(m)}" };
            mi.Click += (_, _) => RcMapSelector(mm);
            f.Items.Add(mi);
        }
        f.ShowAt(anchor, showAtPointer: true);
    }

    private void RcMapSelector(int macro)
    {
        _ra.AddMacroMapping(macro, 0, Nota.Application.RackMacroMapping.RackControl, 0, 0f, 1f);
        _rs.SelectedMacro = macro;
        DeferRebuild();
    }

    private Action? _rcProbe;
    private void RcProbeUpdate() => _rcProbe?.Invoke();

    /// <summary>The chains that would take a note at <paramref name="pitch"/> (velocity 100), or, for an
    /// audio rack, the chains the selector picks.</summary>
    private List<int> RcProbeHits(int pitch)
    {
        var a = _ra;
        var hits = new List<int>();
        bool selectOn = a.RackMode() == RackKindProfile.Select;
        int selector = (int)Math.Round(a.LiveSelector() * 127);
        for (int c = 0; c < a.ChainCount(); c++)
        {
            if (!RcAudible(c)) continue;
            if (_rp.SelectRanges && (selectOn || !_rp.KeyZones))
            {
                a.SelectRange(c, out int sl, out int sh);
                if (selector < sl || selector > sh) continue;
            }
            if (_rp.KeyZones)
            {
                if (pitch < 0) continue;
                a.ChainZone(c, out int kl, out int kh, out int vl, out int vh);
                if (pitch < kl || pitch > kh || RcProbeVelocity < vl || RcProbeVelocity > vh) continue;
            }
            hits.Add(c);
        }
        return hits;
    }
}
