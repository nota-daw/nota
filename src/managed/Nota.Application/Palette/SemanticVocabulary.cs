// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-12): the closed, versioned vocabulary every semantic descriptor is
// written in. Each term has one English label (what the palette prints — "warm · pad") and
// the words that mean it in a query, English and Russian. Russian is for matching only: the
// UI stays English. A term may serve several facets ("bass" is a sound for presets and a
// source for effects) — matching is by term, the facet says where a descriptor may use it.

namespace Nota.Application.Palette;

[Flags]
public enum Facet
{
    None = 0,
    Role = 1, Character = 2, Sound = 4, Source = 8, Task = 16, Genre = 32,
}

/// <summary>One vocabulary term. <see cref="Parent"/> is a broader role it implies
/// ("synth.fm" → "synth"), so "synth" finds every synth.</summary>
public sealed record SemanticTerm(int Id, string Key, string Label, Facet Facets, string? Parent, string[] Words);

public static class SemanticVocabulary
{
    /// <summary>Bumped whenever a term is added, renamed or removed — descriptors written
    /// against an older vocabulary are re-validated by the coverage test.</summary>
    public const int Version = 1;

    private static readonly List<SemanticTerm> _terms = new();
    private static readonly Dictionary<string, SemanticTerm> _byKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SemanticTerm> _byWord = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SemanticTerm> _byRuStem = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _stop = new(StringComparer.Ordinal)
    {
        "for", "on", "the", "a", "an", "to", "in", "with", "of", "and", "my", "some",
        "на", "для", "в", "во", "под", "с", "со", "и", "к", "по", "мне",
    };

    public static IReadOnlyList<SemanticTerm> Terms => _terms;

