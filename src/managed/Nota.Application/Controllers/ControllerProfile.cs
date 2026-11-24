// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Controller profiles (specs/051): one JSON file per controller that tells Nota how to
// recognise it (port name, Identity Reply) and what each physical control is (a role). The
// format is a user-facing contract — specs/051-controller-auto-mapping/contracts/
// controller-profile.md — so the loader is forgiving: a bad control is skipped with a warning,
// a bad file is skipped with a reason, and nothing is ever fatal. Shipped profiles must name
// the manufacturer document they were built from (`source`), which keeps the data AGPL-clean.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Nota.Application.Localization;

namespace Nota.Application.Controllers;

public enum ControllerProtocol { Generic, Mcu, Hui }
public enum ProfileOrigin { Shipped, User }
public enum ControlMessage { Cc, Cc14, Nrpn, Note, PitchBend }
public enum EncoderMode { Absolute, RelativeTwos, RelativeSignBit, RelativeOffset }

public enum ControlRoleKind
{
    Knob, Fader, Button, Pad, MasterFader,
    Play, Stop, Record, Loop, Rewind, Forward,
    PageNext, PagePrev, BankNext, BankPrev, TrackNext, TrackPrev,
    ModeDevice, ModeVolume, ModePan, ModeSend,
    Jog,
    // A button that runs one of Nota's commands (the command palette's ids: "edit.undo",
    // "transport.metronome" …) — for the extra buttons controllers carry beyond transport.
    Command,
}

/// <summary>A role and, for knob / fader / button / pad, its 0-based index; for
/// <see cref="ControlRoleKind.Command"/> the command id — a button's, or an encoder's when it turns
/// right — and <see cref="CommandLeft"/>, an encoder's when it turns left (two-way, FR-030b).</summary>
public readonly record struct ControlRole(ControlRoleKind Kind, int Index = 0, string? Command = null, string? CommandLeft = null,
    ControlRoleKind? LeftKind = null)
{
    /// <summary>A pair one control can do both halves of — an encoder turned right / left, or two
    /// buttons (FR-030c): next / previous track, bank, page; forward / rewind.</summary>
    public static ControlRoleKind? PairOf(ControlRoleKind k) => k switch
    {
        ControlRoleKind.TrackNext => ControlRoleKind.TrackPrev, ControlRoleKind.BankNext => ControlRoleKind.BankPrev,
        ControlRoleKind.PageNext => ControlRoleKind.PagePrev, ControlRoleKind.Forward => ControlRoleKind.Rewind,
        _ => null,
    };

    /// <summary>A two-way control: <see cref="LeftKind"/> or <see cref="CommandLeft"/> runs when it turns left.</summary>
    public bool TwoWay => LeftKind is not null || CommandLeft is not null;

    public static bool IsIndexed(ControlRoleKind k) => k is ControlRoleKind.Knob or ControlRoleKind.Fader
        or ControlRoleKind.Button or ControlRoleKind.Pad;

    /// <summary>Pads keep playing notes and a generic button has no built-in action in format 1:
    /// neither is claimed, both stay free for MIDI Learn.</summary>
    public bool IsClaimed => Kind is not (ControlRoleKind.Pad or ControlRoleKind.Button);
}

/// <summary>Feedback a control accepts: fixed on / off messages (button LEDs) or a value
/// template (rings, motor faders) where <c>vv</c> is the 7-bit value and <c>ll hh</c> the
/// 14-bit low / high bytes.</summary>
public sealed record ControlFeedback(byte[]? On, byte[]? Off, string? ValueTemplate)
{
    /// <summary>A value template ("B0 10 vv", "E0 ll hh") with a 14-bit value filled in: vv the
    /// 7-bit value, ll / hh the 14-bit low and high bytes; null when it isn't a MIDI message in hex.</summary>
    public static byte[]? Fill(string template, int v14)
    {
        var parts = template.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "vv": bytes[i] = (byte)((v14 >> 7) & 0x7F); break;
                case "ll": bytes[i] = (byte)(v14 & 0x7F); break;
                case "hh": bytes[i] = (byte)((v14 >> 7) & 0x7F); break;
                default:
                    if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i])) return null;
                    break;
            }
        }
        return bytes.Length > 0 && (bytes[0] & 0x80) != 0 ? bytes : null;
    }
}

