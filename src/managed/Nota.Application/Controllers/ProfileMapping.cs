// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The controller mapping editor's working copy of a profile (specs/110, #973): the profile's
// control entries, each paired with its JSON object, and the user's edits to them — learn one
// function, move a control that another function had, clear, revert. Saving rebuilds only the
// `controls` array of the original document, so the profile's match rules, init / release
// messages, LED feedback, `source` and anything the user wrote by hand stay exactly as they were;
// an entry the editor didn't touch is written back verbatim, and a re-learned control keeps the
// LED feedback of the physical control it is on (the light belongs to the button, not to the
// function). UI-free, so the smoke test drives it directly.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nota.Application.Controllers;

/// <summary>A function a controller can do: a knob / fader slot (0-based index), a role (play,
/// next track, …) or a command (by palette id).</summary>
public readonly record struct FunctionKey(ControlRoleKind Kind, int Index = 0, string? Command = null)
{
    public static FunctionKey Of(ControlRoleKind kind, int index = 0) => new(kind, ControlRole.IsIndexed(kind) ? index : 0);
    public static FunctionKey OfCommand(string id) => new(ControlRoleKind.Command, 0, id);

    /// <summary>The function a declaration does (an encoder's: when turned right).</summary>
    public static FunctionKey Of(ControlRole r)
        => r.Kind == ControlRoleKind.Command ? OfCommand(r.Command ?? "") : Of(r.Kind, r.Index);

    /// <summary>A two-way declaration's left half, or null.</summary>
    public static FunctionKey? LeftOf(ControlRole r)
        => r.LeftKind is { } lk ? Of(lk) : r.CommandLeft is { } cl ? OfCommand(cl) : null;

    /// <summary>The role a declaration doing this function (and <paramref name="left"/> when it
    /// turns left) carries.</summary>
    public ControlRole Role(FunctionKey? left = null)
        => Kind == ControlRoleKind.Command
            ? new ControlRole(ControlRoleKind.Command, 0, Command, left?.Kind == ControlRoleKind.Command ? left.Value.Command : null)
            : new ControlRole(Kind, Index, null, null, left is { Kind: not ControlRoleKind.Command } l ? l.Kind : null);
}

/// <summary>One control entry: its declaration (null when the loader would skip it — such an
/// entry is kept as it is) and the JSON it came from (null for one the editor wrote).</summary>
public sealed class MappingEntry
{
    public ControlDecl? Decl { get; init; }
    public JsonNode? Json { get; init; }

    public FunctionKey? Right => Decl is { } d ? FunctionKey.Of(d.Role) : null;
    public FunctionKey? Left => Decl is { } d ? FunctionKey.LeftOf(d.Role) : null;
    public bool Covers(FunctionKey k) => Right == k || Left == k;
}

public sealed class ProfileMapping
{
    private readonly JsonNode _root;
    private readonly List<MappingEntry> _original;
    private readonly List<MappingEntry> _entries;

    public ControllerProfile Profile { get; }
    public IReadOnlyList<MappingEntry> Entries => _entries;

    public ProfileMapping(ControllerProfile profile, string json)
    {
        Profile = profile;
        _root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                ?? new JsonObject();
        _original = new List<MappingEntry>();
        if (_root["controls"] is JsonArray arr)
            foreach (var node in arr)
            {
                ControlDecl? decl = null;
                if (node is JsonObject)
                    decl = ProfileLoader.ParseControl(JsonSerializer.Deserialize<JsonElement>(node.ToJsonString()));
                _original.Add(new MappingEntry { Decl = decl, Json = node });
            }
        _entries = new List<MappingEntry>(_original);
    }

    // ---- reading ----------------------------------------------------------------------------

    public MappingEntry? EntryFor(FunctionKey k) => _entries.FirstOrDefault(e => e.Covers(k));
    private MappingEntry? OriginalFor(FunctionKey k) => _original.FirstOrDefault(e => e.Covers(k));
    public ControlDecl? ControlOf(FunctionKey k) => EntryFor(k)?.Decl;

    public bool HasChanges => !_entries.SequenceEqual(_original);

