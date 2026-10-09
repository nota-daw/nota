// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The hub between the phones and Nota. Sockets run on Kestrel threads and only do three
// things there: authenticate, answer pings, and play notes (the engine's track-note entry is
// thread-safe, so a pad hit reaches the audio thread without waiting for the UI). Every other
// command is queued and applied in Tick(), which Nota calls on its UI clock (~30 Hz) — the
// thread that owns the engine. Tick also builds what each phone needs for the screen it shows
// and sends only what changed since the last message.

using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Nota.Application;
using Nota.Application.Samples;

namespace Nota.Remote;

/// <summary>A connected phone, as the popup and Settings list it.</summary>
public sealed record RemoteDeviceInfo(int ConnectionId, string DeviceId, string Name, string Model, int TrackId, int RttMs, string Screen, bool Usb);

public sealed partial class RemoteHub
{
    public const int ProtocolVersion = 1;
    /// <summary>A phone that has gone this long without a word has lost the link: its notes are
    /// released (it pings every second while it is up).</summary>
    private static readonly TimeSpan SilenceRelease = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan SilenceDrop = TimeSpan.FromSeconds(20);

    private readonly IAudioEngine _engine;
    private readonly ConcurrentDictionary<int, RemoteClient> _clients = new();
    private readonly ConcurrentQueue<(RemoteClient Client, RemoteCommand Cmd)> _inbox = new();
    private int _activity;
    private volatile bool _devicesDirty;
    private int _tick;

    public RemotePairing Pairing { get; }
    /// <summary>The app side. Until it is set, Tick does nothing (the window isn't up yet).</summary>
    public IRemoteHost? Host { get; set; }
    /// <summary>Settings → Remote → Phones may.</summary>
    public Func<RemoteAccess> Access { get; set; } = () => RemoteAccess.Control;
    /// <summary>The computer's name for the phone's banners, read off the UI thread.</summary>
    public string HostName { get; set; } = Environment.MachineName;

    public RemoteHub(IAudioEngine engine, RemotePairing pairing)
    {
        _engine = engine;
        Pairing = pairing;
    }

    /// <summary>The paired phones connected right now.</summary>
    public IReadOnlyList<RemoteDeviceInfo> Devices =>
        _clients.Values.Where(c => c.Authenticated).OrderBy(c => c.ConnectedAt)
            .Select(c => new RemoteDeviceInfo(c.ConnectionId, c.Device!.Id, c.Name, c.Model, c.TrackId, c.RttMs, c.Screen, UsbLink(c)))
            .ToList();

    private static bool UsbLink(RemoteClient c)
        => IPAddress.TryParse(c.Address, out var ip) && RemoteLinks.OnUsbLink(ip);

    /// <summary>Names of the phones playing <paramref name="trackId"/> (the track-header badge).</summary>
    public IReadOnlyList<string> PlayersOf(int trackId) =>
        _clients.Values.Where(c => c.Authenticated && c.TrackId == trackId).Select(c => ShortName(c.Name)).ToList();

    /// <summary>"Egor's iPhone" → "Egor": what fits a badge.</summary>
    public static string ShortName(string name)
    {
        int apos = name.IndexOfAny(new[] { '\'', '’' });
        if (apos > 0) return name[..apos];
        int sp = name.IndexOf(' ');
        return sp > 0 && name.Length > 12 ? name[..sp] : name;
    }

    // ---- socket side ----------------------------------------------------------

    internal async Task RunClientAsync(WebSocket ws, string address, CancellationToken ct)
    {
        var c = new RemoteClient(ws, address);
        c.LastActivityTicks = DateTime.UtcNow.Ticks;
        _clients[c.ConnectionId] = c;
        var send = c.StartSendLoop(ct);
        try
        {
            await c.ReceiveLoopAsync(OnMessageAsync, ct);
        }
        finally
        {
            _clients.TryRemove(c.ConnectionId, out _);
            ReleaseNotes(c);
            c.CompleteSend();
            await c.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye");
            try { await send; } catch { }
            if (c.Authenticated) _devicesDirty = true;
        }
    }

