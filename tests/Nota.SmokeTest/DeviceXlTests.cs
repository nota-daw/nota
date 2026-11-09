// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The XL card size (specs/085, #540): Nota EQ-8 and Dynamic EQ-8 widen their View param to
// 0 = L, 1 = S, 2 = XL, so XL saves with the project like S / L. Other devices with a View keep
// 0..1. The size stays editor state: a preset applied in place leaves it alone, and a project
// round-trip keeps it. Also runnable alone: `--device-xl`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DeviceXlTests
{
    private const int Eq8Kind = 0, DynEqKind = 13, Eq3Kind = 16;

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        int t = e.AddAudioTrack();
        int eq = e.AddBuiltinDevice(t, Eq8Kind), dyn = e.AddBuiltinDevice(t, DynEqKind), eq3 = e.AddBuiltinDevice(t, Eq3Kind);

        foreach (var (d, name) in new[] { (eq, "EQ-8"), (dyn, "Dynamic EQ-8") })
        {
            int v = DeviceView.Index(e, t, d);
            bool fresh = v >= 0 && DeviceView.Size(e, t, d) == CardSize.L;
            DeviceView.SetSize(e, t, d, CardSize.XL);
            yield return (fresh && e.DeviceParamMax(t, d, v) == 2f && e.DeviceGetParam(t, d, v) == 2f
                          && DeviceView.Size(e, t, d) == CardSize.XL && DeviceView.SupportsXlParam(e, t, d) && !DeviceView.IsMini(e, t, d),
                $"{name} opens in L and its View holds XL (max {(v >= 0 ? e.DeviceParamMax(t, d, v) : -1)})");
        }

        int v3 = DeviceView.Index(e, t, eq3);
        DeviceView.SetSize(e, t, eq3, CardSize.XL);   // refused: EQ-3's View stops at S
        yield return (v3 >= 0 && e.DeviceParamMax(t, eq3, v3) == 1f && DeviceView.Size(e, t, eq3) == CardSize.L && !DeviceView.SupportsXlParam(e, t, eq3),
            "EQ-3 keeps a two-size View: XL is not written");
        DeviceView.SetMini(e, t, eq3, true);
        yield return (DeviceView.IsMini(e, t, eq3) && DeviceView.Size(e, t, eq3) == CardSize.S, "S still reads as mini");

        // A preset applied in place is sound only: the card stays in XL.
        var plain = new PresetDocument { Type = "builtin-effect", BuiltinKind = Eq8Kind, NamedParams = new() };
        PresetService.ApplyInPlace(plain, e, t, eq);
        PresetService.ResetInPlace(e, t, eq, false);
        yield return (DeviceView.Size(e, t, eq) == CardSize.XL, "a preset or Init leaves EQ-8 in XL");

        // A project round-trip keeps XL.
        var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-xl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        bool kept;
        try
        {
            ProjectService.Save(doc, dir, e);
            doc = ProjectService.Load(dir);
            using var back = new NotaEngine();
            ProjectService.Apply(doc, back, dir);
            int bt = -1;
            for (int i = 0; i < back.TrackCount; i++)
                if (back.TryGetTrackInfo(i, out var ti) && !ti.IsInstrument && !ti.IsReturn && !ti.IsGroup) { bt = ti.Id; break; }
            kept = bt >= 0 && DeviceView.Size(back, bt, eq) == CardSize.XL && DeviceView.Size(back, bt, dyn) == CardSize.XL;
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        yield return (kept, "XL comes back with the project");
    }
}