    static SemanticVocabulary()
    {
        const Facet R = Facet.Role, C = Facet.Character, S = Facet.Sound, Src = Facet.Source, T = Facet.Task, G = Facet.Genre;

        // ---- role: what the device is ------------------------------------------------------
        T_("reverb", R, "reverb", "verb", "reverberation", "hall", "room", "plate", "space", "ревер", "реверб", "реверберация", "пространство", "зал");
        T_("delay", R, "delay", "echo", "pingpong", "дилей", "делэй", "эхо", "задержка");
        T_("compressor", R, "compressor", "comp", "compression", "dynamics", "компрессор", "компрессия", "компр");
        T_("limiter", R, "limiter", "maximizer", "maximiser", "clipper", "лимитер", "максимайзер");
        T_("gate", R, "gate", "noisegate", "гейт");
        T_("eq", R, "eq", "equalizer", "equaliser", "equalisation", "эквалайзер", "эк", "эквализация");
        T_("filter", R, "filter", "lowpass", "highpass", "bandpass", "фильтр");
        T_("saturation", R, "saturation", "saturator", "distortion", "dist", "drive", "overdrive", "fuzz", "сатурация", "сатуратор", "перегруз", "дисторшн", "искажение", "драйв");
        T_("amp", R, "amp", "amplifier", "cabinet", "cab", "усилитель", "комбик");
        T_("bitcrusher", R, "bitcrusher", "crusher", "bitcrush", "decimator", "downsample", "биткрашер");
        T_("chorus", R, "chorus", "ensemble", "хорус");
        T_("flanger", R, "flanger", "фленджер", "флэнджер");
        T_("phaser", R, "phaser", "фэйзер", "фейзер", "фазер");
        T_("autopan", R, "autopan", "panner", "tremolo", "автопан", "тремоло", "панорамирование");
        T_("pitch", R, "pitch", "autotune", "tune", "tuning", "pitchshift", "автотюн", "тюн", "тон");
        T_("glitch", R, "glitch", "stutter", "repeat", "beatrepeat", "глитч", "заикание");
        T_("looper", R, "looper", "лупер");
        T_("utility", R, "utility", "gain", "trim", "mono", "утилита", "гейн");
        T_("analyzer", R, "analyzer", "analyser", "scope", "spectrum", "meter", "анализатор", "спектр");
        T_("multiband", R, "multiband", "мультибэнд", "многополосный");
        T_("rack", R, "rack", "chain", "рэк", "рек", "цепочка");
        T_("synth", R | Src, "synth", "synthesizer", "synthesiser", "синт", "синтезатор");
        Child("synth.subtractive", "synth", "subtractive", "субтрактивный");
        Child("synth.wavetable", "synth", "wavetable", "вейвтейбл", "волновой");
        Child("synth.fm", "synth", "fm", "фм");
        Child("synth.physical", "synth", "physical", "modeling", "modelling", "физическое", "моделирование");
        Child("synth.granular", "synth", "granular", "grain", "гранулярный", "гранулы");
        Child("synth.vector", "synth", "vector", "morph", "векторный");
        T_("sampler", R, "sampler", "sample", "сэмплер", "семплер", "сэмпл");
        T_("drum", R, "kit", "drumkit", "drummachine", "beatbox", "драммашина", "кит");
        T_("arp", R, "arpeggiator", "arp", "арпеджиатор", "арпеджио", "арп");
        T_("chord", R, "chord", "chords", "harmonize", "harmonizer", "аккорд", "аккорды");
        T_("scale", R, "scale", "key", "гамма", "лад", "тональность");
        T_("notelength", R, "length", "duration", "длина", "длительность");
        T_("velocity", R, "velocity", "велосити", "сила");
        T_("random", R, "random", "humanize", "humanise", "randomize", "рандом", "случайный", "очеловечить");

        // ---- character: how it sounds --------------------------------------------------
        T_("warm", C, "warm", "warmth", "теплый", "тепло", "мягкий", "бархатный");
        T_("bright", C, "bright", "brilliant", "shiny", "яркий", "звонкий", "блестящий");
        T_("dark", C, "dark", "moody", "темный", "мрачный", "глухой");
        T_("airy", C, "airy", "air", "breathy", "воздушный", "воздух");
        T_("gritty", C, "gritty", "dirty", "grit", "crunchy", "crunch", "грязный", "хрустящий");
        T_("clean", C, "clean", "transparent", "pure", "чистый", "прозрачный");
        T_("punchy", C, "punchy", "punch", "tight", "плотный", "панчевый", "упругий");
        T_("wide", C, "wide", "stereo", "spacious", "широкий", "стерео", "объемный");
        T_("lofi", C | G, "lofi", "lowfi", "лофай", "лоуфай", "lofihiphop");
        T_("vintage", C, "vintage", "analog", "analogue", "retro", "tape", "винтаж", "винтажный", "аналоговый", "ретро", "ленточный", "пленка");
        T_("soft", C, "soft", "gentle", "smooth", "нежный", "гладкий", "мягко");
        T_("aggressive", C, "aggressive", "hard", "heavy", "harsh", "агрессивный", "тяжелый", "жесткий");
        T_("metallic", C, "metallic", "metal", "металлический");
        T_("lush", C, "lush", "rich", "big", "huge", "сочный", "пышный", "богатый", "большой");
        T_("evolving", C, "evolving", "moving", "motion", "animated", "движущийся", "живой", "развивающийся");
        T_("deep", C, "deep", "sub", "глубокий", "саб");
        T_("glassy", C, "glassy", "glass", "crystal", "стеклянный", "кристальный");
        T_("subtle", C, "subtle", "light", "легкий", "деликатный");

        // ---- sound: what part an instrument preset plays ---------------------------------
        T_("pad", S, "pad", "pads", "пэд", "пэды", "пад", "пады", "подложка");
        T_("bass", S | Src, "bass", "basses", "bassline", "бас", "басы", "басс", "басовый");
        T_("lead", S, "lead", "leads", "solo", "лид", "соло");
        T_("pluck", S, "pluck", "plucks", "plucked", "щипок", "плак", "пиццикато");
        T_("keys", S | Src, "keys", "piano", "rhodes", "organ", "keyboard", "клавиши", "пиано", "фортепиано", "орган");
        T_("bell", S, "bell", "bells", "mallet", "mallets", "колокол", "колокольчик", "колокольчики");
        T_("seq", S, "sequence", "seq", "pattern", "секвенция", "паттерн");
        T_("fx", S, "fx", "sfx", "riser", "impact", "эффект", "эффекты", "райзер");
        T_("drone", S, "drone", "drones", "дрон", "гул");
        T_("strings", S, "strings", "string", "violin", "струнные", "струны", "скрипка");
        T_("brass", S, "brass", "horn", "horns", "духовые", "медь", "труба");
        T_("choir", S, "choir", "choral", "хор", "хоровой");
        T_("texture", S, "texture", "textures", "atmosphere", "atmos", "текстура", "атмосфера");
        T_("perc", S, "percussion", "perc", "перкуссия", "перк");

        // ---- source: what an effect is used on ------------------------------------------
        T_("vocals", Src, "vocals", "vocal", "vox", "voice", "singer", "вокал", "голос", "вокальный");
        T_("drums", Src, "drums", "drum", "beat", "beats", "барабаны", "барабан", "ударные", "бит");
        T_("guitar", Src, "guitar", "гитара", "гитарный");
        T_("bus", Src, "bus", "group", "шина", "группа", "бас-шина");
        T_("master", Src | T, "master", "mastering", "мастер", "мастеринг");
        T_("podcast", Src, "podcast", "speech", "dialog", "dialogue", "подкаст", "речь", "диалог");

        // ---- task: what job it does ------------------------------------------------------
        T_("sidechain", T, "sidechain", "pump", "pumping", "duck", "ducking", "сайдчейн", "пампинг");
        T_("glue", T, "glue", "склейка", "клей");
        T_("deess", T, "deess", "deesser", "sibilance", "деэссер", "деэсс", "шипящие");
        T_("widen", T, "widen", "widening", "widener", "расширение", "расширить");
        T_("tapestop", T, "tapestop", "тейпстоп");
        T_("transient", T, "transient", "transients", "attack", "транзиент", "атака");
        T_("loudness", T, "loudness", "loud", "louder", "громкость", "громче");
        T_("repair", T, "repair", "cleanup", "denoise", "fix", "ремонт", "чистка");
        T_("parallel", T, "parallel", "newyork", "параллельный", "параллельная");
        T_("tuning", T, "correction", "intonation", "коррекция", "интонация");
        T_("modulation", T, "modulation", "modulate", "lfo", "модуляция", "лфо");

        // ---- genre ----------------------------------------------------------------------------
        T_("techno", G, "techno", "техно");
        T_("house", G, "house", "хаус");
        T_("trap", G, "trap", "трэп", "трап");
        T_("hiphop", G, "hiphop", "rap", "хипхоп", "рэп");
        T_("ambient", G, "ambient", "эмбиент", "амбиент");
        T_("dnb", G, "dnb", "drumnbass", "jungle", "драмнбейс", "джангл");
        T_("dubstep", G, "dubstep", "дабстеп");
        T_("synthwave", G, "synthwave", "retrowave", "синтвейв", "ретровейв");
        T_("cinematic", G, "cinematic", "film", "score", "кино", "кинематографичный", "саундтрек");
        T_("pop", G, "pop", "поп");
        T_("rock", G, "rock", "рок");
        T_("jazz", G, "jazz", "джаз");
        T_("trance", G, "trance", "транс");
        T_("edm", G, "edm", "edmusic", "эдм");
        T_("funk", G, "funk", "disco", "фанк", "диско");
        T_("acid", G, "acid", "303", "эсид", "кислотный");
    }

