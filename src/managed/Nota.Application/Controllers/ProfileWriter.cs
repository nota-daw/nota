// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A user profile from the Controller setup wizard (specs/051 FR-030), in the profile file format
// (contracts/controller-profile.md): matched on the port's exact name (and its identity reply,
// when it sent one), the controls as the wizard learned them. Init / release messages and LED
// feedback are left for the user to add by hand.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nota.Application.Controllers;

/// <summary>One learned control: its role (index 0-based for knob / fader; the command id for a
/// command button) and what it sends.</summary>
public readonly record struct LearnedControl(ControlRoleKind Role, int Index, ClassifiedControl Control, string? Command = null, string? CommandLeft = null,
    ControlRoleKind? LeftRole = null);

public static class ProfileWriter
{
    /// <summary>A file-name-safe id for a port name ("user.my-keys-61").</summary>
    public static string IdFor(string portName)
    {
        var slug = Regex.Replace((portName ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return "user." + (slug.Length > 0 ? slug : "controller");
    }

    public static string Write(string portName, IdentityReply? identity, IReadOnlyList<LearnedControl> controls)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("format", ProfileLoader.SupportedFormat);
            w.WriteString("id", IdFor(portName));
            w.WriteString("name", portName);
            w.WriteString("protocol", "generic");
            w.WriteStartObject("match");
            w.WriteStartArray("portNames");
            w.WriteStringValue("^" + Regex.Escape(portName) + "$");
            w.WriteEndArray();
            if (identity is { } id)
            {
                w.WriteStartObject("identity");
                w.WriteString("manufacturer", Hex(id.Manufacturer));
                w.WriteString("family", Hex(id.Family));
                w.WriteString("model", Hex(id.Model));
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartArray("controls");
            foreach (var c in controls) WriteControl(w, c);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }

    /// <summary>One control entry as the profile format writes it (shared with the mapping editor,
    /// specs/110, which writes only the entries it changed).</summary>
    public static void WriteControl(Utf8JsonWriter w, LearnedControl c)
    {
        w.WriteStartObject();
        w.WriteString("role", RoleName(c.Role));
        if (ControlRole.IsIndexed(c.Role)) w.WriteNumber("index", c.Index + 1);
        if (c.Role == ControlRoleKind.Command && c.Command is { Length: > 0 } cmd) w.WriteString("command", cmd);
        if (c.Role == ControlRoleKind.Command && c.CommandLeft is { Length: > 0 } cmdLeft) w.WriteString("commandLeft", cmdLeft);
        if (c.LeftRole is { } leftRole) w.WriteString("roleLeft", RoleName(leftRole));
        w.WriteString("message", c.Control.Message switch
        {
            ControlMessage.Cc14 => "cc14", ControlMessage.Nrpn => "nrpn", ControlMessage.Note => "note",
            ControlMessage.PitchBend => "pitchbend", _ => "cc",
        });
        w.WriteNumber("channel", c.Control.Channel + 1);
        if (c.Control.Message != ControlMessage.PitchBend) w.WriteNumber("number", c.Control.Number);
        if (c.Control.Encoder != EncoderMode.Absolute)
            w.WriteString("encoder", c.Control.Encoder switch
            {
                EncoderMode.RelativeTwos => "relative-twos", EncoderMode.RelativeSignBit => "relative-signbit", _ => "relative-offset",
            });
        w.WriteEndObject();
    }

    /// <summary>One control entry as a JSON object.</summary>
    public static System.Text.Json.Nodes.JsonObject ControlObject(LearnedControl c)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) WriteControl(w, c);
        return System.Text.Json.Nodes.JsonNode.Parse(ms.ToArray())!.AsObject();
    }

    private static string Hex(byte[] b) => string.Join(' ', b.Select(x => x.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

    public static string RoleName(ControlRoleKind k) => k switch
    {
        ControlRoleKind.Knob => "knob", ControlRoleKind.Fader => "fader", ControlRoleKind.Button => "button", ControlRoleKind.Pad => "pad",
        ControlRoleKind.MasterFader => "master-fader", ControlRoleKind.Play => "play", ControlRoleKind.Stop => "stop",
        ControlRoleKind.Record => "record", ControlRoleKind.Loop => "loop", ControlRoleKind.Rewind => "rewind", ControlRoleKind.Forward => "forward",
        ControlRoleKind.PageNext => "page-next", ControlRoleKind.PagePrev => "page-prev", ControlRoleKind.BankNext => "bank-next",
        ControlRoleKind.BankPrev => "bank-prev", ControlRoleKind.TrackNext => "track-next", ControlRoleKind.TrackPrev => "track-prev",
        ControlRoleKind.ModeDevice => "mode-device", ControlRoleKind.ModeVolume => "mode-volume", ControlRoleKind.ModePan => "mode-pan",
        ControlRoleKind.ModeSend => "mode-send", ControlRoleKind.Command => "command", _ => "jog",
    };
}