    /// <summary>Whether the function is on another control than when the editor opened.</summary>
    public bool IsChanged(FunctionKey k)
    {
        var cur = EntryFor(k);
        var orig = OriginalFor(k);
        if (ReferenceEquals(cur, orig)) return false;
        if (cur?.Decl is not { } c || orig?.Decl is not { } o) return true;
        return !(Same(c, o) && c.Encoder == o.Encoder && cur.Right == orig.Right && cur.Left == orig.Left);
    }

    /// <summary>The entries (other than <paramref name="own"/>'s) that use the same physical
    /// control: what learning it for <paramref name="own"/> would take away.</summary>
    public IReadOnlyList<MappingEntry> ConflictsFor(ClassifiedControl c, FunctionKey own, FunctionKey? ownLeft = null)
        => _entries.Where(e => e.Decl is { } d && Same(d, c) && !e.Covers(own) && !(ownLeft is { } l && e.Covers(l)))
            .ToList();

    /// <summary>The entry an incoming channel message belongs to (find by touch), or null.</summary>
    public MappingEntry? Find(int status, int channel, int d1)
        => _entries.FirstOrDefault(e => e.Decl is { } d && d.Channel == channel && d.Message switch
        {
            ControlMessage.Note => status is 0x90 or 0x80 && d1 == d.Number,
            ControlMessage.PitchBend => status == 0xE0,
            ControlMessage.Cc => status == 0xB0 && d1 == d.Number,
            ControlMessage.Cc14 => status == 0xB0 && (d1 == d.Number || d1 == d.Number + 32),
            _ => false,   // NRPN: 99 / 98 / 6 / 38 are shared by every NRPN control
        });

    // ---- editing ----------------------------------------------------------------------------

    /// <summary>Put <paramref name="right"/> (and, for an encoder that does a pair, <paramref name="left"/>
    /// when it turns left) on a control. Whatever else used that control loses it — a two-way
    /// encoder both halves (spec US2.4); ask first with <see cref="ConflictsFor"/>.</summary>
    public void Assign(FunctionKey right, ClassifiedControl c, FunctionKey? left = null)
    {
        // Learning what it already is: nothing changes.
        if (EntryFor(right) is { Decl: { } cur } e && Same(cur, c) && cur.Encoder == c.Encoder
            && e.Right == right && e.Left == left)
            return;

        // The new entry takes the place of the one the function had, so a one-function edit is a
        // one-entry change in the file.
        var place = EntryFor(right) ?? (left is { } pl ? EntryFor(pl) : null);
        var marker = new MappingEntry();
        if (place is not null) _entries.Insert(_entries.IndexOf(place), marker);
        foreach (var x in ConflictsFor(c, right, left).ToList()) _entries.Remove(x);
        Release(right);
        if (left is { } l) Release(l);
        // An encoder that did a pair and now does one half as a button: the other half goes too.
        _entries.RemoveAll(x => x.Decl is { } d && Same(d, c));
        var fresh = Fresh(new ControlDecl(right.Role(left), c.Message, c.Channel, c.Number, c.Encoder, false, null));
        int at = _entries.IndexOf(marker);
        if (at >= 0) _entries[at] = fresh; else _entries.Add(fresh);
    }

    /// <summary>No control does this function any more (a two-way encoder keeps the other half).</summary>
    public void Clear(FunctionKey k) => Release(k);

    /// <summary>Back to the control the function had when the editor opened. Refused — the
    /// entries in the way returned — when that control now does something else.</summary>
    public IReadOnlyList<MappingEntry> Revert(FunctionKey k)
    {
        var orig = OriginalFor(k);
        if (orig is null) { Release(k); return Array.Empty<MappingEntry>(); }
        if (_entries.Contains(orig)) return Array.Empty<MappingEntry>();
        var inWay = _entries.Where(e => e.Decl is { } d && Same(d, orig.Decl!) && !e.Covers(k)).ToList();
        if (inWay.Count > 0) return inWay;
        Release(orig.Right!.Value);
        if (orig.Left is { } l) Release(l);
        foreach (var x in _entries.Where(e => e.Decl is { } d && Same(d, orig.Decl!)).ToList()) _entries.Remove(x);
        int at = _original.IndexOf(orig);
        _entries.Insert(Math.Min(_entries.Count, InsertPoint(at)), orig);
        return Array.Empty<MappingEntry>();
    }

