// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Colour has one home (spec 011): the theme files in Theme/Themes/*.json. NotaPalette
// declares one slot per token by name and re-tints it from the active theme, and
// NotaThemeService publishes those same brushes to XAML under their Brush.* keys
// (NotaPalette.KeyMap). So instead of keeping two copies of a palette in step, these
// checks keep the *wiring* honest — read as text, with no reference to Nota.App, no
// Avalonia and no UI thread:
//
//   1. NotaTheme.axaml holds no colour at all (no brush, colour or shadow resource);
//   2. every Brush.* key a style or view names is in KeyMap, so it resolves to a slot;
//   3. every Shadow.* key a style names is one NotaThemeService builds.
//
// The theme files themselves are checked in DesignTokenCheck.Themes.cs.
// See DESIGN.md § Colour themes.

using System.Text.RegularExpressions;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var root = RepoRoot();
        if (root is null)
        {
            yield return (false, "repo root located (no Nota.sln above the test binary)");
            yield break;
        }

        var app = Path.Combine(root, "src/managed/Nota.App");
        var theme = File.ReadAllText(Path.Combine(app, "Theme/NotaTheme.axaml"));
        var palette = File.ReadAllText(Path.Combine(app, "Theme/NotaPalette.cs"));
        var service = File.ReadAllText(Path.Combine(app, "Theme/NotaThemeService.cs"));

        // 1. A colour in the XAML would be a second copy that no theme switch reaches.
        var inXaml = Regex.Matches(theme, """<(?<t>SolidColorBrush|Color|BoxShadows)\s+x:Key="(?<k>[^"]+)">""")
            .Select(m => m.Groups["k"].Value).ToArray();
        yield return (inXaml.Length == 0 && !theme.Contains("<ResourceDictionary.ThemeDictionaries>"),
            inXaml.Length == 0 ? "NotaTheme.axaml holds no colour (the theme files are the only home)"
                               : $"colour resources left in NotaTheme.axaml: {string.Join(", ", inXaml.Take(8))}");

        // 2. KeyMap is what NotaThemeService publishes, so a key missing from it is a
        //    DynamicResource that resolves to nothing — the control keeps a stale colour.
        var keyMap = KeyMapKeys(palette);
        yield return (keyMap.Count > 0, $"NotaPalette.KeyMap parsed ({keyMap.Count} keys)");
        var referenced = Files(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:DynamicResource |"")(?<key>Brush\.\w+)[}""]").Select(m => m.Groups["key"].Value))
            .Distinct().Where(k => !keyMap.Contains(k)).OrderBy(k => k).ToArray();
        yield return (referenced.Length == 0,
            referenced.Length == 0 ? "every referenced Brush.* key is published (in KeyMap)"
                                   : $"references to unpublished brush keys: {string.Join(", ", referenced)}");

        // 3. Shadows are values built from theme colours on every switch.
        var built = Regex.Matches(service, @"res\[""(?<k>Shadow\.\w+)""\]").Select(m => m.Groups["k"].Value).ToHashSet();
        var shadowRefs = Files(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:DynamicResource |"")(?<key>Shadow\.\w+)[}""]").Select(m => m.Groups["key"].Value))
            .Distinct().Where(k => !built.Contains(k)).OrderBy(k => k).ToArray();
        yield return (built.Count == 4 && shadowRefs.Length == 0,
            shadowRefs.Length == 0 ? $"every Shadow.* key is built from the theme ({string.Join(", ", built.OrderBy(k => k))})"
                                   : $"Shadow.* keys nothing builds: {string.Join(", ", shadowRefs)}");
    }

    private static HashSet<string> KeyMapKeys(string palette)
    {
        var keyMap = Regex.Match(palette, @"KeyMap\s*=\s*new\([^)]*\)\s*\{(?<body>.*?)\n    \};", RegexOptions.Singleline);
        return keyMap.Success
            ? Regex.Matches(keyMap.Groups["body"].Value, @"\[""(?<key>Brush\.\w+)""\]").Select(m => m.Groups["key"].Value).ToHashSet()
            : new HashSet<string>();
    }

    /// <summary>Geometry: Radius.* / Control.* / Space.* in NotaTheme.axaml must equal the
    /// constants in NotaGeometry.cs, and the view layer must not bring literals, drop
    /// shadows or gradients back. See DESIGN.md § Geometry.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunGeometry()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var theme = File.ReadAllText(Path.Combine(app, "Theme/NotaTheme.axaml"));
        var geo = File.ReadAllText(Path.Combine(app, "Theme/NotaGeometry.cs"));

        // XAML side: <CornerRadius x:Key="Radius.Clip">2</CornerRadius>, <sys:Double x:Key="Space.Tile">8</sys:Double>
        var xaml = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(theme, """x:Key="(?<key>(?:Radius|Control|Space)\.\w+)">(?<v>[0-9.]+)<"""))
            xaml[m.Groups["key"].Value] = double.Parse(m.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);

        // C# side: NotaRadius.<Name>Value, NotaSize.<Name>, NotaSpace.<Name>
        var cs = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(geo, @"\b(?<name>\w+)Value\s*=\s*(?<v>[0-9.]+)"))
            cs["Radius." + m.Groups["name"].Value] = double.Parse(m.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (cls, prefix) in new[] { ("NotaSize", "Control."), ("NotaSpace", "Space.") })
        {
            var body = Regex.Match(geo, $@"class {cls}\s*\{{(?<b>.*?)\n\}}", RegexOptions.Singleline).Groups["b"].Value;
            foreach (Match m in Regex.Matches(body, @"const double (?<name>\w+)\s*=\s*(?<v>[0-9.]+)"))
                cs[prefix + m.Groups["name"].Value] = double.Parse(m.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        var drift = xaml.Keys.Union(cs.Keys).OrderBy(k => k)
            .Where(k => !xaml.TryGetValue(k, out var a) || !cs.TryGetValue(k, out var b) || a != b)
            .Select(k => $"{k}: xaml {(xaml.TryGetValue(k, out var a) ? a : double.NaN)} vs cs {(cs.TryGetValue(k, out var b) ? b : double.NaN)}")
            .ToArray();
        yield return (xaml.Count > 0 && drift.Length == 0,
            drift.Length == 0 ? $"NotaTheme geometry matches NotaGeometry.cs ({xaml.Count} keys)"
                              : $"geometry drift: {string.Join("; ", drift)}");

        // Every Radius/Control/Space key a style or view names must exist.
        var used = Files(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:StaticResource |"")(?<key>(?:Radius|Control|Space)\.\w+)[}""]")
                                  .Select(m => m.Groups["key"].Value))
            .Distinct().Where(k => !xaml.ContainsKey(k)).OrderBy(k => k).ToArray();
        yield return (used.Length == 0,
            used.Length == 0 ? "every referenced Radius/Control/Space key exists"
                             : $"references to undefined geometry keys: {string.Join(", ", used)}");

        // The view layer stays literal-free, flat and shadowless.
        var views = Files(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/Theme/"))
            .Select(f => (Path: Rel(app, f), Text: File.ReadAllText(f))).ToArray();
        string[] Offenders(string pattern) => views.Where(v => Regex.IsMatch(v.Text, pattern)).Select(v => v.Path).OrderBy(p => p).ToArray();

        var radiusLiterals = Offenders(@"new CornerRadius\(\s*[0-9]");
        yield return (radiusLiterals.Length == 0,
            radiusLiterals.Length == 0 ? "no CornerRadius literals in views (NotaRadius only)"
                                       : $"CornerRadius literals in: {string.Join(", ", radiusLiterals)}");
        var shadows = Offenders(@"new BoxShadows?\b");
        yield return (shadows.Length == 0,
            shadows.Length == 0 ? "no drop shadows in views" : $"drop shadows in: {string.Join(", ", shadows)}");
        var gradients = Offenders(@"\b(?:Linear|Radial|Conic)GradientBrush\b|NotaPalette\.VGradient\(");
        yield return (gradients.Length == 0,
            gradients.Length == 0 ? "no gradients in views" : $"gradients in: {string.Join(", ", gradients)}");

        // A Rectangle's RadiusX/RadiusY is a corner radius too, so it keeps the 0/1/2/3/4
        // scale (spec 012 R9: the add-device targets drew at 7 and 6). Ratchet in
        // RadiusAllowList.txt (path + "RadiusX = n") with today's offenders.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var offScale = new List<Hit>();
        foreach (var f in SourceFiles(app, ".cs", ".axaml"))
        {
            int n = 0;
            foreach (var line in File.ReadLines(f))
            {
                n++;
                var code = f.EndsWith(".cs") ? CodeOf(line) : Regex.Replace(line, "<!--.*?-->", "");
                foreach (Match m in Regex.Matches(code, @"\bRadius(?<axis>[XY])\s*=\s*""?(?<v>[0-9]+(?:\.[0-9]+)?)\b"))
                    if (!new[] { 0d, 1, 2, 3, 4 }.Contains(double.Parse(m.Groups["v"].Value, inv)))
                        offScale.Add(new Hit(RelManaged(root, f), n, $"Radius{m.Groups["axis"].Value} = {m.Groups["v"].Value}"));
            }
        }
        var list = ReadRatchet(Path.Combine(root, RatchetDir, "RadiusAllowList.txt"));
        var radiusUsed = new HashSet<(string, string)>();
        yield return RatchetVerdict("Rectangle radius off the 0/1/2/3/4 scale", offScale, list.ToHashSet(), radiusUsed);
        yield return RatchetStale("radius allow-list", list, radiusUsed);
        yield return (true, $"radius allow-list: {list.Distinct().Count()} entries (ratchet: may only shrink)");
    }

    /// <summary>Type: the theme and NotaFonts point at the same bundled Geist files, and no
    /// view names a font of its own or sets text below the 7px floor. See DESIGN.md § Type.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunType()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var theme = File.ReadAllText(Path.Combine(app, "Theme/NotaTheme.axaml"));
        var fonts = File.ReadAllText(Path.Combine(app, "Theme/NotaFonts.cs"));

        string XamlFont(string key) => Regex.Match(theme, $"""<FontFamily x:Key="{key}">(?<v>[^<]+)</FontFamily>""").Groups["v"].Value;
        string CsFont(string name) => Regex.Match(fonts, $@"const string {name}\s*=\s*""(?<v>[^""]+)""").Groups["v"].Value;
        var ui = XamlFont("Font.UI"); var mono = XamlFont("Font.Mono");
        yield return (ui.Length > 0 && ui == CsFont("UiUri") && mono.Length > 0 && mono == CsFont("MonoUri"),
            $"Font.UI / Font.Mono match NotaFonts ({ui} · {mono})");

        var files = new[] { "Regular", "Medium", "SemiBold", "Bold" }
            .SelectMany(w => new[] { $"Geist-{w}.ttf", $"GeistMono-{w}.ttf" })
            .Where(f => !File.Exists(Path.Combine(root, "assets/fonts", f))).ToArray();
        yield return (files.Length == 0,
            files.Length == 0 ? "all eight Geist weights are bundled" : $"missing font files: {string.Join(", ", files)}");
        yield return (File.Exists(Path.Combine(root, "LICENSES/OFL-1.1-Geist.txt")), "the Geist OFL licence ships with the fonts");

        var views = Files(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/Theme/"))
            .Select(f => (Path: Rel(app, f), Text: File.ReadAllText(f))).ToArray();

        // A family named by string resolves to whatever the OS has; FontFamily.Default
        // hides the choice. Both must go through NotaFonts.
        var named = views.Where(v => Regex.IsMatch(v.Text,
                @"new\s+Typeface\s*\(\s*""|Typeface\s+\w+\s*=\s*new\s*\(\s*""|new\s+FontFamily\s*\(\s*""|FontFamily\.Default|FontFamily=""(?!\{)"))
            .Select(v => v.Path).OrderBy(p => p).ToArray();
        yield return (named.Length == 0,
            named.Length == 0 ? "no view names a font family (NotaFonts / Font.* only)"
                              : $"font families named in views: {string.Join(", ", named)}");

        // 7px is the floor. Catches FontSize literals and a FormattedText size literal.
        var small = new List<string>();
        foreach (var v in views)
        {
            foreach (Match m in Regex.Matches(v.Text, @"FontSize\s*=\s*""?(?<n>[0-9]+(?:\.[0-9]+)?)""?"))
                if (double.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) < 7) small.Add($"{v.Path} FontSize {m.Groups["n"].Value}");
            foreach (Match m in Regex.Matches(v.Text, @"FlowDirection\.\w+,\s*[\w.]+,\s*(?<n>[0-9]+(?:\.[0-9]+)?)\s*,"))
                if (double.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) < 7) small.Add($"{v.Path} FormattedText {m.Groups["n"].Value}");
        }
        yield return (small.Count == 0,
            small.Count == 0 ? "no text below the 7px floor" : $"text below 7px: {string.Join("; ", small)}");
    }

    /// <summary>Controls: no icon typed as a character (Glyph draws them), and the shared
    /// controls keep the almanac's fixed sizes. See DESIGN.md § Controls.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunControls()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");

        const string icons = "■□▶▷◀◁●○◆◇★☆✓✔✗✕✖✎❄☰⧉‹›▾▴▸◂▼▲⏵⏸⏹⏺⟳⊘─┄▩⠿＋▤";
        var offenders = new List<string>();
        foreach (var f in Files(app, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/")))
        {
            int n = 0;
            foreach (var line in File.ReadLines(f))
            {
                n++;
                var code = f.EndsWith(".cs") ? line.Split("//")[0] : Regex.Replace(line, "<!--.*?-->", "");
                foreach (Match m in Regex.Matches(code, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                    if (m.Groups[1].Value.IndexOfAny(icons.ToCharArray()) >= 0)
                        offenders.Add($"{Rel(app, f)}:{n}");
            }
        }
        yield return (offenders.Count == 0,
            offenders.Count == 0 ? "no icon glyphs typed as text (Glyph draws them)"
                                 : $"icon glyphs typed as text: {string.Join(", ", offenders.Take(10))}");

        var knob = File.ReadAllText(Path.Combine(app, "Controls/Knob.cs"));
        yield return (knob.Contains("NotaSize.KnobSecondary") && knob.Contains("WidthProperty.OverrideMetadata<Knob>"),
            "Knob snaps to the three almanac sizes");
        var sw = File.ReadAllText(Path.Combine(app, "Controls/SwitchTrack.cs"));
        yield return (Regex.IsMatch(sw, @"W = 18, H = 10, KnobD = 7, Inset = 1\.5"), "SwitchTrack is 18×10 with a 7px knob inset 1.5");
        var sl = File.ReadAllText(Path.Combine(app, "Controls/SliderTrack.cs"));
        yield return (Regex.IsMatch(sl, @"TrackH = 3, HandleW = 6, HandleH = 7"), "SliderTrack is a 3px track with a 6×7 handle");
    }

    /// <summary>Numbers and labels: a display culture with a point and U+2212, units after a
    /// thin space, no vowel-dropped parameter labels. See DESIGN.md § Numbers.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunNumbers()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");

        var num = File.ReadAllText(Path.Combine(app, "Theme/NotaNum.cs"));
        var boot = File.ReadAllText(Path.Combine(app, "App.axaml.cs"));
        yield return (num.Contains("NegativeSign = Minus") && num.Contains("Minus = \"\\u2212\"") && boot.Contains("NotaNum.Install()"),
            "the app installs the display culture (point, U+2212 minus)");

        var rules = new (string Name, Regex Re)[]
        {
            ("hyphen in a negative format section", new(@";-[0#]")),
            ("hyphen before infinity", new("\"-(∞|inf)")),
            ("invariant culture on a readout", new(@"FormattableString\.Invariant\(|ToString\([^()]*CultureInfo\.InvariantCulture\)|string\.Format\(\s*(System\.Globalization\.)?CultureInfo\.InvariantCulture")),
            ("plain space before a unit", new(@"(\}|\d) (dB|dBFS|LUFS|kHz|Hz|ms|st|%)(?=[""\s·,)])|\{[^{}""]*:[0#.+\-−;]+\}(dB|Hz|kHz|ms|s|st|%|LUFS)(?=[""\s·,)])")),
            ("vowel-dropped parameter label", new("\"(FDBK|FEEDB|FB|ATK|DEC|SUS|REL|LVL|DET|SPR|QNT|PIT|TRANSP|AMT|VOL|POS|EMPH|RESON)\"")),
        };
        var hits = rules.ToDictionary(r => r.Name, _ => new List<string>());
        var minus = new List<Hit>();
        foreach (var f in Files(app, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.EndsWith("NotaNum.cs")))
        {
            int n = 0;
            foreach (var line in File.ReadLines(f))
            {
                n++;
                var code = line.Split("//")[0];
                // Only what reaches the screen: the string literals on the line.
                var literals = string.Join("\n", Regex.Matches(code, "\"((?:[^\"\\\\]|\\\\.)*)\"").Select(m => m.Value));
                foreach (var (name, re) in rules)
                    if (re.IsMatch(name.StartsWith("invariant") ? code : literals)) hits[name].Add($"{Rel(app, f)}:{n}");
                var full = CodeOf(line);
                foreach (var (at, body) in Literals(full))
                    if (ShowsHyphenMinus(full, at, Unescape(body))) minus.Add(new Hit(RelManaged(root, f), n, body));
            }
        }
        foreach (var (name, _) in rules)
            yield return (hits[name].Count == 0, hits[name].Count == 0 ? $"no {name}" : $"{name}: {string.Join(", ", hits[name].Take(8))}");

        // A negative number on screen takes U+2212 (spec 012 R9): a typed "-12" is a hyphen,
        // shorter and lower than the minus every formatted readout already shows. Ratchet in
        // NumberAllowList.txt (path + literal) with today's offenders.
        var list = ReadRatchet(Path.Combine(root, RatchetDir, "NumberAllowList.txt"));
        var allowed = list.ToHashSet();
        var used = new HashSet<(string, string)>();
        yield return RatchetVerdict("hyphen-minus before a digit in a view literal (use U+2212)", minus, allowed, used);
        yield return RatchetStale("number allow-list", list, used);
        yield return (true, $"number allow-list: {allowed.Count} entries (ratchet: may only shrink)");
    }

    /// <summary>A literal that puts a hyphen-minus in front of a number on screen ("-12",
    /// "+6 / -48"). Not: format strings, key or shortcut text, path data, version strings, or
    /// a hyphen inside a word or range ("track-1", "1-127" — the range rule is DESIGN.md's).</summary>
    private static bool ShowsHyphenMinus(string code, int at, string text)
    {
        if (!Regex.IsMatch(text, @"(?<![\p{L}\p{N}_.\-])-[0-9]")) return false;
        if (text.Contains('{') || text.Contains('#') || Regex.IsMatch(text, @"^[\s+\-0.,;%‰]*$")) return false;   // format
        if (Regex.IsMatch(code[..at], @"(?:ToString|Format|TryParse|Parse)\(\s*$")) return false;                   // format / parse argument
        if (Regex.IsMatch(text, @"Ctrl|Cmd|Shift|Alt\b|[⌘⌥⇧⌃]")) return false;                                    // key text
        if (PathData.IsMatch(text) || Regex.IsMatch(text, @"[0-9]\s+[LlHhVvCcAaZz]\s*-?[0-9]")) return false;     // path data
        if (Regex.IsMatch(text, @"\b\d+\.\d+\.\d+")) return false;                                                // version
        return true;
    }

    /// <summary>Visualisers: one graph window, no fills under curves, meters on the almanac
    /// scale with no ballistics, a playhead without glow. See DESIGN.md § Visualisers.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunVisualisers()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var views = Files(Path.Combine(app, "Controls"), "*.cs")
            .Select(f => (Path: Rel(app, f), Text: File.ReadAllText(f))).ToArray();
        // Deliberate exception: MixBar is a level strip, not a graph window.
        var exempt = new HashSet<string> { "Controls/MixBar.cs" };
        string[] Offenders(string pattern) => views.Where(v => !exempt.Contains(v.Path) && Regex.IsMatch(v.Text, pattern)).Select(v => v.Path).OrderBy(p => p).ToArray();

        var frames = Offenders(@"DrawRectangle\((?:Sunken|Bg|Well|NotaPalette\.BgSunken), (?:new Pen\(\w+(?:, 1)?\)|null), new Rect\(0, 0, w, h\), \d+, \d+\)");
        yield return (frames.Length == 0,
            frames.Length == 0 ? "graphs draw their window through NotaGraph.Window" : $"hand-drawn graph windows in: {string.Join(", ", frames)}");

        var fills = Offenders(@"DrawGeometry\((?:\w*Fill\w*|new SolidColorBrush\(Color\.FromArgb\(0x[0-9A-F]{2}, col\.R|ChamberInk\.Alpha\([^)]*\)), null, (?:fill|geo)\)");
        yield return (fills.Length == 0,
            fills.Length == 0 ? "no fills under curves" : $"fills under curves in: {string.Join(", ", fills)}");

        var ballistics = Offenders(@"_specDb\[k\] - [0-9.]+|Peak \* 0\.9|_pk[LR] \* 0\.9|FallPerTick");
        yield return (ballistics.Length == 0,
            ballistics.Length == 0 ? "meters and spectra have no release ballistics" : $"meter/spectrum ballistics in: {string.Join(", ", ballistics)}");

        var meter = File.ReadAllText(Path.Combine(app, "Controls/MeterBar.cs"));
        yield return (Regex.IsMatch(meter, @"ChannelW = 11, Step = 5"), "level meter channels are 11px with a 5px step");

        var arr = string.Concat(Files(app, "ArrangementView*.cs").Select(File.ReadAllText));
        yield return (!arr.Contains("PlayheadGlow") && Regex.IsMatch(arr, @"PlayheadPen = new Pen\(PlayheadBrush, 1\)"),
            "the arrangement playhead is a 1px accent line with no glow");
    }

    /// <summary>The almanac's closed list of bans (§ 9) that the other checks do not already
    /// hold: colour literals outside the palette, a second hue typed in as RGB, named system
    /// colours, opacity as a state, and brushes that snapshot a theme colour. Gradients,
    /// CornerRadius literals and type under 7px are enforced in RunGeometry / RunType.
    /// See DESIGN.md § Enforced by tests.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunBans()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");

        // Hex strings that are data rather than tokens: tag swatches the library stores, and
        // two device hue tables. Each is only ever painted through NotaPalette.Ink().
        var hexData = new[] { "TagEditorWindow.cs", "DeviceCards/Bodies/StrataDeviceBody.cs", "DeviceCards/Instruments/RhythmInstrumentCard.cs" };
        // Opacity is allowed for exactly one thing: the ghost of a card being dragged.
        var dragGhost = new Regex(@"\b(card|_devDragCard)\.Opacity = (0\.55|1\.0);");

        var rules = new (string Name, Regex Re, Func<string, string, bool>? Allow)[]
        {
            ("hex colour outside NotaPalette.Ink()", new(@"""#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?"""),
                (file, line) => line.Contains("Ink(") || line.Contains("InkColor(") || hexData.Contains(file)),
            ("RGB colour typed as numbers", new(@"Color\.From(?:Argb|Rgb)\((?:[^,()]+,\s*)?(?<r>0x[0-9A-Fa-f]{2}|\d{1,3})\s*,\s*(?<g>0x[0-9A-Fa-f]{2}|\d{1,3})\s*,\s*(?<b>0x[0-9A-Fa-f]{2}|\d{1,3})\s*\)|Color\.Parse\("),
                (_, line) => Regex.IsMatch(line, @"Color\.From(?:Argb|Rgb)\((?:[^,()]+,\s*)?(?:0x00|0)\s*,\s*(?:0x00|0)\s*,\s*(?:0x00|0)\s*\)")),   // black shade is not a hue
            ("named system colour", new(@"\b(?:Colors|Brushes)\.(?!Transparent\b)[A-Z]\w*"), null),
            ("Opacity used as a state", new(@"\bOpacity\s*=\s*[0-9.]"), (_, line) => dragGhost.IsMatch(line)),
            ("brush snapshotting a theme colour", new(@"new (?:[\w.]+\.)?SolidColorBrush\(\s*(?:NotaPalette\.\w+(?:\.Color)?\s*[,)]|[\w.]*TrackColor(?:ForIndex)?\(|NotaPalette\.(?:TrackColors|ReturnColors)\[)|static readonly (?:I?Solid\w*Brush|IBrush)(?:\[\])?\s+\w+\s*=\s*new (?:[\w.]+\.)?SolidColorBrush\((?!Color\.FromArgb\(0x[0-9A-Fa-f]{2}, 0x00, 0x00, 0x00\))|static readonly Color(?:\[\])?\s+\w+\s*=.*NotaPalette"), null),
        };
        var hits = rules.ToDictionary(r => r.Name, _ => new List<string>());
        foreach (var f in Files(app, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/")
                              && !f.EndsWith("Theme/NotaPalette.cs") && !f.EndsWith("Theme/NotaTheme.axaml")
                              && !f.EndsWith("Theme/NotaColourTheme.cs")))   // the theme loader parses colour values by definition
        {
            var rel = Rel(app, f);
            int n = 0;
            foreach (var line in File.ReadLines(f))
            {
                n++;
                var code = f.EndsWith(".cs") ? line.Split("//")[0] : Regex.Replace(line, "<!--.*?-->", "");
                foreach (var (name, re, allow) in rules)
                    if (re.IsMatch(code) && !(allow?.Invoke(rel, code) ?? false)) hits[name].Add($"{rel}:{n}");
            }
        }
        foreach (var (name, _, _) in rules)
            yield return (hits[name].Count == 0, hits[name].Count == 0 ? $"no {name}" : $"{name}: {string.Join(", ", hits[name].Take(8))}");
    }

    /// <summary>Layout: device cards 700 wide with a 22px header (documented exceptions only),
    /// transport at the top, list rows 26. See DESIGN.md § Device cards and § Shell.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunLayout()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");

        var kit = File.ReadAllText(Path.Combine(app, "DeviceCardKit.cs"));
        yield return (Regex.IsMatch(kit, @"HeaderH = 22;"), "device card header is 22px");

        // Accepted exceptions (decided 2026-09-16): parameter stubs are narrow; the Bass and
        // Physical instruments keep their wider layouts. (Plug-ins are S 260 / L 700.) The
        // generic instrument card is an "Open GUI" stub, 170 wide since before it joined the
        // shared shell (spec 012).
        // Volt grew to 790 when it absorbed Nota Synth and Nota Bass (specs/026).
        var exempt = new HashSet<string> { "GenericParamDeviceBody.cs", "GenericMidiBody.cs",
            "BassInstrumentCard.cs", "PhysicalInstrumentCard.cs", "GenericInstrumentCard.cs", "VoltInstrumentCard.cs" };
        var off = Files(Path.Combine(app, "DeviceCards"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !exempt.Contains(Path.GetFileName(f)))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"public double (?:Card)?Width => (?<w>[0-9]+);")
                .Where(m => m.Groups["w"].Value != "700").Select(m => $"{Path.GetFileName(f)} ({m.Groups["w"].Value})"))
            .ToArray();
        yield return (off.Length == 0, off.Length == 0 ? "device cards are 700 wide (documented exceptions aside)" : $"cards off the 700 format: {string.Join(", ", off)}");

        var main = File.ReadAllText(Path.Combine(app, "MainWindow.axaml"));
        yield return (Regex.IsMatch(main, "x:Name=\"TransportBar\" DockPanel.Dock=\"Top\"[^>]*Height=\"\\{StaticResource Control.TransportBar\\}\""),
            "transport is a docked row at the top of the window");
        yield return (Regex.IsMatch(main, "<Border Grid.Column=\"1\" Classes=\"console\">"),
            "transport console is the recessed group inside it");

        var browser = File.ReadAllText(Path.Combine(app, "BrowserView.cs"));
        yield return (Regex.IsMatch(browser, @"const double RowH = 2[6-8];"), "browser list rows are 26–28px");
    }

    /// <summary>The Studio rules (DESIGN.md) that the older checks do not hold:
    /// the 0/1/2/3/4 radius scale, blurred shadows only on floating layers, sentence-case
    /// labels without tracking, values neutral at rest in the shared controls, the docked
    /// shell's sizes, and no value left from the earlier Ember palette.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunStudio()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var theme = File.ReadAllText(Path.Combine(app, "Theme/NotaTheme.axaml"));
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // Radius: 0 docked · 1 plot bars · 2 inner keys, rows, clips · 3 controls, cards · 4 modals.
        var radii = Regex.Matches(theme, """<CornerRadius x:Key="(?<k>Radius\.\w+)">(?<v>[0-9.]+)</CornerRadius>""")
            .Where(m => !new[] { 0d, 1, 2, 3, 4 }.Contains(double.Parse(m.Groups["v"].Value, inv)))
            .Select(m => $"{m.Groups["k"].Value} {m.Groups["v"].Value}").ToArray();
        yield return (radii.Length == 0,
            radii.Length == 0 ? "every Radius.* is on the 0/1/2/3/4 scale" : $"radii off the scale: {string.Join(", ", radii)}");

        // A blurred shadow means the element floats. Only Shadow.Popover / Shadow.Modal may
        // carry a blur; every other shadow is a bevel or an edge. They are built from the
        // theme in NotaThemeService, one res["Shadow.*"] = … statement each.
        var service = File.ReadAllText(Path.Combine(app, "Theme/NotaThemeService.cs"));
        var blurred = Regex.Matches(service, @"res\[""(?<k>Shadow\.\w+)""\]\s*=(?<v>[^;]+);")
            .Where(m => m.Groups["v"].Value.Contains("Blur") && m.Groups["k"].Value is not ("Shadow.Popover" or "Shadow.Modal"))
            .Select(m => m.Groups["k"].Value).ToList();
        yield return (blurred.Count == 0,
            blurred.Count == 0 ? "only Shadow.Popover / Shadow.Modal carry a blur (floating layers)" : $"blurred shadows on docked surfaces: {string.Join(", ", blurred.Distinct())}");
        var axamlShadows = Files(app, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.EndsWith("Theme/NotaTheme.axaml"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), """BoxShadow="(?!\{)"""))
            .Select(f => Rel(app, f)).ToArray();
        yield return (axamlShadows.Length == 0,
            axamlShadows.Length == 0 ? "views take shadows from Shadow.* resources only" : $"literal BoxShadow in: {string.Join(", ", axamlShadows)}");

        // No shouting: labels are sentence case with no tracking. The theme's text classes
        // carry no positive LetterSpacing, NotaType's tracking constants are 0, and the shared
        // label builders do not upper-case.
        var tracked = Regex.Matches(theme, """<Setter Property="LetterSpacing" Value="(?<v>[0-9.]+)"/>""")
            .Where(m => double.Parse(m.Groups["v"].Value, inv) > 0).Count();
        yield return (tracked == 0, tracked == 0 ? "no tracked (letter-spaced) text class in the theme" : $"{tracked} text style(s) still set LetterSpacing");
        // …nor do views: a positive LetterSpacing anywhere in the app is tracking by the back door.
        // The one exception is data, not a label: the Remote pairing code, spaced to be read aloud.
        var viewTracking = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"LetterSpacing\s*=\s*""?(?<v>[0-9.]+)")
                .Where(m => double.Parse(m.Groups["v"].Value, inv) > 0)
                .Select(_ => Path.GetRelativePath(app, f)))
            .Where(r => r.Replace('\\', '/') != "Remote/ConnectPhoneView.cs").Distinct().ToArray();
        yield return (viewTracking.Length == 0,
            viewTracking.Length == 0 ? "no view sets a positive LetterSpacing" : $"positive LetterSpacing in: {string.Join(", ", viewTracking)}");
        var fonts = File.ReadAllText(Path.Combine(app, "Theme/NotaFonts.cs"));
        var trackConsts = Regex.Matches(fonts, @"(?<n>\w+Tracking)\s*=\s*(?<v>[0-9.]+)").Where(m => double.Parse(m.Groups["v"].Value, inv) != 0).Select(m => m.Groups["n"].Value).ToArray();
        yield return (trackConsts.Length == 0, trackConsts.Length == 0 ? "NotaType tracking constants are 0" : $"tracking left in NotaType: {string.Join(", ", trackConsts)}");
        var shouters = new[] { "DeviceCardKit.cs", "Controls/NotaGraph.cs", "ClipEditorKit.cs", "DeviceChainView.Shell.cs", "../Nota.Application/Palette/PaletteSearch.cs" }
            .Where(f => File.ReadAllText(Path.Combine(app, f)).Contains("ToUpperInvariant()")).ToArray();
        yield return (shouters.Length == 0,
            shouters.Length == 0 ? "the shared label builders do not upper-case (NotaText.Label)" : $"upper-casing label builders: {string.Join(", ", shouters)}");

        // Live or chosen: the shared value controls draw a resting value in neutral ink.
        var knob = File.ReadAllText(Path.Combine(app, "Controls/Knob.cs"));
        var graph = File.ReadAllText(Path.Combine(app, "Controls/NotaGraph.cs"));
        var slider = File.ReadAllText(Path.Combine(app, "Controls/SliderTrack.cs"));
        yield return (knob.Contains("RestArc = NotaPalette.VizInk") && graph.Contains("PrimaryPen = new Pen(NotaPalette.VizInk")
                      && slider.Contains("Fill = NotaPalette.VizInk"),
            "knob arcs, slider fills and graph curves are neutral at rest (accent only while live)");

        // The docked shell: a 44px transport row with a 30px console, panels on a 1px seam.
        var geo = File.ReadAllText(Path.Combine(app, "Theme/NotaGeometry.cs"));
        yield return (Regex.IsMatch(geo, @"TransportBar = 44;") && Regex.IsMatch(geo, @"Console = 30;") && Regex.IsMatch(geo, @"Gutter = 1;"),
            "transport row 44 · console 30 · panels meet on a 1px seam");

        // No value of the earlier Ember palette is in a Studio theme (the Ember themes are
        // Ember by definition — spec 011 FR-016).
        var studio = string.Join("\n", Directory.EnumerateFiles(Path.Combine(app, "Theme/Themes"), "studio-*.json").Select(File.ReadAllText));
        var left = EmberHexes.Where(h => studio.Contains(h, StringComparison.OrdinalIgnoreCase)).ToArray();
        yield return (studio.Length > 0 && left.Length == 0,
            left.Length == 0 ? "no Ember palette value in the Studio themes" : $"Ember values in Studio themes: {string.Join(", ", left)}");
    }

    // Signature values of the Ember palette (0.52.1): brass accents, warm grounds.
    private static readonly string[] EmberHexes = { "#D8A03D", "#0B0A09", "#141310", "#E9E4D8", "#A87415", "#EAE5D7", "#241F17", "#F0C060" };

    /// <summary>Icons: one icon set. Glyph draws every icon from its own path data, so path
    /// geometry anywhere else in the app is an icon drawn outside the set (spec 012 R8).
    /// Today's call sites sit in IconAllowList.txt as a per-file count that may only go down
    /// — a file above its count has a new inline icon, a file below it must lower the count.
    /// See DESIGN.md § Controls.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunIcons()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");

        var parse = new Regex(@"\b(?:Stream|Path)?Geometry\.Parse\(");
        var xamlPath = new Regex(@"\b(?:Data|Figures)=""\s*[MmFf]|<(?:StreamGeometry|PathGeometry)\b[^>]*>\s*[MmFf]");
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in SourceFiles(app, ".cs", ".axaml").Where(f => !Norm(f).EndsWith("/Controls/Glyph.cs")))
        {
            int n = 0;
            if (f.EndsWith(".cs"))
                foreach (var line in File.ReadLines(f))
                {
                    var code = CodeOf(line);
                    // A parsed constant and the constant itself are two sites: both go when
                    // the icon becomes a Glyph kind.
                    n += Math.Max(parse.Matches(code).Count,
                        Literals(code).Count(l => PathData.IsMatch(Unescape(l.Body))));
                }
            else
                n = xamlPath.Matches(BlankXmlComments(File.ReadAllText(f))).Count;
            if (n > 0) counts[RelManaged(root, f)] = n;
        }

        var allowed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (path, key) in ReadRatchet(Path.Combine(root, RatchetDir, "IconAllowList.txt")))
            allowed[path] = int.TryParse(key, out var c) ? c : 0;
        var over = counts.Where(kv => kv.Value > allowed.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key} ({kv.Value}, allowed {allowed.GetValueOrDefault(kv.Key)})").ToArray();
        yield return (over.Length == 0,
            over.Length == 0 ? "no new icon geometry outside Glyph (Geometry.Parse, Data=\"M…\", path strings)"
                             : $"icon geometry outside Glyph: {string.Join(", ", over.Take(8))}");
        var slack = allowed.Where(kv => kv.Value > counts.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key} {kv.Value} → {counts.GetValueOrDefault(kv.Key)}").ToArray();
        yield return (slack.Length == 0,
            slack.Length == 0 ? "icon allow-list counts are tight" : $"icon allow-list above today's count (lower it): {string.Join(", ", slack.Take(8))}");
        yield return (true, $"icon allow-list: {allowed.Values.Sum()} sites in {allowed.Count} files (ratchet: may only shrink)");
    }

    // ---- shared by the ratchet checks (labels, icons, minus signs, radii) ---------------
    //
    // A ratchet starts green by listing today's offenders in a text file next to this one,
    // so the check lands before the fixes do. Every later change can only remove lines:
    // a new offender fails, and so does a line that no longer matches anything (stale).

    private const string RatchetDir = "tests/Nota.SmokeTest";

    /// <summary>The regular string literals on a C# line (comment already stripped), with
    /// their index and body as written. Interpolated, verbatim and raw strings are not
    /// returned — their text is built, not written — but a regular literal inside an
    /// interpolation hole is.</summary>
    private static List<(int Index, string Body)> Literals(string code)
    {
        var list = new List<(int, string)>();
        for (int i = 0; i < code.Length;)
            i = code[i] is '"' or '\'' or '$' or '@' ? Lex(code, i, list) : i + 1;
        return list;
    }

    /// <summary>Scans the literal that starts at <paramref name="i"/> (if one does) and returns
    /// the index after it, adding regular string literals to <paramref name="list"/>.</summary>
    private static int Lex(string s, int i, List<(int, string)> list)
    {
        char c = s[i];
        if (c == '\'')
        {
            for (i++; i < s.Length && s[i] != '\''; i += s[i] == '\\' ? 2 : 1) { }
            return i + 1;
        }
        if (c == '"')
        {
            if (i + 2 < s.Length && s[i + 1] == '"' && s[i + 2] == '"') return s.Length;   // raw string: rest of the line
            int start = i;
            for (i++; i < s.Length && s[i] != '"'; i += s[i] == '\\' ? 2 : 1) { }
            list.Add((start, s[(start + 1)..Math.Min(i, s.Length)]));
            return i + 1;
        }
        int j = i;
        while (j < s.Length && s[j] is '$' or '@') j++;
        if (j >= s.Length || s[j] != '"') return i + 1;                                   // @identifier, $ elsewhere
        bool verbatim = s[i..j].Contains('@'), interpolated = s[i..j].Contains('$');
        int depth = 0;
        for (i = j + 1; i < s.Length;)
        {
            char d = s[i];
            if (depth > 0)
            {
                if (d is '"' or '\'' or '$' or '@') { i = Lex(s, i, list); continue; }
                if (d == '{') depth++; else if (d == '}') depth--;
                i++;
            }
            else if (d == '"')
            {
                if (verbatim && i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
                return i + 1;
            }
            else if (d == '\\' && !verbatim) i += 2;
            else if (d == '{' && interpolated)
            {
                if (i + 1 < s.Length && s[i + 1] == '{') { i += 2; continue; }
                depth = 1; i++;
            }
            else i++;
        }
        return i;
    }

    /// <summary>SVG path data: an optional fill rule (F0/F1), then a move-to with two coordinates.</summary>
    private static readonly Regex PathData = new(@"^\s*(?:F[01]\s*)?[Mm]\s*-?[0-9.]+[\s,]+-?[0-9.]");

    private readonly record struct Hit(string Path, int Line, string Key);

    private static string Norm(string path) => path.Replace('\\', '/');

    /// <summary>Path relative to src/managed with forward slashes — the allow-list key.</summary>
    private static string RelManaged(string root, string file) => Norm(Path.GetRelativePath(Path.Combine(root, "src/managed"), file));

    private static IEnumerable<string> SourceFiles(string dir, params string[] exts) =>
        Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => exts.Any(e => f.EndsWith(e)) && !Norm(f).Contains("/obj/") && !Norm(f).Contains("/bin/"))
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>A C# line without its // comment; a // inside a string or char literal stays
    /// (the older checks split on "//" and lose the rest of a line holding a URL).</summary>
    private static string CodeOf(string line)
    {
        var scratch = new List<(int, string)>();
        for (int i = 0; i < line.Length;)
        {
            if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/') return line[..i];
            i = line[i] is '"' or '\'' or '$' or '@' ? Lex(line, i, scratch) : i + 1;
        }
        return line;
    }

    /// <summary>axaml with its comments blanked out; line breaks stay so line numbers hold.</summary>
    private static string BlankXmlComments(string text) =>
        Regex.Replace(text, "<!--.*?-->", m => Regex.Replace(m.Value, "[^\n]", " "), RegexOptions.Singleline);

    /// <summary>The text a C# literal body stands for (\" \\ \uXXXX …).</summary>
    private static string Unescape(string body)
    {
        try { return Regex.Unescape(body); } catch (ArgumentException) { return body; }
    }

    /// <summary>A ratchet allow-list: one `path&lt;TAB&gt;key&lt;TAB&gt;# reason` per line, where
    /// path is relative to src/managed. Blank lines and lines starting with # are comments.</summary>
    private static List<(string Path, string Key)> ReadRatchet(string file)
    {
        var list = new List<(string, string)>();
        if (!File.Exists(file)) return list;
        foreach (var raw in File.ReadLines(file))
        {
            if (raw.Length == 0 || raw.StartsWith('#')) continue;
            var parts = raw.Split('\t');
            if (parts.Length >= 2) list.Add((parts[0].Trim(), parts[1]));
        }
        return list;
    }

    /// <summary>One rule's verdict against a path+key ratchet. Allow-listed hits pass and are
    /// marked used, so the caller can report the entries nothing matched.</summary>
    private static (bool Ok, string Label) RatchetVerdict(string what, IReadOnlyList<Hit> hits,
        HashSet<(string, string)> allowed, HashSet<(string, string)> used)
    {
        var fresh = new List<Hit>();
        foreach (var h in hits)
            if (allowed.Contains((h.Path, h.Key))) used.Add((h.Path, h.Key));
            else fresh.Add(h);
        var listed = hits.Count - fresh.Count;
        return fresh.Count == 0
            ? (true, $"no {what} ({listed} allow-listed)")
            : (false, $"{what}: {string.Join(", ", fresh.Take(8).Select(h => $"{h.Path}:{h.Line} \"{h.Key}\""))}"
                      + (fresh.Count > 8 ? $" (+{fresh.Count - 8} more)" : ""));
    }

    /// <summary>Entries that matched nothing: the offender was fixed, so the line must go.</summary>
    private static (bool Ok, string Label) RatchetStale(string list, IEnumerable<(string Path, string Key)> entries,
        HashSet<(string, string)> used)
    {
        var stale = entries.Distinct().Where(e => !used.Contains(e)).Select(e => $"{e.Path} \"{e.Key}\"").ToArray();
        return stale.Length == 0
            ? (true, $"{list} has no stale entries")
            : (false, $"stale {list} entries (fixed — remove them): {string.Join(", ", stale.Take(8))}"
                      + (stale.Length > 8 ? $" (+{stale.Length - 8} more)" : ""));
    }

    // The rules match paths written with '/' ("/obj/", "Theme/NotaPalette.cs"); Windows
    // hands back '\', so every path is normalised on the way in.
    private static IEnumerable<string> Files(string dir, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        => Directory.EnumerateFiles(dir, pattern, option).Select(f => f.Replace('\\', '/'));

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Walk up from the test binary until the solution file appears.</summary>
    private static string? RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Nota.sln"))) d = d.Parent;
        return d?.FullName;
    }
}
