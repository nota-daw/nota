// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which language of the user manual the app opens (nota#539): the folder follows the language
// the interface runs in; English and any language the manual lacks open the English root.

using System;
using System.Collections.Generic;
using System.IO;
using Nota.App;

namespace Nota.SmokeTest;

internal static class ManualLanguageTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var (code, folder) in new[]
        {
            ("en", ""), ("fr", "fr/"), ("ru", "ru/"),
            ("FR", "fr/"), ("fr-CA", "fr/"), ("ru_RU", "ru/"),
            ("de", ""), ("pt-BR", ""), ("", ""), ("  ", ""), ("zz", ""),
        })
        {
            string got = ManualLanguage.Folder(code);
            yield return (got == folder, $"the manual for the interface language \"{code}\" is \"{folder}\" (got \"{got}\")");
        }
        yield return (ManualLanguage.Folder(null) == "", "no interface language opens the English manual");

        // The OS language never picks the manual: NotaDocs reads the interface language only.
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "VERSION"))) root = Path.GetDirectoryName(root)!;
        var docs = File.ReadAllText(Path.Combine(root!, "src/managed/Nota.App/NotaDocs.cs"));
        yield return (docs.Contains("ManualLanguage.Folder(Loc.Language)") && !docs.Contains("CurrentUICulture"),
            "NotaDocs.Url follows Loc.Language (the interface), not the OS culture");
    }
}