    private static void T_(string key, Facet facets, params string[] words) => Add(key, facets, null, words);
    private static void Add(string key, Facet facets, string? parent, string[] words)
    {
        var label = key switch { "lofi" => "lo-fi", "deess" => "de-ess", "tapestop" => "tape stop", "hiphop" => "hip-hop", "seq" => "sequence", "perc" => "percussion", _ => key };
        if (key.StartsWith("synth.", StringComparison.Ordinal)) label = key[6..];
        var t = new SemanticTerm(_terms.Count, key, label, facets, parent, words);
        _terms.Add(t);
        _byKey[key] = t;
        foreach (var w0 in words)
        {
            var w = TextNorm.Normalize(w0);
            _byWord.TryAdd(w, t);
            if (TextNorm.HasCyrillic(w)) _byRuStem.TryAdd(TextNorm.RuStem(w), t);
        }
        _byWord.TryAdd(TextNorm.Normalize(key), t);
    }

    private static void Child(string key, string parent, params string[] words) => Add(key, Facet.Role, parent, words);

    public static SemanticTerm? ByKey(string key) => _byKey.GetValueOrDefault(key);

    public static bool IsStopWord(string normalizedToken) => _stop.Contains(normalizedToken);

    /// <summary>The term a normalised query token stands for: an exact word, a Russian word in
    /// another inflection, one typo away (4+ letters), or the start of a word (3+ letters).</summary>
    public static SemanticTerm? Lookup(string token)
    {
        if (token.Length == 0) return null;
        var t = token.Contains('-') ? token.Replace("-", "") : token;
        if (t.Length == 0) return null;
        if (_byWord.TryGetValue(t, out var hit)) return hit;
        bool cyr = TextNorm.HasCyrillic(t);
        if (cyr && _byRuStem.TryGetValue(TextNorm.RuStem(t), out hit)) return hit;
        if (t.Length >= 4)
            foreach (var (w, term) in _byWord)
                if (w.Length >= 4 && TextNorm.WithinOneEdit(t, w)) return term;
        if (t.Length >= 3)
        {
            SemanticTerm? best = null; int bestLen = int.MaxValue;
            foreach (var (w, term) in _byWord)
                if (w.Length > t.Length && w.Length - t.Length <= 4 && w.StartsWith(t, StringComparison.Ordinal) && w.Length < bestLen)
                { best = term; bestLen = w.Length; }
            return best;
        }
        return null;
    }

    /// <summary>Exact word lookup only (no typo / prefix) — what a descriptor field may hold.</summary>
    public static SemanticTerm? Exact(string word) => _byWord.GetValueOrDefault(TextNorm.Normalize(word).Replace("-", ""));
}
