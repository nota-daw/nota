// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Project MIDI output roles (specs/115 US4): a named destination such as "Stage Synth", bound to
// one output, that External MIDI tracks follow instead of naming the port themselves. When the
// interface changes before a show, the role is rebound once — with the transport stopped, after
// a look at exactly which tracks move — and every track keeps its own channel (FR-013 … FR-017).
// The table lives in the engine's undo snapshot; this is the rules around it, shared by the
// External MIDI card and the MCP tools.

using Nota.Application.Localization;

namespace Nota.Application.Midi;

public static class MidiOutputRoles
{
    public const int MaxNameLength = 40;

    /// <summary>Why a role edit was refused, or null when it is fine.</summary>
    public static string? CheckName(IAudioEngine engine, string name, string? exceptId = null)
    {
        string n = name.Trim();
        if (n.Length is 0 or > MaxNameLength) return Loc.F("midi_roles.name_length", MaxNameLength);
        if (engine.MidiRoles().Any(r => r.Id != exceptId && string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase)))
            return Loc.F("midi_roles.name_taken", n);
        return null;
    }

    public static MidiOutputRole? Find(IAudioEngine engine, string idOrName) =>
        engine.MidiRoles().FirstOrDefault(r => r.Id == idOrName)
        ?? engine.MidiRoles().FirstOrDefault(r => string.Equals(r.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    /// <summary>A new role (one undo step); its id. Throws <see cref="ArgumentException"/> with the reason.</summary>
    public static string Create(IAudioEngine engine, string name, string? outputUid = null, string? outputName = null)
    {
        if (CheckName(engine, name) is { } why) throw new ArgumentException(why);
        string id = Guid.NewGuid().ToString("N");
        var roles = engine.MidiRoles().ToList();
        roles.Add(new MidiOutputRole(id, name.Trim(), outputUid ?? "", outputName ?? ""));
        engine.SetMidiRoles(roles);
        return id;
    }

    public static void Rename(IAudioEngine engine, string roleId, string name)
    {
        var role = engine.MidiRoles().FirstOrDefault(r => r.Id == roleId) ?? throw new ArgumentException(Loc.T("midi_roles.no_such_role"));
        if (CheckName(engine, name, roleId) is { } why) throw new ArgumentException(why);
        engine.SetMidiRoles(engine.MidiRoles().Select(r => r.Id == roleId ? r with { Name = name.Trim() } : r).ToList());
    }

    /// <summary>The External MIDI tracks that follow a role — what a rebind moves (FR-015's preview).</summary>
    public static IReadOnlyList<int> UsedBy(IAudioEngine engine, string roleId)
    {
        var ids = new List<int>();
        for (int i = 0; i < engine.TrackCount; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && engine.GetExternalMidi(ti.Id) is { } d && d.RoleId == roleId)
                ids.Add(ti.Id);
        return ids;
    }

    /// <summary>Deletes an unused role; a role tracks still follow is refused, naming them.</summary>
    public static void Delete(IAudioEngine engine, string roleId)
    {
        var users = UsedBy(engine, roleId);
        if (users.Count > 0)
            throw new ArgumentException(Loc.F("midi_roles.still_followed", string.Join(", ", users.Select(engine.GetTrackName))));
        engine.SetMidiRoles(engine.MidiRoles().Where(r => r.Id != roleId).ToList());
    }

    /// <summary>Binds the role to an output and moves its tracks there, one undo step (FR-015/016).
    /// Refused while the transport plays.</summary>
    public static void Rebind(IAudioEngine engine, string roleId, string outputUid, string outputName)
    {
        if (engine.IsPlaying) throw new InvalidOperationException(Loc.T("midi_roles.stop_to_rebind"));
        if (!engine.RebindMidiRole(roleId, outputUid, outputName)) throw new ArgumentException(Loc.T("midi_roles.no_such_role"));
    }

    /// <summary>Points an External MIDI track at a role (its channel kept).</summary>
    public static bool Assign(IAudioEngine engine, int trackId, string roleId)
    {
        var role = engine.MidiRoles().FirstOrDefault(r => r.Id == roleId);
        var cur = engine.GetExternalMidi(trackId);
        if (role is null || cur is null) return false;
        return engine.SetExternalMidi(trackId, role.OutputUid, cur.Value.Channel, role.OutputName, role.Id);
    }
}
