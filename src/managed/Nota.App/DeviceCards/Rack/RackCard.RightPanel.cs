// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's right panel (specs/040) and status line.
// Chain: the chosen chain's name, its mix (Volume and Pan; a MIDI chain's Volume scales
// velocity and it has Transpose instead of Pan), its zone or select range (a click opens
// that tab), Duplicate and Delete. Rack out / Output: how chains run (Audio, MIDI) and the
// rack's own output — Instrument: volume, glide, mono; Audio: dry/wet, gain, latency
// compensation; MIDI: merge duplicate notes, pass notes outside every zone through.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed partial class RackCardView
{
    private const double RcRowLabel = 50;

    private Control RcChainPanel()
    {
        var a = _ra;
        int n = a.ChainCount();
        if (n == 0) return new Border { Padding = new Thickness(2), Child = Hint("No chain yet.") };
        int c = Sel;
        var stack = new StackPanel { Spacing = 7 };

        var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Badge, Background = ChainHue(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        string name = RcChainName(c);
        var nameTb = RcName(name, NotaPalette.TextPrimary, NotaType.RowLabel);
        nameTb.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(nameTb, "Double-click to rename");
        nameTb.DoubleTapped += (_, _) => RcPromptRename(nameTb, name, s => a.SetChainName(c, s));
        var idx = RcMono($"{c + 1} / {n}", NotaPalette.TextTertiary, 7.5);
        stack.Children.Add(new DockPanel { LastChildFill = true, Children = { RcDock(dot, Dock.Left), RcDock(idx, Dock.Right), nameTb } });

        // Mix.
        if (_rp.VolumeIsVelocity)
        {
            stack.Children.Add(RcSlider("Volume", () => a.ChainVelScale(c) / 2.0, v => a.SetChainVelScale(c, (float)(v * 2)),
                () => NotaNum.Pct(a.ChainVelScale(c)), reset: () => a.SetChainVelScale(c, 1f)));
        }
        else
        {
            var row = RcSlider("Volume", () => VolNorm(a.ChainGain(c)), v => a.SetChainGain(c, NormVol(v)),
                () => VolText(a.ChainGain(c)), reset: () => a.SetChainGain(c, 1f));
            MidiLearn.Bind(row, MidiTarget.RackChainGain(T, a.AutomationDeviceIndex, c), "Chain Volume");
            stack.Children.Add(row);
        }
        if (_rp.ChainTranspose)
            stack.Children.Add(RcSlider("Transpose", () => (a.ChainTranspose(c) + 48) / 96.0, v => a.SetChainTranspose(c, (int)Math.Round(v * 96 - 48)),
                () => NotaNum.Unit(a.ChainTranspose(c), "+0;−0;0", "st"), reset: () => a.SetChainTranspose(c, 0), bipolar: true));
        if (_rp.ChainPan)
            stack.Children.Add(RcSlider("Pan", () => (a.ChainPan(c) + 1) / 2.0, v => a.SetChainPan(c, (float)(v * 2 - 1)),
                () => NotaNum.Pan(a.ChainPan(c)), reset: () => a.SetChainPan(c, 0f), bipolar: true));

        // Zone / select range: a click opens the tab that edits it.
        if (_rp.KeyZones)
        {
            a.ChainZone(c, out int kl, out int kh, out int vl, out int vh);
            stack.Children.Add(RcInfoRow("Keys", $"{NoteName(kl)} – {NoteName(kh)}"));
            stack.Children.Add(RcInfoRow("Vel", $"{vl}–{vh}"));
        }
        if (_rp.SelectRanges)
        {
            a.SelectRange(c, out int sl, out int sh);
            stack.Children.Add(RcInfoRow("Select", $"{sl}–{sh}"));
        }

        bool full = _rp.Kind == RackKind.MidiEffect && n >= RcMidiChainLimit;
        var dup = RcKey("Duplicate", () => { int d = a.DuplicateChain(c); if (d >= 0) Sel = d; DeferRebuild(); }, enabled: !full,
            tip: full ? $"A MIDI Effect Rack holds up to {RcMidiChainLimit} chains" : "Copy this chain after itself");
        var del = RcKey("Delete", () => RcDeleteChain(c), enabled: n > 1, tip: n > 1 ? "Delete this chain" : "A rack keeps at least one chain");
        dup.HorizontalAlignment = del.HorizontalAlignment = HorizontalAlignment.Stretch;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4, Children = { dup, del } };
        Grid.SetColumn(del, 1);
        var foot = Section(buttons, SectionEdge.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        return new DockPanel { LastChildFill = true, Children = { foot, stack } };
    }

    private Control RcInfoRow(string key, string value)
    {
        var k = RcText(key, NotaPalette.TextTertiary, NotaType.KnobLabel);
        k.Width = RcRowLabel;
        var v = RcMono(value, NotaPalette.TextPrimary, 8);
        var chev = new Glyph(GlyphKind.ChevronRight, 7) { Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel
        {
            LastChildFill = true, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Children = { RcDock(k, Dock.Left), RcDock(chev, Dock.Right), v },
        };
        ToolTip.SetTip(row, $"Edit on the {_rp.ZoneTab} tab");
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            _rs.Centre = RackCentreTab.Zones;
            DeferRebuild();
        };
        return row;
    }

    private Control RcSlider(string label, Func<double> norm, Action<double> set, Func<string> text, Action? reset = null, bool bipolar = false)
    {
        var row = SliderRow(label, norm, set, text, out var sync, reset: reset, bipolar: bipolar, labelWidth: RcRowLabel, valueWidth: 36);
        _irTick.Add(sync);
        return row;
    }

    private Control RcOutputPanel()
    {
        var a = _ra;
        var stack = new StackPanel { Spacing = 8 };
        if (_rp.Modes.Length > 0)
        {
            var labels = Array.ConvertAll(_rp.Modes, m => m.Label);
            int Cur() { int mode = a.RackMode(); return Array.FindIndex(_rp.Modes, m => m.Id == mode); }
            var seg = Segments(labels, Cur, i => { a.SetRackMode(_rp.Modes[i].Id); DeferRebuild(); }, out var segSync, fill: true, padX: 2, fontSize: NotaType.KnobLabel);
            _irTick.Add(segSync);
            var hint = new TextBlock
            {
                Text = _rp.ModeHint(a.RackMode()), FontSize = 7.5, Foreground = NotaPalette.TextSecondary, TextWrapping = TextWrapping.Wrap,
            };
            stack.Children.Add(new StackPanel { Spacing = 4, Children = { RcText("Chains run", NotaPalette.TextTertiary, NotaType.KnobLabel), seg, hint } });
        }

        var toggles = new StackPanel { Spacing = 6 };
        switch (_rp.Kind)
        {
            case RackKind.Instrument:
            {
                var vol = RcSlider("Volume", () => VolNorm(a.RackVolume()), v => a.SetRackVolume(NormVol(v)), () => VolText(a.RackVolume()), reset: () => a.SetRackVolume(1f));
                MidiLearn.Bind(vol, MidiTarget.RackVolume(T, a.AutomationDeviceIndex), "Rack Volume");
                stack.Children.Add(vol);
                var glide = RcSlider("Glide", () => a.RackGlide(), v => a.SetRackGlide((float)v), () => NotaNum.Unit(a.RackGlide() * 500, "0", "ms"), reset: () => a.SetRackGlide(0f));
                ToolTip.SetTip(glide, "Glides between notes in mono mode");
                stack.Children.Add(glide);
                toggles.Children.Add(Switch("Mono (last note)", () => a.RackMono(), () => a.SetRackMono(!a.RackMono()), out var monoSync));
                _irTick.Add(monoSync);
                ToolTip.SetTip(toggles.Children[^1], "All chains share one voice, the last key wins");
                break;
            }
            case RackKind.AudioEffect:
            {
                stack.Children.Add(RcSlider("Dry/wet", () => a.RackDryWet(), v => a.SetRackDryWet((float)v), () => NotaNum.Pct(a.RackDryWet()), reset: () => a.SetRackDryWet(1f)));
                // Gain: ±12 dB around unity, centred.
                static double GainNorm(float lin) => Math.Clamp((lin <= 0.0001f ? -12 : 20 * Math.Log10(lin)) / 24.0 + 0.5, 0, 1);
                static float NormGain(double v) => (float)Math.Pow(10, (v - 0.5) * 24 / 20);
                var gain = RcSlider("Gain", () => GainNorm(a.RackVolume()), v => a.SetRackVolume(NormGain(v)),
                    () => NotaNum.Db(20 * Math.Log10(Math.Max(1e-6, a.RackVolume())), signed: true), reset: () => a.SetRackVolume(1f), bipolar: true);
                MidiLearn.Bind(gain, MidiTarget.RackVolume(T, a.AutomationDeviceIndex), "Rack Gain");
                stack.Children.Add(gain);
                toggles.Children.Add(Switch("Follow input level", () => a.SelectorFollows(), () => { a.SetSelectorFollows(!a.SelectorFollows()); DeferRebuild(); }, out var followSync));
                _irTick.Add(followSync);
                ToolTip.SetTip(toggles.Children[^1], "In Select mode the selector follows the input level instead of its handle");
                toggles.Children.Add(Switch("Latency compensation", () => a.RackPdc(), () => a.SetRackPdc(!a.RackPdc()), out var pdcSync));
                _irTick.Add(pdcSync);
                ToolTip.SetTip(toggles.Children[^1], "Line up chains with different latency");
                break;
            }
            default:
                toggles.Children.Add(Switch("Merge duplicate notes", () => a.RackMerge(), () => a.SetRackMerge(!a.RackMerge()), out var mSync));
                _irTick.Add(mSync);
                ToolTip.SetTip(toggles.Children[^1], "Two chains sending the same pitch play one note");
                toggles.Children.Add(Switch("Pass through outside zones", () => a.RackThru(), () => a.SetRackThru(!a.RackThru()), out var tSync));
                _irTick.Add(tSync);
                ToolTip.SetTip(toggles.Children[^1], "Notes no chain takes reach the instrument unchanged");
                break;
        }
        var foot = Section(toggles, SectionEdge.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        return new DockPanel { LastChildFill = true, Children = { foot, stack } };
    }

    // ---- status line ------------------------------------------------------------------

    private string RcStatusText()
    {
        var a = _ra;
        int n = a.ChainCount(), mapped = RcMappedMacros();
        string chains = n == 1 ? "1 chain" : $"{n} chains";
        string macros = mapped == 1 ? "1 macro mapped" : $"{mapped} macros mapped";
        switch (_rp.Kind)
        {
            case RackKind.AudioEffect:
            {
                string mode = a.RackMode() switch
                {
                    RackKindProfile.Serial => "Serial",
                    RackKindProfile.Select => $"Select {(int)Math.Round(a.LiveSelector() * 127)}{(a.SelectorFollows() ? " · follows input" : "")}",
                    _ => "Parallel",
                };
                return $"{mode} · {chains} · dry/wet {NotaNum.Pct(a.RackDryWet())} · {macros}";
            }
            case RackKind.MidiEffect:
                return $"{(a.RackMode() == RackKindProfile.Select ? "Select" : "Parallel")} · {chains} → merged MIDI · {macros}";
            default:
            {
                var hits = RcProbeHits(_rs.ProbePitch);
                string probe = _rs.ProbePitch >= 0 && _rs.Centre == RackCentreTab.Zones
                    ? $"{NoteName(_rs.ProbePitch)} → {(hits.Count > 0 ? string.Join(" + ", hits.ConvertAll(RcChainName)) : "nothing")}"
                    : a.RackMono() ? "mono" : "poly";
                return $"{chains} · {probe} · {macros}";
            }
        }
    }
}
