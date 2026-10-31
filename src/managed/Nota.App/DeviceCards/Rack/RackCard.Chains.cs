// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the rack card's Chains tab (specs/040): the chain list (colour stripe,
// name, mute, solo, zone summary, level) beside the chosen chain's devices left to right as
// compact cards. An Instrument Rack chain shows three sections: its MIDI effects, its
// instrument, its audio effects (issue #329). A compact card shows a device's key
// parameters (RackKeyParams); while a macro is being mapped it lists every parameter and a
// click maps or unmaps it. A mapped parameter follows its macro and opens it on a click.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Presentation;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal enum RcSlot { Midi, Instrument, Effect }

internal sealed partial class RackCardView
{
    private const double RcChainListWidth = 124, RcCompactWidth = 96;

    // A device's parameter surface, however the rack addresses it.
    private sealed record RcParams(int Count, Func<int, string> Name, Func<int, float> Min, Func<int, float> Max,
        Func<int, float> Get, Action<int, float> Set, Func<float, string> Text);

    private RcParams RcParamsOf(int c, RcSlot slot, int idx)
    {
        var a = _ra;
        return slot switch
        {
            RcSlot.Instrument => new(a.ChainInstrumentParamCount(c), p => a.ChainInstrumentParamName(c, p), _ => 0f, _ => 1f,
                p => a.ChainInstrumentParamGet(c, p), (p, v) => a.ChainInstrumentParamSet(c, p, v), v => Pct(v)),
            RcSlot.Midi => new(a.ChainMidiParamCount(c, idx), p => a.ChainMidiParamName(c, idx, p), p => a.ChainMidiParamMin(c, idx, p),
                p => a.ChainMidiParamMax(c, idx, p), p => a.ChainMidiParamGet(c, idx, p), (p, v) => a.ChainMidiParamSet(c, idx, p, v), DeviceParamControls.Fmt),
            _ => new(a.ChainDeviceParamCount(c, idx), p => a.ChainDeviceParamName(c, idx, p), p => a.ChainDeviceParamMin(c, idx, p),
                p => a.ChainDeviceParamMax(c, idx, p), p => a.ChainDeviceParamGet(c, idx, p), (p, v) => a.ChainDeviceParamSet(c, idx, p, v), DeviceParamControls.Fmt),
        };
    }

    /// <summary>The mapping address of a chain child: −1 the instrument, ≥ 0 an audio device, ≤ −1000 a MIDI effect.</summary>
    private static int RcTarget(RcSlot slot, int idx) => slot switch
    {
        RcSlot.Instrument => -1,
        RcSlot.Midi => RackMacroMapping.MidiDeviceBase - idx,
        _ => idx,
    };

    // ---- the tab ----------------------------------------------------------------------

    private Control RcChainsTab()
    {
        var list = RcChainList();
        list.Width = RcChainListWidth;
        var lane = RcDeviceLane();
        Grid.SetColumn(lane, 1);
        return new Grid { ColumnDefinitions = new ColumnDefinitions($"{RcChainListWidth},*"), ColumnSpacing = 6, Children = { list, lane } };
    }

    // ---- chain list -------------------------------------------------------------------

    private Control RcChainList()
    {
        var a = _ra;
        var rows = new StackPanel { Spacing = 3 };
        for (int c = 0; c < a.ChainCount(); c++) rows.Children.Add(RcChainRow(c));
        var add = RcAddChainKey();
        rows.Children.Add(add);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var hint = Hint(_rp.ChainsHint);
        if (hint is TextBlock tb) { tb.TextWrapping = TextWrapping.Wrap; tb.FontSize = 7.5; }
        DockPanel.SetDock(hint, Dock.Bottom);
        var dock = new DockPanel { LastChildFill = true, Background = Brushes.Transparent, Children = { hint, scroll } };
        // A device dropped on the list (not on a row) starts a new chain.
        DragDrop.SetAllowDrop(dock, true);
        DragDrop.AddDragOverHandler(dock, (_, e) =>
        {
            if (!BrowserView.IsBrowserDrag || BrowserView.CurrentDrag is not { } it || !RcIsDevice(it)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow();
        });
        DragDrop.AddDropHandler(dock, (_, e) =>
        {
            if (BrowserView.CurrentDrag is not { } it) return;
            e.Handled = true;
            RcDropAsNewChain(it);
        });
        return dock;
    }

    private Control RcAddChainKey()
    {
        var label = Glyph.Labeled("Chain", GlyphKind.Plus, 8, NotaType.KnobLabel);
        var key = new Panel { Height = 16, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
        var dash = new Rectangle
        {
            Stroke = NotaPalette.BorderStrong, StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double> { 3, 2.5 },
            RadiusX = NotaRadius.ClipValue, RadiusY = NotaRadius.ClipValue,
        };
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        key.Children.Add(dash);
        key.Children.Add(label);
        bool full = _rp.Kind == RackKind.MidiEffect && _ra.ChainCount() >= RcMidiChainLimit;
        ToolTip.SetTip(key, full ? $"A MIDI Effect Rack holds up to {RcMidiChainLimit} chains" : "Add a chain — or drop a device from the Browser on the list");
        key.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(key).Properties.IsLeftButtonPressed || full) return;
            e.Handled = true;
            if (_rp.TakesInstrument) { ShowAddChainMenu(_ra, key); return; }
            int c = _ra.AddChain(-1);
            if (c >= 0) Sel = c;
            DeferRebuild();
        };
        return key;
    }

    private const int RcMidiChainLimit = 16;

