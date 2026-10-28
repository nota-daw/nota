// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Plug-in card preview (specs/046): the picture of a plug-in's window lives on the engine
// device. Only hosted plug-ins capture one (CI has none, so that part is checked by hand), but
// the storage is the device's, so built-ins exercise it here: the C ABI round trip, the serial,
// clearing and bad input, a move / duplicate / copy-paste / duplicated track carrying it,
// save/load, and a device grouped into a rack keeping it through the rack's blob (a duplicated
// rack, a reopened project); a v26 project opens without previews. Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --preview`.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class PluginPreviewTests
{
    private const int Eq = 0, Utility = 4;

    // Not a real JPEG: the engine stores bytes and never decodes them.
    private static byte[] Blob(byte seed, int n = 3000)
    {
        var b = new byte[n];
        for (int i = 0; i < n; i++) b[i] = (byte)(seed + i * 7);
        return b;
    }

    private static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

    public static IEnumerable<(bool ok, string label)> Run()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        int eq = eng.AddBuiltinDevice(t, Eq), ut = eng.AddBuiltinDevice(t, Utility);
        yield return (eq == 0 && ut == 1, "preview: a track with an instrument and two effects");
        yield return (eng.GetPluginPreview(t, eq).Length == 0 && eng.PluginPreviewSerial(t, eq) == 0
                      && eng.PluginPreviewSerial(t, -1) == 0,
            "preview: new devices have none (serial 0)");

        var a = Blob(1);
        bool setA = eng.SetPluginPreview(t, eq, a);
        int sa = eng.PluginPreviewSerial(t, eq);
        yield return (setA && sa > 0 && Same(eng.GetPluginPreview(t, eq), a) && eng.PluginPreviewSerial(t, ut) == 0,
            "preview: set/get round-trips on one effect only");
        var b = Blob(2, 5000);
        eng.SetPluginPreview(t, eq, b);
        int sb = eng.PluginPreviewSerial(t, eq);
        yield return (sb > 0 && sb != sa && Same(eng.GetPluginPreview(t, eq), b), "preview: a new picture gets a new serial");
        var i1 = Blob(3);
        yield return (eng.SetPluginPreview(t, -1, i1) && Same(eng.GetPluginPreview(t, -1), i1) && eng.PluginPreviewSerial(t, -1) > 0,
            "preview: the instrument holds one too");

        yield return (!eng.SetPluginPreview(t, 99, a) && !eng.SetPluginPreview(9999, 0, a)
                      && eng.GetPluginPreview(t, 99).Length == 0 && eng.PluginPreviewSerial(9999, 0) == 0,
            "preview: an unknown device is refused and reads none");
        yield return (!eng.SetPluginPreview(t, ut, new byte[4 * 1024 * 1024 + 1]) && eng.PluginPreviewSerial(t, ut) == 0,
            "preview: over 4 MB is refused");

        // It's on the device, so it travels with it.
        eng.MoveDevice(t, 0, 1);
        yield return (eng.PluginPreviewSerial(t, 0) == 0 && eng.PluginPreviewSerial(t, 1) == sb && eng.TrackDeviceBuiltinKind(t, 1) == Eq,
            "preview: a moved device keeps its picture");
        int dup = eng.DuplicateDevice(t, 1);
        yield return (dup == 2 && Same(eng.GetPluginPreview(t, dup), b) && eng.PluginPreviewSerial(t, dup) == sb,
            "preview: a duplicate shares the picture");
        int t2 = eng.AddAudioTrack();
        int pasted = eng.CopyDevice(t, 1) ? eng.PasteDevice(t2, -1) : -1;
        yield return (pasted == 0 && Same(eng.GetPluginPreview(t2, pasted), b), "preview: copy/paste onto another track carries it");
        int t3 = eng.DuplicateTrack(t);
        yield return (t3 > 0 && Same(eng.GetPluginPreview(t3, 1), b), "preview: a duplicated track's devices keep theirs");
        eng.RemoveDevice(t, 2);
        yield return (eng.Undo() && Same(eng.GetPluginPreview(t, 2), b), "preview: undoing a delete brings the picture back");

        eng.SetPluginPreview(t, 2, Array.Empty<byte>());
        yield return (eng.GetPluginPreview(t, 2).Length == 0 && eng.PluginPreviewSerial(t, 2) == 0 && Same(eng.GetPluginPreview(t, 1), b),
            "preview: empty clears it (the shared copy elsewhere stays)");

        // Save/load keeps each device's picture.
        string dir = Path.Combine(Path.GetTempPath(), "nota-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = ProjectService.Capture(eng, new TransportState(120, 1, false, false), new List<string>());
            ProjectService.Save(doc, dir, eng);
            using var eng2 = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(dir), eng2, dir);
            int r = -1;
            for (int i = 0; i < eng2.TrackCount && r < 0; i++)
                if (eng2.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) r = ti.Id;
            yield return (r > 0 && eng2.TrackDeviceCount(r) == 3, "preview: the project reopens with its chain");
            yield return (r > 0 && eng2.PluginPreviewSerial(r, 0) == 0 && Same(eng2.GetPluginPreview(r, 1), b)
                          && eng2.PluginPreviewSerial(r, 2) == 0 && Same(eng2.GetPluginPreview(r, -1), i1),
                "preview: save/load keeps exactly the pictures there were");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }

        // Grouped into a rack, a device's picture rides the rack's blob: a duplicated rack and a
        // saved and reopened one both give it back on ungroup.
        using (var re = new NotaEngine())
        {
            int rt = re.AddAudioTrack();
            int rEq = re.AddBuiltinDevice(rt, Eq);
            re.SetPluginPreview(rt, rEq, b);
            bool wrapped = re.WrapDeviceInRack(rt, rEq) >= 0 && re.TrackDeviceBuiltinKind(rt, 0) != Eq;
            int copy = re.DuplicateDevice(rt, 0);
            bool dupBack = copy == 1 && re.UngroupRack(rt, DeviceFamily.Audio, 1, out _) >= 0
                           && re.TrackDeviceBuiltinKind(rt, 1) == Eq && Same(re.GetPluginPreview(rt, 1), b);
            yield return (wrapped && dupBack, "preview: a duplicated rack's device gives its picture back on ungroup");
            re.RemoveDevice(rt, 1);

            string rdir = Path.Combine(Path.GetTempPath(), "nota-preview-rack-" + Guid.NewGuid().ToString("N"));
            try
            {
                var doc = ProjectService.Capture(re, new TransportState(120, 1, false, false), new List<string>());
                ProjectService.Save(doc, rdir, re);
                using var re2 = new NotaEngine();
                ProjectService.Apply(ProjectService.Load(rdir), re2, rdir);
                int rt2 = -1;
                for (int i = 0; i < re2.TrackCount && rt2 < 0; i++)
                    if (re2.TryGetTrackInfo(i, out var ti) && !ti.IsInstrument && re2.TrackDeviceCount(ti.Id) == 1) rt2 = ti.Id;
                bool back = rt2 > 0 && re2.UngroupRack(rt2, DeviceFamily.Audio, 0, out _) >= 0
                            && re2.TrackDeviceBuiltinKind(rt2, 0) == Eq && Same(re2.GetPluginPreview(rt2, 0), b);
                yield return (back, "preview: a saved and reopened rack's device gives its picture back on ungroup");
            }
            finally
            {
                try { if (Directory.Exists(rdir)) Directory.Delete(rdir, recursive: true); } catch (IOException) { }
            }
        }

        var old = ProjectMigrations.Migrate(new JsonObject { ["formatVersion"] = 26 });
        yield return ((old["formatVersion"]?.GetValue<int>() ?? 0) == ProjectService.CurrentFormatVersion
                      && ProjectService.CurrentFormatVersion >= 27 && new DeviceDto().Preview is null && new InstrumentDto().Preview is null,
            "preview: a v26 project migrates and opens with none");
    }
}
