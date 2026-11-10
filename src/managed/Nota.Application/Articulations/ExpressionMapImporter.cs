// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Cubase .expressionmap → an ArticulationMap.
//
// The format handled here is the XML "InstrumentMap" document, verified against a real file
// (Art Conductor / Babylonwaves "CSSS Violins 1 A"):
//   InstrumentMap
//     string name="name"                      the map's name
//     member "slotvisuals"                    a list of USlotVisuals (a copy of each slot's own)
//     member "slots"  → list of PSoundSlot    one articulation each:
//        obj PSlotThruTrigger "remote"        status / data1 — the remote key (not an articulation output)
//        obj PSlotMidiAction  "action"
//           member "midiMessages" → POutputEvent { status, data1, data2 }   ordered output
//           channel (-1 = the note's own channel), key / key2 / controller1* (editor mirrors)
//        member "sv"   → USlotVisuals { text (short name), description, group, articulationtype,
//                                       symbol, displaytype }
//        member "name" → string "s"           the articulation's name
//        int    "color"                       a Cubase palette INDEX, not an RGB value
//
// Not verifiable from that file, so reported rather than guessed: switch modes (the file carries
// no latching / momentary flag), non-zero groups, program-change and bank-change events, the
// slot-level channel when it is not -1, and the RGB behind a colour index.

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Nota.Application.Localization;

namespace Nota.Application.Articulations;

public static class ExpressionMapImporter
{
    /// <summary>Largest file accepted (a real map is ~100 KB).</summary>
    public const long MaxBytes = 16 * 1024 * 1024;

