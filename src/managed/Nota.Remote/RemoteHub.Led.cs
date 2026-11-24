// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Macros screen's LED display (specs/114, contracts/remote-led.md). On each UI tick the hub
// renders one frame per device page some phone shows — never one for a page nobody watches —
// and sends it, RLE4 in base64, to each phone on that page whose last frame differs. Two phones
// on one page get the same bytes and sequence number. A knob that moves (from the phone, a
// controller, the mouse or automation) puts its name and value in the header for about a second.

using System.Diagnostics;
using Nota.Application.Controllers;
using Nota.Application.Displays;

namespace Nota.Remote;

public sealed partial class RemoteHub
{
    private const long OverlayMs = 1100;
    private static readonly TimeSpan LedIdle = TimeSpan.FromSeconds(5);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // A watched device page: its display state, parameter access, last knob values (to spot a
    // turn), the frame it last rendered and that frame's sequence number.
    private sealed class LedPage
    {
        public readonly DisplayState State = new();
        public DisplayParams? Params;
        public string ParamsKey = "";
        public readonly double[] Knob = Enumerable.Repeat(double.NaN, 8).ToArray();
        public int TurnKnob = -1;
        public long TurnUntil;
        public byte[]? Bytes;
        public string? Base64;
        public int Seq;
        public int RenderedTick = -1;
        public long LastUsed;
    }

    private readonly Dictionary<string, LedPage> _ledPages = new();
    private readonly LedFrame _ledFrame = new();

    /// <summary>A phone moved knob <paramref name="knob"/> on its Macros page: show it at once.</summary>
    private void LedTurned(RemoteClient c, RemoteMacroDevice dev, int page, int knob)
    {
        if (_ledPages.TryGetValue(LedKey(dev, page), out var lp))
        {
            lp.TurnKnob = knob;
            lp.TurnUntil = _clock.ElapsedMilliseconds + OverlayMs;
        }
    }

    private static string LedKey(RemoteMacroDevice dev, int page) => $"{dev.Address}|{dev.TypeKey}|{page}";

    /// <summary>Send <paramref name="c"/> its Macros page's frame when it changed.</summary>
    private void PushLed(IRemoteHost host, RemoteClient c)
    {
        if (c.Screen != "Macros") { c.SentLed = null; c.SentLedKey = null; return; }
        var devs = MacroDevicesFor(host, c.TrackId);
        string key;
        string json;
        if (devs.Count == 0)
        {
            key = "none|" + c.TrackId;
            if (c.SentLedKey == key) return;
            DisplayRenderer.Message(_ledFrame, "NO DEVICES");
            json = LedJson(c.TrackId, 0, 0, 0, Convert.ToBase64String(_ledFrame.Rle4()));
            c.Send(json);
            c.SentLedKey = key;
            c.SentLed = null;
            return;
        }
        int di = Math.Clamp(c.MacroDevice, 0, devs.Count - 1);
        var dev = devs[di];
        int pg = Math.Clamp(c.MacroPage, 0, dev.Pages.Count - 1);
        key = LedKey(dev, pg);
        var lp = RenderLed(dev, pg, key);
        if (lp?.Bytes is null) return;
        if (c.SentLedKey == key && ReferenceEquals(c.SentLed, lp.Bytes)) return;
        c.Send(LedJson(c.TrackId, di, pg, lp.Seq, lp.Base64!));
        c.SentLed = lp.Bytes;
        c.SentLedKey = key;
    }

    private static string LedJson(int trackId, int di, int pg, int seq, string b64) => Json(w =>
    {
        w.WriteString("t", "led");
        w.WriteNumber("id", trackId);
        w.WriteNumber("di", di);
        w.WriteNumber("pg", pg);
        w.WriteNumber("w", LedFrame.W);
        w.WriteNumber("h", LedFrame.H);
        w.WriteNumber("bpp", 4);
        w.WriteString("enc", "rle4");
        w.WriteNumber("seq", seq);
        w.WriteString("f", b64);
    });