    private Task OnMessageAsync(RemoteClient c, JsonElement m)
    {
        c.LastActivityTicks = DateTime.UtcNow.Ticks;
        string t = Str(m, "t");
        switch (t)
        {
            case "hello": Hello(c, m); return Task.CompletedTask;
            case "pair": PairWithCode(c, m); return Task.CompletedTask;
            case "ping":
                // Echo at once, from this thread: the round trip is the link, not the UI tick.
                if (m.TryGetProperty("rtt", out var rtt) && rtt.TryGetInt32(out int ms)) c.RttMs = ms;
                c.Send($"{{\"t\":\"pong\",\"c\":{Num(m, "c").ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
                // The phone claims the notes it holds; anything it stopped claiming (an off a
                // browser swallowed in fast multi-touch, one lost to a link flap) is released
                // instead of ringing until the phone disconnects.
                if (c.Authenticated && m.TryGetProperty("h", out var held) && held.ValueKind == JsonValueKind.Array)
                    foreach (var (track, pitch) in c.UnclaimHeld(held))
                        _engine.TrackNoteOff(track, pitch);
                return Task.CompletedTask;
        }
        if (!c.Authenticated) return Task.CompletedTask;

        Interlocked.Increment(ref _activity);
        switch (t)
        {
            case "on":
            {
                int p = Int(m, "p");
                if (p is < 0 or > 127) break;
                float v = (float)Math.Clamp(Num(m, "v", 0.8), 0.01, 1.0);
                int track = c.TrackId;
                if (track <= 0) break;
                if (c.NoteOn(p, track) is int prev) _engine.TrackNoteOff(prev, p);
                _engine.TrackNoteOn(track, p, v);
                break;
            }
            case "off":
            {
                int p = Int(m, "p");
                if (c.NoteOff(p, out int track)) _engine.TrackNoteOff(track, p);
                break;
            }
            case "x":
            {
                // Per-note expression (the Keys screen's expressive mode): d 0 = bend in
                // semitones, 1 = pressure, 2 = slide. Only for a note this phone holds, on the
                // track that note started on.
                int p = Int(m, "p"), d = Int(m, "d");
                if (d is < 0 or > 2 || !c.TrackOf(p, out int track)) break;
                double v = Num(m, "v", d == 2 ? 0.5 : 0.0);
                v = d == 0 ? Math.Clamp(v, -24, 24) : Math.Clamp(v, 0, 1);
                _engine.TrackNoteExpression(track, p, (NoteExpressionDim)d, (float)v);
                break;
            }
            case "panic": ReleaseNotes(c); break;
            case "bye": _ = c.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye"); break;
            default:
                _inbox.Enqueue((c, RemoteCommand.Parse(t, m)));
                break;
        }
        return Task.CompletedTask;
    }

    private void Hello(RemoteClient c, JsonElement m)
    {
        if (Int(m, "v", ProtocolVersion) > ProtocolVersion) { Deny(c, "version"); return; }
        var dev = Pairing.Authenticate(Str(m, "token"));
        if (dev is null) { Deny(c, "forgotten"); return; }
        Accept(c, dev, token: null, m);
    }

    private void PairWithCode(RemoteClient c, JsonElement m)
    {
        var r = Pairing.Pair(Str(m, "code"), Str(m, "name"), c.Address, out var error);
        if (r is null) { Deny(c, error ?? "code"); return; }
        Accept(c, r.Value.Device, r.Value.Token, m);
    }

    private void Accept(RemoteClient c, TrustedDevice dev, string? token, JsonElement m)
    {
        // A phone that reconnects (screen unlock, Wi-Fi back) replaces its stale socket.
        foreach (var old in _clients.Values)
            if (old != c && old.Device?.Id == dev.Id)
            {
                ReleaseNotes(old);
                _ = old.CloseAsync(WebSocketCloseStatus.PolicyViolation, "replaced");
            }
        c.Name = dev.Name;
        c.Model = PairingModel(Str(m, "model"));
        c.TrackId = Int(m, "track");
        c.Follow = Bool(m, "follow");
        c.Screen = Str(m, "screen");
        c.Device = dev;
        c.SentProject = c.SentWho = c.SentTransport = c.SentLit = c.SentLearn = c.SentScreenState = null;
        c.Send(Json(w =>
        {
            w.WriteString("t", "welcome");
            w.WriteNumber("v", ProtocolVersion);
            w.WriteString("id", dev.Id);
            w.WriteString("name", dev.Name);
            w.WriteString("host", HostName);
            w.WriteNumber("conn", c.ConnectionId);
            w.WriteString("via", UsbLink(c) ? "usb" : "wifi");   // the header's link badge
            if (token is not null) w.WriteString("token", token);
        }));
        _devicesDirty = true;
    }

    private static string PairingModel(string s) => s.Length > 40 ? s[..40] : s;

    private static void Deny(RemoteClient c, string why)
        => c.Send($"{{\"t\":\"denied\",\"why\":\"{why}\"}}");

    private void ReleaseNotes(RemoteClient c)
    {
        foreach (var (track, pitch) in c.TakeAllHeld()) _engine.TrackNoteOff(track, pitch);
    }

    // ---- UI thread ------------------------------------------------------------

    /// <summary>Drop a phone (the popup's ✕). It may reconnect while it is still trusted.</summary>
    public void Disconnect(int connectionId)
    {
        if (!_clients.TryGetValue(connectionId, out var c)) return;
        ReleaseNotes(c);
        c.Send("{\"t\":\"kicked\"}");
        _ = c.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnected in Nota");
    }

    /// <summary>Forget a phone: it is dropped and has to scan a new code.</summary>
    public void Forget(string deviceId)
    {
        Pairing.Forget(deviceId);
        foreach (var c in _clients.Values)
            if (c.Device?.Id == deviceId)
            {
                ReleaseNotes(c);
                c.Send("{\"t\":\"denied\",\"why\":\"forgotten\"}");
                _ = c.CloseAsync(WebSocketCloseStatus.NormalClosure, "forgotten");
            }
        _devicesDirty = true;
    }

    /// <summary>Remote turned off: everyone leaves at once, with their notes released.</summary>
    public void DisconnectAll()
    {
        foreach (var c in _clients.Values)
        {
            ReleaseNotes(c);
            _ = c.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Remote is off");
        }
        _clients.Clear();
        _devicesDirty = true;
    }

    /// <summary>Count of messages from phones since start — the Remote button flashes while it moves.</summary>
    public int ActivityCount => Volatile.Read(ref _activity);

    /// <summary>Called on Nota's UI clock: apply what the phones asked for, then send each one
    /// the state its screen needs.</summary>
    public void Tick()
    {
        var host = Host;
        if (host is null) return;
        _tick++;

        while (_inbox.TryDequeue(out var item))
        {
            if (!item.Client.Authenticated || !_clients.ContainsKey(item.Client.ConnectionId)) continue;
            try { Apply(host, item.Client, item.Cmd); }
            catch (Exception) { /* a stale id from a phone must never take the UI clock down */ }
        }

        var clients = _clients.Values.Where(c => c.Authenticated).ToList();
        ExpireHeldFaders();
        long now = DateTime.UtcNow.Ticks;
        foreach (var c in clients)
        {
            var silent = TimeSpan.FromTicks(now - c.LastActivityTicks);
            if (silent > SilenceRelease) ReleaseNotes(c);
            if (silent > SilenceDrop) { _ = c.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "silent"); continue; }
        }

        if (clients.Count > 0)
        {
            var snap = new Snapshot(this, host);
            foreach (var c in clients)
            {
                if (c.Backlog > 16) continue;   // the phone isn't keeping up: let it drain first
                try { Push(host, snap, c); }
                catch (Exception) { /* a track removed mid-tick; next tick sees the new list */ }
            }
        }

        if (_devicesDirty)
        {
            _devicesDirty = false;
            host.DevicesChanged();
        }
        if (_activity != _lastActivity) { _lastActivity = _activity; host.Activity(); }
    }

    private int _lastActivity;

    internal static string Json(Action<Utf8JsonWriter> body)
    {
        var buf = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    // ---- JSON reading helpers ----------------------------------------------------

    internal static string Str(JsonElement m, string name)
        => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    internal static int Int(JsonElement m, string name, int fallback = 0)
        => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d)
            ? (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue) : fallback;

    internal static double Num(JsonElement m, string name, double fallback = 0)
        => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d) ? d : fallback;

    internal static bool Bool(JsonElement m, string name)
        => m.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && d != 0));
}

/// <summary>A phone command, parsed on the socket thread and applied on the UI tick.</summary>
internal readonly record struct RemoteCommand(string T, int Id, int I, double V, double V2, bool On, int Phase, string S)
{
    public static RemoteCommand Parse(string t, JsonElement m) => new(
        t,
        RemoteHub.Int(m, "id"),
        RemoteHub.Int(m, "i"),
        RemoteHub.Num(m, "v"),
        RemoteHub.Num(m, "v2"),
        RemoteHub.Bool(m, "on"),
        RemoteHub.Int(m, "ph"),
        RemoteHub.Str(m, "s"));
}
