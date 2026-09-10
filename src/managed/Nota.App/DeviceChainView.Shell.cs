// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the ONE shared card shell (mockup 2f header) used by every
// built-in instrument and effect editor, so the chain reads as one family. It draws
// the 26px chrome bar over a body host: power dot (bypass / status), name, subtitle,
// preset picker, A/B compare (full param snapshots), a small stereo meter with a
// pinned dB readout, per-instrument voice count, reorder ◀▶, remove ✕ and a drag
// handle — each shown only where it applies. Card bodies (IInstrumentCard / IDeviceBody)
// return content only; this wraps them.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    private static readonly IBrush ShellBg = new SolidColorBrush(Color.Parse("#171613"));
    private static readonly IBrush ShellHdr = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush ShellInset = new SolidColorBrush(Color.Parse("#100F0D"));

    // The three device chains the one shell serves — each has its own param / bypass /
    // reorder / preset API, branched on below.
    internal enum ChainKind { Instrument, Effect, Midi }

    // A/B snapshots + last-applied preset per card, keyed so a MIDI effect and an audio
    // effect at the same index don't collide. Survives Rebuild(); cleared on track switch.
    private sealed class CardExtra { public float[]? A, B; public int Active; public string Preset = ""; }
    private readonly System.Collections.Generic.Dictionary<int, CardExtra> _cardExtra = new();
    private static int ExtraKey(ChainKind k, int di) => k switch { ChainKind.Instrument => -1, ChainKind.Midi => -1000 - di, _ => di };
    private CardExtra Extra(int key)
    {
        if (!_cardExtra.TryGetValue(key, out var e)) { e = new CardExtra(); _cardExtra[key] = e; }
        return e;
    }

    private int ParamCount(ChainKind k, int di) => k switch
    { ChainKind.Instrument => _engine.PluginParamCount(_trackId, -1), ChainKind.Midi => _engine.MidiEffectParamCount(_trackId, di), _ => _engine.DeviceParamCount(_trackId, di) };
    private float GetP(ChainKind k, int di, int i) => k switch
    { ChainKind.Instrument => _engine.PluginParamGet(_trackId, -1, i), ChainKind.Midi => _engine.MidiEffectGetParam(_trackId, di, i), _ => _engine.DeviceGetParam(_trackId, di, i) };
    private void SetPr(ChainKind k, int di, int i, float v)
    { if (k == ChainKind.Instrument) _engine.PluginParamSet(_trackId, -1, i, v); else if (k == ChainKind.Midi) _engine.MidiEffectSetParam(_trackId, di, i, v); else _engine.DeviceSetParam(_trackId, di, i, v); }
    private float[] CaptureParams(ChainKind k, int di)
    { int n = ParamCount(k, di); var a = new float[n]; for (int i = 0; i < n; i++) a[i] = GetP(k, di, i); return a; }
    private void ApplyParams(ChainKind k, int di, float[] p)
    { int n = Math.Min(p.Length, ParamCount(k, di)); for (int i = 0; i < n; i++) SetPr(k, di, i, p[i]); }

    internal readonly record struct ShellSpec(
        string Name, string Subtitle, int DeviceIndex, int Count, bool Bypassed, bool Bypassable,
        bool CanMove, bool CanDelete, int PresetKind, bool IsInstrument, double Width, ChainKind Kind,
        Func<Nota.Application.IAudioEngine, int, int, string?>? VoiceLabel = null);

    // Small tertiary caption / mono readout helpers.
    private static TextBlock Caps(string t, double fs = 9) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Mono(string t, double w) { var tb = new TextBlock { Text = t, FontSize = 9, Foreground = TextSecondary, Width = w, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }

    private Border BuildCardShell(ShellSpec s, Control body)
    {
        int di = s.DeviceIndex;
        var extra = Extra(ExtraKey(s.Kind, di));
        // Rich chrome (preset picker · A/B · meter) needs room; narrow effect cards get
        // the essential chrome only. Instruments (≥700) and wide effects show everything.
        bool full = !double.IsNaN(s.Width) && s.Width >= 520;

        // ---- power dot: bypass toggle (effects) or a static status light (instruments) ----
        var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Fill = s.Bypassed ? Brushes.Transparent : Success, Stroke = s.Bypassed ? TextSecondary : null, StrokeThickness = 1 };
        var dotBtn = new Border { Child = dot, Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        if (s.Bypassable) { dotBtn.Cursor = new Cursor(StandardCursorType.Hand); ToolTip.SetTip(dotBtn, s.Bypassed ? "Bypassed — click to enable" : "Active — click to bypass"); dotBtn.PointerPressed += (_, _) => { if (s.Kind == ChainKind.Midi) _engine.SetMidiEffectBypassed(_trackId, di, !s.Bypassed); else _engine.SetDeviceBypassed(_trackId, di, !s.Bypassed); Rebuild(); Changed?.Invoke(); }; }

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(dotBtn);
        left.Children.Add(new TextBlock { Text = s.Name, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(s.Subtitle)) left.Children.Add(new TextBlock { Text = s.Subtitle, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        if (full) left.Children.Add(PresetPicker(s, extra));

        // ---- right group ----
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

        // Voice count (instruments that report it).
        if (s.IsInstrument && _engine.InstrumentVoiceCount(_trackId) >= 0)
        {
            var vt = Mono("0/16", 30); vt.Foreground = Teal; vt.TextAlignment = TextAlignment.Left;
            _deviceLiveRefreshers.Add(() => { int v = _engine.InstrumentVoiceCount(_trackId); vt.Text = s.VoiceLabel?.Invoke(_engine, _trackId, v) ?? (_engine.PluginParamGet(_trackId, -1, IndexOfId("mono")) > 0.5f ? "MONO" : $"{Math.Max(0, v)}/16"); });
            right.Children.Add(vt);
        }

        if (full) { right.Children.Add(ABControl(s, extra)); right.Children.Add(MeterBadge()); }

        void Move(int to) { if (s.Kind == ChainKind.Midi) _engine.MoveMidiEffect(_trackId, di, to); else _engine.MoveDevice(_trackId, di, to); Rebuild(); Changed?.Invoke(); }
        if (s.CanMove)
        {
            right.Children.Add(Glyph("◀", di > 0, () => Move(di - 1)));
            right.Children.Add(Glyph("▶", di < s.Count - 1, () => Move(di + 1)));
        }
        if (s.CanDelete) right.Children.Add(Glyph("✕", true, () => { if (s.Kind == ChainKind.Midi) _engine.RemoveMidiEffect(_trackId, di); else _engine.RemoveDevice(_trackId, di); Rebuild(); Changed?.Invoke(); }));
        var handle = new TextBlock
        {
            Text = "⠿", FontSize = 11, Foreground = TextDisabled, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, Padding = new Thickness(2, 0),
            Cursor = s.CanMove ? new Cursor(StandardCursorType.SizeAll) : null,
        };
        ToolTip.SetTip(handle, "Drag to reorder");
        right.Children.Add(handle);

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        headerGrid.Children.Add(left);
        Grid.SetColumn(right, 1); headerGrid.Children.Add(right);
        var headerBar = new Border { Height = 26, Background = ShellHdr, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = headerGrid };
        // Click selects the device (accent border + keyboard target); right-click opens the
        // context menu (copy/cut/paste/delete/save preset).
        headerBar.PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(headerBar).Properties;
            if (pt.IsRightButtonPressed) { ShowDeviceContextMenu(headerBar, s.Kind, di); return; }
            if (pt.IsLeftButtonPressed) SelectDevice(s.Kind, di);
        };

        DockPanel.SetDock(headerBar, Dock.Top);
        var bodyHost = new Border { Child = body };
        var root = new DockPanel { LastChildFill = true, Children = { headerBar, bodyHost } };
        bool sel = IsSelected(s.Kind, di);
        // Selection is drawn as an inset overlay ring (not by thickening the card's own edge
        // border): the edge border sits on the rounded-clip boundary under the square-cornered
        // header, where a 2px stroke reads as "cut after the curve". The overlay lives safely
        // inside the clip so its rounded corners render cleanly.
        var selRing = new Border
        {
            BorderBrush = AccentBright, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(7), Margin = new Thickness(0.5),
            IsHitTestVisible = false, IsVisible = sel,
        };
        var card = new Border
        {
            Width = s.Width, Height = CardH, Background = ShellBg, BorderBrush = BorderDef,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Opacity = s.Bypassed ? 0.7 : 1.0,
            Child = new Panel { Children = { root, selRing } },
        };
        // The ⠿ handle drags the card to a new slot within its domain (audio FX / MIDI FX).
        if (s.CanMove && SelectableKind(s.Kind)) HookDeviceDrag(handle, card, s.Kind, di);
        return card;
    }

    private int IndexOfId(string id)
    { int n = _engine.PluginParamCount(_trackId, -1); for (int i = 0; i < n; i++) if (_engine.PluginParamId(_trackId, -1, i) == id) return i; return -1; }

    // Preset box "Name ▾" → flyout of factory presets for this kind, applied in place.
    private Control PresetPicker(ShellSpec s, CardExtra extra)
    {
        var label = new TextBlock { Text = string.IsNullOrEmpty(extra.Preset) ? "Init" : extra.Preset, FontSize = 10, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 90 };
        var box = new Border
        {
            Height = 18, MinWidth = 96, Background = ShellInset, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new DockPanel { Children = { new TextBlock { Text = "▾", FontSize = 8, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Dock.Right }, label } },
        };
        var presets = _factory.All().Where(p => p.BuiltinKind == s.PresetKind
            && (s.Kind == ChainKind.Midi ? p.IsMidiEffect : (p.IsInstrument == s.IsInstrument && !p.IsMidiEffect))).ToList();
        box.PointerPressed += (_, _) =>
        {
            var flyout = new MenuFlyout();
            if (presets.Count == 0) flyout.Items.Add(new MenuItem { Header = "No presets", IsEnabled = false });
            foreach (var p in presets)
            {
                var mi = new MenuItem { Header = p.DisplayName };
                var info = p;
                mi.Click += (_, _) => { _factory.ApplyInPlace(_engine, info.Id, _trackId, s.DeviceIndex); Extra(ExtraKey(s.Kind, s.DeviceIndex)).Preset = info.DisplayName; Rebuild(); };
                flyout.Items.Add(mi);
            }
            flyout.ShowAt(box, showAtPointer: true);
        };
        return box;
    }

    // A | B compare of two full-param snapshots + copy-active-to-other.
    private Control ABControl(ShellSpec s, CardExtra extra)
    {
        int di = s.DeviceIndex; var kind = s.Kind;
        float[] Capture() => CaptureParams(kind, di);
        void Apply(float[] p) => ApplyParams(kind, di, p);
        if (extra.A == null) { extra.A = Capture(); extra.B = (float[])extra.A.Clone(); extra.Active = 0; }
        Border? bA = null, bB = null;
        void Hi() { if (bA == null || bB == null) return; bA.Background = extra.Active == 0 ? AccentSubtleB : Brushes.Transparent; ((TextBlock)bA.Child!).Foreground = extra.Active == 0 ? AccentBright : TextTertiary; bB.Background = extra.Active == 1 ? AccentSubtleB : Brushes.Transparent; ((TextBlock)bB.Child!).Foreground = extra.Active == 1 ? AccentBright : TextTertiary; }
        Border Slot(string t, int slot)
        {
            var b = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = t, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TextTertiary } };
            b.PointerPressed += (_, _) =>
            {
                if (slot == extra.Active) return;
                if (extra.Active == 0) extra.A = Capture(); else extra.B = Capture();   // save current into active
                extra.Active = slot;
                Apply(slot == 0 ? extra.A! : extra.B!);
                Rebuild();
            };
            return b;
        }
        bA = Slot("A", 0); bB = Slot("B", 1);
        var copy = new TextBlock { Text = "→", FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(copy, "Copy active slot to the other");
        copy.PointerPressed += (_, _) => { var cur = Capture(); if (extra.Active == 0) extra.B = (float[])cur.Clone(); else extra.A = (float[])cur.Clone(); };
        Hi();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { bA, bB, copy } };
        return new Border { Background = ShellInset, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2, 1), VerticalAlignment = VerticalAlignment.Center, Child = row };
    }

    // Small stereo meter + a pinned dB readout (fixed width so neighbours don't shift).
    // Fed from the track meter (per-device metering is deferred).
    private Control MeterBadge()
    {
        var meter = new StereoMeter { Width = 44, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        var db = Mono("−∞", 30);
        _deviceLiveRefreshers.Add(() =>
        {
            bool ok = _engine.TryGetTrackMeter(_trackId, out var mt);
            meter.Set(ok ? mt.PeakL : 0f, ok ? mt.PeakR : 0f);
            float peak = ok ? mt.Peak : 0f; double d = peak > 1e-4f ? 20 * Math.Log10(peak) : -80;
            db.Text = d <= -79 ? "−∞" : $"{d:0.0}";
        });
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { meter, db } };
    }
}
