// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-13.4): descriptors for scanned VST3 / AU plug-ins. In order of trust:
// a map of well-known plug-ins by vendor and name ("FabFilter Pro-Q" → eq), the VST3
// sub-categories or AU type the scan reports ("Fx|Reverb", "Instrument|Synth"), then
// heuristics on the name ("*verb*", "*comp*", "*delay*"). What none of them knows stays
// unclassified — a plug-in is still found by its name and vendor.

using System.Text.RegularExpressions;

namespace Nota.Application.Palette;

public static class PluginDescriptors
{
    // (vendor substring, name regex) → descriptor. Vendor "" matches any vendor.
    private static readonly (string Vendor, Regex Name, SemanticDescriptor D)[] Known =
    {
        K("fabfilter", "pro-?q", "eq", "proq", "clean", source: "master vocals bus"),
        K("fabfilter", "pro-?c", "compressor", "proc", "punchy", source: "bus drums vocals", task: "glue"),
        K("fabfilter", "pro-?l", "limiter", "prol", "clean", source: "master", task: "loudness"),
        K("fabfilter", "pro-?r", "reverb", "pror", "lush"),
        K("fabfilter", "pro-?ds", "compressor", "prods deesser", "", source: "vocals", task: "deess"),
        K("fabfilter", "pro-?g", "gate", "prog"),
        K("fabfilter", "pro-?mb", "multiband", "promb", "", source: "master"),
        K("fabfilter", "saturn", "saturation", "saturn", "warm gritty"),
        K("fabfilter", "timeless", "delay", "", "vintage"),
        K("fabfilter", "volcano", "filter", "", "evolving"),
        K("fabfilter", "twin", "synth.subtractive", "", "bright"),
        K("valhalla", "delay", "delay", "", "vintage"),
        K("valhalla", "supermassive", "reverb", "", "lush airy", genre: "ambient"),
        K("valhalla", "freq ?echo", "delay", "", "evolving"),
        K("valhalla", "space ?modulator", "flanger", "", "evolving"),
        K("valhalla", "", "reverb", "verb", "lush airy"),
        K("soundtoys", "decapitator", "saturation", "decap", "warm gritty"),
        K("soundtoys", "echoboy", "delay", "", "vintage"),
        K("soundtoys", "primaltap", "delay", "", "vintage lofi"),
        K("soundtoys", "little ?alterboy", "pitch", "", "", source: "vocals"),
        K("soundtoys", "crystallizer", "glitch", "", "evolving"),
        K("soundtoys", "microshift", "chorus", "", "wide", task: "widen"),
        K("soundtoys", "radiator", "saturation", "", "warm vintage"),
        K("xfer", "serum", "synth.wavetable", "serum", "bright", sound: "bass lead pad"),
        K("xfer", "ott", "multiband", "ott", "bright aggressive", genre: "edm dubstep"),
        K("xfer", "lfo ?tool", "utility", "", "punchy", task: "sidechain"),
        K("", "^vital", "synth.wavetable", "", "bright", sound: "bass lead pad"),
        K("", "^massive", "synth.wavetable", "", "aggressive", sound: "bass lead"),
        K("", "^diva", "synth.subtractive", "", "warm vintage", sound: "pad bass lead"),
        K("", "^repro", "synth.subtractive", "", "warm vintage"),
        K("u-he", "zebra", "synth", "", "evolving"),
        K("", "^dexed", "synth.fm", "dx7", "bright", sound: "keys bell bass"),
        K("", "surge", "synth", "", "bright"),
        K("", "tal-u-no|tal-?juno", "synth.subtractive", "juno", "warm vintage", sound: "pad"),
        K("", "^kontakt", "sampler", "", ""),
        K("spitfire", "labs", "sampler", "", "soft", genre: "cinematic"),
        K("izotope", "ozone", "limiter", "", "clean", source: "master", task: "loudness"),
        K("izotope", "neutron", "eq", "", "clean", source: "bus"),
        K("izotope", "\\brx\\b", "eq", "", "clean", task: "repair"),
        K("izotope", "nectar", "eq", "", "", source: "vocals"),
        K("oeksound", "soothe", "eq", "soothe", "clean", source: "vocals master", task: "deess repair"),
        K("antares", "auto-?tune", "pitch", "autotune", "", source: "vocals", task: "tuning"),
        K("celemony", "melodyne", "pitch", "", "", source: "vocals", task: "tuning"),
        K("waves", "cla-?76|1176", "compressor", "1176", "punchy aggressive", source: "drums vocals"),
        K("waves", "cla-?2a|la-?2a", "compressor", "la2a opto", "warm soft", source: "vocals bass"),
        K("waves", "\\bl[123]\\b", "limiter", "", "clean", source: "master", task: "loudness"),
        K("waves", "deesser", "compressor", "", "", source: "vocals", task: "deess"),
        K("waves", "h-?delay", "delay", "", "vintage"),
        K("waves", "r-?verb|h-?reverb", "reverb", "", "lush"),
        K("cableguys", "shaper ?box|volume ?shaper", "utility", "", "punchy", task: "sidechain"),
        K("", "kickstart", "utility", "", "punchy", task: "sidechain"),
        K("tokyo dawn", "nova", "eq", "dynamiceq", "clean", task: "deess"),
        K("tokyo dawn", "kotelnikov", "compressor", "", "clean", source: "master bus", task: "glue"),
        K("tokyo dawn", "slick ?eq", "eq", "", "warm", source: "master"),
    };

