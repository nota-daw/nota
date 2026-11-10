// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota's own articulation map file (specs/085, contracts/nota-articulation-map-file.md): the same
// JSON in the library folder (*.notamap), in an export, and embedded in a project's track. Writing
// is plain; reading repairs what it can deterministically and says what it changed — a broken map
// must never stop a project from loading, and must never reach the engine broken.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nota.Application.Localization;

namespace Nota.Application.Articulations;

public static class ArticulationMapFile
{
    public const string Format = "nota-articulation-map";
    public const string Extension = ".notamap";
    public const long MaxBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions ReadOpts = new() { MaxDepth = 64, CommentHandling = JsonCommentHandling.Skip };

    // ---- write ------------------------------------------------------------------------------

    public static string Write(ArticulationMap map) => ToJson(map).ToJsonString(WriteOpts);

    public static JsonObject ToJson(ArticulationMap map)
    {
        var o = new JsonObject
        {
            ["format"] = Format,
            ["version"] = ArticulationMap.CurrentVersion,
            ["id"] = map.Id,
            ["name"] = map.Name,
            ["instrumentName"] = map.InstrumentName,
            ["description"] = map.Description,
            ["defaultArticulationId"] = map.DefaultArticulationId,
            ["groups"] = new JsonArray(map.Groups.Select(g => (JsonNode)new JsonObject { ["id"] = g.Id, ["name"] = g.Name }).ToArray()),
            ["articulations"] = new JsonArray(map.Articulations.Select(a => (JsonNode)ToJson(a)).ToArray()),
            ["metadata"] = Dict(map.Metadata),
        };
        return o;
    }

    private static JsonObject ToJson(Articulation a) => new()
    {
        ["id"] = a.Id,
        ["name"] = a.Name,
        ["shortName"] = a.ShortName,
        ["colorArgb"] = a.ColorArgb,
        ["sourceColorIndex"] = a.SourceColorIndex,
        ["groupId"] = a.GroupId,
        ["trigger"] = a.Trigger == TriggerMode.Momentary ? "momentary" : "latching",
        ["actions"] = new JsonArray(a.Actions.Select(x => (JsonNode)new JsonObject
        {
            ["kind"] = KindName(x.Kind),
            ["channel"] = x.Channel,
            ["data1"] = x.Data1,
            ["data2"] = x.Data2,
        }).ToArray()),
        ["noteOffsetMs"] = a.NoteOffsetMs,
        ["triggerLeadMs"] = a.TriggerLeadMs,
        ["enabled"] = a.Enabled,
        ["metadata"] = Dict(a.Metadata),
    };

    private static JsonObject Dict(Dictionary<string, string> d)
    {
        var o = new JsonObject();
        foreach (var (k, v) in d.OrderBy(p => p.Key, StringComparer.Ordinal)) o[k] = v;
        return o;
    }

    private static string KindName(MidiActionKind k) => k switch
    {
        MidiActionKind.Keyswitch => "keyswitch",
        MidiActionKind.ControlChange => "controlChange",
        MidiActionKind.ProgramChange => "programChange",
        MidiActionKind.BankSelectMsb => "bankSelectMsb",
        _ => "bankSelectLsb",
    };

    private static MidiActionKind? KindOf(string? s) => s switch
    {
        "keyswitch" => MidiActionKind.Keyswitch,
        "controlChange" => MidiActionKind.ControlChange,
        "programChange" => MidiActionKind.ProgramChange,
        "bankSelectMsb" => MidiActionKind.BankSelectMsb,
        "bankSelectLsb" => MidiActionKind.BankSelectLsb,
        _ => null,
    };

    // ---- read -------------------------------------------------------------------------------

