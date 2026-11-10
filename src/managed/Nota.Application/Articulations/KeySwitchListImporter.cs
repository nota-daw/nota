// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Studio One .keyswitch ("Music.KeySwitchList") → an ArticulationMap.
//
// Verified against a real file (Babylonwaves "CSSS Violins 1 Attribute.keyswitch") by comparing
// every entry with the same patch's Cubase expression map:
//
//   <Music.KeySwitchList name="…">
//     <Attributes name="Long" id="1" pitch="24" activation="note24.1|note35.1|unknown"/>
//
//   name        the articulation's name; id 0 / "---" is the empty default entry
//   id          position in the list (matches the Cubase slot order)
//   pitch       the entry's primary keyswitch, for display
//   activation  "|"-separated tokens, in the order they are sent:
//                 noteP.V   keyswitch note P, velocity V          (note24.1 = Cubase 144/24/1)
//                 noteP     keyswitch note P, velocity 127        (note35   = Cubase 144/35/127)
//                 unknown   an action this format cannot describe — in the Cubase twin of this file
//                           it is the CC 58 that selects the articulation. It is NOT recoverable here.
//
// The format carries no colours, groups, channels or switch mode.
// Studio One's separate .soundvariation format has not been seen yet and is NOT handled here.

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Nota.Application.Localization;

namespace Nota.Application.Articulations;

public static class KeySwitchListImporter
{
    public const long MaxBytes = 16 * 1024 * 1024;
    private const string RootName = "Music.KeySwitchList";

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
                if (r.NodeType == XmlNodeType.Element) return r.Name == RootName;
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
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxBytes,
    };

    private static void Import(Stream stream, string fallbackName, ArticulationImportResult res)
    {
        XDocument doc;
        try
        {
            using var r = XmlReader.Create(stream, ReaderSettings());
            doc = XDocument.Load(r);
        }
        catch (XmlException ex)
        {
            res.Error(Loc.F("articulations.import.xml_error", ex.LineNumber, ex.Message));
            return;
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != RootName)
        {
            res.Error(Loc.T("articulations.import.not_keyswitch_list"));
            return;
        }

        var map = new ArticulationMap { Name = (string?)root.Attribute("name") is { Length: > 0 } n ? n : fallbackName };
        map.Metadata["source.format"] = "keyswitch";

        var used = new HashSet<int>();
        int nextFallback = 1;
        int skippedEmpty = 0, withUnknown = 0;
        foreach (var e in root.Elements().Where(x => x.Name.LocalName == "Attributes"))   // one entry each
        {
            string name = (string?)e.Attribute("name") ?? "";
            string activation = (string?)e.Attribute("activation") ?? "";
            int.TryParse((string?)e.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fileId);

            // The empty default entry: no name worth keeping and nothing to send.
            if ((name is "" or "---") && activation.Length == 0) { skippedEmpty++; continue; }

            // Saved with the map, so language-neutral: the file's own number, not interface text.
            string label = name.Length > 0 ? name : "#" + fileId.ToString(CultureInfo.InvariantCulture);
            // The file's own id keeps re-imports stable; a missing, zero or repeated id gets the next free one.
            int id = fileId > 0 && used.Add(fileId) ? fileId : 0;
            if (id == 0) { while (!used.Add(nextFallback)) nextFallback++; id = nextFallback; }

            var art = new Articulation
            {
                Id = id,
                Name = label,
                ShortName = label.Length <= 6 ? label : label[..6],
            };
            if ((string?)e.Attribute("pitch") is { Length: > 0 } pitch) art.Metadata["source.pitch"] = pitch;
            if (activation.Length > 0) art.Metadata["source.activation"] = activation;

            bool sawUnknown = false;
            foreach (var tokenRaw in activation.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (tokenRaw.Equals("unknown", StringComparison.OrdinalIgnoreCase)) { sawUnknown = true; continue; }
                var act = ParseNoteToken(tokenRaw);
                if (act is null)
                {
                    res.Warn(Loc.F("articulations.import.unrecognised_activation", label, tokenRaw));
                    continue;
                }
                if (!act.IsValid())
                {
                    res.Warn(Loc.F("articulations.import.activation_out_of_range", label, tokenRaw));
                    continue;
                }
                art.Actions.Add(act);
            }
            if (sawUnknown) withUnknown++;
            if (art.Actions.Count == 0) res.Warn(Loc.F("articulations.import.no_usable_action", label));
            map.Articulations.Add(art);
        }

        if (skippedEmpty > 0) res.Info(Loc.Plural("articulations.import.skipped_empty", skippedEmpty));
        if (withUnknown > 0) res.Warn(Loc.Plural("articulations.import.unknown_actions", withUnknown));
        res.Info(Loc.T("articulations.import.keyswitch_no_extras"));

        if (map.Articulations.Count == 0)
        {
            res.Error(Loc.T("articulations.import.no_articulations"));
            return;
        }
        res.Map = map;
    }

    /// <summary>"note24.1" → keyswitch 24 at velocity 1; "note35" → keyswitch 35 at velocity 127.</summary>
    private static MidiAction? ParseNoteToken(string token)
    {
        if (!token.StartsWith("note", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = token[4..];
        int vel = 127;
        int dot = rest.IndexOf('.');
        if (dot >= 0)
        {
            if (!int.TryParse(rest[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out vel)) return null;
            rest = rest[..dot];
        }
        if (!int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out int pitch)) return null;
        return new MidiAction { Kind = MidiActionKind.Keyswitch, Data1 = pitch, Data2 = vel };
    }
}