    private static (string, Regex, SemanticDescriptor) K(string vendor, string name, string role, string aliases, string character = "",
        string sound = "", string source = "", string task = "", string genre = "")
    {
        static string[] S(string s) => s.Length == 0 ? [] : s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (vendor, new Regex(name, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new SemanticDescriptor { Role = role, Aliases = S(aliases), Character = S(character), Sound = S(sound), Source = S(source), Task = S(task), Genre = S(genre) });
    }

    // VST3 sub-category / AU type words → role or facet.
    private static readonly (string Word, string Role, string Extra)[] CategoryWords =
    {
        ("reverb", "reverb", ""), ("delay", "delay", ""), ("distortion", "saturation", ""), ("dynamics", "compressor", ""),
        ("eq", "eq", ""), ("filter", "filter", ""), ("pitch shift", "pitch", ""), ("analyzer", "analyzer", ""),
        ("modulation", "chorus", "task:modulation"), ("restoration", "", "task:repair"), ("spatial", "", "task:widen"),
        ("mastering", "", "source:master"), ("tools", "utility", ""), ("drum", "drum", ""), ("piano", "", "sound:keys"),
        ("sampler", "sampler", ""), ("synth", "synth", ""), ("generator", "synth", ""),
    };

    // Name fragments → role, for plug-ins nothing else classifies.
    private static readonly (Regex Pattern, string Role)[] NameRules =
    {
        (Rx("verb|reverb|hall\\b|plate\\b|room\\b|space"), "reverb"),
        (Rx("delay|echo"), "delay"),
        (Rx("de-?ess"), "compressor"),
        (Rx("comp|1176|la-?2a|leveler|leveller"), "compressor"),
        (Rx("limit|maximi[sz]"), "limiter"),
        (Rx("\\beq\\b|equali[sz]|eq\\d|\\beq-"), "eq"),
        (Rx("satur|drive|dist|fuzz|tube|tape|crush"), "saturation"),
        (Rx("chorus|ensemble"), "chorus"),
        (Rx("flang"), "flanger"),
        (Rx("phas"), "phaser"),
        (Rx("filter|wah"), "filter"),
        (Rx("gate"), "gate"),
        (Rx("tune|pitch"), "pitch"),
        (Rx("glitch|stutter"), "glitch"),
        (Rx("synth|osc"), "synth"),
        (Rx("sampler|sample"), "sampler"),
        (Rx("drum|808|909|kit\\b"), "drum"),
        (Rx("analy[sz]|scope|meter"), "analyzer"),
    };

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The descriptor of a scanned plug-in. <paramref name="category"/> is the scan's
    /// category string (VST3 sub-categories joined by '|' or '/', or the AU type), "" when unknown.</summary>
    public static SemanticDescriptor For(string name, string vendor, bool isInstrument, string category = "")
    {
        foreach (var (v, rx, d) in Known)
            if ((v.Length == 0 || vendor.Contains(v, StringComparison.OrdinalIgnoreCase)) && (rx.ToString().Length == 0 || rx.IsMatch(name)))
                return Finish(d, isInstrument);

        var desc = new SemanticDescriptor();
        if (category.Length > 0)
        {
            var parts = category.ToLowerInvariant().Split(['|', '/'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
                foreach (var (word, role, extra) in CategoryWords)
                {
                    if (part != word) continue;
                    if (role.Length > 0 && desc.Role.Length == 0) desc = desc with { Role = role };
                    if (extra.Length > 0)
                    {
                        var (facet, key) = (extra[..extra.IndexOf(':')], extra[(extra.IndexOf(':') + 1)..]);
                        desc = facet switch
                        {
                            "task" => desc with { Task = desc.Task.Append(key).Distinct().ToArray() },
                            "source" => desc with { Source = desc.Source.Append(key).Distinct().ToArray() },
                            _ => desc with { Sound = desc.Sound.Append(key).Distinct().ToArray() },
                        };
                    }
                }
        }
        if (desc.Role.Length == 0 || desc.Role is "synth" or "utility")
            foreach (var (rx, role) in NameRules)
                if (rx.IsMatch(name)) { desc = desc with { Role = role }; break; }
        return Finish(desc.WithTokensFrom(name), isInstrument);
    }

    // An instrument the rules couldn't place is at least a synth; an effect stays unclassified.
    private static SemanticDescriptor Finish(SemanticDescriptor d, bool isInstrument)
        => isInstrument && d.Role.Length == 0 ? d with { Role = "synth" } : d;
}
