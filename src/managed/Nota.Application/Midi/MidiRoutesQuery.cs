// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The MIDI routes overview (specs/115 US3, FR-018 … FR-021): one row per instrument track — what
// it listens to (every input, or ports with their channels), the track it takes MIDI from, and
// where it sends (its own instrument, or External MIDI's port or role and channel) — with every
// endpoint that isn't there marked. Read-only: building rows never changes playback or routing.
// The Mixer's MIDI routes panel and MCP get_midi_routes both read it.

namespace Nota.Application.Midi;

/// <summary>One endpoint in a row: a port, a role or a track, with its availability.</summary>
public sealed record MidiRouteEnd(string Key, string Name, string Detail, RouteAvailability Availability);

/// <summary>One instrument track's routes. <see cref="Inputs"/> is empty for every input
/// (<see cref="AllInputs"/>); <see cref="Destination"/> is null for a local instrument.</summary>
public sealed record MidiRouteRow(
    int TrackId, string TrackName, int TrackColorIndex,
    bool AllInputs, string AllInputsChannels, IReadOnlyList<MidiRouteEnd> Inputs,
    MidiRouteEnd? MidiFrom,
    string InstrumentName, MidiRouteEnd? Destination)
{
    /// <summary>Anything this row names that isn't there.</summary>
    public bool HasMissing => Inputs.Any(i => i.Availability != RouteAvailability.Available)
                              || MidiFrom is { Availability: not RouteAvailability.Available }
                              || Destination is { Availability: not RouteAvailability.Available };
}

public static class MidiRoutesQuery
{
    /// <summary>"3, 7, 11" — or "" for all sixteen.</summary>
    public static string ChannelsText(ushort mask)
    {
        if (mask == MidiInputRoute.AllChannels || mask == 0) return "";
        return string.Join(", ", Enumerable.Range(1, 16).Where(c => ((mask >> (c - 1)) & 1) != 0));
    }

    /// <summary>Every instrument track's row, in track order, with their current names. Filters keep
    /// the rows whose inputs or MIDI In include <paramref name="source"/> (a port uid or "track:id")
    /// and whose destination is <paramref name="destination"/> (an output uid or "role:id").</summary>
    public static IReadOnlyList<MidiRouteRow> Rows(IAudioEngine engine, string? source = null, string? destination = null)
    {
        var ports = engine.MidiPorts();
        var outputs = engine.MidiOutputs();
        var roles = engine.MidiRoles();
        var names = new Dictionary<int, string>();
        var instruments = new List<NotaTrackInfo>();
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti)) continue;
            names[ti.Id] = engine.GetTrackName(ti.Id);
            if (ti.IsInstrument) instruments.Add(ti);
        }

        var rows = new List<MidiRouteRow>();
        foreach (var ti in instruments)
        {
            var list = engine.GetTrackMidiInputs(ti.Id);
            var inputs = list.All ? new List<MidiRouteEnd>() : list.Routes.Select(r =>
            {
                string live = ports.FirstOrDefault(p => p.Uid == r.Uid).Name ?? "";
                string name = live.Length > 0 ? live : r.Name.Length > 0 ? r.Name : r.Uid;
                return new MidiRouteEnd(r.Uid, name, ChannelsText(r.Channels), MidiRouteResolver.ResolveInput(ports, r.Uid));
            }).ToList();

            MidiRouteEnd? from = null;
            int src = engine.GetTrackMidiSource(ti.Id);
            if (src > 0)
                from = names.TryGetValue(src, out var sn)
                    ? new MidiRouteEnd("track:" + src, sn, "", RouteAvailability.Available)
                    : new MidiRouteEnd("track:" + src, "", "", RouteAvailability.Missing);   // a removed track

            MidiRouteEnd? dest = null;
            if (engine.GetExternalMidi(ti.Id) is { } d)
            {
                string channel = d.Channel == ExternalMidiDestination.KeepChannels ? "keep" : d.Channel.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (d.RoleId.Length > 0 && roles.FirstOrDefault(r => r.Id == d.RoleId) is { } role)
                {
                    var a = role.OutputUid.Length == 0 ? RouteAvailability.Unbound : MidiRouteResolver.ResolveOutput(outputs, role.OutputUid);
                    dest = new MidiRouteEnd("role:" + role.Id, role.Name, channel, a);
                }
                else if (d.OutputUid.Length > 0)
                {
                    string live = outputs.FirstOrDefault(o => o.Uid == d.OutputUid).Name ?? "";
                    dest = new MidiRouteEnd(d.OutputUid, live.Length > 0 ? live : d.OutputName.Length > 0 ? d.OutputName : d.OutputUid,
                        channel, MidiRouteResolver.ResolveOutput(outputs, d.OutputUid));
                }
                else dest = new MidiRouteEnd("", "", channel, RouteAvailability.Unbound);
            }

            var row = new MidiRouteRow(ti.Id, names[ti.Id], engine.GetTrackColorIndex(ti.Id),
                list.All, list.All ? ChannelsText(list.AllChannels) : "", inputs, from, engine.DeviceName(ti.Id, -1), dest);
            if (source is { Length: > 0 } && !(row.Inputs.Any(x => x.Key == source) || row.MidiFrom?.Key == source || (row.AllInputs && source == "all"))) continue;
            // A port matches the tracks sent to it directly and those following a role bound to it.
            bool toDest = row.Destination is { } rd && (rd.Key == destination ||
                          (rd.Key.StartsWith("role:", StringComparison.Ordinal) && roles.FirstOrDefault(r => "role:" + r.Id == rd.Key)?.OutputUid == destination));
            if (destination is { Length: > 0 } && !toDest) continue;
            rows.Add(row);
        }
        return rows;
    }
}