public sealed record ControlDecl(
    ControlRole Role,
    ControlMessage Message,
    int Channel,          // 0..15
    int Number,           // CC / note 0..127, NRPN 0..16383, 0 for pitch bend
    EncoderMode Encoder,
    bool Motorised,
    ControlFeedback? Feedback);

/// <summary>Identity Reply fields to match (manufacturer 1 or 3 bytes; family / model 2 each).</summary>
public sealed record IdentityMatch(byte[] Manufacturer, byte[]? Family, byte[]? Model)
{
    public int Specificity => 1 + (Family is null ? 0 : 1) + (Model is null ? 0 : 1);
}

public sealed record ControllerProfile(
    string Id,
    string Name,
    ControllerProtocol Protocol,
    string? Source,
    IReadOnlyList<Regex> PortNames,
    IdentityMatch? Identity,
    IReadOnlyList<byte[]> Init,
    IReadOnlyList<byte[]> Release,
    bool Pickup,
    bool ClaimAll,
    IReadOnlyList<ControlDecl> Controls,
    ProfileOrigin Origin,
    string FileName);

public static class ProfileLoader
{
    public const int SupportedFormat = 1;
    private static readonly Regex IdPattern = new("^[a-z0-9.-]+$", RegexOptions.CultureInvariant);

    /// <summary>Parse one profile file. Returns the profile (null when the file as a whole is
    /// unusable) and every warning, each prefixed with the file name.</summary>
    public static (ControllerProfile? Profile, IReadOnlyList<string> Warnings) Load(string json, string fileName, ProfileOrigin origin)
    {
        var warnings = new List<string>();
        void Warn(string msg) => warnings.Add(Loc.F("controllers.profile.warning", fileName, msg));
        (ControllerProfile?, IReadOnlyList<string>) Fail(string msg) { Warn(msg); return (null, warnings); }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { return Fail(Loc.F("controllers.profile.bad_json", ex.Message)); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail(Loc.T("controllers.profile.not_object"));

            if (!root.TryGetProperty("format", out var fmt) || fmt.ValueKind != JsonValueKind.Number || !fmt.TryGetInt32(out int format))
                return Fail(Loc.T("controllers.profile.no_format"));
            if (format > SupportedFormat) return Fail(Loc.F("controllers.profile.newer_format", format));
            if (format < 1) return Fail(Loc.F("controllers.profile.unknown_format", format));

            string? id = Str(root, "id");
            if (id is null || !IdPattern.IsMatch(id)) return Fail(Loc.T("controllers.profile.bad_id"));
            string? name = Str(root, "name");
            if (string.IsNullOrWhiteSpace(name)) return Fail(Loc.T("controllers.profile.no_name"));
            string? source = Str(root, "source");
            if (origin == ProfileOrigin.Shipped && string.IsNullOrWhiteSpace(source))
                return Fail(Loc.T("controllers.profile.no_source"));

            var protocol = (Str(root, "protocol") ?? "generic") switch
            {
                "generic" => ControllerProtocol.Generic,
                "mcu" => ControllerProtocol.Mcu,
                "hui" => ControllerProtocol.Hui,
                var p => (ControllerProtocol?)null,
            };
            if (protocol is null) return Fail(Loc.F("controllers.profile.bad_protocol", Str(root, "protocol")));

            // match
            var portNames = new List<Regex>();
            IdentityMatch? identity = null;
            if (root.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.Object)
            {
                if (match.TryGetProperty("portNames", out var pn) && pn.ValueKind == JsonValueKind.Array)
                    foreach (var e in pn.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.String) { Warn(Loc.T("controllers.profile.port_not_string")); continue; }
                        try { portNames.Add(new Regex(e.GetString()!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))); }
                        catch (ArgumentException) { Warn(Loc.F("controllers.profile.bad_regex", e.GetString())); }
                    }
                if (match.TryGetProperty("identity", out var idn) && idn.ValueKind == JsonValueKind.Object)
                {
                    var mfr = Hex(Str(idn, "manufacturer"));
                    var fam = Hex(Str(idn, "family"));
                    var mdl = Hex(Str(idn, "model"));
                    if (mfr is null || (mfr.Length != 1 && mfr.Length != 3)) Warn(Loc.T("controllers.profile.bad_manufacturer"));
                    else if ((Str(idn, "family") is not null && fam?.Length != 2) || (Str(idn, "model") is not null && mdl?.Length != 2))
                        Warn(Loc.T("controllers.profile.bad_family"));
                    else identity = new IdentityMatch(mfr, fam, mdl);
                }
            }
            if (portNames.Count == 0 && identity is null) return Fail(Loc.T("controllers.profile.no_match"));

