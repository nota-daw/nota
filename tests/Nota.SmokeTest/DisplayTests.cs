// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// specs/113: the LED display Nota renders for the phone's Macros screen. The frame itself (dots,
// font, RLE4 on the wire), then every built-in device: each controller page has its own picture,
// that picture draws on a fresh device through the C ABI without throwing and lights the graph
// area, and turning a knob changes it. NOTA_DISPLAY_DUMP=<dir> writes each page as ASCII for a
// look by eye.

using Nota.Application;
using Nota.Application.Controllers;
using Nota.Application.Displays;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DisplayTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Frame()) yield return r;
        foreach (var r in EveryDevice()) yield return r;
    }

    private static IEnumerable<(bool, string)> Frame()
    {
        var f = new LedFrame();
        f.Line(0, 0, 127, 47, 15);
        f.Dot(10, 10, 4);
        f.Dot(10, 10, 2);   // max-blend: a dimmer layer never darkens
        yield return (f[0, 0] == 15 && f[127, 47] == 15 && f[10, 10] == 4, "led frame: lines reach both corners; dots blend to the brighter level");
        int end = f.Text("CUTOFF 1.2K", 0, 20, 9);
        yield return (end == 44 && LedFrame.TextWidth("CUTOFF 1.2K") == 43 && f[0, 21] == 9, "led frame: the 3 × 5 font is 4 dots a glyph");
        yield return (LedFrame.Normalize("Filter 1 Freq → −6 dB") == "FILTER 1 FREQ > -6 DB", "led frame: text is upper case with the font's own arrows and minus");
        var rle = f.Rle4();
        var back = LedFrame.FromRle4(rle);
        yield return (back is not null && back.Equals(f) && rle.Length < 600, $"led frame: RLE4 round-trips ({rle.Length} bytes for a line and a label)");
        yield return (LedFrame.FromRle4(rle.AsSpan(0, rle.Length - 1)) is null, "led frame: a short RLE4 stream is refused");
        var blank = new LedFrame();
        yield return (blank.Rle4().Length == LedFrame.W * LedFrame.H / 16, "led frame: a dark frame is 384 bytes");
        DisplayRenderer.Header(f, "Volt", "Main", "LP 890HZ", "FILTER 1 FREQ 890 HZ");
        yield return (f[1, 0] == 13 && f[2, 1] == 0, "led frame: the knob overlay is an inverted bar in the header");
    }

    private static IEnumerable<(bool, string)> EveryDevice()
    {
        string? dump = Environment.GetEnvironmentVariable("NOTA_DISPLAY_DUMP");
        if (dump is { Length: > 0 }) Directory.CreateDirectory(dump);
        var known = DevicePageCatalog.Known.Where(k => !k.TypeKey.StartsWith("rack:", StringComparison.Ordinal)).ToList();
        var unregistered = known.Where(k => DeviceDisplays.PagesOf(k.TypeKey).Count == 0).Select(k => k.TypeKey).ToList();
        yield return (unregistered.Count == 0, $"displays: every built-in device has its own pictures{(unregistered.Count > 0 ? " — missing: " + string.Join(", ", unregistered) : "")}");

        using var e = new NotaEngine();
        var misnamed = new List<string>();
        var threw = new List<string>();
        var dark = new List<string>();
        var still = new List<string>();
        int pagesDrawn = 0;
        foreach (var (section, kind, key) in known)
        {
            int t = e.AddInstrumentTrack();
            DeviceAddress a;
            switch (section)
            {
                case FocusSection.Instrument: e.SetTrackBuiltinInstrument(t, kind); a = DeviceAddress.Instrument(t); break;
                case FocusSection.Audio: a = new DeviceAddress(t, FocusSection.Audio, e.AddBuiltinDevice(t, kind)); break;
                default: a = new DeviceAddress(t, FocusSection.Midi, e.AddMidiEffect(t, kind)); break;
            }
            var fd = FocusedDevice.Build(e, a, null);
            if (fd is null) continue;
            var registered = DeviceDisplays.PagesOf(key);
            for (int p = 0; p < fd.PageCount; p++)
            {
                string pageName = fd.PageName(p);
                if (p >= registered.Count || !string.Equals(registered[p].Page, pageName, StringComparison.OrdinalIgnoreCase))
                    misnamed.Add($"{key} page {p + 1} \"{pageName}\"");
                var page = DeviceDisplays.Find(key, p, pageName);
                if (page is null) continue;
                var knobs = fd.Knobs(p).Select(k => k.Target is null ? null : k.Name).ToArray();
                var ps = new DisplayParams(e, a);
                var state = new DisplayState();
                LedFrame Draw(double time, int turning)
                {
                    var f = new LedFrame();
                    state.NextFrame();
                    page.Draw(new DisplayContext(e, a, ps, state, time, key, fd.DeviceName, p, pageName, knobs, turning), f);
                    _ = page.Readout?.Invoke(new DisplayContext(e, a, ps, state, time, key, fd.DeviceName, p, pageName, knobs, turning));
                    return f;
                }
                LedFrame first;
                try { first = Draw(1.0, -1); }
                catch (Exception ex) { threw.Add($"{key}/{pageName}: {ex.GetType().Name} {ex.Message}"); continue; }
                pagesDrawn++;
                int lit = 0;
                for (int y = LedFrame.Top; y < LedFrame.H; y++)
                    for (int x = 0; x < LedFrame.W; x++) if (first[x, y] > 0) lit++;
                if (lit < 40) dark.Add($"{key}/{pageName} ({lit})");

                // Turn each knob of the page in turn: the picture or its readout must move for at least one.
                bool moved = false;
                var before = first.Rle4();
                for (int k = 0; k < knobs.Length && !moved; k++)
                {
                    if (fd.Target(p, k) is not { } tg) continue;
                    var old = Read(e, tg);
                    Write(e, tg, old < 0.5 ? 0.95 : 0.05);
                    try
                    {
                        var after = Draw(1.0, k).Rle4();
                        moved = !after.AsSpan().SequenceEqual(before);
                    }
                    catch (Exception ex) { threw.Add($"{key}/{pageName} knob {k + 1}: {ex.GetType().Name} {ex.Message}"); }
                    Write(e, tg, old);
                }
                if (!moved && knobs.Any(n => n is not null)) still.Add($"{key}/{pageName}");

                if (dump is { Length: > 0 })
                {
                    var full = new LedFrame();
                    DisplayRenderer.Render(new DisplayContext(e, a, ps, state, 1.0, key, DisplayRenderer.ShortName(fd.DeviceName), p, pageName, knobs, -1), full, null);
                    File.WriteAllText(Path.Combine(dump, $"{key.Replace(':', '_')}__{p + 1}_{Safe(pageName)}.txt"), full.ToAscii());
                }
            }
        }
        yield return (misnamed.Count == 0, $"displays: their pages match the controller's pages by name{(misnamed.Count > 0 ? " — " + string.Join("; ", misnamed.Take(10)) : "")}");
        yield return (threw.Count == 0, $"displays: every page draws on a fresh device ({pagesDrawn} pages){(threw.Count > 0 ? " — " + string.Join("; ", threw.Take(8)) : "")}");
        yield return (dark.Count == 0, $"displays: every page lights its graph area{(dark.Count > 0 ? " — dark: " + string.Join(", ", dark.Take(10)) : "")}");
        yield return (still.Count == 0, $"displays: turning a page's knobs changes its picture{(still.Count > 0 ? " — still: " + string.Join(", ", still.Take(12)) : "")}");
    }

    private static string Safe(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static double Read(NotaEngine e, ControllerTarget t)
    {
        switch (t.Kind)
        {
            case ControllerTargetKind.PluginParam: return e.PluginParamGet(t.TrackId, t.Device, t.Param);
            case ControllerTargetKind.DeviceParam:
            {
                float lo = e.DeviceParamMin(t.TrackId, t.Device, t.Param), hi = e.DeviceParamMax(t.TrackId, t.Device, t.Param);
                return hi > lo ? (e.DeviceGetParam(t.TrackId, t.Device, t.Param) - lo) / (hi - lo) : 0;
            }
            case ControllerTargetKind.MidiDeviceParam:
            {
                float lo = e.MidiEffectParamMin(t.TrackId, t.Device, t.Param), hi = e.MidiEffectParamMax(t.TrackId, t.Device, t.Param);
                return hi > lo ? (e.MidiEffectGetParam(t.TrackId, t.Device, t.Param) - lo) / (hi - lo) : 0;
            }
            default: return 0;
        }
    }

    private static void Write(NotaEngine e, ControllerTarget t, double n)
    {
        switch (t.Kind)
        {
            case ControllerTargetKind.PluginParam: e.PluginParamSet(t.TrackId, t.Device, t.Param, (float)n); break;
            case ControllerTargetKind.DeviceParam:
            {
                float lo = e.DeviceParamMin(t.TrackId, t.Device, t.Param), hi = e.DeviceParamMax(t.TrackId, t.Device, t.Param);
                e.DeviceSetParam(t.TrackId, t.Device, t.Param, (float)(lo + n * (hi - lo)));
                break;
            }
            case ControllerTargetKind.MidiDeviceParam:
            {
                float lo = e.MidiEffectParamMin(t.TrackId, t.Device, t.Param), hi = e.MidiEffectParamMax(t.TrackId, t.Device, t.Param);
                e.MidiEffectSetParam(t.TrackId, t.Device, t.Param, (float)(lo + n * (hi - lo)));
                break;
            }
        }
    }
}
