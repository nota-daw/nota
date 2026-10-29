// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The sidechain key source dropdown every keyed card shares (specs/050). It lists "Internal"
// (no key), then every other track — and, under a track whose instrument has parts (a Drum
// Rack's pads, an Instrument Rack's chains, Nota Rhythm's voices), each part indented: the
// tree "Drums ▸ Whole track / Kick / Snare …" as one flat list, so a pad is two clicks away.
// While closed the list follows the key (MCP, project load) and rebuilds when the tracks, their
// parts or the route pool change (pads come and go while the card is up). Sources the route
// pool can't take are greyed.

using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Nota.Application;

namespace Nota.App;

internal sealed class SidechainSourcePicker
{
    private readonly IAudioEngine _engine;
    private readonly int _ownTrack;
    private readonly Func<SidechainKey> _get;
    private readonly Func<int, int, int> _set;
    private readonly string _none;
    private readonly List<SidechainKey> _keys = new();
    private string _shape = "";
    private bool _syncing;

    /// <summary>The dropdown, styled like the cards' other compact combos.</summary>
    public ComboBox Combo { get; }

    /// <summary>A source was picked (after it was set): the card's own follow-up, such as
    /// switching External on.</summary>
    public event Action<SidechainKey>? Picked;

    /// <param name="get">The consumer's key now.</param>
    /// <param name="set">Set (source, part); returns 0, or -2 when no route is free.</param>
    /// <param name="none">The no-key entry ("Internal" for a detector, "None" for React).</param>
    public SidechainSourcePicker(IAudioEngine engine, int ownTrack, Func<SidechainKey> get, Func<int, int, int> set,
        string none = "Internal", double minWidth = 92)
    {
        _engine = engine;
        _ownTrack = ownTrack;
        _get = get;
        _set = set;
        _none = none;
        Combo = new ComboBox
        {
            FontSize = 8, Height = 18, MinHeight = 18, MinWidth = minWidth, Padding = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The open list shows a part by its own name under its track; the closed box names the
        // track too ("Drums · Kick"), since two tracks may both have a "Kick".
        Combo.SelectionBoxItemTemplate = new FuncDataTemplate<Row>((row, _) => new TextBlock { Text = row?.Full ?? "" });
        Rebuild();
        Combo.SelectionChanged += (_, _) =>
        {
            int s = Combo.SelectedIndex;
            if (_syncing || s < 0 || s >= _keys.Count) return;
            var k = _keys[s];
            if (k == _get()) return;
            if (_set(k.Source, k.Part) == -2)
            {
                ToolTip.SetTip(Combo, "No free routes — every sidechain route is in use");
                Sync();
                return;
            }
            Picked?.Invoke(k);
        };
    }

    /// <summary>"Internal", "Drums" or "Drums · Kick" (with "missing" for a deleted part).</summary>
    public string SourceName() => Name(_engine, _get(), _none);

    /// <summary>The name of a key's source, as the cards and readouts show it.</summary>
    public static string Name(IAudioEngine engine, SidechainKey k, string none = "Internal")
    {
        if (k.Source < 0) return none;
        string track = TrackNames.Of(engine, k.Source);
        if (k.Part == SidechainKey.WholeTrack) return track;
        if (k.Part == SidechainKey.Missing) return $"{track} · missing";
        string part = engine.TrackPartName(k.Source, k.Part);
        return $"{track} · {(part.Length > 0 ? part : $"Part {k.Part + 1}")}";
    }

    /// <summary>Follow the key while the list is closed — call from the card's readout tick.</summary>
    public void Sync()
    {
        if (Combo.IsDropDownOpen) return;
        int want = _keys.IndexOf(_get());
        if (want < 0 || Shape() != _shape) { Rebuild(); return; }
        if (Combo.SelectedIndex != want) Select(want);
    }

    // What the list shows, as text: the tracks with their names and parts, frozen tracks, and
    // whether the pool is full. Cheap enough for the readout tick.
    private string Shape()
    {
        var sb = new StringBuilder();
        sb.Append(_engine.SidechainRoutesFree() == 0 ? 'F' : 'o');
        for (int i = 0; i < _engine.TrackCount; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti) || ti.Id == _ownTrack) continue;
            sb.Append('|').Append(ti.Id).Append(':').Append(TrackNames.Of(_engine, ti));
            int parts = _engine.TrackPartCount(ti.Id);
            if (parts > 0 && _engine.IsTrackFrozen(ti.Id)) sb.Append('*');
            for (int p = 0; p < parts; p++) sb.Append('/').Append(_engine.TrackPartName(ti.Id, p));
        }
        return sb.ToString();
    }

    private void Select(int i)
    {
        _syncing = true;
        Combo.SelectedIndex = i;
        _syncing = false;
    }

    /// <summary>One row of a source list: a track (Part false) or one of its parts, indented
    /// under it. Disabled rows carry the reason as a tooltip.</summary>
    public readonly record struct Entry(SidechainKey Key, string Label, bool IsPart, bool Enabled, string? Tip);

    /// <summary>Every source a key on <paramref name="ownTrack"/> may pick, in track order, each
    /// track followed by its parts — for the cards that list sources in their own flyout.
    /// <paramref name="parts"/> false lists whole tracks only (a MIDI key has no parts).</summary>
    public static List<Entry> Entries(IAudioEngine engine, int ownTrack, SidechainKey cur, bool parts = true)
    {
        var list = new List<Entry>();
        bool full = engine.SidechainRoutesFree() == 0;
        // A source is takeable when it already has a route, when it is the one this key holds
        // (moving off it frees that route), or while any route is free.
        bool Takeable(int src, int part) => !full || engine.SidechainRouteInUse(src, part) || cur == new SidechainKey(src, part);
        const string NoRoute = "No free routes — every sidechain route is in use";
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == ownTrack) continue;
            bool whole = Takeable(ti.Id, SidechainKey.WholeTrack);
            list.Add(new Entry(new SidechainKey(ti.Id, SidechainKey.WholeTrack), TrackNames.Of(engine, ti), false, whole, whole ? null : NoRoute));
            int n = parts ? engine.TrackPartCount(ti.Id) : 0;
            bool frozen = n > 0 && engine.IsTrackFrozen(ti.Id);
            for (int p = 0; p < n; p++)
            {
                string name = engine.TrackPartName(ti.Id, p);
                bool ok = !frozen && Takeable(ti.Id, p);
                list.Add(new Entry(new SidechainKey(ti.Id, p), name.Length > 0 ? name : $"Part {p + 1}", true, ok,
                    frozen ? "Frozen — its parts don't play until the track is unfrozen" : ok ? null : NoRoute));
            }
            if (parts && cur.Source == ti.Id && cur.Part == SidechainKey.Missing)
                list.Add(new Entry(cur, "missing part", true, false, "The keyed part was deleted — undo brings it back"));
        }
        return list;
    }

    /// <summary>Key a device off a picked source; a full route pool leaves the key as it was.
    /// Returns false then (the card may say so).</summary>
    public static bool SetDeviceKey(IAudioEngine engine, int track, int di, SidechainKey k)
        => engine.SetDeviceSidechainKey(track, di, k.Source, k.Part) != -2;

    /// <summary>A device's key sources as menu rows for the cards whose pickers are plain menus:
    /// each track, then its parts as "Track · Part". A row the route pool can't take (or a frozen
    /// track's part) has no Pick and carries the reason in Tip — show it disabled.</summary>
    public static List<(string Label, bool Checked, Action? Pick, string? Tip)> MenuRows(IAudioEngine engine, int track, int di,
        Action changed, string prefix = "")
    {
        var cur = engine.DeviceSidechainKey(track, di);
        var rows = new List<(string, bool, Action?, string?)>();
        foreach (var en in Entries(engine, track, cur))
        {
            var k = en.Key;
            Action? pick = en.Enabled ? () => { SetDeviceKey(engine, track, di, k); changed(); } : null;
            rows.Add((prefix + Name(engine, k), cur == k, pick, en.Tip));
        }
        return rows;
    }

    /// <summary>A radio menu item for a source row: disabled with its reason when it can't be
    /// picked. <paramref name="after"/> runs after a pick (the card's refresh).</summary>
    public static MenuItem MenuItemFor((string Label, bool Checked, Action? Pick, string? Tip) row, Action? after = null)
    {
        var mi = new MenuItem { Header = row.Label, ToggleType = MenuItemToggleType.Radio, IsChecked = row.Checked, IsEnabled = row.Pick != null };
        if (row.Tip != null) ToolTip.SetTip(mi, row.Tip);
        if (row.Pick is { } pick) mi.Click += (_, _) => { pick(); after?.Invoke(); };
        return mi;
    }

    // A list row: its label in the open list, and the full source name for the closed box.
    private sealed record Row(string Short, string Full)
    {
        public override string ToString() => Short;
    }

    private void Rebuild()
    {
        var cur = _get();
        _syncing = true;
        _keys.Clear();
        Combo.Items.Clear();
        void Add(SidechainKey k, string label, bool indent, bool enabled, string? tip)
        {
            _keys.Add(k);
            var row = new Row(label, k.Source < 0 ? label : Name(_engine, k, _none));
            var item = new ComboBoxItem { Content = row, IsEnabled = enabled, Padding = new Thickness(indent ? 18 : 6, 2, 6, 2) };
            if (tip != null) ToolTip.SetTip(item, tip);
            Combo.Items.Add(item);
        }
        Add(SidechainKey.None, _none, false, true, null);
        foreach (var e in Entries(_engine, _ownTrack, cur)) Add(e.Key, e.Label, e.IsPart, e.Enabled, e.Tip);
        int at = _keys.IndexOf(cur);
        Combo.SelectedIndex = at >= 0 ? at : 0;
        _shape = Shape();
        _syncing = false;
    }
}