            var init = Messages(root, "init", Warn);
            var release = Messages(root, "release", Warn);
            bool pickup = !root.TryGetProperty("pickup", out var pk) || pk.ValueKind != JsonValueKind.False;
            // A port that carries only control messages (a keyboard's DAW port): claim all of it,
            // so an undeclared button or touch note never plays a note.
            bool claimAll = root.TryGetProperty("claimAll", out var ca) && ca.ValueKind == JsonValueKind.True;

            var controls = new List<ControlDecl>();
            if (protocol == ControllerProtocol.Generic && root.TryGetProperty("controls", out var cs) && cs.ValueKind == JsonValueKind.Array)
            {
                var seen = new HashSet<(ControlMessage, int, int)>();
                int i = 0;
                foreach (var c in cs.EnumerateArray())
                {
                    i++;
                    var decl = Control(c, i, Warn);
                    if (decl is null) continue;
                    if (!seen.Add((decl.Message, decl.Channel, decl.Number)))
                    {
                        Warn(Loc.F("controllers.profile.duplicate", i));
                        continue;
                    }
                    controls.Add(decl);
                }
            }

            return (new ControllerProfile(id, name!, protocol.Value, source, portNames, identity, init, release, pickup,
                claimAll, controls, origin, fileName), warnings);
        }
    }

    /// <summary>One control entry on its own (the mapping editor pairs each declaration with its
    /// JSON, specs/110); null when the loader would skip it.</summary>
    public static ControlDecl? ParseControl(JsonElement c) => Control(c, 0, _ => { });

    private static ControlDecl? Control(JsonElement c, int i, Action<string> warn)
    {
        if (c.ValueKind != JsonValueKind.Object) { warn(Loc.F("controllers.profile.control_not_object", i)); return null; }
        string? roleName = Str(c, "role");
        ControlRoleKind? kind = RoleKind(roleName);
        if (kind is null) { warn(Loc.F("controllers.profile.bad_role", i, roleName)); return null; }
        // A two-way button role: an encoder that goes to the next track turned right, the previous left.
        ControlRoleKind? leftKind = null;
        if (Str(c, "roleLeft") is { } leftName)
        {
            leftKind = RoleKind(leftName);
            if (leftKind is null || ControlRole.PairOf(kind.Value) != leftKind)
            { warn(Loc.F("controllers.profile.bad_role_left", i, leftName, roleName)); leftKind = null; }
        }
        return Rest(c, i, warn, kind.Value, leftKind, roleName);
    }

    private static ControlRoleKind? RoleKind(string? roleName) => roleName switch
        {
            "knob" => ControlRoleKind.Knob, "fader" => ControlRoleKind.Fader, "button" => ControlRoleKind.Button,
            "pad" => ControlRoleKind.Pad, "master-fader" => ControlRoleKind.MasterFader,
            "play" => ControlRoleKind.Play, "stop" => ControlRoleKind.Stop, "record" => ControlRoleKind.Record,
            "loop" => ControlRoleKind.Loop, "rewind" => ControlRoleKind.Rewind, "forward" => ControlRoleKind.Forward,
            "page-next" => ControlRoleKind.PageNext, "page-prev" => ControlRoleKind.PagePrev,
            "bank-next" => ControlRoleKind.BankNext, "bank-prev" => ControlRoleKind.BankPrev,
            "track-next" => ControlRoleKind.TrackNext, "track-prev" => ControlRoleKind.TrackPrev,
            "mode-device" => ControlRoleKind.ModeDevice, "mode-volume" => ControlRoleKind.ModeVolume,
            "mode-pan" => ControlRoleKind.ModePan, "mode-send" => ControlRoleKind.ModeSend,
            "jog" => ControlRoleKind.Jog, "command" => ControlRoleKind.Command,
            _ => null,
        };

    private static ControlDecl? Rest(JsonElement c, int i, Action<string> warn, ControlRoleKind kindValue, ControlRoleKind? leftKind, string? roleName)
    {
        ControlRoleKind? kind = kindValue;

        int index = 0;
        if (ControlRole.IsIndexed(kind.Value))
        {
            if (!c.TryGetProperty("index", out var ix) || ix.ValueKind != JsonValueKind.Number || !ix.TryGetInt32(out int n) || n < 1 || n > 128)
            { warn(Loc.F("controllers.profile.no_index", i, roleName)); return null; }
            index = n - 1;
        }
        string? command = null, commandLeft = null;
        if (kind == ControlRoleKind.Command)
        {
            command = Str(c, "command");
            commandLeft = Str(c, "commandLeft") is { Length: > 0 } left ? left : null;
            if (string.IsNullOrWhiteSpace(command)) { warn(Loc.F("controllers.profile.no_command", i)); return null; }
        }

        ControlMessage? msg = Str(c, "message") switch
        {
            "cc" => ControlMessage.Cc, "cc14" => ControlMessage.Cc14, "nrpn" => ControlMessage.Nrpn,
            "note" => ControlMessage.Note, "pitchbend" => ControlMessage.PitchBend, _ => null,
        };
        if (msg is null) { warn(Loc.F("controllers.profile.bad_message", i, Str(c, "message"))); return null; }

        if (!c.TryGetProperty("channel", out var chEl) || chEl.ValueKind != JsonValueKind.Number || !chEl.TryGetInt32(out int channel) || channel < 1 || channel > 16)
        { warn(Loc.F("controllers.profile.bad_channel", i)); return null; }

        int number = 0;
        if (msg != ControlMessage.PitchBend)
        {
            int max = msg switch { ControlMessage.Nrpn => 16383, ControlMessage.Cc14 => 31, _ => 127 };
            if (!c.TryGetProperty("number", out var nEl) || nEl.ValueKind != JsonValueKind.Number || !nEl.TryGetInt32(out number) || number < 0 || number > max)
            { warn(Loc.F("controllers.profile.bad_number", i, max)); return null; }
        }

        EncoderMode? enc = (Str(c, "encoder") ?? "absolute") switch
        {
            "absolute" => EncoderMode.Absolute, "relative-twos" => EncoderMode.RelativeTwos,
            "relative-signbit" => EncoderMode.RelativeSignBit, "relative-offset" => EncoderMode.RelativeOffset, _ => null,
        };
        if (enc is null) { warn(Loc.F("controllers.profile.bad_encoder", i, Str(c, "encoder"))); return null; }

        bool motorised = c.TryGetProperty("motorised", out var mo) && mo.ValueKind == JsonValueKind.True;

        ControlFeedback? fb = null;
        if (c.TryGetProperty("feedback", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            byte[]? on = Hex(Str(f, "on")), off = Hex(Str(f, "off"));
            string? value = Str(f, "value");
            if ((Str(f, "on") is not null && on is null) || (Str(f, "off") is not null && off is null))
                warn(Loc.F("controllers.profile.bad_feedback", i));
            else fb = new ControlFeedback(on, off, value);
        }

        return new ControlDecl(new ControlRole(kind.Value, index, command, commandLeft, leftKind), msg.Value, channel - 1, number, enc.Value, motorised, fb);
    }

    private static List<byte[]> Messages(JsonElement root, string prop, Action<string> warn)
    {
        var list = new List<byte[]>();
        if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            var bytes = e.ValueKind == JsonValueKind.String ? Hex(e.GetString()) : null;
            if (bytes is null || bytes.Length == 0 || (bytes[0] & 0x80) == 0) { warn(Loc.F("controllers.profile.bad_hex", prop, e)); continue; }
            list.Add(bytes);
        }
        return list;
    }

    private static string? Str(JsonElement o, string prop)
        => o.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>"F0 7E 7F" (spaces optional) → bytes; null when not hex pairs.</summary>
    public static byte[]? Hex(string? s)
    {
        if (s is null) return null;
        var t = s.Replace(" ", "").Replace(",", "");
        if (t.Length == 0 || t.Length % 2 != 0) return null;
        var b = new byte[t.Length / 2];
        for (int i = 0; i < b.Length; i++)
            if (!byte.TryParse(t.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b[i])) return null;
        return b;
    }
}
