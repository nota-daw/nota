// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IArticulationLibrary over a folder of Nota map files (specs/085): <data>/articulations/<id>.notamap.
// The file name comes from the map id, never from its name, so a rename doesn't move the file and a
// name can't escape the folder. Writes go to a temp file first, then replace the old one.

using System.Text;
using Nota.Application.Articulations;

namespace Nota.Infrastructure.Articulations;

public sealed class ArticulationLibraryStore : IArticulationLibrary
{
    private readonly string _dir;
    private List<string> _unreadable = new();

    /// <param name="dir">The folder; default <c>NotaPaths.SubDir("articulations")</c>.</param>
    public ArticulationLibraryStore(string? dir = null) => _dir = dir ?? NotaPaths.SubDir("articulations");

    public string Folder => _dir;
    public IReadOnlyList<string> Unreadable => _unreadable;

    public IReadOnlyList<ArticulationMap> List()
    {
        var maps = new List<ArticulationMap>();
        var bad = new List<string>();
        if (Directory.Exists(_dir))
            foreach (var f in Directory.EnumerateFiles(_dir, "*" + ArticulationMapFile.Extension))
            {
                var r = ArticulationMapFile.ReadFile(f);
                if (r.Map is { } m) maps.Add(m); else bad.Add(f);
            }
        _unreadable = bad;
        return maps.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
    }

    public ArticulationMap? Get(string mapId)
    {
        if (PathOf(mapId) is not { } p || !File.Exists(p)) return null;
        return ArticulationMapFile.ReadFile(p).Map;
    }

    public void Save(ArticulationMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var path = PathOf(map.Id) ?? throw new ArgumentException("Not a valid articulation map id.", nameof(map));
        Directory.CreateDirectory(_dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ArticulationMapFile.Write(map), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);   // never leave a half-written map behind
    }

    public bool Delete(string mapId)
    {
        if (PathOf(mapId) is not { } p || !File.Exists(p)) return false;
        File.Delete(p);
        return true;
    }

    // Ids are 32 hex digits from Guid "N"; anything else (a path, "..", a hostile id from a file) is refused.
    private string? PathOf(string? mapId) =>
        mapId is { Length: > 0 and <= 64 } && mapId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? Path.Combine(_dir, mapId + ArticulationMapFile.Extension)
            : null;
}