    /// <summary>True when <paramref name="path"/> looks like a Cubase expression map by its content,
    /// not its extension.</summary>
    public static bool Sniff(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.Length <= MaxBytes && Sniff(fs);
        }
        catch { return false; }
    }

    public static bool Sniff(Stream s)
    {
        try
        {
            using var r = XmlReader.Create(s, ReaderSettings());
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element) return r.Name == "InstrumentMap";
            return false;
        }
        catch { return false; }
    }

    public static ArticulationImportResult ImportFile(string path)
    {
        var res = new ArticulationImportResult();
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) { res.Error(Loc.F("articulations.import.file_not_found", path)); return res; }
            if (fi.Length > MaxBytes) { res.Error(Loc.F("articulations.import.too_large", fi.Length)); return res; }
            using var fs = fi.OpenRead();
            Import(fs, Path.GetFileNameWithoutExtension(path), res);
            if (res.Map is not null) res.Map.Metadata["source.file"] = fi.Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            res.Map = null;
            res.Error(Loc.F("articulations.import.could_not_read", ex.Message));
        }
        return res;
    }

    public static ArticulationImportResult Import(Stream stream, string fallbackName)
    {
        var res = new ArticulationImportResult();
        Import(stream, fallbackName, res);
        return res;
    }

    private static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,   // no entity expansion from a hostile file
        XmlResolver = null,
        MaxCharactersInDocument = MaxBytes,
        IgnoreComments = false,
    };

    /// <summary>The name of a slot the file leaves unnamed: its 1-based position. Saved with the map,
    /// so it is language-neutral, never interface text.</summary>
    private static string UnnamedSlot(int index) => "#" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static void Import(Stream stream, string fallbackName, ArticulationImportResult res)
    {
        XDocument doc;
        try
        {
            using var r = XmlReader.Create(stream, ReaderSettings());
            doc = XDocument.Load(r, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            res.Error(Loc.F("articulations.import.xml_error", ex.LineNumber, ex.Message));
            return;
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "InstrumentMap")
        {
            res.Error(Loc.T("articulations.import.not_expression_map"));
            return;
        }

        var map = new ArticulationMap { Name = NamedString(root, "name") ?? fallbackName };
        if (string.IsNullOrWhiteSpace(map.Name)) map.Name = fallbackName;
        map.Metadata["source.format"] = "expressionmap";

        // Header comments (maker, content / engine version) are kept as provenance.
        var comments = doc.Nodes().OfType<XComment>().Select(c => c.Value.Trim()).Where(c => c.Length > 0).ToList();
        if (comments.Count > 0) map.Metadata["source.comments"] = string.Join("\n", comments);

        var slotsMember = Member(root, "slots");
        var slots = slotsMember?.Element("list")?.Elements("obj").Where(o => (string?)o.Attribute("class") == "PSoundSlot").ToList();
        if (slots is null || slots.Count == 0)
        {
            res.Error(Loc.T("articulations.import.no_sound_slots"));
            return;
        }

        int nextId = 1;
        int skippedEmpty = 0;
        var groupsSeen = new SortedSet<int>();
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            string slotName = NamedString(Member(slot, "name"), "s") ?? "";
            var vis = Member(slot, "sv")?.Element("list")?.Elements("obj").FirstOrDefault(o => (string?)o.Attribute("class") == "USlotVisuals");
            var action = slot.Elements("obj").FirstOrDefault(o => (string?)o.Attribute("class") == "PSlotMidiAction");
            var events = Member(action, "midiMessages")?.Element("list")?.Elements("obj")
                .Where(o => (string?)o.Attribute("class") == "POutputEvent").ToList() ?? new List<XElement>();

            // Cubase's first slot is the empty "no articulation" state: no name, no output.
            if (events.Count == 0 && (slotName is "" or "---"))
            {
                skippedEmpty++;
                continue;
            }

            string label = slotName.Length > 0 ? slotName : UnnamedSlot(i);
            var art = new Articulation
            {
                Id = nextId++,
                Name = slotName.Length > 0 ? slotName : (NamedString(vis, "description") ?? label),
                ShortName = NamedString(vis, "text") ?? "",
            };
            if (art.ShortName.Length == 0) art.ShortName = art.Name.Length <= 6 ? art.Name : art.Name[..6];
            if (NamedString(vis, "description") is { Length: > 0 } desc && desc != art.Name) art.Metadata["description"] = desc;
            if ((string?)slot.Attribute("ID") is { } sid) art.Metadata["source.id"] = sid;

            if (NamedInt(slot, "color") is { } colorIdx) art.SourceColorIndex = colorIdx;

            int group = NamedInt(vis, "group") ?? 0;
            if (group > 0) { art.GroupId = group; groupsSeen.Add(group); }
            if (NamedInt(vis, "articulationtype") is { } at && at != 0)
                art.Metadata["source.articulationtype"] = at.ToString(CultureInfo.InvariantCulture);

            // Slot-level channel: -1 = the note's own. Anything else is stored 0-based or 1-based
            // depending on the writer, which one file cannot settle — keep it, say so, do not apply it.
            int slotChannel = NamedInt(action, "channel") ?? -1;
            if (slotChannel != -1)
            {
                art.Metadata["source.channel"] = slotChannel.ToString(CultureInfo.InvariantCulture);
                res.Warn(Loc.F("articulations.import.slot_channel_kept", label, slotChannel));
            }

            foreach (var ev in events)
            {
                int? status = NamedInt(ev, "status"), d1 = NamedInt(ev, "data1"), d2 = NamedInt(ev, "data2");
                if (status is null || d1 is null)
                {
                    res.Warn(Loc.F("articulations.import.message_without_data", label));
                    continue;
                }
                var act = ToAction(status.Value, d1.Value, d2 ?? 0, out string? why);
                if (act is null)
                {
                    res.Warn(Loc.F("articulations.import.unsupported_message", label, status, d1, d2, why));
                    art.Metadata["unsupported.midi"] = (art.Metadata.GetValueOrDefault("unsupported.midi") ?? "") + $"{status}:{d1}:{d2};";
                    continue;
                }
                if (!act.IsValid())
                {
                    res.Warn(Loc.F("articulations.import.message_out_of_range", label, status, d1, d2));
                    continue;
                }
                art.Actions.Add(act);
            }
            if (art.Actions.Count == 0) res.Warn(Loc.F("articulations.import.no_usable_action", label));
            map.Articulations.Add(art);
        }

        // The file numbers groups but never names them: the name stays empty (saved data is
        // language-neutral) and the interface shows a numbered label for it.
        foreach (int g in groupsSeen)
            map.Groups.Add(new ArticulationGroup { Id = g, Name = "" });
        if (groupsSeen.Count > 0)
            res.Warn(Loc.T("articulations.import.groups_unnamed"));

        if (skippedEmpty > 0)
            res.Info(Loc.Plural("articulations.import.skipped_empty", skippedEmpty));
        res.Info(Loc.T("articulations.import.switch_mode_not_stored"));
        if (map.Articulations.Any(a => a.SourceColorIndex is not null))
            res.Info(Loc.T("articulations.import.colour_indices"));

        if (map.Articulations.Count == 0)
        {
            res.Error(Loc.T("articulations.import.no_articulations"));
            return;
        }
        res.Map = map;
    }

    /// <summary>Cubase POutputEvent → a model action. The status byte's low nibble is the channel
    /// (0 = no explicit channel, which in this format means the note's own).</summary>
    private static MidiAction? ToAction(int status, int d1, int d2, out string? why)
    {
        why = null;
        if (status is < 128 or > 255) { why = Loc.T("articulations.import.reason_status_out_of_range"); return null; }
        int kind = status & 0xF0, ch = status & 0x0F;
        int? channel = ch == 0 ? null : ch + 1;   // a non-zero nibble names the channel explicitly (1-based)
        switch (kind)
        {
            case 0x90: return new MidiAction { Kind = MidiActionKind.Keyswitch, Channel = channel, Data1 = d1, Data2 = d2 };
            case 0xB0:
                return d1 switch
                {
                    0 => new MidiAction { Kind = MidiActionKind.BankSelectMsb, Channel = channel, Data1 = 0, Data2 = d2 },
                    32 => new MidiAction { Kind = MidiActionKind.BankSelectLsb, Channel = channel, Data1 = 32, Data2 = d2 },
                    _ => new MidiAction { Kind = MidiActionKind.ControlChange, Channel = channel, Data1 = d1, Data2 = d2 },
                };
            case 0xC0: return new MidiAction { Kind = MidiActionKind.ProgramChange, Channel = channel, Data1 = d1 };
            default: why = Loc.F("articulations.import.reason_message_type", kind.ToString("X2", CultureInfo.InvariantCulture)); return null;
        }
    }

    // --- the format's "typed child with a name attribute" idiom ---------------------------------

    private static XElement? Member(XElement? parent, string name) =>
        parent?.Elements("member").FirstOrDefault(m => (string?)m.Attribute("name") == name);

    private static string? NamedString(XElement? parent, string name) =>
        parent?.Elements("string").FirstOrDefault(e => (string?)e.Attribute("name") == name)?.Attribute("value")?.Value;

    private static int? NamedInt(XElement? parent, string name)
    {
        var v = parent?.Elements("int").FirstOrDefault(e => (string?)e.Attribute("name") == name)?.Attribute("value")?.Value;
        return int.TryParse(v?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;
    }
}
