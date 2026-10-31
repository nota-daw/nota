// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Devices panel's selection on a track's chain (specs/044): several devices across the
// MIDI effects, the instrument and the audio effects, picked with click, Shift-click (a range
// in chain order) and ⌘/Ctrl-click (toggle). It also decides what ⌘G can make of it — which
// rack, or why not — so the panel, its menu and the smoke test share one rule.

namespace Nota.Presentation;

/// <summary>Where a device sits in a track's chain, in chain order.</summary>
public enum DeviceSection { Midi = 0, Instrument = 1, Audio = 2 }

/// <summary>A device in the chain: its section and index there (the instrument is index -1).</summary>
public readonly record struct ChainSlot(DeviceSection Section, int Index)
{
    public static ChainSlot Instrument => new(DeviceSection.Instrument, -1);
}

/// <summary>What Group into rack would make of a selection.</summary>
public enum GroupShapeKind { None, AudioRack, MidiRack, InstrumentRack, Refused }

/// <summary>The rack a selection groups into and the runs it takes, or the reason it can't.</summary>
public readonly record struct GroupShape(GroupShapeKind Kind, int MidiFirst, int MidiCount, bool WithInstrument,
                                         int FxFirst, int FxCount, string? Reason)
{
    public bool CanGroup => Kind is GroupShapeKind.AudioRack or GroupShapeKind.MidiRack or GroupShapeKind.InstrumentRack;
}

public sealed class DeviceSelection
{
    private readonly List<ChainSlot> _members = new();

    /// <summary>The selected devices in chain order.</summary>
    public IReadOnlyList<ChainSlot> Members => _members;
    public ChainSlot? Anchor { get; private set; }
    public int Count => _members.Count;
    public bool IsEmpty => _members.Count == 0;
    public bool Contains(ChainSlot s) => _members.Contains(s);
    public bool HasInstrument => _members.Contains(ChainSlot.Instrument);
    public IEnumerable<int> Indices(DeviceSection section) => _members.Where(m => m.Section == section).Select(m => m.Index);

    /// <summary>Plain click: only this device.</summary>
    public void Click(ChainSlot s)
    {
        _members.Clear();
        _members.Add(s);
        Anchor = s;
    }

    /// <summary>⌘/Ctrl-click: this device in or out; it becomes the anchor.</summary>
    public void Toggle(ChainSlot s)
    {
        if (!_members.Remove(s)) Insert(s);
        Anchor = s;
    }

    /// <summary>Shift-click: every device from the anchor to this one in chain order
    /// (<paramref name="midiCount"/> MIDI effects → the instrument if <paramref name="hasInstrument"/> → <paramref name="fxCount"/> audio effects).</summary>
    public void ExtendTo(ChainSlot s, int midiCount, bool hasInstrument, int fxCount)
    {
        if (Anchor is not { } a) { Click(s); return; }
        var order = Order(midiCount, hasInstrument, fxCount);
        int i = order.IndexOf(a), j = order.IndexOf(s);
        if (i < 0 || j < 0) { Click(s); return; }
        _members.Clear();
        for (int k = Math.Min(i, j); k <= Math.Max(i, j); k++) _members.Add(order[k]);
    }

    public void Clear()
    {
        _members.Clear();
        Anchor = null;
    }

    /// <summary>Select exactly these (after an edit: the new rack, the restored devices, a paste).</summary>
    public void Set(IEnumerable<ChainSlot> slots)
    {
        _members.Clear();
        foreach (var s in slots) Insert(s);
        Anchor = _members.Count > 0 ? _members[0] : null;
    }

    /// <summary>Drop selected devices that no longer exist (the chain shrank under the selection).</summary>
    public void Prune(int midiCount, bool hasInstrument, int fxCount)
    {
        _members.RemoveAll(m => m.Section switch
        {
            DeviceSection.Midi => m.Index >= midiCount,
            DeviceSection.Instrument => !hasInstrument,
            _ => m.Index >= fxCount,
        });
        if (Anchor is { } a && !_members.Contains(a)) Anchor = _members.Count > 0 ? _members[0] : null;
    }

    /// <summary>What ⌘G makes of the selection (data-model.md § Group shape).</summary>
    public GroupShape Shape(int midiCount, int fxCount)
    {
        if (IsEmpty) return new(GroupShapeKind.None, -1, 0, false, -1, 0, null);
        var midi = Indices(DeviceSection.Midi).OrderBy(i => i).ToList();
        var fx = Indices(DeviceSection.Audio).OrderBy(i => i).ToList();
        bool inst = HasInstrument;
        static bool Consecutive(List<int> l) => l.Count == 0 || l[^1] - l[0] == l.Count - 1;
        GroupShape Refuse(string why) => new(GroupShapeKind.Refused, -1, 0, false, -1, 0, why);
        if (!Consecutive(midi) || !Consecutive(fx)) return Refuse("Select devices next to each other to group them");
        int mf = midi.Count > 0 ? midi[0] : -1, ff = fx.Count > 0 ? fx[0] : -1;
        if (inst)
        {
            if (midi.Count > 0 && midi[^1] != midiCount - 1) return Refuse("Select devices next to each other to group them");
            if (fx.Count > 0 && ff != 0) return Refuse("Select devices next to each other to group them");
            return new(GroupShapeKind.InstrumentRack, mf, midi.Count, true, ff, fx.Count, null);
        }
        if (midi.Count > 0 && fx.Count > 0)
            return Refuse("MIDI and audio effects go in one rack only with the instrument between them");
        return midi.Count > 0
            ? new(GroupShapeKind.MidiRack, mf, midi.Count, false, -1, 0, null)
            : new(GroupShapeKind.AudioRack, -1, 0, false, ff, fx.Count, null);
    }

    private static List<ChainSlot> Order(int midiCount, bool hasInstrument, int fxCount)
    {
        var o = new List<ChainSlot>(midiCount + fxCount + 1);
        for (int i = 0; i < midiCount; i++) o.Add(new(DeviceSection.Midi, i));
        if (hasInstrument) o.Add(ChainSlot.Instrument);
        for (int i = 0; i < fxCount; i++) o.Add(new(DeviceSection.Audio, i));
        return o;
    }

    // Keep members in chain order: section, then index.
    private void Insert(ChainSlot s)
    {
        int at = _members.FindIndex(m => m.Section > s.Section || (m.Section == s.Section && m.Index > s.Index));
        if (at < 0) _members.Add(s);
        else _members.Insert(at, s);
    }
}
