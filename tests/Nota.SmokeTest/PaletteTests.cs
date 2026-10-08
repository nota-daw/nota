// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-27): descriptor coverage, golden queries (EN + RU) → expected items in
// the top three, prefixes and layout, the context table (CP-3) checked against the drag &
// drop paths on a real engine, undo, and the per-keystroke budget on a synthetic index of
// 20 000 items. Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --palette`;
// `-- --palette-dump <query>` prints what a query ranks.

using System.Diagnostics;
using Nota.Application.Palette;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class PaletteTests
{
    // A few well-known plug-ins, as the scan describes them ("Name | Format | inst|fx | Vendor | Category").
    private sealed class FakePlugins : IPluginCatalog
    {
        private static readonly string[] Rows =
        {
            "Pro-Q 3 | VST3 | fx | FabFilter | Fx|EQ",
            "ValhallaVintageVerb | VST3 | fx | Valhalla DSP, LLC | Fx|Reverb",
            "Serum | VST3 | inst | Xfer Records | Instrument|Synth",
            "soothe2 | VST3 | fx | oeksound | Fx",
            "Decapitator | AU | fx | Soundtoys | Effect",
            "OTT | VST3 | fx | Xfer Records | Fx|Dynamics",
            "Mystery Box | VST3 | fx | Someone | Fx",
        };
        public int Scan(string workerPath) => Rows.Length;
        public int Count => Rows.Length;
        public string? Description(int index) => index >= 0 && index < Rows.Length ? Rows[index] : null;
        public string? Id(int index) => $"fake:{index}";
        public int IndexOfId(string identifier) => -1;
        public void AddScanPath(string dir) { }
        public void RemoveScanPath(int index) { }
        public int ScanPathCount => 0;
        public string? ScanPath(int index) => null;
    }

    internal static (PaletteIndex Index, PaletteSearch Search, Settings Settings) BuildIndex()
    {
        var browser = new BrowserViewModel(new FakePlugins(), new EmptyPresetLibrary(), new FactoryPresetCatalog());
        var registry = new CommandRegistry();
        foreach (var c in registry.All) registry.Bind(c.Id, (_, _) => { });
        var index = new PaletteIndex();
        index.Set("actions", new ActionPaletteProvider(registry).Build());
        index.Set("tracks", new[] { "Drums", "Bass", "Keys", "Lead Vox", "Pad" }.Select((n, i) => new PaletteItem
        {
            Kind = PaletteKind.Track, Id = "t:" + (i + 1), Name = n, Sub = i == 3 ? "Audio" : "MIDI", Tag = "Track", Payload = i + 1,
        }).ToList());
        index.Set("devices", new DevicePaletteProvider(browser).Build());
        index.Set("presets", new PresetPaletteProvider(browser).Build());
        index.Set("modular", new ModularPaletteProvider().Build());
        var settings = new Settings();
        return (index, new PaletteSearch(index, new PaletteUsage(settings)), settings);
    }

    private static readonly PaletteContext Devices = new()
    {
        Origin = PaletteOrigin.Devices, TrackId = 2, TrackName = "Bass", TrackType = PaletteTrackType.Instrument,
        InstrumentKind = 6, InstrumentName = "Nota Volt",
    };

    public static void Dump(string query)
    {
        var (_, search, _) = BuildIndex();
        var r = search.Run(query, Devices, flat: true);
        Console.WriteLine($"query '{query}' · layout '{r.LayoutHint}' · facets [{string.Join(", ", r.Query.Facets.Select(f => f.Key))}] · left '{r.Query.LexLeft}'");
        foreach (var h in r.Items.Take(15))
            Console.WriteLine($"  {h.Score,6:0.00}  lex {h.Lexical:0.00} facet {h.FacetScore:0.00}  {h.Item.Kind,-7} {h.Item.Name}  ·  {h.Item.Sub}  {r.ReasonFor(h)}");
    }

    // Query → a name (or "Kind:Name") that must be among the first three results.
    private static readonly (string Query, string Expect)[] Golden =
    {
        // names, fuzzy (CP-9)
        ("afl", "Nota Auto Filter"), ("nv", "Nota Vintage"), ("vintage", "Nota Vintage"), ("reverb", "Nota Reverb"),
        ("comp", "Nota Compressor"), ("eq8", "Nota EQ-8"), ("eq 3", "Nota EQ-3"), ("volt", "Nota Volt"),
        ("aurora", "Nota Aurora"), ("drum rack", "Nota Drum Rack"), ("pro-q", "Pro-Q 3"), ("proq", "Pro-Q 3"),
        ("serum", "Serum"), ("valhalla", "ValhallaVintageVerb"), ("decap", "Decapitator"), ("beat repeat", "Nota Beat Repeat"),
        // aliases / slang
        ("verb", "Nota Reverb"), ("ott", "Nota Prism"), ("echo", "Nota Delay"), ("autotune", "Nota Auto Shift"),
        ("de-esser", "De-Esser"), ("deesser", "Nota Dynamic EQ-8"), ("limiter", "Nota Ceiling"), ("bitcrusher", "Nota Crush"),
        ("arpeggiator", "Nota Arp"), ("303", "Nota Bass"), ("wavetable", "Nota Aurora"), ("fm", "Nota Operator"), ("granular", "Nota Grain"),
        // typos (CP-10)
        ("reverv", "Nota Reverb"), ("compresor", "Nota Compressor"), ("dealy", "Nota Delay"), ("chorsu", "Nota Chorus"),
        // wrong layout (CP-10)
        ("кумуки", "Nota Reverb"), ("вудфн", "Nota Delay"), ("сщьз", "Nota Compressor"), ("мщдв", "Nota Volt"),
        // semantics (CP-14)
        ("sidechain", "Sidechain Pump"), ("glue", "Glue Bus"), ("compressor for drums", "Nota Compressor"),
        ("компрессор на барабаны", "Nota Compressor"), ("reverb for vocals", "Nota Reverb"), ("ревер для вокала", "Nota Reverb"),
        ("tape saturation", "Tape Saturation"), ("тёплый перегруз", "Nota Vintage"), ("bass for techno", "Preset:*techno"),
        ("эквалайзер", "Nota EQ-8"), ("лимитер для мастеринга", "Nota Ceiling"), ("гитарный усилитель", "Nota Valve"),
        ("granular texture", "Nota Grain"), ("автотюн", "Nota Auto Shift"), ("тёплый пэд", "Preset:*pad"), ("warm pad", "Preset:*pad"),
        ("dark reverb", "Nota Chamber"), ("acid bass", "Preset:*acid"), ("pluck", "Preset:*pluck"),
        // actions & tracks (CP-6a)
        ("quant", "Quantize Notes"), (">quant", "Quantize Notes"), ("export", "Export Audio…"), ("render", "Export Audio…"),
        ("settings audio", "Settings: Audio"), ("hotkeys", "Settings: Shortcuts"), ("metronome", "Metronome"),
        ("@vox", "Lead Vox"), ("bass", "Bass"), ("phaser", "Nota Phaser"),
    };

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // ---- coverage (CP-27) ---------------------------------------------------------------
        var noRole = BuiltinDeviceCatalog.All.Where(d => d.Descriptor.Role.Length == 0 || d.Descriptor.Aliases.Length == 0).Select(d => d.Name).ToList();
        yield return (noRole.Count == 0, noRole.Count == 0 ? $"every built-in device ({BuiltinDeviceCatalog.All.Count}) has a role and aliases"
            : $"built-in devices without role/aliases: {string.Join(", ", noRole)}");
        var unknown = BuiltinDeviceCatalog.All.SelectMany(d => d.Descriptor.UnknownKeys().Select(k => $"{d.Name} {k}"))
            .Concat(ModularCatalog.All.SelectMany(n => n.Descriptor.UnknownKeys().Select(k => $"{n.Name} {k}"))).ToList();
        yield return (unknown.Count == 0, unknown.Count == 0 ? "descriptors use only vocabulary terms in their facets"
            : $"unknown descriptor terms: {string.Join(", ", unknown.Take(8))}");
        var dupWords = SemanticVocabulary.Terms.SelectMany(t => t.Words.Select(w => (w: TextNorm.Normalize(w), t.Key)))
            .GroupBy(x => x.w).Where(g => g.Select(x => x.Key).Distinct().Count() > 1).Select(g => g.Key).ToList();
        yield return (dupWords.Count == 0, dupWords.Count == 0 ? $"vocabulary v{SemanticVocabulary.Version}: {SemanticVocabulary.Terms.Count} terms, no word means two terms"
            : $"vocabulary words used by two terms: {string.Join(", ", dupWords)}");

        var (index, search, settings) = BuildIndex();
        var presets = index.Slice("presets").Where(p => p.PresetOrigin == PresetOrigin.Factory).ToList();
        var bare = presets.Where(p => p.Descriptor is not { } d || (d.Sound.Length == 0 && d.Source.Length == 0) || d.Character.Length == 0)
            .Select(p => $"{p.Device}/{p.Name}").ToList();
        yield return (presets.Count > 500 && bare.Count == 0, bare.Count == 0
            ? $"every factory preset ({presets.Count}) has a sound or source and a character"
            : $"{bare.Count} factory presets lack sound/source or character: {string.Join(", ", bare.Take(6))}");
        var plugin = index.Find("d:pl:Pro-Q 3|VST3");
        yield return (plugin?.Descriptor?.Role == "eq" && index.Find("d:pl:Mystery Box|VST3")?.Descriptor?.Role == ""
                      && index.Find("d:pl:Serum|VST3")?.Descriptor?.Role == "synth.wavetable",
            "plug-ins: known map (Pro-Q → eq, Serum → wavetable); unknown stays unclassified (CP-13.4)");
        yield return (PluginDescriptors.For("Room Verb X", "Acme", false).Role == "reverb"
                      && PluginDescriptors.For("Thing", "Acme", false, "Fx|Delay").Role == "delay",
            "…else the VST3 category, else name heuristics");

        // ---- golden queries ------------------------------------------------------------------
        int passed = 0; var misses = new List<string>();
        foreach (var (query, expect) in Golden)
        {
            var r = search.Run(query, Devices, flat: true);
            var top = r.Items.Take(3).ToList();
            bool ok = expect.StartsWith("Preset:*", StringComparison.Ordinal)
                ? top.Any(h => h.Item.Kind == PaletteKind.Preset && (h.Item.Descriptor?.Terms().Contains(SemanticVocabulary.ByKey(expect[8..])!.Id) ?? false))
                : top.Any(h => h.Item.Name == expect);
            if (ok) passed++; else misses.Add($"'{query}' → {string.Join(" | ", top.Select(h => h.Item.Name))}");
        }
        yield return (misses.Count == 0 && Golden.Length >= 50, misses.Count == 0
            ? $"golden queries: {passed}/{Golden.Length} find the expected item in the top three"
            : $"golden queries: {passed}/{Golden.Length} — misses: {string.Join("; ", misses)}");

        // ---- prefixes, highlighting, reasons, layout ------------------------------------------
        var quant = search.Run(">quant", Devices);
        yield return (quant.Items.Count > 0 && quant.Items.All(h => h.Item.Kind == PaletteKind.Action), "'>quant' finds only actions (CP-7)");
        var warm = search.Run("#warm", Devices);
        yield return (warm.Items.Count > 0 && warm.Items.All(h => h.Item.Kind == PaletteKind.Preset), "'#warm' finds only presets");
        yield return (search.Run("~lfo", Devices with { Origin = PaletteOrigin.Arrangement }).Items.Count == 0
                      && search.Run("~lfo", Devices with { Origin = PaletteOrigin.Modular }).Items.Count > 0,
            "Modular nodes are offered only from Modular");
        var afl = search.Run("afl", Devices, flat: true).Items[0];
        yield return (afl.Item.Name == "Nota Auto Filter" && afl.Mask == ((1UL << 5) | (1UL << 10) | (1UL << 12)),
            "'afl' lights A·F·l in Nota Auto Filter (CP-9)");
        var tp = search.Run("тёплый пэд", Devices, flat: true);
        var semantic = tp.Items.FirstOrDefault(h => tp.ReasonFor(h).Length > 0);
        yield return (tp.ReasonFor(semantic) == "warm · pad", $"a semantic hit says why: '{tp.ReasonFor(semantic)}' (CP-15)");
        var ru = search.Run("кумуки", Devices);
        yield return (ru.LayoutHint == "reverb", $"a query in the wrong layout is read as '{ru.LayoutHint}'");
        var exact = search.Run("vintage", Devices, flat: true).Items;
        yield return (exact[0].Item.Name == "Nota Vintage", "an exact name beats every semantic match");

        // ---- context: availability, the target line, hidden actions ----------------------------
        var none = new PaletteContext { Origin = PaletteOrigin.Arrangement };
        var reverb = index.Find("d:be:2")!; var volt = index.Find("d:bi:6")!; var arp = index.Find("d:bm:0")!;
        yield return (!PaletteTargets.Check(reverb, none).Ok && PaletteTargets.Check(reverb, none).Reason == "Select a track first"
                      && PaletteTargets.Describe(volt, none, ApplyMode.Default).Text == "→ new MIDI track",
            "no track: an effect is inactive with a reason, an instrument makes a track (CP-4)");
        var audio = Devices with { TrackType = PaletteTrackType.Audio, InstrumentKind = -1, InstrumentName = "" };
        yield return (!PaletteTargets.Check(arp, audio).Ok, "a MIDI effect on an audio track says why not");
        var sel = Devices with { Insert = new InsertPoint(ChainSection.Audio, 0, "Nota Vintage") };
        yield return (PaletteTargets.Describe(reverb, sel, ApplyMode.Default).Text == "→ Bass · after Nota Vintage"
                      && PaletteTargets.Describe(reverb, sel, ApplyMode.Replace).Text == "→ Bass · replaces Nota Vintage"
                      && PaletteTargets.Describe(volt, sel, ApplyMode.Default).Text == "→ Bass · replaces Nota Volt"
                      && PaletteTargets.Describe(volt, sel, ApplyMode.NewTrack).Text == "→ new MIDI track"
                      && PaletteTargets.Describe(reverb, sel, ApplyMode.KeepOpen).Text.EndsWith("keep open", StringComparison.Ordinal),
            "the target line follows the item and the modifiers (CP-6)");
        var hidden = search.Run("quantize", none, it => it.Id == "a:roll.quantize" ? Availability.No("Open a MIDI clip first") : Availability.Yes);
        var hiddenQ = search.Run("qua", none, it => it.Id == "a:roll.quantize" ? Availability.No("Open a MIDI clip first") : Availability.Yes);
        yield return (hidden.Items.Any(h => h.Item.Id == "a:roll.quantize" && h.Disabled && h.Why.Length > 0)
                      && !search.Run("notes", none, it => it.Id == "a:roll.quantize" ? Availability.No("x") : Availability.Yes).Items.Any(h => h.Item.Id == "a:roll.quantize"),
            "an action that can't run is hidden unless named, then shown inactive with the reason (CP-8)");
        _ = hiddenQ;

        // ---- recent + frecency -----------------------------------------------------------
        var usage = new PaletteUsage(settings);
        usage.Record("d:be:3", fromPalette: true);
        var empty = search.Run("", Devices, suggested: PaletteTargets.Suggested(Devices));
        yield return (empty.Rows.Count > 0 && empty.Rows[0].Header == "RECENT" && empty.Items[0].Item.Name == "Nota Delay"
                      && empty.Rows.Any(r => r.Header?.StartsWith("SUGGESTED", StringComparison.Ordinal) == true),
            "an empty query shows Recent, then Suggested for the context (CP-19)");

        // ---- insertion through the shared service on a real engine (CP-3, CP-25, undo) ----------
        foreach (var r in EngineChecks()) yield return r;

        // ---- the registry is the one source (CP-24) -------------------------------------------
        foreach (var r in RegistryDrift()) yield return r;

        // ---- performance (CP-22) ----------------------------------------------------------
        foreach (var r in Performance()) yield return r;
    }

    private static IEnumerable<(bool, string)> EngineChecks()
    {
        using var eng = new NotaEngine();
        var factory = new FactoryPresetCatalog();
        var svc = new DeviceInsertService(eng, factory, null, null);
        var browser = new BrowserViewModel(new EmptyPluginCatalog(), new EmptyPresetLibrary(), factory);
        BrowserItem Dev(int kind, BrowserItemKind k) => (k == BrowserItemKind.BuiltinEffect ? browser.EffectTree : k == BrowserItemKind.BuiltinMidiEffect ? browser.MidiTree : browser.InstrumentTree)
            .First(d => d.Kind == k && d.BuiltinKind == kind);
        var eq = Dev(0, BrowserItemKind.BuiltinEffect); var delay = Dev(3, BrowserItemKind.BuiltinEffect);
        var reverb = Dev(2, BrowserItemKind.BuiltinEffect); var volt = Dev(6, BrowserItemKind.BuiltinInstrument);
        var aurora = Dev(5, BrowserItemKind.BuiltinInstrument);

        string Chain(int t) => string.Join(",", Enumerable.Range(0, eng.TrackDeviceCount(t)).Select(i => eng.TrackDeviceBuiltinKind(t, i)));
        int Track() { int t = svc.CreateInstrumentTrack(volt, 0); svc.Insert(eq, new InsertTarget(t)); svc.Insert(delay, new InsertTarget(t)); return t; }

        // Devices, card 0 selected: the palette's target vs the drag & drop gap after it.
        int a = Track(), b = Track();
        var ctx = new PaletteContext { Origin = PaletteOrigin.Devices, TrackId = a, TrackType = PaletteTrackType.Instrument, InstrumentKind = 6, Insert = new InsertPoint(ChainSection.Audio, 0, "Nota EQ-8") };
        var item = new PaletteItem { Kind = PaletteKind.Device, Id = "d:be:2", Name = "Nota Reverb", Role = PaletteDeviceRole.AudioEffect, Payload = reverb };
        svc.Insert(reverb, PaletteInsert.TargetFor(ctx, item, ApplyMode.Default));
        svc.Insert(reverb, new InsertTarget(b, InsertPlacement.Insert, 1));
        yield return (Chain(a) == Chain(b) && Chain(a) == "0,2,3", $"Devices: the palette inserts after the selected card like a drop in that gap ({Chain(a)})");

        int c = Track(), d = Track();
        svc.Insert(reverb, PaletteInsert.TargetFor(ctx with { TrackId = c }, item, ApplyMode.Replace));
        svc.Insert(reverb, new InsertTarget(d, InsertPlacement.Replace, 0));
        yield return (Chain(c) == Chain(d) && Chain(c) == "2,3", "⌥Enter replaces the selected card like a drop on it");

        int e = Track(), f = Track();
        var arr = new PaletteContext { Origin = PaletteOrigin.Arrangement, TrackId = e, TrackType = PaletteTrackType.Instrument, InstrumentKind = 6 };
        svc.Insert(reverb, PaletteInsert.TargetFor(arr, item, ApplyMode.Default));
        svc.RouteToTrack(reverb, f);
        yield return (Chain(e) == Chain(f) && Chain(e) == "0,3,2", "Arrangement: an effect goes to the end of the chain like a drop on the track");

        int g = Track();
        var instItem = new PaletteItem { Kind = PaletteKind.Device, Id = "d:bi:5", Name = "Nota Aurora", Role = PaletteDeviceRole.Instrument, Payload = aurora };
        int tracksBefore = eng.TrackCount;
        svc.Insert(aurora, PaletteInsert.TargetFor(arr with { TrackId = g }, instItem, ApplyMode.Default));
        yield return (eng.TrackInstrumentKind(g) == 5 && eng.TrackCount == tracksBefore && Chain(g) == "0,3",
            "an instrument replaces the track's instrument and keeps its effects (CP-3)");
        var r = svc.Insert(aurora, PaletteInsert.TargetFor(arr with { TrackId = g }, instItem, ApplyMode.NewTrack));
        yield return (r.CreatedTrack && eng.TrackCount == tracksBefore + 1, "⌘Enter puts an instrument on a new track");

        int h = Track();
        int devicesBefore = eng.TrackDeviceCount(h);
        svc.Insert(reverb, new InsertTarget(h, InsertPlacement.Insert, 1));
        bool undone = eng.Undo() && eng.TrackDeviceCount(h) == devicesBefore && Chain(h) == "0,3";
        yield return (undone, "one insertion is one undo step (add + move)");

        var fp = factory.All().First(p => p.IsInstrument && p.BuiltinKind == 5 && p.Category == "Pads");
        var presetRow = new BrowserItem { Name = fp.DisplayName, Kind = BrowserItemKind.Preset, Path = "factory:" + fp.Id };
        int k = Track(); int count0 = eng.TrackCount;
        var pr = svc.ApplyPreset(presetRow, new InsertTarget(k, InPlace: true));
        yield return (pr.Ok && eng.TrackCount == count0 && eng.TrackInstrumentKind(k) == 5 && Chain(k) == "0,3",
            "an instrument preset loads in place, swapping the instrument (palette)");
        var dropped = svc.ApplyPreset(presetRow, new InsertTarget(k));
        yield return (dropped.CreatedTrack, "…while a dropped one still starts its own track");
    }

    // Source checks: the menu names only catalog commands, every command has a handler, and
    // every shortcut the catalog declares is listed in Settings → Shortcuts.
    private static IEnumerable<(bool, string)> RegistryDrift()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Nota.sln"))) dir = Path.GetDirectoryName(dir);
        if (dir is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(dir, "src/managed/Nota.App");
        var catalog = CommandCatalog.Create();
        var ids = catalog.Select(c => c.Id).ToHashSet();

        var axaml = File.ReadAllText(Path.Combine(app, "MainWindow.axaml"));
        var menuIds = System.Text.RegularExpressions.Regex.Matches(axaml, "app:Commands\\.Id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        var strayClicks = System.Text.RegularExpressions.Regex.Matches(axaml, "<NativeMenuItem [^>]*Click=").Count;
        var unknownMenu = menuIds.Where(i => !ids.Contains(i)).ToList();
        yield return (menuIds.Count >= 35 && unknownMenu.Count == 0 && strayClicks == 0,
            unknownMenu.Count == 0 && strayClicks == 0 ? $"the menu runs {menuIds.Count} registered commands, none by its own handler"
                : $"menu items outside the registry: {string.Join(", ", unknownMenu)} · Click handlers: {strayClicks}");

        var bindings = File.ReadAllText(Path.Combine(app, "MainWindow.Commands.cs"));
        var unbound = ids.Where(i => !bindings.Contains($"\"{i}\"") && i != CommandCatalog.Palette).ToList();
        yield return (unbound.Count == 0, unbound.Count == 0 ? $"every catalog command ({ids.Count}) is bound"
            : $"commands without a handler: {string.Join(", ", unbound)}");

        var prefs = File.ReadAllText(Path.Combine(app, "PreferencesWindow.cs"));
        var listed = System.Text.RegularExpressions.Regex.Matches(prefs, "\\(\"([^\"]+)\", \"").Select(m => m.Groups[1].Value)
            .SelectMany(k => k.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(Canon).ToHashSet();
        var missing = catalog.Where(c => c.Gesture.Length > 0 && !listed.Contains(Canon(c.Gesture))).Select(c => $"{c.Gesture} ({c.Title})").ToList();
        yield return (missing.Count == 0, missing.Count == 0 ? "every catalog shortcut is listed in Settings → Shortcuts"
            : $"shortcuts missing from Settings → Shortcuts: {string.Join(", ", missing)}");

        // Modifier order doesn't matter: ⌥⌘S and ⌘⌥S are the same key.
        static string Canon(string k) => new string(k.Where(c => "⌘⌃⌥⇧".Contains(c)).OrderBy(c => c).ToArray()) + new string(k.Where(c => !"⌘⌃⌥⇧".Contains(c)).ToArray());
    }

    private static IEnumerable<(bool, string)> Performance()
    {
        // 1 000 plug-ins, 15 000 presets, 300 actions, 200 tracks (+ the real catalogue).
        var rnd = new Random(7);
        string[] syll = { "ka", "lo", "ve", "ri", "mon", "sta", "zu", "per", "tex", "vo", "lin", "gra", "dor", "fi", "ne" };
        string Word() => string.Concat(Enumerable.Range(0, rnd.Next(2, 4)).Select(_ => syll[rnd.Next(syll.Length)]));
        var items = new List<PaletteItem>();
        string[] cats = { "Pads", "Bass", "Leads", "Keys", "Vocals", "Drums", "Mastering", "Textures" };
        for (int i = 0; i < 1000; i++)
            items.Add(new PaletteItem { Kind = PaletteKind.Device, Id = $"d:pl:{i}", Name = $"{Word()} {Word()}", Device = Word(), Role = PaletteDeviceRole.AudioEffect,
                Descriptor = PluginDescriptors.For(Word(), Word(), false) });
        var dev = BuiltinDeviceCatalog.All;
        for (int i = 0; i < 15000; i++)
        {
            var d = dev[rnd.Next(dev.Count)]; var cat = cats[rnd.Next(cats.Length)];
            string name = $"{Word()} {(rnd.Next(4) == 0 ? "Warm " : "")}{Word()}";
            items.Add(new PaletteItem { Kind = PaletteKind.Preset, Id = $"p:{i}", Name = name, Device = d.Name, Folder = cat, Role = PaletteDeviceRole.AudioEffect,
                Descriptor = PresetDescriptors.For(d.Descriptor, cat, name) });
        }
        for (int i = 0; i < 300; i++) items.Add(new PaletteItem { Kind = PaletteKind.Action, Id = $"a:{i}", Name = $"{Word()} {Word()}", Sub = "Edit" });
        for (int i = 0; i < 200; i++) items.Add(new PaletteItem { Kind = PaletteKind.Track, Id = $"t:{i}", Name = $"{Word()} {i}" });
        var sw = Stopwatch.StartNew();
        var index = new PaletteIndex();
        index.Set("synthetic", PaletteIndex.Prepare(items));
        double buildMs = sw.Elapsed.TotalMilliseconds;
        var search = new PaletteSearch(index, new PaletteUsage(new Settings()));
        var ctx = new PaletteContext { Origin = PaletteOrigin.Devices, TrackId = 1, TrackType = PaletteTrackType.Instrument };
        string[] typed = { "w", "wa", "war", "warm", "warm ", "warm p", "warm pa", "warm pad", "r", "re", "rev", "reve", "kumuki", "кумуки",
                           "comp", "компрессор на б", "компрессор на барабаны", "afl", "vintage", "zz" };
        for (int i = 0; i < 3; i++) foreach (var q in typed) search.Run(q, ctx);   // warm up the JIT
        var times = new List<double>();
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var q in typed) { sw.Restart(); search.Run(q, ctx); times.Add(sw.Elapsed.TotalMilliseconds); }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        times.Sort();
        double median = times[times.Count / 2], worst = times[^1];
        yield return (median <= 8, $"per keystroke on {index.Count:N0} items: median {median:0.0} ms, worst {worst:0.0} ms (budget 8 ms) · index built in {buildMs:0} ms");
        yield return (alloc / typed.Length < 256 * 1024, $"…allocating {alloc / typed.Length / 1024} KB per keystroke (results only, nothing per item)");
    }
}
