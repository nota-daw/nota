// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The MCP theme tools' view of the app (spec 087): the theme list, files, catalogue, the
// validator, the skin previews, and saving, wearing and trying on themes. Every member runs
// on the UI thread (the tools call it through IEngineDispatch). Writes go only to the themes
// folder, under a file name made from the validated id — never from the client's text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Nota.Application;
using Nota.Application.Themes;
using Nota.Mcp;

namespace Nota.App;

internal sealed class ThemeAccess : IThemeAccess
{
    private static IReadOnlyDictionary<string, (string Group, string Role)>? _roles;

    public ThemeListing List()
    {
        // The folder as it is now: a client may have written a file there by hand.
        NotaThemes.ReloadUser();
        var worn = NotaThemeService.TryOnTheme ?? NotaThemes.Find(NotaThemeService.WornTheme?.Id) ?? NotaThemes.Default;
        var c = NotaThemeService.Choice;
        return new ThemeListing(
            NotaThemes.All.Select(t => new ThemeListEntry(t.Id, t.Name, t.IsDark ? "dark" : "light",
                t.IsUser ? "user" : "built-in", t.Warnings.Count, t.Warnings.Any(w => w.Kind == ThemeWarning.FontMissing),
                t.Geometry.FontFamily, t.Geometry.Roundness)).ToArray(),
            worn.Id, NotaThemeService.TryOnTheme is not null,
            new ThemeChoiceInfo(c.FollowSystem, c.Single, c.Dark, c.Light),
            NotaThemes.UserLoadProblems.Select(p => new ThemeFileProblem(p.FileName, p.Reason)).ToArray());
    }

    public string? GetFile(string id) => NotaThemes.Find(id) is { } t ? NotaThemes.ToFile(t) : null;

    public ThemeCatalogue Catalogue()
    {
        var roles = Roles();
        return new ThemeCatalogue(
            NotaThemes.CatalogueList.Select(name =>
            {
                var (group, role) = roles.TryGetValue(name, out var r) ? r : ("", "");
                return new ThemeTokenInfo(name, group, role, NotaThemes.AlphaTokens.Contains(name));
            }).ToArray(),
            Enum.GetValues<RadiusStep>().Select(s => new ThemeRadiusStepInfo(SkinGeometry.Key(s), SkinGeometry.Design(s))).ToArray(),
            new ThemeRanges(
                new[] { SkinGeometry.MinRoundness, SkinGeometry.MaxRoundness },
                new[] { SkinGeometry.MinStep, SkinGeometry.MaxStep },
                new[] { 1, SkinGeometry.MaxFamilyLength }),
            new[] { 1, 2 });
    }

    public IReadOnlyList<string> SystemFonts() => NotaThemes.SystemFontNames();

    public ThemeValidation Validate(string json) => NotaThemes.Validate(json);

    public async Task<ThemePreviews> PreviewAsync(string json, IReadOnlyList<string>? surfaces, bool themeAccent)
    {
        var v = NotaThemes.Validate(json);
        if (!v.IsValid) return new ThemePreviews(v, Array.Empty<ThemePreviewImage>(), Array.Empty<string>(), Array.Empty<string>());
        var theme = NotaThemes.FromParsed(v, ThemeSource.Preview);
        var (images, unknown) = await SkinPreviewRenderer.RenderAsync(theme, themeAccent, surfaces);
        return new ThemePreviews(v,
            images.Select(i => new ThemePreviewImage(i.Name, i.Description, i.Png)).ToArray(),
            images.Select(i => i.Name).ToArray(), unknown.ToArray());
    }

    public ThemeSaveResult Save(string json, bool overwrite)
    {
        var v = NotaThemes.Validate(json);
        if (!v.IsValid) return new ThemeSaveResult(v, false, null, "the theme is not valid — see the problems");
        var id = v.Theme!.Id;
        if (NotaThemes.IsBuiltIn(id))
            return new ThemeSaveResult(v, false, null, $"'{id}' is a built-in theme — give the theme its own id");
        var existing = NotaThemes.User.FirstOrDefault(t => t.Id == id);
        var path = Path.Combine(NotaThemes.ThemesFolder, id + ".json");
        if (!overwrite && (existing is not null || File.Exists(path)))
            return new ThemeSaveResult(v, false, null, $"a user theme '{id}' exists — pass overwrite to replace it");
        try
        {
            Directory.CreateDirectory(NotaThemes.ThemesFolder);
            // Write the new file in full first (temp + move), so a failed write leaves the old theme.
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, NotaThemes.ToFile(NotaThemes.FromParsed(v, ThemeSource.User)));
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            // Another file in the folder may hold this id under a different name: it is replaced
            // now, so it goes — only after the new one is safely on disk.
            if (existing?.FilePath is { } old && !SameFile(old, path))
                File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ThemeSaveResult(v, false, null, e.Message);
        }
        NotaThemes.ReloadUser();
        return new ThemeSaveResult(v, true, path, null);
    }

    // Whether two paths in the themes folder name one file. Names that differ only in case
    // (My-Skin.json vs my-skin.json) are one file on a case-insensitive file system (Windows,
    // default macOS) — deleting one would delete the other — and two files on Linux. The
    // folder listing tells which: two entries for the name only when they are distinct.
    private static bool SameFile(string a, string b)
    {
        a = Path.GetFullPath(a);
        b = Path.GetFullPath(b);
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        var name = Path.GetFileName(b);
        var dir = Path.GetDirectoryName(b);
        if (dir is null || !Directory.Exists(dir)) return false;
        return Directory.EnumerateFiles(dir)
            .Count(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) < 2;
    }

    public string? Apply(string id)
    {
        if (NotaThemes.Find(id) is null) return $"No theme '{id}' — see list_themes.";
        // Single-theme mode (contract): under follow system the pick would only fill its kind's
        // slot, and on a desktop of the other kind another theme would stay on screen.
        PreferencesWindow.PickTheme(id, singleMode: true);
        return null;
    }

    public bool TryOn(string json, out ThemeValidation validation)
    {
        validation = NotaThemes.Validate(json);
        if (!validation.IsValid) return false;
        NotaThemeService.TryOn(NotaThemes.FromParsed(validation, ThemeSource.TryOn));
        return true;
    }

    public string RevertTryOn()
    {
        NotaThemeService.RevertTryOn();
        return NotaThemes.Find(NotaThemeService.WornTheme?.Id)?.Id ?? NotaThemeService.Choice.Single;
    }

    // What each token is for: Theme/token-roles.json, made from NotaPalette's comments.
    private static IReadOnlyDictionary<string, (string, string)> Roles()
    {
        if (_roles is not null) return _roles;
        var map = new Dictionary<string, (string, string)>();
        using var s = typeof(ThemeAccess).Assembly.GetManifestResourceStream("Nota.Themes.token-roles.json");
        if (s is not null)
        {
            using var doc = JsonDocument.Parse(s);
            foreach (var e in doc.RootElement.EnumerateArray())
                map[e.GetProperty("name").GetString() ?? ""] = (e.GetProperty("group").GetString() ?? "", e.GetProperty("role").GetString() ?? "");
        }
        return _roles = map;
    }
}
