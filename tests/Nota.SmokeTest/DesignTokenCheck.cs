// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The palette lives in two files that nothing but discipline kept in step:
// Theme/NotaTheme.axaml serves XAML through {DynamicResource Brush.*}, and
// Theme/NotaPalette.cs serves the custom-drawn layer, which cannot cheaply
// resolve a resource inside a Render override. A token changed in one and
// missed in the other is invisible until someone opens the other variant.
//
// So this reads both as text — no reference to Nota.App, no Avalonia, no UI
// thread — and enforces what the two files claim about each other:
//
//   1. the Dark and Light dictionaries declare the same keys;
//   2. a NotaPalette slot tagged `// Brush.Key` holds the same dark/light pair
//      that NotaTheme.axaml gives that key;
//   3. every key NotaPalette.KeyMap hands out actually exists in the theme.
//
// See DESIGN.md § How theming works.

using System.Text.RegularExpressions;

namespace Nota.SmokeTest;

internal static class DesignTokenCheck
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var root = RepoRoot();
        if (root is null)
        {
            yield return (false, "repo root located (no Nota.sln above the test binary)");
            yield break;
        }

        var themePath = Path.Combine(root, "src/managed/Nota.App/Theme/NotaTheme.axaml");
        var palettePath = Path.Combine(root, "src/managed/Nota.App/Theme/NotaPalette.cs");
        if (!File.Exists(themePath) || !File.Exists(palettePath))
        {
            yield return (false, "both palette files found");
            yield break;
        }

        var theme = File.ReadAllText(themePath);
        var palette = File.ReadAllText(palettePath);

        var dark = Brushes(theme, "Dark");
        var light = Brushes(theme, "Light");

        // 1. Both variants must declare the same keys: a token added to one branch
        //    and not the other is a colour that vanishes when the variant flips.
        var onlyDark = dark.Keys.Except(light.Keys).OrderBy(k => k).ToArray();
        var onlyLight = light.Keys.Except(dark.Keys).OrderBy(k => k).ToArray();
        yield return (onlyDark.Length == 0 && onlyLight.Length == 0,
            onlyDark.Length == 0 && onlyLight.Length == 0
                ? $"NotaTheme Dark and Light declare the same {dark.Count} keys"
                : $"NotaTheme variants disagree — only Dark: [{string.Join(", ", onlyDark)}]; only Light: [{string.Join(", ", onlyLight)}]");

        // 2. Every mirrored slot must carry the theme's pair. A slot claims its
        //    mirror in a trailing comment: T("#dark", "#light"); // Brush.Key — note
        var slot = new Regex(
            """public\s+static\s+readonly\s+SolidColorBrush\s+(?<name>\w+)\s*=\s*T\(\s*"(?<d>#[0-9A-Fa-f]{6,8})"\s*,\s*"(?<l>#[0-9A-Fa-f]{6,8})"\s*\)\s*;\s*//\s*(?<key>Brush\.\w+)""",
            RegexOptions.Compiled);

        var mismatched = new List<string>();
        int mirrored = 0;
        foreach (Match m in slot.Matches(palette))
        {
            var key = m.Groups["key"].Value;
            if (!dark.TryGetValue(key, out var td) || !light.TryGetValue(key, out var tl))
            {
                mismatched.Add($"{m.Groups["name"].Value} claims {key}, which the theme does not declare");
                continue;
            }
            mirrored++;
            var sd = m.Groups["d"].Value;
            var sl = m.Groups["l"].Value;
            if (!Same(sd, td)) mismatched.Add($"{key} dark: palette {sd} vs theme {td}");
            if (!Same(sl, tl)) mismatched.Add($"{key} light: palette {sl} vs theme {tl}");
        }

        yield return (mirrored > 0, $"NotaPalette mirrors {mirrored} theme keys by name");
        yield return (mismatched.Count == 0,
            mismatched.Count == 0
                ? "every mirrored slot matches the theme in both variants"
                : $"palette/theme drift ({mismatched.Count}): {string.Join("; ", mismatched.Take(8))}");

        // 3. KeyMap is how a code-built view resolves a token by its XAML key, so
        //    every key it offers has to be a key the theme really defines.
        var keyMap = Regex.Match(palette, @"KeyMap\s*=\s*new\([^)]*\)\s*\{(?<body>.*?)\n    \};", RegexOptions.Singleline);
        var unknown = new List<string>();
        int mapped = 0;
        if (keyMap.Success)
        {
            foreach (Match m in Regex.Matches(keyMap.Groups["body"].Value, @"\[""(?<key>Brush\.\w+)""\]"))
            {
                mapped++;
                if (!dark.ContainsKey(m.Groups["key"].Value)) unknown.Add(m.Groups["key"].Value);
            }
        }
        yield return (keyMap.Success && mapped > 0, $"NotaPalette.KeyMap parsed ({mapped} keys)");
        yield return (unknown.Count == 0,
            unknown.Count == 0
                ? "every KeyMap key exists in NotaTheme"
                : $"KeyMap offers keys the theme lacks: {string.Join(", ", unknown)}");
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
        var used = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:StaticResource |"")(?<key>(?:Radius|Control|Space)\.\w+)[}""]")
                                  .Select(m => m.Groups["key"].Value))
            .Distinct().Where(k => !xaml.ContainsKey(k)).OrderBy(k => k).ToArray();
        yield return (used.Length == 0,
            used.Length == 0 ? "every referenced Radius/Control/Space key exists"
                             : $"references to undefined geometry keys: {string.Join(", ", used)}");

        // The view layer stays literal-free, flat and shadowless.
        var views = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/Theme/"))
            .Select(f => (Path: Path.GetRelativePath(app, f), Text: File.ReadAllText(f))).ToArray();
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

        var views = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/Theme/"))
            .Select(f => (Path: Path.GetRelativePath(app, f), Text: File.ReadAllText(f))).ToArray();

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
        foreach (var f in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/")))
        {
            int n = 0;
            foreach (var line in File.ReadLines(f))
            {
                n++;
                var code = f.EndsWith(".cs") ? line.Split("//")[0] : Regex.Replace(line, "<!--.*?-->", "");
                foreach (Match m in Regex.Matches(code, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                    if (m.Groups[1].Value.IndexOfAny(icons.ToCharArray()) >= 0)
                        offenders.Add($"{Path.GetRelativePath(app, f)}:{n}");
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
        foreach (var f in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
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
                    if (re.IsMatch(name.StartsWith("invariant") ? code : literals)) hits[name].Add($"{Path.GetRelativePath(app, f)}:{n}");
            }
        }
        foreach (var (name, _) in rules)
            yield return (hits[name].Count == 0, hits[name].Count == 0 ? $"no {name}" : $"{name}: {string.Join(", ", hits[name].Take(8))}");
    }

    /// <summary>Visualisers: one graph window, no fills under curves, meters on the almanac
    /// scale with no ballistics, a playhead without glow. See DESIGN.md § Visualisers.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunVisualisers()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var views = Directory.EnumerateFiles(Path.Combine(app, "Controls"), "*.cs")
            .Append(Path.Combine(app, "EqCurve.cs"))
            .Select(f => (Path: Path.GetRelativePath(app, f), Text: File.ReadAllText(f))).ToArray();
        // Deliberate exceptions: MixBar is a level strip, not a graph window; RhythmWaveViz
        // draws the waveform itself as a shape, which is data, not a fill under a curve.
        var exempt = new HashSet<string> { "Controls/MixBar.cs", "Controls/RhythmWaveViz.cs" };
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

        var arr = string.Concat(Directory.EnumerateFiles(app, "ArrangementView*.cs").Select(File.ReadAllText));
        yield return (!arr.Contains("PlayheadGlow") && Regex.IsMatch(arr, @"PlayheadPen = new Pen\(PlayheadBrush, 1\)"),
            "the arrangement playhead is a 1px brass line with no glow");
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
        foreach (var f in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/")
                              && !f.EndsWith("Theme/NotaPalette.cs") && !f.EndsWith("Theme/NotaTheme.axaml")))
        {
            var rel = Path.GetRelativePath(app, f);
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

        // Accepted exceptions (decided 2026-09-16): plug-in / parameter stubs are narrow; the
        // Rhythm, Flux, Bass and Physical instruments keep their wider layouts.
        var exempt = new HashSet<string> { "PluginDeviceBody.cs", "GenericParamDeviceBody.cs", "GenericMidiBody.cs",
            "RhythmInstrumentCard.cs", "FluxInstrumentCard.cs", "BassInstrumentCard.cs", "PhysicalInstrumentCard.cs" };
        var off = Directory.EnumerateFiles(Path.Combine(app, "DeviceCards"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !exempt.Contains(Path.GetFileName(f)))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"public double (?:Card)?Width => (?<w>[0-9]+);")
                .Where(m => m.Groups["w"].Value != "700").Select(m => $"{Path.GetFileName(f)} ({m.Groups["w"].Value})"))
            .ToArray();
        yield return (off.Length == 0, off.Length == 0 ? "device cards are 700 wide (documented exceptions aside)" : $"cards off the 700 format: {string.Join(", ", off)}");

        var main = File.ReadAllText(Path.Combine(app, "MainWindow.axaml"));
        yield return (Regex.IsMatch(main, "x:Name=\"TransportBar\" DockPanel.Dock=\"Top\"[^>]*Height=\"\\{StaticResource Control.Console\\}\""),
            "transport is a 42px strip at the top of the window");

        var browser = File.ReadAllText(Path.Combine(app, "BrowserView.cs"));
        yield return (Regex.IsMatch(browser, @"const double RowH = 2[6-8];"), "browser list rows are 26–28px");
    }

    /// <summary>The Brush.* keys and values of one ThemeDictionary.</summary>
    private static Dictionary<string, string> Brushes(string theme, string variant)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var block = Regex.Match(theme,
            $"""x:Key="{variant}">(?<body>.*?)\n        </ResourceDictionary>""", RegexOptions.Singleline);
        if (!block.Success) return dict;
        foreach (Match m in Regex.Matches(block.Groups["body"].Value,
                     """<SolidColorBrush\s+x:Key="(?<key>Brush\.\w+)">(?<val>#[0-9A-Fa-f]{6,8})</SolidColorBrush>"""))
            dict[m.Groups["key"].Value] = m.Groups["val"].Value;
        return dict;
    }

    // #RRGGBB and #FFRRGGBB name the same colour; compare on the opaque form.
    private static bool Same(string a, string b) => Norm(a) == Norm(b);

    private static string Norm(string hex)
    {
        hex = hex.TrimStart('#').ToUpperInvariant();
        if (hex.Length == 8 && hex.StartsWith("FF", StringComparison.Ordinal)) hex = hex[2..];
        return hex;
    }

    /// <summary>Walk up from the test binary until the solution file appears.</summary>
    private static string? RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Nota.sln"))) d = d.Parent;
        return d?.FullName;
    }
}
