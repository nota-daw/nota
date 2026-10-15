// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the ONE shared card shell (mockup 2f header) used by every
// built-in instrument and effect editor, so the chain reads as one family. It draws
// the 22px chrome bar over a body host. The header has fixed slots, left to right:
// name · preset picker · badges (type, live voice count, the card's own accessories) ·
// power (bypass). The power slot is reserved on every card, so right edges line up down a
// chain. A/B compare, move and delete live in the header's context menu. Card bodies (IInstrumentCard / IDeviceBody)
// return content only; this wraps them.

using System;
using System.Collections.Generic;
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
    private static readonly IBrush ShellBg = NotaPalette.SurfaceCard;   // card body: the panel tone
    private static readonly IBrush ShellHdr = NotaPalette.SurfaceRaised; // header: raised, with a bevel
    private static readonly IBrush ShellInset = NotaPalette.BgSunken;

    // The three device chains the one shell serves — each has its own param / bypass /
    // reorder / preset API, branched on below.
    internal enum ChainKind { Instrument, Effect, Midi }

    // A/B snapshots + last-applied preset per card, kept per track so switching tracks and
    // back restores them, and keyed so a MIDI effect and an audio effect at the same index
    // don't collide. Sig ties an entry to the device it was made for: when that slot now
    // holds a different device (swapped, removed behind our back, undo), the card starts fresh.
    private sealed class CardExtra { public string Sig = ""; public float[]? A, B; public int Active; public string Preset = "", PresetId = ""; public PluginCard.ViewState Plugin = new(); }
    private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, CardExtra>> _cardExtra = new();   // trackId → key → extra
    private static int ExtraKey(ChainKind k, int di) => k switch { ChainKind.Instrument => -1, ChainKind.Midi => -1000 - di, _ => di };
    private static ChainKind KeyKind(int key) => key >= 0 ? ChainKind.Effect : key == -1 ? ChainKind.Instrument : ChainKind.Midi;
    private static int KeyIndex(int key) => key >= 0 ? key : key == -1 ? -1 : -1000 - key;
    private string CardSig(int t, ChainKind k, int di) => k switch
    {
        ChainKind.Instrument => $"{_engine.TrackInstrumentKind(t)}|{_engine.DeviceName(t, -1)}",
        ChainKind.Midi => $"{_engine.MidiEffectKind(t, di)}|{_engine.MidiEffectName(t, di)}",
        _ => $"{_engine.TrackDeviceBuiltinKind(t, di)}|{_engine.DeviceName(t, di)}",
    };
    private System.Collections.Generic.Dictionary<int, CardExtra> TrackExtras(int t)
    {
        if (!_cardExtra.TryGetValue(t, out var d)) { d = new(); _cardExtra[t] = d; }
        return d;
    }
    private System.Collections.Generic.Dictionary<int, CardExtra> TrackExtras() => TrackExtras(_trackId);
    private CardExtra ExtraFor(int t, ChainKind k, int di)
    {
        var d = TrackExtras(t); int key = ExtraKey(k, di); string sig = CardSig(t, k, di);
        if (!d.TryGetValue(key, out var e) || e.Sig != sig) { e = new CardExtra { Sig = sig }; d[key] = e; }
        return e;
    }
    private CardExtra Extra(ChainKind k, int di) => ExtraFor(_trackId, k, di);

    // Keep each card's extra glued to its device when this view reorders / removes devices.
    // map: old index → new index, or -1 when the device is gone.
    private void RemapExtras(ChainKind k, Func<int, int> map)
    {
        if (k != ChainKind.Instrument) DevicesRemapped?.Invoke(_trackId, k == ChainKind.Midi, map);
        if (k == ChainKind.Instrument || !_cardExtra.TryGetValue(_trackId, out var d)) return;
        var domain = d.Where(kv => KeyKind(kv.Key) == k).ToList();
        foreach (var kv in domain) d.Remove(kv.Key);
        foreach (var kv in domain) { int ni = map(KeyIndex(kv.Key)); if (ni >= 0) d[ExtraKey(k, ni)] = kv.Value; }
    }
    private void ExtrasMoved(ChainKind k, int from, int to) => RemapExtras(k, i =>
        i == from ? to : from < to && i > from && i <= to ? i - 1 : to < from && i >= to && i < from ? i + 1 : i);
    private void ExtrasRemoved(ChainKind k, int di) => RemapExtras(k, i => i == di ? -1 : i > di ? i - 1 : i);

    private int ParamCount(ChainKind k, int di) => k switch
    { ChainKind.Instrument => _engine.PluginParamCount(_trackId, -1), ChainKind.Midi => _engine.MidiEffectParamCount(_trackId, di), _ => _engine.DeviceParamCount(_trackId, di) };
    private float GetP(ChainKind k, int di, int i) => k switch
    { ChainKind.Instrument => _engine.PluginParamGet(_trackId, -1, i), ChainKind.Midi => _engine.MidiEffectGetParam(_trackId, di, i), _ => _engine.DeviceGetParam(_trackId, di, i) };
    private void SetPr(ChainKind k, int di, int i, float v)
    { if (k == ChainKind.Instrument) _engine.PluginParamSet(_trackId, -1, i, v); else if (k == ChainKind.Midi) _engine.MidiEffectSetParam(_trackId, di, i, v); else _engine.DeviceSetParam(_trackId, di, i, v); }
    private float[] CaptureParams(ChainKind k, int di)
    { int n = ParamCount(k, di); var a = new float[n]; for (int i = 0; i < n; i++) a[i] = GetP(k, di, i); return a; }
    private void ApplyParams(ChainKind k, int di, float[] p)
    {
        int n = Math.Min(p.Length, ParamCount(k, di));
        int view = k == ChainKind.Instrument ? Nota.Application.InstrumentView.Index(_engine, _trackId) : -1;   // A/B keeps the card size
        // One undo step for the whole switch (specs/016).
        var targets = new List<MixTarget>(n);
        for (int i = 0; i < n; i++)
            if (i != view)
                targets.Add(k == ChainKind.Instrument ? MixTarget.PluginParam(_trackId, -1, i)
                    : k == ChainKind.Midi ? MixTarget.MidiDeviceParam(_trackId, di, i) : MixTarget.DeviceParam(_trackId, di, i));
        Nota.Application.Editing.MixEdit.Step(_engine, () => { for (int i = 0; i < n; i++) if (i != view) SetPr(k, di, i, p[i]); }, targets.ToArray());
    }

    internal readonly record struct ShellSpec(
        string Name, string Subtitle, int DeviceIndex, int Count, bool Bypassed, bool Bypassable,
        bool CanMove, bool CanDelete, int PresetKind, bool IsInstrument, double Width, ChainKind Kind,
        Func<Nota.Application.IAudioEngine, int, int, string?>? VoiceLabel = null,
        CardPresets? Presets = null, Control? HeaderExtra = null, bool Compact = false,   // Compact: a mini card — no type badge, a narrower preset picker
        bool Utility = false);   // Utility: not a device (Sends) — the same header, without the device menu, selection or presets

    /// <summary>A card's own preset list, in place of the factory presets for its kind — the
    /// Drum Rack's kits, which load pads rather than set parameters. <paramref name="Current"/>
    /// names the one the device holds now ("" when none fits); <paramref name="Apply"/> loads
    /// one by id and returns a warning, or "".</summary>
    internal sealed record CardPresets(
        IReadOnlyList<(string Id, string Name)> Items, Func<string> Current, Func<string, string> Apply);

    // Small tertiary caption / mono readout helpers.
    private static TextBlock Caps(string t, double fs = 9) => new() { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Mono(string t, double w) { var tb = new TextBlock { Text = t, FontSize = 9, Foreground = TextSecondary, Width = w, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }

    private Border BuildCardShell(ShellSpec s, Control body)
    {
        int di = s.DeviceIndex;
        var extra = s.Utility ? new CardExtra() : Extra(s.Kind, di);

        // ---- header (22), in fixed slots: name · preset · badges · power. The preset picker
        // sits in the centre of the header, between two equal halves: the name on the left
        // (trimmed if it runs into the picker), the badges right-aligned before the power slot
        // on the right; the power slot is reserved (empty) on a card that can't be bypassed. A/B, move and delete live in
        // the header's context menu (with the presets again); the whole header drags the card
        // to a new slot; Delete removes the selected card.
        var name = new TextBlock
        {
            // A compact card drops the family prefix ("Nota Arp" → "Arp") to leave room for the presets.
            Text = s.Compact && s.Name.StartsWith("Nota ", StringComparison.Ordinal) ? s.Name[5..] : s.Name, FontSize = NotaType.DeviceName, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = s.Compact ? 6 : 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

        // Type badge (mono, muted, sentence case). An instrument that reports its voices shows
        // them here, live, as the badge's value — e.g. "Synth · 3/16".
        string type = NotaText.Label(s.Subtitle ?? "");
        var badge = new TextBlock
        {
            Text = type, FontFamily = NotaFonts.MonoFamily, FontSize = NotaType.Eyebrow,
            Foreground = NotaPalette.TextMuted, VerticalAlignment = VerticalAlignment.Center,
        };
        if (s.IsInstrument && _engine.InstrumentVoiceCount(_trackId) >= 0)
        {
            _deviceLiveRefreshers.Add(() =>
            {
                int v = _engine.InstrumentVoiceCount(_trackId);
                string voices = s.VoiceLabel?.Invoke(_engine, _trackId, v)
                    ?? (_engine.PluginParamGet(_trackId, -1, IndexOfId("mono")) > 0.5f ? "Mono" : $"{Math.Max(0, v)}/16");
                badge.Text = type.Length > 0 ? $"{type} · {voices}" : voices;
            });
        }
        if ((badge.Text.Length > 0 || s.IsInstrument) && !s.Compact) badges.Children.Add(badge);
        if (s.HeaderExtra is { } headerExtra) badges.Children.Add(headerExtra);

        // The power slot: the bypass switch, or the same width left empty.
        var power = new Border { Width = SwitchTrack.W, VerticalAlignment = VerticalAlignment.Center };

        if (s.Bypassable)
        {
            var bypass = new SwitchTrack { IsOn = !s.Bypassed, Cursor = new Cursor(StandardCursorType.Hand) };
            ToolTip.SetTip(bypass, s.Bypassed ? "Bypassed — click to enable" : "Active — click to bypass");
            bypass.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(bypass).Properties.IsLeftButtonPressed) return;
                e.Handled = true;   // not a drag, not a selection click
                if (s.Kind == ChainKind.Midi) _engine.SetMidiEffectBypassed(_trackId, di, !s.Bypassed);
                else _engine.SetDeviceBypassed(_trackId, di, !s.Bypassed);
                Rebuild(); Changed?.Invoke();
            };
            power.Child = bypass;
        }

        // Two equal halves around the picker keep it centred whatever the name or the badges
        // (a live voice count changes width) are doing.
        var right = new Grid { ColumnSpacing = 8 };
        right.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        right.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        right.Children.Add(badges);
        Grid.SetColumn(power, 1); right.Children.Add(power);
        var headerGrid = new Grid { ColumnSpacing = 8 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        headerGrid.Children.Add(name);
        Grid.SetColumn(right, 2); headerGrid.Children.Add(right);
        var headerBar = new Border
        {
            Height = DeviceCardKit.HeaderH, Background = ShellHdr, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(NotaSpace.DeviceInsetWide, 0), Child = headerGrid,
            Cursor = s.CanMove && SelectableKind(s.Kind) ? new Cursor(StandardCursorType.SizeAll) : null,
        };
        headerBar.BindResource(Border.BoxShadowProperty, "Shadow.Raised");   // the raised strip's top bevel
        if (!s.Utility) ToolTip.SetTip(headerBar, s.CanMove ? "Drag to reorder · right-click for A/B, move and delete" : "Right-click for more");

        // Factory presets for this device (or the card's own list), and applying one in place.
        List<(string Id, string Name)> presets;
        Action<string> applyById;
        if (s.Presets is { } own)
        {
            presets = own.Items.ToList();
            // What the device holds now wins over what this card last applied: a kit loaded
            // from the browser, or a project reopened, still names its kit.
            string now = own.Current();
            if (now.Length > 0 && presets.FindIndex(p => p.Id == now) is var ni and >= 0) { extra.PresetId = now; extra.Preset = presets[ni].Name; }
            applyById = id =>
            {
                string warn = own.Apply(id);
                if (warn.Length > 0) StatusMessage?.Invoke(warn);
                Changed?.Invoke();
            };
        }
        else
        {
            presets = !s.Utility && (s.PresetKind >= 0 || s.IsInstrument || s.Kind == ChainKind.Midi)
                ? _factory.All().Where(p => p.BuiltinKind == s.PresetKind
                    && (s.Kind == ChainKind.Midi ? p.IsMidiEffect : (p.IsInstrument == s.IsInstrument && !p.IsMidiEffect)))
                    .Select(p => (p.Id, p.DisplayName)).ToList()
                : new List<(string Id, string Name)>();
            applyById = id => _factory.ApplyInPlace(_engine, id, _trackId, di);
        }
        int curPreset = presets.FindIndex(p => p.Id == extra.PresetId);
        void ApplyPreset(int i) { var p = presets[i]; applyById(p.Id); extra.PresetId = p.Id; extra.Preset = p.Name; Rebuild(); }
        void StepPreset(int dir)
        {
            int n = presets.Count; if (n == 0) return;
            ApplyPreset(curPreset < 0 ? (dir > 0 ? 0 : n - 1) : ((curPreset + dir) % n + n) % n);
        }
        // A compact (mini) card keeps the same picker — same list, same current preset — only narrower:
        // the preset belongs to the device, not to the card size.
        // The picker has its own centre slot: the badges' live voice count changes width
        // (Consort's "Duo 1/2" → "Poly 12/16") without moving it.
        if (presets.Count > 0)
        {
            var picker = PresetPicker(presets.Select(p => p.Name).ToList(), curPreset, extra.Preset, ApplyPreset, StepPreset,
                nameWidth: s.Compact ? 74 : 132);
            Grid.SetColumn(picker, 1); headerGrid.Children.Add(picker);
        }

        void Move(int to) { if (s.Kind == ChainKind.Midi) _engine.MoveMidiEffect(_trackId, di, to); else _engine.MoveDevice(_trackId, di, to); ExtrasMoved(s.Kind, di, to); Rebuild(); Changed?.Invoke(); }

        // What the header used to carry, now in its context menu.
        void HeaderMenu(MenuFlyout flyout)
        {
            if (presets.Count > 0)
            {
                var menu = new MenuItem { Header = $"Preset: {(string.IsNullOrEmpty(extra.Preset) ? "Init" : extra.Preset)}", Icon = MenuKit.Icon(GlyphKind.List) };
                var prev = new MenuItem { Header = "Previous preset", Icon = MenuKit.Icon(GlyphKind.ChevronLeft) }; prev.Click += (_, _) => StepPreset(-1);
                var next = new MenuItem { Header = "Next preset", Icon = MenuKit.Icon(GlyphKind.ChevronRight) }; next.Click += (_, _) => StepPreset(+1);
                menu.Items.Add(prev); menu.Items.Add(next); menu.Items.Add(new Separator());
                for (int i = 0; i < presets.Count; i++)
                {
                    int iv = i;
                    var mi = new MenuItem { Header = presets[i].Name, ToggleType = MenuItemToggleType.Radio, IsChecked = i == curPreset };
                    mi.Click += (_, _) => ApplyPreset(iv);
                    menu.Items.Add(mi);
                }
                flyout.Items.Add(menu);
            }
            // A / B compare of two full-parameter snapshots.
            float[] Capture() => CaptureParams(s.Kind, di);
            if (extra.A == null) { extra.A = Capture(); extra.B = (float[])extra.A.Clone(); extra.Active = 0; }
            var ab = new MenuItem { Header = $"Compare: {(extra.Active == 0 ? "A" : "B")}", Icon = MenuKit.Icon(GlyphKind.Compare) };
            void Switch(int slot)
            {
                if (slot == extra.Active) return;
                if (extra.Active == 0) extra.A = Capture(); else extra.B = Capture();
                extra.Active = slot;
                ApplyParams(s.Kind, di, slot == 0 ? extra.A! : extra.B!);
                Rebuild();
            }
            var toA = new MenuItem { Header = "A", ToggleType = MenuItemToggleType.Radio, IsChecked = extra.Active == 0 }; toA.Click += (_, _) => Switch(0);
            var toB = new MenuItem { Header = "B", ToggleType = MenuItemToggleType.Radio, IsChecked = extra.Active == 1 }; toB.Click += (_, _) => Switch(1);
            var copy = new MenuItem { Header = extra.Active == 0 ? "Copy A to B" : "Copy B to A", Icon = MenuKit.Icon(GlyphKind.Copy) };
            copy.Click += (_, _) => { var c = Capture(); if (extra.Active == 0) extra.B = (float[])c.Clone(); else extra.A = (float[])c.Clone(); };
            ab.Items.Add(toA); ab.Items.Add(toB); ab.Items.Add(new Separator()); ab.Items.Add(copy);
            flyout.Items.Add(ab);
            if (s.CanMove)
            {
                var left = new MenuItem { Header = "Move left", IsEnabled = di > 0, Icon = MenuKit.Icon(GlyphKind.StepLeft) }; left.Click += (_, _) => Move(di - 1);
                var rightMi = new MenuItem { Header = "Move right", IsEnabled = di < s.Count - 1, Icon = MenuKit.Icon(GlyphKind.StepRight) }; rightMi.Click += (_, _) => Move(di + 1);
                flyout.Items.Add(left); flyout.Items.Add(rightMi);
            }
            // This device's page in the user manual (a plug-in gets the plug-ins page).
            var docs = new MenuItem { Header = "Documentation" };
            docs.Click += (_, _) =>
            {
                int kind = s.Kind switch
                {
                    ChainKind.Instrument => _engine.TrackInstrumentKind(_trackId),
                    ChainKind.Midi => _engine.MidiEffectKind(_trackId, di),
                    _ => _engine.TrackDeviceBuiltinKind(_trackId, di),
                };
                NotaDocs.Open(headerBar, NotaDocs.DevicePage(s.Kind, kind));
            };
            flyout.Items.Add(docs);
            flyout.Items.Add(new Separator());
        }

        // Click selects the device (accent ring + keyboard target); right-click opens the menu.
        if (!s.Utility) headerBar.PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(headerBar).Properties;
            if (pt.IsRightButtonPressed) { ShowDeviceContextMenu(headerBar, s.Kind, di, HeaderMenu); return; }
            if (pt.IsLeftButtonPressed) SelectDevice(s.Kind, di);
        };

        DockPanel.SetDock(headerBar, Dock.Top);
        var bodyHost = new Border { Child = body };
        var root = new DockPanel { LastChildFill = true, Children = { headerBar, bodyHost } };
        bool sel = !s.Utility && IsSelected(s.Kind, di);
        // Selection is drawn as a focus ring outside the card, with a small gap (not by
        // thickening the card's own edge border, nor as an inset overlay): drawn on or inside
        // the edge it crowds the header — the title and the preset picker read as touching it.
        // The ring hangs outside the clipped shell; the chain row's margin and spacing leave it room.
        const double RingGap = 3;
        var selRing = new Border
        {
            BorderBrush = Brass, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(NotaRadius.BodyValue + RingGap), Margin = new Thickness(-RingGap),
            IsHitTestVisible = false, IsVisible = sel,
        };
        var shell = new Border
        {
            Background = ShellBg, BorderBrush = BorderDef,
            BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Body, ClipToBounds = true,
            Child = root,
        };
        var card = new Border
        {
            Width = s.Width, Height = CardH,
            Child = new Panel { Children = { shell, selRing } },
        };
        // Bypassed: the body takes the inactive look but stays playable (no opacity for state).
        if (s.Bypassed) card.AttachedToVisualTree += (_, _) => Inactive.Set(bodyHost, true, interactive: true);
        // The whole header drags the card to a new slot within its domain (audio FX / MIDI FX).
        if (s.CanMove && SelectableKind(s.Kind)) HookDeviceDrag(headerBar, card, s.Kind, di);
        return card;
    }

    private int IndexOfId(string id)
    { int n = _engine.PluginParamCount(_trackId, -1); for (int i = 0; i < n; i++) if (_engine.PluginParamId(_trackId, -1, i) == id) return i; return -1; }

}
