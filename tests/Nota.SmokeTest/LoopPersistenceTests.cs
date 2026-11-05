// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The loop region is part of the project (#455): its start and end, and whether it's on, survive
// save / load; a file from before the region was saved loads as before, the region untouched.
// Runs with `dotnet run --project tests/Nota.SmokeTest -- --loop` and the full run.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LoopPersistenceTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in RoundTrip(on: true)) yield return r;
        foreach (var r in RoundTrip(on: false)) yield return r;
        foreach (var r in OlderFile()) yield return r;
    }

    private static IEnumerable<(bool, string)> RoundTrip(bool on)
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-loop-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var eng = new NotaEngine();
            ProjectTemplate.ApplyDefault(eng);
            eng.SetLoop(on, 16, 32);   // bars 5–9
            new ProjectStore().Save(eng, new TransportState(120, 1, false, on), dir);
            using var e2 = new NotaEngine();
            var result = new ProjectStore().Load(e2, dir);
            yield return (e2.LoopStart == 16 && e2.LoopEnd == 32 && e2.LoopEnabled == on && result.Transport.LoopOn == on,
                $"loop: the region (bars 5–9) survives save / load, loop {(on ? "on" : "off")}");
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    private static IEnumerable<(bool, string)> OlderFile()
    {
        using var eng = new NotaEngine();
        ProjectTemplate.ApplyDefault(eng);
        var doc = ProjectService.Capture(eng, new TransportState(120, 1, false, false), new List<string>());
        bool noRegion = doc.Transport.LoopStart is null && doc.Transport.LoopEnd is null;   // none set: none written
        using var e2 = new NotaEngine();
        e2.SetLoop(false, 8, 12);
        ProjectService.Apply(doc, e2, Path.GetTempPath());
        yield return (noRegion && e2.LoopStart == 8 && e2.LoopEnd == 12 && !e2.LoopEnabled,
            "loop: a project without a saved region writes none, and loading it leaves the region as it was");
    }
}
