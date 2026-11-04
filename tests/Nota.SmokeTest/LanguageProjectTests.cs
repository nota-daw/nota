// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The interface language never changes saved work (spec 068, SC-005, FR-009): the same project
// saved while English runs and while French runs is the same file, and a name the user typed is
// the same after a switch.

using System.Text.RegularExpressions;
using Nota.Application.Localization;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LanguageProjectTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var root = RepoRootOrNull();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        // Nota ships English only (spec 077): the other language is the test fixture.
        var fr = TranslationTable.Parse(File.ReadAllText(Path.Combine(root, "tests/Nota.SmokeTest/Fixtures/Languages/xx.json")), out var error);
        if (fr is null) { yield return (false, "xx.json parsed: " + error); yield break; }

        string tmp = Path.GetTempPath();
        string wav = Path.Combine(tmp, "nota_smoke_lang_sine.wav");
        WavWriter.WriteSine(wav, seconds: 0.5, freq: 330, sampleRate: 44100);
        var transport = new TransportState(120.0, 1.0, false, false);
        var dirs = new List<string>();
        var manifests = new Dictionary<string, string>();
        try
        {
            foreach (var language in new[] { "en", "fr" })
            {
                Loc.Install(language == "fr" ? fr : null, language);
                string dir = Path.Combine(tmp, $"nota-smoke-lang-{language}-" + Guid.NewGuid().ToString("N") + ".nota");
                dirs.Add(dir);
                using var engine = new NotaEngine();
                int audio = engine.AddAudioTrack();
                engine.SetTrackName(audio, "Voix Léa — take 2");
                engine.AddAudioClipEx(audio, wav, startBeat: 0, sourceOffsetFrames: 0, lengthFrames: 0, gain: 1f);
                engine.AddInstrumentTrack();
                ProjectService.Save(ProjectService.Capture(engine, transport, new List<string>()), dir, engine);
                manifests[language] = File.ReadAllText(Path.Combine(dir, ProjectService.ManifestName));
            }
            yield return (manifests.Values.All(m => m.Contains("Voix L") && m.Contains("take 2")),
                "a name typed by the user is saved as typed in either language");
            // The save time is the one allowed difference.
            string Strip(string m) => Regex.Replace(m, "\"(?:savedAt|SavedAt|saved|modified|Modified|createdAt|CreatedAt)\"\\s*:\\s*\"[^\"]*\"", "\"t\":\"\"");
            yield return (Strip(manifests["en"]) == Strip(manifests["fr"]),
                "the same project saved in English and in French is the same file (SC-005)");
        }
        finally
        {
            Loc.ResetForTests();
            Loc.Install(null, "en");
            foreach (var d in dirs) try { Directory.Delete(d, true); } catch { }
            try { File.Delete(wav); } catch { }
        }
    }

    private static string? RepoRootOrNull()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src/managed")) && Directory.Exists(Path.Combine(d.FullName, "tests"))) return d.FullName;
        return null;
    }
}
