// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — colour themes and skins (spec 087, issue #336): a client (an AI helping someone
// turn a palette into a usable skin) reads a theme as a template, changes it, validates it,
// gets back one image per part of the app showing how it looks, and saves or wears it. The
// theme travels as the theme file's own JSON text, so what a client sends is exactly what it
// would save. A malformed theme is a validation result, never an error across the transport.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Nota.Application;
using Nota.Application.Themes;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class ThemeTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, IThemeAccess themes)
    : EngineTools(engine, dispatch, refresh)
{
    private const string Format =
        "A theme is the JSON text of a theme file: { \"formatVersion\": 1, 2 or 3, \"id\": \"lower-case-hyphenated\", \"name\": \"1–40 chars\", "
        + "\"kind\": \"dark\"|\"light\", \"colors\": { every token: \"#RRGGBB\" (or #AARRGGBB where the catalogue allows translucency) } }. "
        + "Format 2 may add \"font\": { \"family\": \"<installed font>\" } and \"radius\": { \"scale\": 0–2, \"steps\": { \"card\": px, … } }; "
        + "format 3 also \"font\": { \"values\": \"<installed font>\" } for values and readouts (family then optional). "
        + "A skin that sets radius keeps its corners under the classic look; one that doesn't takes the classic corners.";

    public sealed record Problem(
        [property: Description("Rule broken: F1–F6 (format, id, name, kind, tokens, colours), V1–V4 (font/radius), JSON")] string Rule,
        [property: Description("Token or key path, e.g. colors.Panel or radius.scale")] string? Field,
        string Message);

    public sealed record Warning(
        [property: Description("readability (a contrast or colour-distance pairing below its minimum) or font-missing")] string Kind,
        [property: Description("Readability pairing id, T3.1–T3.9 (specs/011 ui-contract)")] string? Pairing,
        [property: Description("The tokens involved (for font-missing: the family)")] string[] Tokens,
        [property: Description("WCAG contrast ratio, or CIE76 ΔE for T3.7/T3.8")] double Measured,
        double Minimum, string Message);

    public sealed record ValidationResult(
        [property: Description("True when the theme can be saved and worn (warnings don't block)")] bool Valid,
        Problem[] Problems, Warning[] Warnings);

    public sealed record SaveResult(ValidationResult Validation, bool Saved,
        [property: Description("The file written in Nota's themes folder")] string? File, string? Reason);

    public sealed record TryOnResult(ValidationResult Validation, bool Wearing);

    public sealed record WornResult(string Worn);
    public sealed record LookResult(string Look, string Worn);

    private static ValidationResult Result(ThemeValidation v) => new(
        v.IsValid,
        v.Problems.Select(p => new Problem(p.Rule, p.Field, p.Message)).ToArray(),
        v.Warnings.Select(w => new Warning(w.Kind, w.Pairing, w.Tokens.ToArray(), w.Measured, w.Minimum, w.Message)).ToArray());

    [McpServerTool(Name = "list_themes"), Description(
        "List Nota's colour themes (skins): the built-ins and the user themes in the themes folder, with kind, warnings, "
        + "typeface and roundness; which one is worn now (and whether it is a try-on); the stored Appearance choice, with "
        + "the look (\"new\" or \"classic\"); and "
        + "the theme files that could not be loaded, with the reason.")]
    public Task<ThemeListing> ListThemes() => Read(themes.List);

    [McpServerTool(Name = "get_theme"), Description(
        "Read one theme's file (JSON text, tokens in catalogue order). Use a built-in as the template for a new skin: "
        + "change the id and name, then the colours (and, in formatVersion 2, font and radius). With look \"classic\" a "
        + "built-in comes back in its classic palette (Nota 0.59's colours) — a start for a classic skin.")]
    public async Task<string> GetTheme(
        [Description("Theme id from list_themes, e.g. studio-dark")] string id,
        [Description("\"new\" (default): the file as it is; \"classic\": a built-in's classic palette")] string? look = null) =>
        await Read(() => themes.GetFile(id, look)) ?? throw new ArgumentException($"No theme '{id}' — see list_themes.");

    [McpServerTool(Name = "set_look"), Description(
        "Switch Nota's look and store it, as Preferences → Appearance → Look does: \"new\" (the current look) or "
        + "\"classic\" (Nota 0.59's colours, tighter corners and values in Geist Mono, over any theme; a user skin keeps "
        + "what it sets). Live, no restart. Returns the look and the theme worn.")]
    public async Task<LookResult> SetLook([Description("\"new\" or \"classic\"")] string look)
    {
        var worn = await Read(() => themes.SetLook(look));
        return new LookResult(look.Trim().ToLowerInvariant(), worn);
    }

    [McpServerTool(Name = "get_theme_catalogue"), Description(
        "The colour tokens every theme must define, in file order, each with its group, what it colours, and whether it may "
        + "be translucent; the radius steps with their design values in px; the accepted ranges ([min, max]) for the radius "
        + "scale, a radius step in px and the font family name's length; the supported format versions. "
        + "Readability is checked on text tokens over the surfaces (contrast ≥ 4.5 for TextPrimary/Secondary/Muted, …), "
        + "on ink over Accent/Mute/Solo/Record and the track colours, and on how distinct the track colours are.")]
    public Task<ThemeCatalogue> GetThemeCatalogue() => Read(themes.Catalogue);

    [McpServerTool(Name = "list_system_fonts"), Description(
        "The font families installed on this computer — a skin's font.family must be one of them, or Nota uses its own Geist.")]
    public async Task<string[]> ListSystemFonts() => (await Read(themes.SystemFonts)).ToArray();

    [McpServerTool(Name = "validate_theme"), Description(
        "Check a theme without saving or showing it: every format problem (each naming the token or key) and every "
        + "readability warning (pairing, tokens, measured vs minimum). Cheap — call it after each change. " + Format)]
    public async Task<ValidationResult> ValidateTheme([Description("The theme file's JSON text")] string theme) =>
        Result(await Read(() => themes.Validate(theme)));

    [McpServerTool(Name = "preview_theme"), Description(
        "Render how Nota looks in a theme: one PNG per part of the app (overview, top-bar, arrangement, mixer, device-rack, "
        + "piano-roll, browser, preferences, menu, dialog), from a fixed sample project, offscreen — the user's window does "
        + "not change and nothing is saved. Takes a few seconds. The first content block is the validation as JSON; then, "
        + "per part, its name and description followed by the image. An invalid theme returns only the validation. " + Format)]
    public async Task<CallToolResult> PreviewTheme(
        [Description("The theme file's JSON text")] string theme,
        [Description("Only these parts (names as listed above); all when omitted")] string[]? surfaces = null,
        [Description("\"user\" (default): with the accent colour the user picked, as the skin would really look; "
                     + "\"theme\": with the theme's own accent")] string? accent = null,
        [Description("\"new\" or \"classic\"; the look the user wears when omitted")] string? look = null)
    {
        var result = await await Read(() => themes.PreviewAsync(theme, surfaces, string.Equals(accent, "theme", StringComparison.OrdinalIgnoreCase), look));
        var head = new
        {
            validation = Result(result.Validation),
            rendered = result.Rendered,
            unknown = result.Unknown,
        };
        var content = new List<ContentBlock>
        {
            new TextContentBlock { Text = JsonSerializer.Serialize(head, JsonOptions) },
        };
        foreach (var image in result.Images)
        {
            content.Add(new TextContentBlock { Text = $"{image.Name} — {image.Description}" });
            content.Add(ImageContentBlock.FromBytes(image.Png, "image/png"));
        }
        return new CallToolResult { Content = content, IsError = false };
    }

    [McpServerTool(Name = "save_theme"), Description(
        "Save a valid theme as a user theme (<id>.json in Nota's themes folder); it appears in Preferences → Appearance "
        + "at once. Refused for an invalid theme, a built-in's id, or an existing user theme unless overwrite is true. "
        + "Saving does not wear it — call apply_theme. " + Format)]
    public async Task<SaveResult> SaveTheme(
        [Description("The theme file's JSON text")] string theme,
        [Description("Replace the user theme with the same id")] bool overwrite = false)
    {
        var r = await Read(() => themes.Save(theme, overwrite));
        return new SaveResult(Result(r.Validation), r.Saved, r.File, r.Reason);
    }

    [McpServerTool(Name = "apply_theme"), Description(
        "Wear a listed theme (built-in or user) and store the choice, as picking it in Preferences → Appearance, in "
        + "single-theme mode (Follow system is turned off, so this theme is the one on screen). "
        + "Ends any try-on.")]
    public async Task<WornResult> ApplyTheme([Description("Theme id from list_themes")] string id)
    {
        var error = await Read(() => themes.Apply(id));
        if (error is not null) throw new ArgumentException(error);
        return new WornResult(id);
    }

    [McpServerTool(Name = "try_on_theme"), Description(
        "Wear a theme live without saving or storing it, so the user can look at the real app. It lasts until "
        + "revert_try_on, a pick in Preferences, apply_theme or a restart. The user sees it — revert it if it isn't kept. " + Format)]
    public async Task<TryOnResult> TryOnTheme([Description("The theme file's JSON text")] string theme)
    {
        var (wearing, v) = await Read(() => (themes.TryOn(theme, out var validation), validation));
        return new TryOnResult(Result(v), wearing);
    }

    [McpServerTool(Name = "revert_try_on"), Description("End a try-on: Nota wears the stored theme again. Does nothing without a try-on.")]
    public async Task<WornResult> RevertTryOn() => new(await Read(themes.RevertTryOn));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
