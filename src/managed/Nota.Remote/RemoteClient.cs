// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One connected phone. Its socket is read on a Kestrel thread: notes go straight to the engine
// from there (the engine's track-note entry is thread-safe), so a pad hit never waits for the UI
// tick; everything else is queued for the hub's UI-thread tick. Outgoing messages go through a
// channel drained by a single send loop, because a WebSocket allows one sender at a time.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Nota.Remote;

public sealed class RemoteClient
{
    private static int _nextId;

    private readonly WebSocket _ws;
    private readonly Channel<byte[]> _out = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _notesGate = new();
    private readonly Dictionary<int, int> _held = new();   // pitch → track it started on

    public int ConnectionId { get; } = Interlocked.Increment(ref _nextId);
    public string Address { get; }
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;

    /// <summary>The trusted device behind this connection (null until it says hello or pairs).</summary>
    public TrustedDevice? Device { get; internal set; }
    public string Name { get; internal set; } = "Phone";
    public string Model { get; internal set; } = "";

    /// <summary>The track this phone plays (0 = none yet). Read on the socket thread for notes.</summary>
    public volatile int TrackId;
    /// <summary>Where the track sat in the list, so a deleted track hands over to its neighbour.</summary>
    public int LastTrackIndex;
    /// <summary>The track follows Nota's selection.</summary>
    public volatile bool Follow;
    /// <summary>The screen the phone shows: what state it needs pushed.</summary>
    public volatile string Screen = "";
    /// <summary>The Macros screen's device (index into the track's macro devices).</summary>
    public int MacroDevice;
    /// <summary>Round-trip time the phone measured, ms (−1 = not yet).</summary>
    public volatile int RttMs = -1;
    public long LastActivityTicks;

    // What this phone was last sent, so a tick only sends what changed.
    internal string? SentProject;
    internal string? SentWho;
    internal string? SentTransport;
    internal string? SentLit;
    internal string? SentLearn;
    internal string? SentScreenState;

    public bool Authenticated => Device is not null;

    internal RemoteClient(WebSocket ws, string address)
    {
        _ws = ws;
        Address = address;
    }

    /// <summary>Queue a message. Dropped when the phone has stopped reading (a stale socket).</summary>
    public void Send(byte[] utf8Json)
    {
        if (_ws.State == WebSocketState.Open) _out.Writer.TryWrite(utf8Json);
    }

    public void Send(string json) => Send(Encoding.UTF8.GetBytes(json));

    /// <summary>Messages waiting to go out — the hub skips a phone's state tick while it lags.</summary>
    public int Backlog => _out.Reader.CanCount ? _out.Reader.Count : 0;

    internal async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _out.Reader.ReadAllAsync(ct))
            {
                if (_ws.State != WebSocketState.Open) break;
                await _ws.SendAsync(msg, WebSocketMessageType.Text, true, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    internal void CompleteSend() => _out.Writer.TryComplete();

    /// <summary>Reads whole text messages until the socket closes.</summary>
    internal async Task ReceiveLoopAsync(Func<RemoteClient, JsonElement, Task> onMessage, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        try
        {
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                int len = 0;
                WebSocketReceiveResult r;
                do
                {
                    if (len == buf.Length) { await CloseAsync(WebSocketCloseStatus.MessageTooBig, "too big"); return; }
                    r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf, len, buf.Length - len), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    len += r.Count;
                } while (!r.EndOfMessage);
                if (r.MessageType != WebSocketMessageType.Text) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(buf.AsMemory(0, len)); }
                catch (JsonException) { continue; }
                using (doc)
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        await onMessage(this, doc.RootElement);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    public async Task CloseAsync(WebSocketCloseStatus status, string reason)
    {
        CompleteSend();
        try
        {
            if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _ws.CloseOutputAsync(status, reason, CancellationToken.None);
        }
        catch { /* already gone */ }
    }

    // ---- held notes ---------------------------------------------------------
    // A note-off goes to the track its note-on went to, so switching tracks (or another phone
    // deleting the track) mid-hold never hangs a note.

    /// <summary>Remember a note-on to <paramref name="track"/>. Returns the track of the same
    /// pitch's earlier note if it was still held (a retrigger), which must end first.</summary>
    internal int? NoteOn(int pitch, int track)
    {
        lock (_notesGate)
        {
            int? prev = _held.Remove(pitch, out int p) ? p : null;
            _held[pitch] = track;
            return prev;
        }
    }

    internal bool NoteOff(int pitch, out int track)
    {
        lock (_notesGate) return _held.Remove(pitch, out track);
    }

    /// <summary>Drop every note the phone no longer claims in its ping (the "h" array) and
    /// return them for release. A late <c>off</c> that is still in flight is harmless: it
    /// arrives before the ping did (one socket, in order) or finds nothing held.</summary>
    internal List<(int Track, int Pitch)> UnclaimHeld(JsonElement claimed)
    {
        var keep = new HashSet<int>();
        foreach (var v in claimed.EnumerateArray())
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int p) && p is >= 0 and <= 127)
                keep.Add(p);
        lock (_notesGate)
        {
            var gone = _held.Where(kv => !keep.Contains(kv.Key))
                .Select(kv => (kv.Value, kv.Key)).ToList();
            foreach (var (_, pitch) in gone) _held.Remove(pitch);
            return gone;
        }
    }

    /// <summary>Every note still held (the link dropped, or the phone left): release them.</summary>
    internal List<(int track, int pitch)> TakeAllHeld()
    {
        lock (_notesGate)
        {
            var list = _held.Select(kv => (kv.Value, kv.Key)).ToList();
            _held.Clear();
            return list;
        }
    }

    internal int[] HeldPitches()
    {
        lock (_notesGate) return _held.Keys.ToArray();
    }
}
