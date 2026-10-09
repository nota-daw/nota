// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — where a browser item dropped on the panel lands, shown before the
// drop. An audio / MIDI effect goes into its own chain: over a card's middle half it
// replaces that card (ring around it), over a card's edges or the gaps it is inserted
// there (the same accent bar the drag-reorder draws). An instrument replaces the track's
// instrument (ring around it; a rack gets a new chain instead). Anything else — samples,
// presets, external files — lights the whole panel as before.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Nota.Presentation;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

/// <summary>How a browser item dropped on the Devices panel is placed.</summary>
public enum DeviceDropMode
{
    /// <summary>The usual routing: effects append, instruments go to a rack / nowhere.</summary>
    Default,
    /// <summary>A new effect goes in at <see cref="DeviceDropTarget.Index"/> (a gap, 0..count).</summary>
    Insert,
    /// <summary>The device at <see cref="DeviceDropTarget.Index"/> (-1 = the instrument) is swapped for the new one.</summary>
    Replace,
}

/// <summary>A Devices-panel drop target. <paramref name="Midi"/>: the MIDI-effect chain, else audio effects.</summary>
public readonly record struct DeviceDropTarget(DeviceDropMode Mode, int Index, bool Midi);

public sealed partial class DeviceChainView
{
    private Control? _instrumentCard;   // the instrument / rack card in _row (null on audio, return, master)

    // Hints live on a layer over the scroller so showing them never reflows the cards
    // (a reflow under the pointer would flip the target back and forth).
    private readonly Canvas _dropLayer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Border _dropInsertBar = new()
    {
        Width = 3, Background = AccentBright, CornerRadius = NotaRadius.Clip, IsVisible = false,
    };
    private readonly Border _dropReplaceRing = new()
    {
        BorderBrush = AccentBright, BorderThickness = new Thickness(2), CornerRadius = NotaRadius.Body,
        Background = NotaPalette.Wash(NotaPalette.AccentBright, 0x1E), IsVisible = false,
    };