    /// <summary>Render <paramref name="dev"/>'s page once this tick; the page with its frame.</summary>
    private LedPage? RenderLed(RemoteMacroDevice dev, int pg, string key)
    {
        long now = _clock.ElapsedMilliseconds;
        if (!_ledPages.TryGetValue(key, out var lp)) _ledPages[key] = lp = new LedPage();
        lp.LastUsed = now;
        if (lp.RenderedTick == _tick) return lp;
        lp.RenderedTick = _tick;

        var page = dev.Pages[pg];
        // Parameter access by name, rebuilt when the device changes shape.
        string pk = dev.TypeKey + "|" + ParamCount(dev.Address);
        if (lp.Params is null || lp.ParamsKey != pk) { lp.Params = new DisplayParams(_engine, dev.Address); lp.ParamsKey = pk; }

        // A knob that moved since the last frame (anyone's hand) takes the header.
        var names = new string?[8];
        for (int k = 0; k < 8; k++)
        {
            var p = k < page.Knobs.Count ? page.Knobs[k] : null;
            names[k] = p?.Name;
            if (p is null) { lp.Knob[k] = double.NaN; continue; }
            double v = RemoteParams.Get(_engine, p);
            if (!double.IsNaN(lp.Knob[k]) && Math.Abs(v - lp.Knob[k]) > 0.0015) { lp.TurnKnob = k; lp.TurnUntil = now + OverlayMs; }
            lp.Knob[k] = v;
        }
        int turning = now < lp.TurnUntil ? lp.TurnKnob : -1;
        string? overlay = null;
        if (turning >= 0 && page.Knobs[turning] is { } tp)
            overlay = DisplayRenderer.Overlay(tp.Name, RemoteParams.Text(_engine, tp, lp.Knob[turning]));

        lp.State.NextFrame();
        var ctx = new DisplayContext(_engine, dev.Address, lp.Params, lp.State, now / 1000.0, dev.TypeKey,
            DisplayRenderer.ShortName(dev.Name), pg, page.Name, names, turning);
        DisplayRenderer.Render(ctx, _ledFrame, overlay);
        var bytes = _ledFrame.Rle4();
        if (lp.Bytes is null || !bytes.AsSpan().SequenceEqual(lp.Bytes))
        {
            lp.Bytes = bytes;
            lp.Base64 = Convert.ToBase64String(bytes);
            lp.Seq++;
        }
        return lp;
    }

    private int ParamCount(DeviceAddress a) => a.Section switch
    {
        FocusSection.Instrument => _engine.PluginParamCount(a.TrackId, -1),
        FocusSection.Audio => _engine.DeviceParamCount(a.TrackId, a.Index) + _engine.PluginParamCount(a.TrackId, a.Index) * 1000,
        _ => _engine.MidiEffectParamCount(a.TrackId, a.Index),
    };

    /// <summary>Forget pages nobody has watched for a while.</summary>
    private void ExpireLedPages()
    {
        if (_ledPages.Count == 0) return;
        long cutoff = _clock.ElapsedMilliseconds - (long)LedIdle.TotalMilliseconds;
        foreach (var k in _ledPages.Where(kv => kv.Value.LastUsed < cutoff).Select(kv => kv.Key).ToList()) _ledPages.Remove(k);
    }

    /// <summary>The frame a phone on <paramref name="trackId"/>'s device <paramref name="di"/>, page
    /// <paramref name="pg"/> would get now (tests, MCP later); null when there is no such page.</summary>
    public LedFrame? RenderDisplay(int trackId, int di, int pg)
    {
        if (Host is not { } host) return null;
        var devs = MacroDevicesFor(host, trackId);
        if (di < 0 || di >= devs.Count || pg < 0 || pg >= devs[di].Pages.Count) return null;
        _tick++;
        var lp = RenderLed(devs[di], pg, LedKey(devs[di], pg));
        return lp?.Bytes is { } b ? LedFrame.FromRle4(b) : null;
    }
}
