// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Device fold (specs/022): the Devices panel's "folded into a strip" flag lives on the engine
// device. It round-trips through the C ABI for audio effects, MIDI effects and the instrument,
// is safe out of range, follows a move and a delete + undo, and survives save/load; a v22
// project opens unfolded. Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --fold`.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DeviceFoldTests
{
    private const int Eq = 0, Utility = 4, Arp = 0;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        int eq = eng.AddBuiltinDevice(t, Eq), ut = eng.AddBuiltinDevice(t, Utility);
        int arp = eng.AddMidiEffect(t, Arp);
        yield return (eq == 0 && ut == 1 && arp == 0, "fold: a track with an instrument, two effects and a MIDI effect");
        yield return (!eng.DeviceFolded(t, -1) && !eng.DeviceFolded(t, eq) && !eng.DeviceFolded(t, ut) && !eng.MidiEffectFolded(t, arp),
            "fold: new devices start unfolded");

        eng.SetDeviceFolded(t, eq, true);
        eng.SetDeviceFolded(t, -1, true);
        eng.SetMidiEffectFolded(t, arp, true);
        yield return (eng.DeviceFolded(t, eq) && !eng.DeviceFolded(t, ut) && eng.DeviceFolded(t, -1) && eng.MidiEffectFolded(t, arp),
            "fold: set/get round-trips for an effect, the instrument and a MIDI effect");
        yield return (!eng.DeviceBypassed(t, eq) && !eng.DeviceBypassed(t, -1) && !eng.MidiEffectBypassed(t, arp), "fold: folding doesn't bypass");
        // The folded instrument strip's switch is the instrument's bypass (#148): it toggles while folded.
        eng.SetDeviceBypassed(t, -1, true);
        bool offFolded = eng.DeviceBypassed(t, -1) && eng.DeviceFolded(t, -1);
        eng.SetDeviceBypassed(t, -1, false);
        yield return (offFolded && !eng.DeviceBypassed(t, -1) && eng.DeviceFolded(t, -1),
            "fold: a folded instrument switches off and on and stays folded");

        eng.SetDeviceFolded(t, 99, true); eng.SetMidiEffectFolded(t, 99, true); eng.SetDeviceFolded(9999, 0, true);
        yield return (!eng.DeviceFolded(t, 99) && !eng.MidiEffectFolded(t, 99) && !eng.DeviceFolded(9999, 0),
            "fold: out of range is a no-op and reads unfolded");

        // The flag is on the device, so it travels with it.
        eng.MoveDevice(t, 0, 1);
        yield return (!eng.DeviceFolded(t, 0) && eng.DeviceFolded(t, 1) && eng.TrackDeviceBuiltinKind(t, 1) == Eq,
            "fold: a moved device keeps its fold");
        eng.RemoveDevice(t, 1);
        yield return (eng.TrackDeviceCount(t) == 1 && !eng.DeviceFolded(t, 0), "fold: delete the folded device");
        yield return (eng.Undo() && eng.TrackDeviceCount(t) == 2 && eng.DeviceFolded(t, 1) && !eng.DeviceFolded(t, 0),
            "fold: undoing the delete brings the device back folded");

        // Save/load keeps exactly the folded devices folded.
        string dir = Path.Combine(Path.GetTempPath(), "nota-fold-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = ProjectService.Capture(eng, new TransportState(120, 1, false, false), new List<string>());
            ProjectService.Save(doc, dir, eng);
            using var eng2 = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(dir), eng2, dir);
            int t2 = -1;
            for (int i = 0; i < eng2.TrackCount; i++)
                if (eng2.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) t2 = ti.Id;
            yield return (t2 > 0 && eng2.TrackDeviceCount(t2) == 2 && eng2.TrackMidiEffectCount(t2) == 1,
                "fold: the project reopens with its chain");
            yield return (t2 > 0 && !eng2.DeviceFolded(t2, 0) && eng2.DeviceFolded(t2, 1)
                          && eng2.DeviceFolded(t2, -1) && eng2.MidiEffectFolded(t2, 0),
                "fold: save/load keeps the effect, instrument and MIDI-effect folds");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }

        // A v22 document has no folds: it migrates to v23 and every device opens unfolded.
        var old = ProjectMigrations.Migrate(new JsonObject { ["formatVersion"] = 22 });
        yield return (ProjectService.CurrentFormatVersion == 23 && old["formatVersion"]?.GetValue<int>() == 23,
            "fold: a v22 project migrates to v23");
        yield return (!new DeviceDto().Folded && !new MidiDeviceDto().Folded && !new InstrumentDto().Folded,
            "fold: absent means unfolded");
    }
}
