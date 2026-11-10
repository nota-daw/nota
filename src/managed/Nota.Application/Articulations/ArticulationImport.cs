// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One entry point for every articulation map file (specs/085, FR-006): the importer is chosen by
// what the file contains, never by its extension, so a renamed file still imports and a file that
// merely carries a map's extension is refused. Formats Nota has not verified against a real file
// (Studio One .soundvariation) are refused with a message, not guessed.

using Nota.Application.Localization;

namespace Nota.Application.Articulations;

public enum ArticulationFileKind { Unknown, NotaMap, CubaseExpressionMap, StudioOneKeySwitchList }

public static class ArticulationImport
{
    /// <summary>Extensions offered in the file picker. Matching one decides nothing: content does.</summary>
    public static readonly string[] PickerExtensions = { ".expressionmap", ".keyswitch", ArticulationMapFile.Extension };

    /// <summary>What the file is, by its content.</summary>
    public static ArticulationFileKind Detect(string path)
    {
        if (ArticulationMapFile.Sniff(path)) return ArticulationFileKind.NotaMap;
        if (ExpressionMapImporter.Sniff(path)) return ArticulationFileKind.CubaseExpressionMap;
        if (KeySwitchListImporter.Sniff(path)) return ArticulationFileKind.StudioOneKeySwitchList;
        return ArticulationFileKind.Unknown;
    }

    /// <summary>Imports <paramref name="path"/> with the importer its content calls for. The source
    /// file is only read. An unknown or unsupported file returns an error and no map.</summary>
    public static ArticulationImportResult ImportFile(string path)
    {
        if (!File.Exists(path))
        {
            var missing = new ArticulationImportResult();
            missing.Error(Loc.F("articulations.import.file_not_found", path));
            return missing;
        }
        switch (Detect(path))
        {
            case ArticulationFileKind.NotaMap: return ArticulationMapFile.ReadFile(path);
            case ArticulationFileKind.CubaseExpressionMap: return ExpressionMapImporter.ImportFile(path);
            case ArticulationFileKind.StudioOneKeySwitchList: return KeySwitchListImporter.ImportFile(path);
        }
        var res = new ArticulationImportResult();
        res.Error(Path.GetExtension(path).Equals(".soundvariation", StringComparison.OrdinalIgnoreCase)
            ? Loc.T("articulations.import.soundvariation_unsupported")
            : Loc.T("articulations.import.unknown_format"));
        return res;
    }
}