    private Control RcChainRow(int c)
    {
        var a = _ra;
        bool chosen = c == Sel, audible = RcAudible(c);
        string name = RcChainName(c);

        var stripe = new Border { Width = 3, Height = 9, CornerRadius = NotaRadius.Bar, Background = ChainHue(c), VerticalAlignment = VerticalAlignment.Center };
        var nameTb = RcName(name, chosen ? NotaPalette.AccentBright : audible ? NotaPalette.TextPrimary : NotaPalette.TextDisabled, NotaType.KnobLabel);
        var m = IrMsBtn("M", a.ChainMute(c), NotaPalette.Mute, () => { a.SetChainMute(c, !a.ChainMute(c)); DeferRebuild(); });
        var s = IrMsBtn("S", a.ChainSolo(c), NotaPalette.Solo, () => { a.SetChainSolo(c, !a.ChainSolo(c)); DeferRebuild(); });
        ToolTip.SetTip(m, "Mute chain");
        ToolTip.SetTip(s, "Solo chain");
        MidiLearn.Bind(m, MidiTarget.RackChainMute(T, a.LearnDeviceIndex, c), "Chain Mute");
        MidiLearn.Bind(s, MidiTarget.RackChainSolo(T, a.LearnDeviceIndex, c), "Chain Solo");
        var top = new DockPanel { LastChildFill = true, Children = { RcDock(stripe, Dock.Left), RcDock(s, Dock.Right), RcDock(m, Dock.Right), nameTb } };
        stripe.Margin = new Thickness(0, 0, 4, 0);
        m.Margin = new Thickness(0, 0, 2, 0);

        var summary = RcMono(RcChainSummary(c), NotaPalette.TextSecondary, 7.5);
        var meterFill = new Border { Background = NotaPalette.Success, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = NotaRadius.Bar, Width = 0 };
        var meter = new Border { Height = 3, MinWidth = 16, Background = NotaPalette.BgSunken, CornerRadius = NotaRadius.Bar, Child = meterFill, VerticalAlignment = VerticalAlignment.Center };
        _irTick.Add(() => meterFill.Width = meter.Bounds.Width * Math.Clamp(a.ChainMeter(c), 0, 1));
        summary.Margin = new Thickness(0, 0, 5, 0);
        var bottom = new DockPanel { LastChildFill = true, Children = { RcDock(summary, Dock.Left), meter } };

        var row = new Border
        {
            Height = 34, CornerRadius = NotaRadius.Clip, BorderThickness = new Thickness(1), Padding = new Thickness(5, 0),
            Background = chosen ? RowSel : NotaPalette.SurfaceRaised, BorderBrush = chosen ? NotaPalette.Accent : NotaPalette.BorderDefault,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { top, bottom } },
        };
        if (!audible) row.AttachedToVisualTree += (_, _) => Inactive.Set(row, true, interactive: true);
        _rcRows.Add((row, c)); // a lane device dragged onto the row moves to this chain
        row.PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(row).Properties;
            if (pt.IsRightButtonPressed) { e.Handled = true; RcChainMenu(row, c); return; }
            if (!pt.IsLeftButtonPressed) return;
            if (e.ClickCount == 2 && nameTb.IsPointerOver) { RcPromptRename(nameTb, name, n => a.SetChainName(c, n)); return; }
            RcSelectChain(c); // ⌘V now pastes at the end of this chain
            if (Sel != c) { Sel = c; DeferRebuild(); }
        };
        DragDrop.SetAllowDrop(row, true);
        DragDrop.AddDragOverHandler(row, (_, e) =>
        {
            if (!BrowserView.IsBrowserDrag || BrowserView.CurrentDrag is not { } it || !RcIsDevice(it)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow(); row.BorderBrush = NotaPalette.AccentBright;
        });
        DragDrop.AddDragLeaveHandler(row, (_, _) => row.BorderBrush = chosen ? NotaPalette.Accent : NotaPalette.BorderDefault);
        DragDrop.AddDropHandler(row, (_, e) =>
        {
            row.BorderBrush = chosen ? NotaPalette.Accent : NotaPalette.BorderDefault;
            if (BrowserView.CurrentDrag is not { } it) return;
            e.Handled = true;
            Sel = c;
            RcDropOnChain(c, it);
        });
        return row;
    }

    private string RcChainSummary(int c)
    {
        var a = _ra;
        if (_rp.Kind == RackKind.AudioEffect)
        {
            if (a.RackMode() == RackKindProfile.Select) { a.SelectRange(c, out int lo, out int hi); return $"sel {lo}–{hi}"; }
            int n = a.ChainDeviceCount(c);
            return n == 1 ? "1 device" : $"{n} devices";
        }
        a.ChainZone(c, out int kl, out int kh, out int vl, out int vh);
        string s = $"{NoteName(kl)}–{NoteName(kh)}";
        if (vl > 0 || vh < 127) s += $" · v{vl}–{vh}";
        return s;
    }

    private void RcChainMenu(Control anchor, int c)
    {
        var a = _ra;
        var f = new MenuFlyout();
        f.Items.Add(MenuKit.Header(RcChainName(c), null, $"Chain · {c + 1}"));
        f.Items.Add(new Separator());
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => RcPromptRename(anchor, RcChainName(c), n => a.SetChainName(c, n));
        f.Items.Add(rename);
        bool full = _rp.Kind == RackKind.MidiEffect && a.ChainCount() >= RcMidiChainLimit;
        var dup = new MenuItem { Header = "Duplicate", IsEnabled = !full };
        dup.Click += (_, _) => { int d = a.DuplicateChain(c); if (d >= 0) Sel = d; DeferRebuild(); };
        f.Items.Add(dup);
        if (_ra.RackAddress is not null) { f.Items.Add(new Separator()); f.Items.Add(RcPasteItem(c)); }
        if (a.ChainCount() > 1)
        {
            f.Items.Add(new Separator());
            f.Items.Add(MenuKit.Danger("Delete chain", GlyphKind.Trash, () => RcDeleteChain(c)));
        }
        f.ShowAt(anchor, showAtPointer: true);
    }

    private void RcDeleteChain(int c)
    {
        if (_ra.ChainCount() <= 1) return; // a rack keeps one chain
        _ra.RemoveChain(c);
        Sel = Math.Max(0, c - 1);
        DeferRebuild();
    }

    // ---- drops ------------------------------------------------------------------------

    private static bool RcIsDevice(BrowserItem it) => it.Kind is BrowserItemKind.BuiltinInstrument or BrowserItemKind.PluginInstrument
        or BrowserItemKind.BuiltinEffect or BrowserItemKind.PluginEffect or BrowserItemKind.BuiltinMidiEffect or BrowserItemKind.Preset;

    // ---- presets dropped from the Browser -----------------------------------------------
    // A factory preset, a preset file (user or Mosaic pack) or a drum kit: it brings the device
    // it is for, which lands like that device, and then the preset is applied to it in place.

    private enum RcPresetFor { None, Instrument, Effect, Midi, Plugin }
    private readonly record struct RcPreset(RcPresetFor For, int Kind, string FactoryId = "", string File = "", string KitId = "");

    private RcPreset RcResolvePreset(BrowserItem it)
    {
        if (it.Kind != BrowserItemKind.Preset) return default;
        string path = it.Path;
        if (path.StartsWith("kit:", StringComparison.Ordinal)) return new(RcPresetFor.Instrument, 4, KitId: path["kit:".Length..]);
        if (path.StartsWith("rhythmkit:", StringComparison.Ordinal)) return default; // a Nota Rhythm kit fills a Rhythm track
        if (path.StartsWith("factory:", StringComparison.Ordinal))
        {
            string id = path["factory:".Length..];
            if (_ctx.Factory is { } f)
                foreach (var fp in f.All())
                    if (fp.Id == id)
                        return new(fp.IsMidiEffect ? RcPresetFor.Midi : fp.IsInstrument ? RcPresetFor.Instrument : RcPresetFor.Effect, fp.BuiltinKind, FactoryId: id);
            return default;
        }
        if (App.Services?.GetService(typeof(IPresetStore)) is not IPresetStore store || store.Describe(path) is not { } d) return default;
        var to = d.Type switch
        {
            "builtin-instrument" => RcPresetFor.Instrument,
            "builtin-effect" => RcPresetFor.Effect,
            "builtin-midi-effect" => RcPresetFor.Midi,
            "plugin-instrument" or "plugin-effect" => RcPresetFor.Plugin,
            _ => RcPresetFor.None,
        };
        return new(to, d.BuiltinKind, File: path);
    }

    // Apply a resolved preset to the device it brought: the chain's instrument (device −1), or
    // the effect / MIDI effect at `device` in chain c.
    private void RcApplyPreset(RcPreset pr, int c, int device)
    {
        IAudioEngine target = pr.For switch
        {
            RcPresetFor.Instrument => pr.KitId.Length > 0 ? NestedRackEngineProxy.Create(E, c) : ChainInstrumentProxy(c),
            RcPresetFor.Effect => ChainDeviceProxy(_ra, c, device, pr.Kind),
            _ => RackChainMidiEngineProxy.Create(E, _ra, c),
        };
        string warn;
        if (pr.KitId.Length > 0)
        {
            warn = "";
            if (App.Services?.GetService(typeof(IDrumKits)) is IDrumKits kits) kits.LoadInto(target, T, pr.KitId, out warn);
        }
        else if (pr.FactoryId.Length > 0) warn = _ctx.Factory?.ApplyInPlace(target, pr.FactoryId, T, device) ?? "";
        else warn = (App.Services?.GetService(typeof(IPresetStore)) as IPresetStore)?.ApplyFileInPlace(target, pr.File, T, device) ?? "";
        if (warn.Length > 0) _ctx.Status(warn);
    }

    private static bool RcIsInstrument(BrowserItem it) => it.Kind is BrowserItemKind.BuiltinInstrument or BrowserItemKind.PluginInstrument;

    // A rack takes only its own kinds; a refused drop says why in the status bar.
    private bool RcAccepts(BrowserItem it, out string why)
    {
        why = "";
        if (it.Kind == BrowserItemKind.Preset)
        {
            var pr = RcResolvePreset(it);
            switch (pr.For)
            {
                case RcPresetFor.Plugin: why = "Plug-in presets can't go in a rack chain yet."; return false;
                case RcPresetFor.None: why = "This preset can't go in a rack."; return false;
                case RcPresetFor.Instrument when !_rp.TakesInstrument:
                    why = _rp.Kind == RackKind.MidiEffect ? "A MIDI Effect Rack takes MIDI effects only." : "An Audio Effect Rack takes audio effects only."; return false;
                case RcPresetFor.Effect when !_rp.TakesAudioEffects: why = "A MIDI Effect Rack takes MIDI effects only."; return false;
                case RcPresetFor.Effect when pr.Kind == 5: why = "A rack can't go inside a rack."; return false;
                case RcPresetFor.Midi when !_rp.TakesMidiEffects: why = "An Audio Effect Rack takes audio effects only."; return false;
                case RcPresetFor.Midi when pr.Kind is < 0 or > 5: why = "A rack can't go inside a rack."; return false;
            }
            return true;
        }
        if (RcIsInstrument(it))
        {
            // A Drum Rack may sit in an Instrument Rack chain; an Instrument Rack can't (specs/041).
            if (_rp.TakesInstrument && it.Kind == BrowserItemKind.BuiltinInstrument && it.BuiltinKind == 3) { why = "An Instrument Rack can't go inside a rack — add its instruments as chains."; return false; }
            if (_rp.TakesInstrument) return true;
            why = _rp.Kind == RackKind.MidiEffect ? "A MIDI Effect Rack takes MIDI effects only." : "An Audio Effect Rack takes audio effects only.";
            return false;
        }
        if (IsEffect(it))
        {
            if (!_rp.TakesAudioEffects) { why = "A MIDI Effect Rack takes MIDI effects only."; return false; }
            if (it.Kind == BrowserItemKind.PluginEffect && !_ra.HostsPlugins) { why = "This rack can't host plug-ins."; return false; }
            if (it.Kind == BrowserItemKind.BuiltinEffect && it.BuiltinKind == 5) { why = "A rack can't go inside a rack."; return false; }
            return true;
        }
        if (it.Kind == BrowserItemKind.BuiltinMidiEffect)
        {
            if (!_rp.TakesMidiEffects) { why = "An Audio Effect Rack takes audio effects only."; return false; }
            if (it.BuiltinKind is < 0 or > 5) { why = "A rack can't go inside a rack."; return false; }
            return true;
        }
        why = "Drop a device here.";
        return false;
    }

    private void RcDropOnChain(int c, BrowserItem it)
    {
        if (!RcAccepts(it, out var why)) { _ctx.Status(why); return; }
        var a = _ra;
        if (it.Kind == BrowserItemKind.Preset)
        {
            var pr = RcResolvePreset(it);
            E.BeginUndoGroup(); // the device and its preset are one step
            try
            {
                switch (pr.For)
                {
                    case RcPresetFor.Instrument:
                        // On a chain an instrument preset replaces the chain's instrument.
                        if (!a.SetChainInstrument(c, pr.Kind)) { _ctx.Status($"Couldn't load {it.Name}."); return; }
                        RcApplyPreset(pr, c, -1);
                        break;
                    case RcPresetFor.Effect:
                    {
                        int d = a.AddChainDevice(c, pr.Kind);
                        if (d >= 0) RcApplyPreset(pr, c, d);
                        break;
                    }
                    case RcPresetFor.Midi:
                    {
                        int j = a.AddChainMidi(c, pr.Kind);
                        if (j >= 0) RcApplyPreset(pr, c, j);
                        break;
                    }
                }
            }
            finally { E.EndUndoGroup(); }
            Sel = c;
            DeferRebuild();
            return;
        }
        if (RcIsInstrument(it))
        {
            // On a chain an instrument replaces the chain's own; a plug-in starts a new layer.
            if (it.Kind == BrowserItemKind.BuiltinInstrument && a.SetChainInstrument(c, it.BuiltinKind)) { DeferRebuild(); return; }
            RcDropAsNewChain(it);
            return;
        }
        if (it.Kind == BrowserItemKind.BuiltinMidiEffect) { a.AddChainMidi(c, it.BuiltinKind); DeferRebuild(); return; }
        DropEffectOnChain(a, c, it);
    }

    /// <summary>A Browser item dropped on the rack from outside the card (a Session column,
    /// specs/043): the same as a drop on the card's empty chain area.</summary>
    internal void DropAsNewChain(BrowserItem it) => RcDropAsNewChain(it);

    private void RcDropAsNewChain(BrowserItem it)
    {
        if (!RcAccepts(it, out var why)) { _ctx.Status(why); return; }
        var a = _ra;
        if (_rp.Kind == RackKind.MidiEffect && a.ChainCount() >= RcMidiChainLimit) { _ctx.Status($"A MIDI Effect Rack holds up to {RcMidiChainLimit} chains."); return; }
        int c = -1;
        if (it.Kind == BrowserItemKind.Preset && RcResolvePreset(it) is { For: RcPresetFor.Instrument } pr)
        {
            E.BeginUndoGroup(); // the chain and its preset are one step
            try
            {
                c = a.AddChain(pr.Kind);
                if (c >= 0 && a.ChainInstrumentKind(c) == pr.Kind) RcApplyPreset(pr, c, -1);
                else if (c >= 0) { a.RemoveChain(c); c = -1; _ctx.Status($"Couldn't load {it.Name}."); }
            }
            finally { E.EndUndoGroup(); }
        }
        else if (it.Kind == BrowserItemKind.BuiltinInstrument)
        {
            // An instrument the rack can't host would leave an empty chain: take it back.
            c = a.AddChain(it.BuiltinKind);
            if (c >= 0 && a.ChainInstrumentKind(c) != it.BuiltinKind) { a.RemoveChain(c); c = -1; _ctx.Status($"{it.Name} can't go in a rack chain."); }
        }
        else if (it.Kind == BrowserItemKind.PluginInstrument) c = a.AddPluginInstrumentChain(it.CatalogIndex);
        else
        {
            E.BeginUndoGroup(); // the new chain and what lands on it are one step
            try
            {
                c = a.AddChain(-1);
                if (c >= 0) RcDropOnChain(c, it);
            }
            finally { E.EndUndoGroup(); }
        }
        if (c >= 0) Sel = c;
        DeferRebuild();
    }

    // ---- device lane: the chosen chain, left to right ----------------------------------

    private Control RcDeviceLane()
    {
        var a = _ra;
        if (a.ChainCount() == 0) return new Border { Padding = new Thickness(4), Child = Hint("Add a chain to start.") };
        int c = Sel;
        Control head;
        if (_rs.Mapping)
        {
            var label = RcText("Mapping", NotaPalette.Teal, NotaType.KnobLabel, FontWeight.SemiBold);
            var macro = RcName($"Macro {_rs.MappingMacro + 1} · {a.MacroName(_rs.MappingMacro)}", NotaPalette.TextPrimary, NotaType.KnobLabel);
            var how = RcText("Click to add or remove", NotaPalette.TextSecondary, 7.5, FontWeight.Normal);
            head = new Border
            {
                Height = 16, CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 0), BorderThickness = new Thickness(1),
                Background = NotaPalette.Wash(NotaPalette.Teal, 0x24), BorderBrush = NotaPalette.Teal,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, macro, how } },
            };
        }
        else
        {
            int n = (_rp.TakesInstrument && a.ChainInstrumentKind(c) > -2 ? 1 : 0) + a.ChainDeviceCount(c) + a.ChainMidiCount(c);
            var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Badge, Background = ChainHue(c), VerticalAlignment = VerticalAlignment.Center };
            string flow = _rp.Kind == RackKind.MidiEffect ? "notes" : "signal";
            head = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Height = 16, Children =
                {
                    dot, RcName(RcChainName(c), NotaPalette.TextPrimary, NotaType.RowLabel),
                    RcMono($"{n} device{(n == 1 ? "" : "s")} · {flow} left → right", NotaPalette.TextTertiary, 7.5),
                },
            };
        }

        // The horizontal scrollbar floats over the bottom of the lane (16 px when it expands): keep the
        // cards above it so it never covers their Open row.
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(0, 0, 0, 18) };
        _rcStrip = strip;
        RcCheckSelection();
        void Chev() => strip.Children.Add(new Glyph(GlyphKind.ChevronRight, 8) { Foreground = NotaPalette.TextDisabled, VerticalAlignment = VerticalAlignment.Center });
        bool first = true;
        void Add(Control card) { if (!first) Chev(); strip.Children.Add(card); first = false; }

        if (_rp.TakesMidiEffects)
        {
            for (int j = 0; j < a.ChainMidiCount(c); j++) Add(RcCompactCard(c, RcSlot.Midi, j));
            if (_rp.TakesInstrument) { if (!first) Chev(); strip.Children.Add(RcDropTarget(c, RcSlot.Midi)); first = false; }
        }
        if (_rp.TakesInstrument)
            Add(a.ChainInstrumentKind(c) > -2 ? RcCompactCard(c, RcSlot.Instrument, 0) : RcNoInstrument(c));
        if (_rp.TakesAudioEffects)
            for (int d = 0; d < a.ChainDeviceCount(c); d++) Add(RcCompactCard(c, RcSlot.Effect, d));
        if (!first) Chev();
        strip.Children.Add(RcDropTarget(c, _rp.TakesAudioEffects ? RcSlot.Effect : RcSlot.Midi));

        var scroll = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(head, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { head, scroll } };
    }

    // A chain without an instrument: a slot that picks one.
    private Control RcNoInstrument(int c)
    {
        var tb = Hint("No instrument");
        var slot = new Border
        {
            Width = RcCompactWidth, CornerRadius = NotaRadius.Panel, BorderThickness = new Thickness(1), BorderBrush = NotaPalette.BorderDefault,
            Background = NotaPalette.SurfaceRaised, Cursor = new Cursor(StandardCursorType.Hand), Child = new Border { Padding = new Thickness(6), Child = tb },
        };
        ToolTip.SetTip(slot, "Pick an instrument — or drop one from the Browser");
        slot.PointerPressed += (_, e) => { if (e.GetCurrentPoint(slot).Properties.IsLeftButtonPressed) { e.Handled = true; RcInstrumentMenu(slot, c); } };
        return slot;
    }

    private void RcInstrumentMenu(Control anchor, int c)
    {
        var f = new MenuFlyout();
        void Add(string h, int k) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => { _ra.SetChainInstrument(c, k); DeferRebuild(); }; f.Items.Add(mi); }
        Add("Nota Volt", 6); Add("Nota Physical", 2); Add("Nota Aurora", 5); Add("Nota Pendulum", 8); Add("Nota Operator", 9); Add("Nota Grain", 10);
        Add("Nota Flux", 11); Add("Nota Monolith", 13); Add("Nota Pentad", 14); Add("Nota Consort", 15); Add("Nota Keys", 16); Add("Nota Twin", 18);
        Add("Nota Mosaic", 17);
        // A Drum Rack in a chain: racks nest one level (specs/041).
        f.Items.Add(new Separator()); Add("Nota Drum Rack", 4);
        f.ShowAt(anchor, showAtPointer: true);
    }

    // The dashed target at the end of a section: click for the add menu, or drop a device.
    private Control RcDropTarget(int c, RcSlot slot)
    {
        var plus = new Glyph(GlyphKind.Plus, 10) { Foreground = NotaPalette.TextTertiary, HorizontalAlignment = HorizontalAlignment.Center };
        var word = new TextBlock { Text = slot == RcSlot.Midi ? "MIDI" : "FX", FontSize = 7, Foreground = NotaPalette.TextTertiary, HorizontalAlignment = HorizontalAlignment.Center };
        var dash = new Rectangle
        {
            Stroke = NotaPalette.BorderDefault, StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double> { 3, 2.5 },
            RadiusX = NotaRadius.ControlValue, RadiusY = NotaRadius.ControlValue,
        };
        var box = new Panel
        {
            Width = 30, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
            Children = { dash, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { plus, word } } },
        };
        ToolTip.SetTip(box, slot == RcSlot.Midi ? "Add a MIDI effect — or drop one from the Browser" : "Add an effect — or drop one from the Browser");
        box.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (slot == RcSlot.Midi) RcAddMidiMenu(box, c); else ShowAddDeviceMenu(_ra, c, box, withPaste: _ra.RackAddress is not null);
        };
        DragDrop.SetAllowDrop(box, true);
        DragDrop.AddDragOverHandler(box, (_, e) =>
        {
            if (!BrowserView.IsBrowserDrag || BrowserView.CurrentDrag is not { } it || !RcIsDevice(it)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy; e.Handled = true; _ctx.HideDropGlow();
            dash.Stroke = NotaPalette.Teal; dash.Fill = NotaPalette.Wash(NotaPalette.Teal, 0x22);
        });
        DragDrop.AddDragLeaveHandler(box, (_, _) => { dash.Stroke = NotaPalette.BorderDefault; dash.Fill = null; });
        DragDrop.AddDropHandler(box, (_, e) =>
        {
            dash.Stroke = NotaPalette.BorderDefault; dash.Fill = null;
            if (BrowserView.CurrentDrag is not { } it) return;
            e.Handled = true;
            RcDropOnChain(c, it);
        });
        return box;
    }

    private static readonly (string Name, int Kind)[] RcMidiKinds =
        [("Nota Arp", 0), ("Nota Chord", 1), ("Nota Scale", 2), ("Nota Length", 3), ("Nota Velocity", 4), ("Nota Random", 5)];

    private void RcAddMidiMenu(Control anchor, int c)
    {
        var f = new MenuFlyout();
        if (_ra.RackAddress is not null && E.DeviceClipboardKind() is 2 or 4 && RcCanPaste(c)) { f.Items.Add(RcPasteItem(c)); f.Items.Add(new Separator()); }
        foreach (var (name, kind) in RcMidiKinds)
        {
            var mi = new MenuItem { Header = name };
            mi.Click += (_, _) => { _ra.AddChainMidi(c, kind); DeferRebuild(); };
            f.Items.Add(mi);
        }
        f.ShowAt(anchor, showAtPointer: true);
    }

    // ---- compact device card ----------------------------------------------------------

    private Control RcCompactCard(int c, RcSlot slot, int idx)
    {
        var a = _ra;
        string name; string tag; bool bypassed; int kind; RackDeviceFamily family;
        switch (slot)
        {
            case RcSlot.Instrument:
                name = a.ChainInstrumentName(c); kind = a.ChainInstrumentKind(c); tag = kind == -1 ? "Plug" : "Inst";
                bypassed = false; family = RackDeviceFamily.Instrument;
                break;
            case RcSlot.Midi:
                name = a.ChainMidiName(c, idx); kind = a.ChainMidiKind(c, idx); tag = "MIDI";
                bypassed = a.ChainMidiBypassed(c, idx); family = RackDeviceFamily.MidiEffect;
                break;
            default:
                name = a.ChainDeviceName(c, idx); kind = a.ChainDeviceBuiltinKind(c, idx); tag = kind < 0 ? "Plug" : "FX";
                bypassed = a.ChainDeviceBypassed(c, idx); family = RackDeviceFamily.AudioEffect;
                break;
        }
        if (string.IsNullOrEmpty(name)) name = slot == RcSlot.Instrument ? "Instrument" : "Device";
        bool canBypass = slot != RcSlot.Instrument;

        var dot = new Border
        {
            Width = 6, Height = 6, CornerRadius = NotaRadius.Control, VerticalAlignment = VerticalAlignment.Center,
            Background = bypassed ? NotaPalette.TextDisabled : NotaPalette.Success, Cursor = canBypass ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
        };
        if (canBypass)
        {
            ToolTip.SetTip(dot, bypassed ? "Turn on" : "Turn off");
            dot.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(dot).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                if (slot == RcSlot.Midi) a.SetChainMidiBypassed(c, idx, !bypassed); else a.SetChainDeviceBypassed(c, idx, !bypassed);
                DeferRebuild();
            };
        }
        // An effect's card also holds the Remove control: like a compact track card it drops the
        // family prefix ("Nota Reverb" → "Reverb") so the name still reads.
        string label = slot != RcSlot.Instrument && name.StartsWith("Nota ", StringComparison.Ordinal) ? name[5..] : name;
        var nameTb = RcName(label, NotaPalette.TextPrimary, NotaType.KnobLabel);
        ToolTip.SetTip(nameTb, name);
        var tagTb = RcMono(tag, NotaPalette.TextTertiary, 7);
        dot.Margin = new Thickness(0, 0, 4, 0);
        tagTb.Margin = new Thickness(3, 0, 0, 0);
        var headRow = new DockPanel { LastChildFill = true };
        headRow.Children.Add(RcDock(dot, Dock.Left));
        // The Remove control (#110), as on the track chain's cards; a chain's instrument is
        // replaced from its menu, not removed.
        if (slot != RcSlot.Instrument)
        {
            var remove = DeviceCardKit.RemoveButton("Remove device", () =>
            {
                if (RcSelOf(c, slot, idx) is { } sel) { RcDelete(sel); return; }
                if (slot == RcSlot.Midi) a.RemoveChainMidi(c, idx); else a.RemoveChainDevice(c, idx);
                DeferRebuild();
            });
            remove.Margin = new Thickness(2, 0, -4, 0);
            headRow.Children.Add(RcDock(remove, Dock.Right));
        }
        headRow.Children.Add(RcDock(tagTb, Dock.Right));
        headRow.Children.Add(nameTb);
        var head = new Border
        {
            Height = 18, Padding = new Thickness(5, 0), BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = headRow,
        };

        // Parameters: the key ones at rest, every one while mapping (so any can be mapped).
        var P = RcParamsOf(c, slot, idx);
        int target = RcTarget(slot, idx);
        IEnumerable<int> shown = _rs.Mapping
            ? Enumerable.Range(0, P.Count)
            : RackKeyParams.Resolve(family, kind, P.Count, P.Name);
        var rows = new StackPanel { Spacing = 2 };
        foreach (int p in shown) rows.Children.Add(RcParamRow(c, target, P, p));
        Control body = _rs.Mapping
            ? new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
            : rows;
        body.Margin = new Thickness(4, 3);

        var open = new TextBlock { Text = "Open", FontSize = 7.5, Foreground = NotaPalette.TextSecondary, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(open, "Open the full editor");
        var grip = new Glyph(GlyphKind.Grip, 8) { Foreground = NotaPalette.TextDisabled, Cursor = new Cursor(StandardCursorType.SizeWestEast), VerticalAlignment = VerticalAlignment.Center };
        var foot = new Border
        {
            Height = 15, Padding = new Thickness(5, 0), BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new DockPanel { LastChildFill = false, Children = { RcDock(open, Dock.Left), RcDock(grip, Dock.Right) } },
        };

        var card = new Border
        {
            Width = RcCompactWidth, CornerRadius = NotaRadius.Panel, BorderThickness = new Thickness(1), ClipToBounds = true,
            Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.BorderDefault,
        };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        card.Child = new DockPanel { LastChildFill = true, Children = { head, foot, body } };
        if (bypassed) card.AttachedToVisualTree += (_, _) => Inactive.Set(card, true, interactive: true);

        void Open()
        {
            switch (slot)
            {
                case RcSlot.Instrument: OpenChainInstrumentGui(a, c, card); break;
                case RcSlot.Midi: RcOpenChainMidi(c, idx, card); break;
                default: OpenChainDeviceGui(a, c, idx, card); break;
            }
        }
        open.PointerPressed += (_, e) => { if (e.GetCurrentPoint(open).Properties.IsLeftButtonPressed) { e.Handled = true; Open(); } };
        head.PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(head).Properties;
            if (pt.IsRightButtonPressed) { e.Handled = true; RcCardMenu(card, c, slot, idx, bypassed, Open); }
            else if (pt.IsLeftButtonPressed && e.ClickCount == 2) { e.Handled = true; Open(); }
        };
        if (slot == RcSlot.Instrument) { grip.IsVisible = false; return card; }
        // Selected: the track chain's focus ring, outside the card with a small gap.
        var ring = new Border
        {
            BorderBrush = NotaPalette.Accent, BorderThickness = new Thickness(1.5), Margin = new Thickness(-2),
            CornerRadius = new CornerRadius(NotaRadius.PanelValue + 2), IsHitTestVisible = false, IsVisible = RcIsSelected(c, slot, idx),
        };
        var host = new Panel { Children = { card, ring }, Margin = new Thickness(2, 2, 2, 0) };
        RcHookCardDrag(head, host, c, slot, idx);
        RcHookCardDrag(grip, host, c, slot, idx);
        _rcCards.Add((ring, (RcCardTag)host.Tag!));
        return host;
    }

    private void RcCardMenu(Control anchor, int c, RcSlot slot, int idx, bool bypassed, Action open)
    {
        var a = _ra;
        var f = new MenuFlyout();
        var o = new MenuItem { Header = "Open" };
        o.Click += (_, _) => open();
        f.Items.Add(o);
        if (slot == RcSlot.Instrument)
        {
            var replace = new MenuItem { Header = "Replace instrument" };
            replace.Click += (_, _) => RcInstrumentMenu(anchor, c);
            f.Items.Add(replace);
        }
        else
        {
            var b = new MenuItem { Header = bypassed ? "Turn on" : "Turn off" };
            b.Click += (_, _) =>
            {
                if (slot == RcSlot.Midi) a.SetChainMidiBypassed(c, idx, !bypassed); else a.SetChainDeviceBypassed(c, idx, !bypassed);
                DeferRebuild();
            };
            f.Items.Add(b);
            if (_ra.RackAddress is not null) { f.Items.Add(new Separator()); RcAddDeviceMenuItems(f, c, slot, idx); }
            else
            {
                f.Items.Add(new Separator());
                f.Items.Add(MenuKit.Danger("Delete", GlyphKind.Trash, () =>
                {
                    if (slot == RcSlot.Midi) a.RemoveChainMidi(c, idx); else a.RemoveChainDevice(c, idx);
                    DeferRebuild();
                }, MenuKit.DeleteKey));
            }
        }
        f.ShowAt(anchor, showAtPointer: true);
    }

    // ---- a parameter row in a compact card --------------------------------------------

    private Control RcParamRow(int c, int target, RcParams P, int p)
    {
        var a = _ra;
        string name = P.Name(p);
        float min = P.Min(p), max = P.Max(p);
        float span = Math.Max(1e-6f, max - min);
        int macro = MappedMacro(a, c, target, p);
        bool mapped = macro >= 0, mapping = _rs.Mapping, onThis = mapping && macro == _rs.MappingMacro;

        var label = new TextBlock
        {
            Text = NotaText.Label(name), FontSize = 7.5, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = mapped ? NotaPalette.Teal : NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        var value = RcMono(P.Text(P.Get(p)), mapped ? NotaPalette.TealBright : NotaPalette.TextPrimary, 7.5);
        var top = new DockPanel { LastChildFill = true };
        top.Children.Add(RcDock(value, Dock.Right));
        if (mapped)
        {
            var tag = new Border
            {
                CornerRadius = NotaRadius.Badge, Padding = new Thickness(2, 0), Margin = new Thickness(3, 0, 0, 0), Background = NotaPalette.Wash(NotaPalette.Teal, 0x33),
                VerticalAlignment = VerticalAlignment.Center, Child = RcMono($"M{macro + 1}", NotaPalette.TealBright, 7),
            };
            top.Children.Add(RcDock(tag, Dock.Right));
        }
        top.Children.Add(label);

        var track = new SliderTrack { Norm = (P.Get(p) - min) / span };
        if (mapped) track.Ink = NotaPalette.Teal;
        if (mapped || mapping) track.IsHitTestVisible = false; // a mapped parameter follows its macro
        track.Changed += v => { P.Set(p, (float)(min + v * span)); value.Text = P.Text(P.Get(p)); };
        _ctx.AddRackParamRefresher(() =>
        {
            if (track.Dragging) return;
            float nv = P.Get(p);
            double n = (nv - min) / span;
            if (Math.Abs(n - track.Norm) > 1e-4) track.Norm = n;
            value.Text = P.Text(nv);
        });

        var row = new Border
        {
            CornerRadius = NotaRadius.Badge, Padding = new Thickness(2, 1), BorderThickness = new Thickness(1),
            BorderBrush = mapping ? NotaPalette.Teal : Brushes.Transparent,
            Background = onThis ? NotaPalette.Wash(NotaPalette.Teal, 0x30) : Brushes.Transparent,
            Child = new StackPanel { Spacing = 2, Children = { top, track } },
        };
        if (mapping)
        {
            row.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(row, onThis ? $"Unmap from {a.MacroName(_rs.MappingMacro)}" : $"Map to {a.MacroName(_rs.MappingMacro)}");
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                RcToggleMapping(c, target, p, P);
            };
        }
        else if (mapped)
        {
            row.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(row, $"Controlled by {a.MacroName(macro)} · click to open the macro");
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                _rs.SelectedMacro = macro;
                _rs.Centre = RackCentreTab.Macros;
                DeferRebuild();
            };
        }
        return row;
    }

    private void RcToggleMapping(int c, int target, int p, RcParams P)
    {
        var a = _ra;
        int m = _rs.MappingMacro;
        if (m < 0) return;
        if (MappedMacro(a, c, target, p) == m) { RemoveParamMapping(a, c, target, p); DeferRebuild(); return; }
        float min = P.Min(p), max = P.Max(p), span = max - min, cur = P.Get(p);
        RcAddMapping(m, c, target, p, Math.Max(min, cur - 0.3f * span), Math.Min(max, cur + 0.3f * span));
        DeferRebuild();
    }

    /// <summary>Map a target to a macro as one undo step: a new target takes the macro's curve (a
    /// Mixed macro gives it Lin); one moved from another macro keeps its own range and curve.</summary>
    private void RcAddMapping(int macro, int c, int target, int p, float min, float max)
    {
        var a = _ra;
        int curve = RcMacroCurve(macro);
        bool was = MappedMacro(a, c, target, p) >= 0;
        E.BeginUndoGroup();
        try
        {
            int i = a.AddMacroMapping(macro, c, target, p, min, max);
            if (i >= 0 && !was && curve > 0) a.SetMappingCurve(i, curve);
        }
        finally { E.EndUndoGroup(); }
    }

    /// <summary>A macro's curve: its targets' common curve, −1 when they differ (Mixed), 0 with none.</summary>
    private int RcMacroCurve(int macro)
    {
        int curve = int.MinValue;
        for (int i = 0; i < _ra.MappingCount(); i++)
        {
            if (!_ra.TryGetMapping(i, out var m) || m.Macro != macro) continue;
            int k = _ra.MappingCurve(i);
            if (curve == int.MinValue) curve = k;
            else if (curve != k) return -1;
        }
        return curve == int.MinValue ? 0 : curve;
    }

    private static Control RcDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }

    // A chain's MIDI effect in the device window: its parameters as rows.
    private void RcOpenChainMidi(int c, int j, Control anchor)
    {
        var a = _ra;
        var P = RcParamsOf(c, RcSlot.Midi, j);
        Control Row(int p)
        {
            float min = P.Min(p), max = P.Max(p), span = Math.Max(1e-6f, max - min);
            var row = SliderRow(P.Name(p), () => (P.Get(p) - min) / span, v => { P.Set(p, (float)(min + v * span)); _ctx.InvokeRackParamRefreshers(); },
                () => P.Text(P.Get(p)), out var sync, labelWidth: 70);
            _ctx.AddRackParamRefresher(sync);
            return row;
        }
        OpenParamWindow(anchor, a.ChainMidiName(c, j), P.Count, Row, () => $"{RcChainName(c)} · MIDI effect {j + 1}");
    }
}