    /// <summary>True when <paramref name="path"/> is a Nota map by its content.</summary>
    public static bool Sniff(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length > MaxBytes) return false;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path), ReadOpts);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String && f.GetString() == Format;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public static ArticulationImportResult ReadFile(string path)
    {
        var res = new ArticulationImportResult();
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) { res.Error(Loc.F("articulations.import.file_not_found", path)); return res; }
            if (fi.Length > MaxBytes) { res.Error(Loc.F("articulations.import.too_large", fi.Length)); return res; }
            Read(Encoding.UTF8.GetString(File.ReadAllBytes(path)), Path.GetFileNameWithoutExtension(path), res);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            res.Map = null;
            res.Error(Loc.F("articulations.import.could_not_read", ex.Message));
        }
        return res;
    }

    public static ArticulationImportResult Read(string json, string fallbackName)
    {
        var res = new ArticulationImportResult();
        Read(json, fallbackName, res);
        return res;
    }

    private static void Read(string json, string fallbackName, ArticulationImportResult res)
    {
        if (json.Length > MaxBytes) { res.Error(Loc.F("articulations.import.too_large", (long)json.Length)); return; }
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: ReadOpts); }
        catch (JsonException ex) { res.Error(Loc.F("articulations.file.not_json", ex.Message)); return; }
        if (root is not JsonObject o || Str(o, "format") != Format)
        {
            res.Error(Loc.T("articulations.file.wrong_format"));
            return;
        }
        int version = Int(o, "version") ?? 1;
        if (version > ArticulationMap.CurrentVersion)
        {
            res.Error(Loc.F("articulations.file.newer_version", version, ArticulationMap.CurrentVersion));
            return;
        }

        var map = new ArticulationMap
        {
            Id = Str(o, "id") is { Length: > 0 } mapId ? mapId : Guid.NewGuid().ToString("N"),
            InstrumentName = Str(o, "instrumentName") ?? "",
            Description = Str(o, "description"),
            Metadata = Strings(o["metadata"]),
        };
        // Saved data stays language-neutral: a nameless map is named after its file, else its id.
        map.Name = Str(o, "name") is { Length: > 0 } nm ? nm : fallbackName.Length > 0 ? fallbackName : "#" + map.Id;

        // Groups: ids > 0 and unique; anything else is dropped (its articulations become ungrouped).
        var groupIds = new HashSet<int>();
        bool droppedGroup = false;
        foreach (var g in Objects(o["groups"]))
        {
            if (Int(g, "id") is int gid && gid > 0 && groupIds.Add(gid)) map.Groups.Add(new ArticulationGroup { Id = gid, Name = Str(g, "name") ?? "" });
            else droppedGroup = true;
        }
        if (droppedGroup) res.Warn(Loc.T("articulations.file.group_dropped"));

        // Articulations: valid unique ids kept as written (notes point at them); a missing or
        // repeated one gets the next free id, in file order, and the change is reported.
        var src = Objects(o["articulations"]).ToList();
        var taken = new HashSet<int>();
        var wanted = src.Select(a => Int(a, "id") is int i && i > 0 && taken.Add(i) ? i : 0).ToList();
        int next = taken.Count == 0 ? 1 : taken.Max() + 1;
        for (int k = 0; k < src.Count; k++)
        {
            var a = src[k];
            int id = wanted[k];
            string name = Str(a, "name") ?? "";
            if (id == 0)
            {
                id = next++;
                res.Warn(Loc.F("articulations.file.repaired_id", name.Length > 0 ? name : "#" + id.ToString(CultureInfo.InvariantCulture), id));
            }
            if (name.Length == 0) name = "#" + id.ToString(CultureInfo.InvariantCulture);
            var art = new Articulation
            {
                Id = id,
                Name = name,
                ShortName = Str(a, "shortName") is { Length: > 0 } sn ? sn : name.Length <= 6 ? name : name[..6],
                ColorArgb = UInt(a, "colorArgb"),
                SourceColorIndex = Int(a, "sourceColorIndex"),
                GroupId = Int(a, "groupId") is int gi && groupIds.Contains(gi) ? gi : 0,
                Enabled = Bool(a, "enabled") ?? true,
                Metadata = Strings(a["metadata"]),
            };
            string trig = Str(a, "trigger") ?? "latching";
            if (trig == "momentary") art.Trigger = TriggerMode.Momentary;
            else if (trig != "latching") res.Warn(Loc.F("articulations.file.unknown_trigger", name, trig));

            double off = Dbl(a, "noteOffsetMs") ?? 0, lead = Dbl(a, "triggerLeadMs") ?? 0;
            double cOff = double.IsFinite(off) ? Math.Clamp(off, -ArticulationMapCompiler.MaxOffsetMs, ArticulationMapCompiler.MaxOffsetMs) : 0;
            double cLead = double.IsFinite(lead) ? Math.Clamp(lead, 0, ArticulationMapCompiler.MaxLeadMs) : 0;
            if (cOff != off || cLead != lead) res.Warn(Loc.F("articulations.file.clamped", name));
            art.NoteOffsetMs = cOff;
            art.TriggerLeadMs = cLead;

            bool dropped = false;
            foreach (var x in Objects(a["actions"]))
            {
                var kind = KindOf(Str(x, "kind"));
                var act = kind is null ? null : new MidiAction
                {
                    Kind = kind.Value,
                    Channel = x["channel"] is null ? null : Int(x, "channel") ?? -1,
                    Data1 = Int(x, "data1") ?? 0,
                    Data2 = Int(x, "data2") ?? 0,
                };
                if (act is null || !act.IsValid()) { dropped = true; continue; }
                art.Actions.Add(act);
            }
            if (dropped) res.Warn(Loc.F("articulations.file.action_dropped", name));
            map.Articulations.Add(art);
        }

        if (Int(o, "defaultArticulationId") is int def)
        {
            if (map.Find(def) is not null) map.DefaultArticulationId = def;
            else res.Warn(Loc.T("articulations.file.default_missing"));
        }
        res.Map = map;
    }

    // ---- tolerant field readers: a wrong type reads as absent ---------------------------------

    private static IEnumerable<JsonObject> Objects(JsonNode? n) =>
        n is JsonArray arr ? arr.OfType<JsonObject>() : Enumerable.Empty<JsonObject>();

    private static string? Str(JsonObject o, string k) =>
        o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonObject o, string k)
    {
        if (o[k] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        return v.TryGetValue<double>(out var d) && double.IsFinite(d) && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue ? (int)d : null;
    }

    private static uint? UInt(JsonObject o, string k)
    {
        if (o[k] is not JsonValue v) return null;
        if (v.TryGetValue<uint>(out var u)) return u;
        return v.TryGetValue<long>(out var l) && l is >= 0 and <= uint.MaxValue ? (uint)l : null;
    }

    private static double? Dbl(JsonObject o, string k) =>
        o[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static bool? Bool(JsonObject o, string k) =>
        o[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static Dictionary<string, string> Strings(JsonNode? n)
    {
        var d = new Dictionary<string, string>();
        if (n is JsonObject o)
            foreach (var (k, v) in o)
                if (v is JsonValue jv && jv.TryGetValue<string>(out var s)) d[k] = s;
        return d;
    }
}