    /// <summary>The pre-filled wizard's result (spec FR-016): every function in
    /// <paramref name="scope"/> is what the wizard learned, or nothing.</summary>
    public void ReplaceAll(IEnumerable<(FunctionKey Right, FunctionKey? Left, ClassifiedControl Control)> learned, IEnumerable<FunctionKey> scope)
    {
        var list = learned.ToList();
        var covered = list.SelectMany(x => x.Left is { } l ? new[] { x.Right, l } : new[] { x.Right }).ToHashSet();
        foreach (var k in scope.Where(k => !covered.Contains(k))) Release(k);
        foreach (var (r, l, c) in list) Assign(r, c, l);
    }

    // Take the function off whatever entry does it; a two-way entry keeps its other half.
    private void Release(FunctionKey k)
    {
        foreach (var e in _entries.Where(e => e.Covers(k)).ToList())
        {
            int at = _entries.IndexOf(e);
            _entries.RemoveAt(at);
            var d = e.Decl!;
            FunctionKey? other = e.Left is null ? null : e.Right == k ? e.Left : e.Right;
            if (other is { } o)
                _entries.Insert(at, Fresh(d with { Role = o.Role() }));
        }
    }

    // A new entry for a declaration: written as the wizard writes it, with the LED feedback and
    // motor flag of the original entry on the same physical control.
    private MappingEntry Fresh(ControlDecl d)
    {
        var json = ProfileWriter.ControlObject(new LearnedControl(d.Role.Kind, d.Role.Index,
            new ClassifiedControl(d.Message, d.Channel, d.Number, d.Encoder), d.Role.Command, d.Role.CommandLeft, d.Role.LeftKind));
        var donor = _original.FirstOrDefault(e => e.Decl is { } o && Same(o, d));
        if (donor?.Json is JsonObject src)
        {
            if (src["feedback"] is { } fb) json["feedback"] = fb.DeepClone();
            if (src["motorised"] is { } mo) json["motorised"] = mo.DeepClone();
            d = d with { Feedback = donor.Decl!.Feedback, Motorised = donor.Decl.Motorised };
        }
        return new MappingEntry { Decl = d, Json = json };
    }

    // Where an original entry goes back: after the entries that were before it.
    private int InsertPoint(int originalIndex)
    {
        int n = 0;
        foreach (var e in _entries)
        {
            int oi = _original.IndexOf(e);
            if (oi >= 0 && oi < originalIndex) n = _entries.IndexOf(e) + 1;
        }
        return n;
    }

    // ---- writing ----------------------------------------------------------------------------

    /// <summary>The profile document with its controls as edited; everything else verbatim.</summary>
    public string ToJson()
    {
        var root = _root.DeepClone();
        var arr = new JsonArray();
        foreach (var e in _entries) arr.Add(e.Json?.DeepClone());
        if (root is JsonObject o) o["controls"] = arr;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    // ---- physical controls --------------------------------------------------------------------

    /// <summary>Two declarations are the same physical control (research R4).</summary>
    public static bool Same(ControlDecl a, ControlDecl b) => Same(a.Message, a.Channel, a.Number, b.Message, b.Channel, b.Number);
    public static bool Same(ControlDecl a, ClassifiedControl c) => Same(a.Message, a.Channel, a.Number, c.Message, c.Channel, c.Number);

    private static bool Same(ControlMessage am, int ac, int an, ControlMessage bm, int bc, int bn)
    {
        if (ac != bc) return false;
        if (am == ControlMessage.PitchBend || bm == ControlMessage.PitchBend) return am == bm;
        if (am == ControlMessage.Note || bm == ControlMessage.Note) return am == bm && an == bn;
        if (am == ControlMessage.Nrpn || bm == ControlMessage.Nrpn) return am == bm && an == bn;
        if (am == ControlMessage.Cc14 && bm == ControlMessage.Cc) return bn == an || bn == an + 32;
        if (bm == ControlMessage.Cc14 && am == ControlMessage.Cc) return an == bn || an == bn + 32;
        return an == bn;
    }
}