    private void HookBrowserDrop()
    {
        _dropLayer.Children.Add(_dropReplaceRing);
        _dropLayer.Children.Add(_dropInsertBar);
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) =>
        {
            var (ok, _) = DropTargetAt(e, showHints: true);
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        });
        DragDrop.AddDragLeaveHandler(this, (_, _) => ClearDropHints());
        DragDrop.AddDropHandler(this, (_, e) =>
        {
            var (ok, target) = DropTargetAt(e, showHints: false);
            ClearDropHints();
            if (!ok) return;
            var items = BrowserView.DroppedItems(e);
            if (items.Count == 0) return;
            foreach (var item in items) ItemDropped?.Invoke(item, target);
            e.Handled = true;
        });
    }

    private void ClearDropHints()
    {
        _dropGlow.IsVisible = false;
        _dropInsertBar.IsVisible = false;
        _dropReplaceRing.IsVisible = false;
    }

    // Resolves the drop target under the pointer and (optionally) shows its hint.
    private (bool Ok, DeviceDropTarget Target) DropTargetAt(DragEventArgs e, bool showHints)
    {
        if (showHints) ClearDropHints();
        if (_trackId <= 0 || !BrowserView.IsAcceptableDrag(e)) return (false, default);
        double x = e.GetPosition(_row).X;
        switch (BrowserView.CurrentDrag?.Kind)
        {
            case BrowserItemKind.BuiltinInstrument:
            case BrowserItemKind.PluginInstrument:
            {
                if (_instrumentCard is null || !IsInstrumentTrack(_trackId)) return (false, default);
                if (showHints) RingOver(_instrumentCard);
                int kind = _engine.TrackInstrumentKind(_trackId);
                // A rack takes the instrument as a new chain / pad (the default routing).
                return (true, kind is 3 or 4 ? default : new DeviceDropTarget(DeviceDropMode.Replace, -1, false));
            }
            case BrowserItemKind.BuiltinEffect:
            case BrowserItemKind.PluginEffect:
                return (true, EffectTargetAt(ChainKind.Effect, x, showHints));
            case BrowserItemKind.BuiltinMidiEffect:
                if (!IsInstrumentTrack(_trackId)) return (false, default);
                return (true, EffectTargetAt(ChainKind.Midi, x, showHints));
            default:
                if (showHints) _dropGlow.IsVisible = true;
                return (true, default);
        }
    }

    // Over a card's middle half → replace it; its outer quarters and the gaps → insert.
    private DeviceDropTarget EffectTargetAt(ChainKind kind, double x, bool showHints)
    {
        var cards = DomainCards(kind);
        bool midi = kind == ChainKind.Midi;
        for (int i = 0; i < cards.Count; i++)
        {
            var b = cards[i].Bounds;
            if (x < b.X + b.Width * 0.25) { if (showHints) BarAtGap(kind, i); return new(DeviceDropMode.Insert, i, midi); }
            if (x <= b.Right - b.Width * 0.25) { if (showHints) RingOver(cards[i]); return new(DeviceDropMode.Replace, i, midi); }
        }
        if (showHints) BarAtGap(kind, cards.Count);
        return new(DeviceDropMode.Insert, cards.Count, midi);
    }

    // The insertion bar centred in the spacing before gap's card (or after the last one).
    private void BarAtGap(ChainKind kind, int gap)
    {
        var cards = DomainCards(kind);
        double half = _row.Spacing / 2;
        Rect card;   // the card the bar stands beside (for its x and vertical extent)
        double x;
        if (gap < cards.Count) { card = cards[gap].Bounds; x = card.X - half; }
        else if (cards.Count > 0) { card = cards[^1].Bounds; x = card.Right + half; }
        else
        {
            // An empty chain: MIDI effects open the row; audio effects follow the MIDI
            // effects and the instrument (i.e. sit before Sends / Add device).
            int at = 0;
            if (kind == ChainKind.Effect)
                foreach (var c in _row.Children)
                    if (c.Tag is DevTag { Kind: ChainKind.Midi } || ReferenceEquals(c, _instrumentCard)) at++;
            if (at >= _row.Children.Count) return;
            card = _row.Children[at].Bounds; x = card.X - half;
        }
        var top = _row.TranslatePoint(new Point(x - _dropInsertBar.Width / 2, card.Y + 6), _dropLayer);
        if (top is null) return;
        Canvas.SetLeft(_dropInsertBar, top.Value.X);
        Canvas.SetTop(_dropInsertBar, top.Value.Y);
        _dropInsertBar.Height = CardH - 12;
        _dropInsertBar.IsVisible = true;
    }

    private void RingOver(Control card)
    {
        var p = card.TranslatePoint(default, _dropLayer);
        if (p is null) return;
        Canvas.SetLeft(_dropReplaceRing, p.Value.X);
        Canvas.SetTop(_dropReplaceRing, p.Value.Y);
        _dropReplaceRing.Width = card.Bounds.Width;
        _dropReplaceRing.Height = card.Bounds.Height;
        _dropReplaceRing.IsVisible = true;
    }

    /// <summary>Keeps card state (preset labels, A/B, automation targets) glued to its device
    /// after <see cref="DeviceInsertService"/> appended an effect at <paramref name="added"/>,
    /// moved it to <paramref name="final"/> and maybe removed the card it replaced — then
    /// selects the new card. The engine half happened in the service, in its undo group.</summary>
    internal void AdoptPlacement(int trackId, bool midi, int added, int final, bool replaced)
    {
        var k = midi ? ChainKind.Midi : ChainKind.Effect;
        TrackExtras(trackId).Remove(ExtraKey(k, added));   // a fresh slot: drop any stale entry
        if (trackId != _trackId) return;                  // only the shown chain has moved cards to follow
        if (final != added) ExtrasMoved(k, added, final);
        if (replaced) ExtrasRemoved(k, final + 1);
        _selChainKind = k; _selDeviceIndex = final;
        Rebuild(); Changed?.Invoke();
    }

    /// <summary>Where the command palette would insert (CP-2): the selected card's section and
    /// index (the instrument when it is selected), else nothing.</summary>
    internal Nota.Application.Palette.InsertPoint CurrentInsertPoint()
    {
        if (_trackId <= 0) return Nota.Application.Palette.InsertPoint.None;
        if (HasDeviceSelection)
        {
            bool midi = _selChainKind == ChainKind.Midi;
            int n = midi ? _engine.TrackMidiEffectCount(_trackId) : _engine.TrackDeviceCount(_trackId);
            if (_selDeviceIndex < n)
                return new(midi ? Nota.Application.Palette.ChainSection.Midi : Nota.Application.Palette.ChainSection.Audio, _selDeviceIndex,
                    midi ? _engine.MidiEffectName(_trackId, _selDeviceIndex) : _engine.DeviceName(_trackId, _selDeviceIndex));
        }
        return Nota.Application.Palette.InsertPoint.None;
    }
}
