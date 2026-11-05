// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · send levels in the track headers (specs/069, #143). A Large track card carries
// a row of inline knobs, one per return in bus order, so a send can be set without leaving
// the Arrangement. The knobs write the live level only (not an undo step, not an automation
// lane) and follow sends moved anywhere else on the meter tick.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class ArrangementView
{
    private const double SendRowTop = 6;
    // A full cell: the inline knob, then the bus letter over the level; narrower → knob only.
    private const double SendCellW = 41, SendCellGap = 4;

    // Header send knobs, refreshed each meter tick so other surfaces move them.
    private readonly List<(int trackId, int bus, Knob knob, TextBlock? value, Control cell, string name)> _sendKnobs = new();

    /// <summary>Whether a track card at this height shows its send row (specs/069 FR-001).
    /// The Large layout below the preset has no room for another line, so the knobs wait
    /// for the Large height itself.</summary>
    private bool ShowSendsOn(TrackVM t, HeaderTier tier, double rowH)
        => !t.IsReturn && _returns.Count > 0
           && tier == HeaderTier.Large && rowH + 0.5 >= LargeRowHeight;

    // The returns in bus order, with their bus and name.
    private List<(int bus, string name)> SendBuses()
    {
        var buses = new List<(int bus, string name)>();
        if (_engine is not { } e) return buses;
        foreach (var r in _returns)
        {
            int bus = e.TrackReturnIndex(r.Id);
            if (bus >= 0) buses.Add((bus, r.Name));
        }
        buses.Sort((a, b) => a.bus.CompareTo(b.bus));
        return buses;
    }

    private static string SendLetter(int bus) => ((char)('A' + Math.Clamp(bus, 0, 25))).ToString();

    // The return each track's shown send lane points at, so the lane follows its bus when a
    // return before it is deleted (the engine renumbers the lanes the same way).
    private readonly Dictionary<int, int> _autoSendReturn = new();

    private int ReturnOnBus(int bus)
    {
        if (_engine is not { } e) return -1;
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.IsReturn && e.TrackReturnIndex(ti.Id) == bus) return ti.Id;
        return -1;
    }

    private void NoteSendTarget(TrackVM t)
    {
        if (t.AutoTarget != AutomationTarget.Send) { _autoSendReturn.Remove(t.Id); return; }
        int r = ReturnOnBus(t.AutoDeviceIndex);
        if (r > 0) _autoSendReturn[t.Id] = r; else _autoSendReturn.Remove(t.Id);
    }

    // Move a shown send lane to its return's current bus; its return gone → back to Volume.
    private void ResyncSendTarget(TrackVM t)
    {
        if (_engine is not { } e) return;
        if (!_autoSendReturn.TryGetValue(t.Id, out int r)) { NoteSendTarget(t); return; }
        int bus = e.TrackReturnIndex(r);
        if (bus == t.AutoDeviceIndex) return;
        if (bus < 0)
        {
            t.AutoTarget = AutomationTarget.Volume;
            _autoSendReturn.Remove(t.Id);
        }
        t.AutoDeviceIndex = bus;
        t.AutoParamIndex = t.AutoLane = -1;
        _autoTargets[t.Id] = (t.AutoTarget, t.AutoDeviceIndex, -1, "", -1, -1, -1);
    }

    // The name of the return on a bus ("" when none is there).
    // Reads the engine, not _returns: a Refresh labels the lanes before it rebuilds the returns.
    private string SendBusName(int bus)
    {
        if (_engine is not { } e) return "";
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.IsReturn && e.TrackReturnIndex(ti.Id) == bus) return TrackNames.Of(e, ti);
        return "";
    }
    private static string SendLevelText(double v) => v <= 0.005 ? "–" : NotaNum.Str(v * 100, "0");
    private static string SendTip(int bus, string name, double v) => Loc.F("arrangement.send", SendLetter(bus), name) + " · " + NotaNum.Pct(v);

    /// <summary>The send row for a track card: one inline knob per return. <paramref name="contentW"/>
    /// is the width the card's controls have; cells drop their letter and level below it.</summary>
    private Control? BuildSendRow(TrackVM t, double contentW)
    {
        if (_engine is not { } e) return null;
        var buses = SendBuses();
        if (buses.Count == 0) return null;
        bool full = buses.Count * SendCellW + (buses.Count - 1) * SendCellGap <= contentW;
        var row = new Grid { Margin = new Thickness(0, SendRowTop, 0, 0), ColumnSpacing = SendCellGap, Height = Knob.SizeInline };
        for (int i = 0; i < buses.Count; i++) row.ColumnDefinitions.Add(new ColumnDefinition(full ? GridLength.Star : GridLength.Auto));
        if (!full) row.HorizontalAlignment = HorizontalAlignment.Left;
        for (int i = 0; i < buses.Count; i++)
        {
            var (bus, name) = buses[i];
            float level = e.GetTrackSend(t.Id, bus);
            var knob = new Knob(level, 1.0) { Inline = true, Accent = true, Default = 0, Format = v => NotaNum.Pct(v) };
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Background = Brushes.Transparent };
            cell.Children.Add(knob);
            TextBlock? value = null;
            if (full)
            {
                var letter = new TextBlock { Text = SendLetter(bus), FontSize = 8, FontWeight = FontWeight.Medium, Foreground = Brush("Brush.TextSecondary") };
                value = new TextBlock { Text = SendLevelText(level), FontSize = 8, Classes = { "Mono" }, Foreground = Brush("Brush.TextTertiary") };
                cell.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { letter, value } });
            }
            ToolTip.SetTip(cell, SendTip(bus, name, level));
            int b = bus;
            var shown = value;
            SendControl.Attach(knob, e, t.Id, b, v =>
            {
                if (shown is not null) shown.Text = SendLevelText(v);
                ToolTip.SetTip(cell, SendTip(b, name, v));
            });
            ValueEntry.Attach(knob, Loc.F("common.send", SendLetter(bus)), value);
            _sendKnobs.Add((t.Id, bus, knob, value, cell, name));
            Grid.SetColumn(cell, i);
            row.Children.Add(cell);
        }
        return row;
    }

    // Follow sends changed elsewhere — the Mixer, the Sends card, MCP — unless in the hand.
    private void FollowSends(IAudioEngine e)
    {
        foreach (var (id, bus, knob, value, cell, name) in _sendKnobs)
        {
            if (knob.Dragging) continue;
            double v = e.GetTrackSend(id, bus);
            if (Math.Abs(knob.Value - v) <= 1e-3) continue;
            knob.Value = v;
            if (value is not null) value.Text = SendLevelText(v);
            ToolTip.SetTip(cell, SendTip(bus, name, v));
        }
    }
}
