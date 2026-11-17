// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// specs/099 US3: a project saved with the #568 tempo track (format v33, `tempoPoints`) opens
// with its tempo on the master Tempo lane and every bar where it was. The expected times come
// from LegacyTempoReference, an independent integration of the old step / linear / Bezier map.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TempoMigrationTests
{
    private static JsonObject Point(double beat, double bpm, int shape = 0, double x1 = 1.0 / 3, double y1 = 0,
                                    double x2 = 2.0 / 3, double y2 = 1, double incoming = 0)
        => new() { ["beat"] = beat, ["bpm"] = bpm, ["shape"] = shape, ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2, ["incomingBpm"] = incoming };

    private static string Bundle(string root, string name, JsonArray? tempo, double bpm = 120)
    {
        string dir = Path.Combine(root, name + ".nota");
        Directory.CreateDirectory(dir);
        var doc = new JsonObject
        {
            ["formatVersion"] = 33,
            ["app"] = "Nota",
            ["transport"] = new JsonObject { ["bpm"] = bpm, ["masterVolume"] = 1.0, ["timeSigNumerator"] = 4, ["timeSigDenominator"] = 4 },
            ["tracks"] = new JsonArray(),
            ["masterVolumeAutomation"] = new JsonArray(),
            ["master"] = new JsonObject { ["devices"] = new JsonArray() },
            ["sections"] = new JsonArray(),
        };
        if (tempo is not null) doc["tempoPoints"] = tempo;
        File.WriteAllText(Path.Combine(dir, ProjectService.ManifestName), doc.ToJsonString());
        return dir;
    }

    private static (NotaEngine engine, ProjectDocument doc) Open(string bundle)
    {
        var e = new NotaEngine();
        var doc = ProjectService.Load(bundle);
        ProjectService.Apply(doc, e, bundle);
        return (e, doc);
    }

    public static IEnumerable<(bool, string)> Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "nota-tempo-v33-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var legacy = new[]
            {
                new TempoMigration.LegacyPoint(0, 120, 0),
                new TempoMigration.LegacyPoint(16, 100, 1),
                new TempoMigration.LegacyPoint(32, 140, 2, .25, 0, .75, 1),
                new TempoMigration.LegacyPoint(48, 90, 2, .1, .7, .4, .95),
                new TempoMigration.LegacyPoint(64, 110, 0, IncomingBpm: 80),
                new TempoMigration.LegacyPoint(80, 130, 1),
                new TempoMigration.LegacyPoint(96, 75, 0),
            };
            var arr = new JsonArray();
            foreach (var p in legacy) arr.Add(Point(p.Beat, p.Bpm, p.Shape, p.X1, p.Y1, p.X2, p.Y2, p.IncomingBpm));
            var (e, doc) = Open(Bundle(root, "stepped-linear-smooth", arr));
            using (e)
            {
                int lanes = e.AutomationLaneCount(e.MasterTrackId);
                bool hasLane = Enumerable.Range(0, lanes).Any(i => e.AutomationLaneInfo(e.MasterTrackId, i).Target == AutomationTarget.Tempo);
                yield return (hasLane && e.IsTempoAutomated && !doc.TempoClamped,
                    "tempo migration: #568 tempo points open as the master Tempo lane");
                double worst = 0;
                for (int bar = 0; bar <= 200; bar++)
                {
                    double beat = bar * 4;
                    worst = Math.Max(worst, Math.Abs(e.BeatToSeconds(beat) - LegacyTempoReference.Seconds(legacy, beat)));
                }
                yield return (worst < 1e-3, $"tempo migration: bars 1–200 start within 1 ms of the #568 map (worst {worst * 1000:0.000} ms)");
            }

            var wild = new JsonArray { Point(0, 400, 1), Point(8, 10, 0), Point(16, 120, 0) };
            var (w, wdoc) = Open(Bundle(root, "out-of-range", wild));
            using (w)
            {
                yield return (wdoc.TempoClamped && Math.Abs(w.TempoAt(0) - 300) < 1e-9 && Math.Abs(w.TempoAt(10) - 20) < 1e-9,
                    "tempo migration: tempo outside 20–300 BPM is limited and flagged for the one-time notice");
            }

            var (plain, _) = Open(Bundle(root, "single-tempo", null, bpm: 97));
            using (plain)
                yield return (!plain.IsTempoAutomated && plain.AutomationLaneCount(plain.MasterTrackId) == 0,
                    "tempo migration: a project without tempo points keeps its single tempo");

            // 2,049 alternating steps: each change is a same-beat pair on the lane (4,097 points),
            // still 2,049 tempo map points — the project must open with its timing.
            var many = new JsonArray();
            for (int i = 0; i < 2049; i++) many.Add(Point(i * 2, i % 2 == 0 ? 120 : 90, 0));
            var (mm, _) = Open(Bundle(root, "many-steps", many));
            using (mm)
                yield return (mm.IsTempoAutomated && Math.Abs(mm.TempoAt(4096.5) - 120) < 1e-9 && Math.Abs(mm.TempoAt(4095) - 90) < 1e-9,
                    $"tempo migration: a 2,049-step map opens ({mm.GetAutomationPoints(mm.MasterTrackId, 0).Length} lane points)");

            // 600 steep Bezier ramps between 40 and 280 BPM: split as finely as one ramp alone
            // would be (up to 64 pieces), the lane would pass the engine's 4,096 map points and
            // the project would fail to open. The migration splits less finely until it fits.
            var curvy = new List<TempoMigration.LegacyPoint>();
            for (int i = 0; i <= 600; i++)
                curvy.Add(new TempoMigration.LegacyPoint(i * 2, i % 2 == 0 ? 40 : 280, i == 600 ? 0 : 2, .95, 0, 1, .05));
            var curvyLane = TempoMigration.Convert(curvy, out _);
            int curvyBeats = TempoMigration.DistinctBeats(curvyLane);
            var curvyArr = new JsonArray();
            foreach (var p in curvy) curvyArr.Add(Point(p.Beat, p.Bpm, p.Shape, p.X1, p.Y1, p.X2, p.Y2, p.IncomingBpm));
            var (cv, _) = Open(Bundle(root, "many-curves", curvyArr));
            using (cv)
            {
                double cvWorst = 0;
                foreach (int beat in new[] { 2, 301, 600, 899, 1200 })   // the reference is slow: a few beats
                    cvWorst = Math.Max(cvWorst, Math.Abs(cv.BeatToSeconds(beat) - LegacyTempoReference.Seconds(curvy, beat)));
                yield return (cv.IsTempoAutomated && curvyBeats <= TempoMigration.MaxMapPoints && curvyBeats > 1201 && cvWorst < 0.05,
                    $"tempo migration: 600 steep curves fit the engine's map ({curvyBeats} of {TempoMigration.MaxMapPoints} points, worst {cvWorst * 1000:0.0} ms)");
            }

            var broken = new JsonArray { Point(4, 120), Point(2, 90) };   // first point not at 0, beats backwards
            var (b, _) = Open(Bundle(root, "malformed", broken));
            using (b)
                yield return (!b.IsTempoAutomated, "tempo migration: a malformed tempo map is dropped, the stored tempo kept");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
