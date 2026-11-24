# Contract: C ABI additions (`nota_engine.h`)

Delivered in slice 1. All calls are **message thread** (the managed UI thread) unless noted.
Existing MIDI calls keep their meaning; `nota_midi_poll_control_events` still reports CC + note-on
copies for MIDI Learn — for **unclaimed** messages, and for claimed ones only while MIDI Learn is
listening (`nota_midi_set_learn_listening`). An explicit MIDI Learn mapping wins (FR-012): once a
claimed control is learned, the app drops it from the claim table.

```c
/* ---- MIDI control surfaces (specs/051) -----------------------------------
 * Controllers recognised by a profile claim some of their messages: a claimed message is
 * delivered to the app through the surface queue below and never reaches an instrument.
 * Unclaimed messages are routed exactly as before. Claims are published as one immutable
 * table (all sources at once) and swapped atomically; the MIDI thread only reads it.
 * `source` is the 0..15 index the input tags events with (see nota_midi_port_*). */

/* Bumped whenever MIDI sources / destinations appear or disappear (hot-plug). Cheap; poll
 * it on the UI tick and re-read the port list when it changes. Any thread. */
NOTA_API uint32_t    nota_midi_ports_generation(void);

/* Connected (enabled) input ports, as the MIDI thread numbers them. Strings are engine-owned,
 * valid until the next nota_midi_port_* / nota_midi_refresh_devices call. NULL if out of range. */
NOTA_API int32_t     nota_midi_port_count(NotaEngine* engine);   /* re-reads the list */
NOTA_API int32_t     nota_midi_port_source(const NotaEngine* engine, int32_t index);   /* 0..15 */
NOTA_API const char* nota_midi_port_uid(const NotaEngine* engine, int32_t index);
NOTA_API const char* nota_midi_port_name(const NotaEngine* engine, int32_t index);
/* The paired output (same device / entity; same name on RtMidi). "" when none. */
NOTA_API const char* nota_midi_port_output_uid(const NotaEngine* engine, int32_t index);

/* Claim table. One entry per claimed message; whole_port = 1 claims every channel message
 * of that source (MCU / HUI / the setup wizard's capture) and ignores status/channel/data1.
 * status: 0x80 note (on and off), 0xB0 CC, 0xE0 pitch bend, 0xD0 channel pressure;
 * data1 = note / CC number (0 for bend / pressure). Replaces the previous table entirely;
 * count 0 clears all claims. NOTA_OK, or NOTA_ERR_INVALID_ARG on a malformed entry. */
typedef struct NotaMidiClaim {
    int32_t source;
    int32_t whole_port;
    int32_t status;
    int32_t channel;   /* 0..15 */
    int32_t data1;     /* 0..127 */
} NotaMidiClaim;
NOTA_API NotaResult  nota_midi_set_claims(NotaEngine* engine, const NotaMidiClaim* claims, int32_t count);

/* Drain claimed channel messages: int32 quints {source, status, channel, data1, data2}
 * (status high nibble; note-on with velocity 0 is reported as 0x80). Returns the count
 * written (<= max). Lossy when the 1024-entry queue overflows (the overflow count is
 * reported by nota_midi_surface_dropped). */
NOTA_API int32_t     nota_midi_poll_surface_events(NotaEngine* engine, int32_t* out, int32_t max);
NOTA_API uint32_t    nota_midi_surface_dropped(const NotaEngine* engine);

/* While MIDI Learn waits for a message, claimed messages are ALSO copied to the
 * MIDI-learn queue (nota_midi_poll_control_events), so a control a profile owns can still
 * be learned (FR-012). They keep going to the surface queue, never to instruments. */
NOTA_API void        nota_midi_set_learn_listening(NotaEngine* engine, int32_t listening);

/* Drain one complete SysEx message (F0 … F7 inclusive) received from a source that has a
 * claim, or from a source while an identity request to it is outstanding (≤ 2 s after
 * nota_midi_send of the Identity Request F0 7E 7F 06 01 F7 to that port's output — any
 * other send does not open the window). Returns the byte length copied into out (0 = none
 * pending, -n = message of n bytes did not fit, it is dropped). *out_source receives the
 * source. Messages longer than 512 bytes are dropped on the MIDI thread. */
NOTA_API int32_t     nota_midi_poll_sysex(NotaEngine* engine, int32_t* out_source, uint8_t* out, int32_t cap);

/* Send raw MIDI bytes (one or more complete messages, SysEx allowed) to the output port with
 * `output_uid`. Opens the output on first use; never called from the audio thread.
 * NOTA_OK, NOTA_ERR_INVALID_ARG (malformed bytes), NOTA_ERR_UNKNOWN (no such output / send failed). */
NOTA_API NotaResult  nota_midi_send(NotaEngine* engine, const char* output_uid, const uint8_t* bytes, int32_t len);

/* Deterministic self-test, no hardware: feeds byte streams through the input parser with a
 * claim table and checks (1) claimed → surface queue and not instruments, (2) unclaimed →
 * instruments + MIDI-learn copy as before, (3) whole-port claims, (4) SysEx reassembly
 * across packets incl. interleaved real-time bytes, (5) the 512-byte SysEx cap, (6) an output
 * loopback receives nota_midi_send bytes unchanged. 0 = pass, else the first failed check. */
NOTA_API int32_t     nota_midi_surface_selftest(void);

/* Test ports (MCP controller_inject, NOTA_CONTROLLER_TEST=1 only — the managed side gates it).
 * Adds a virtual input source + loopback output named `name` to the port list (bumping the
 * ports generation); inject feeds raw bytes through the same parser / claim path as a real
 * source; sent bytes to its output are kept (64 KiB ring) for nota_midi_test_take_sent. */
NOTA_API int32_t     nota_midi_test_port_add(NotaEngine* engine, const char* name);   /* source or -1 */
NOTA_API NotaResult  nota_midi_test_inject(NotaEngine* engine, int32_t source, const uint8_t* bytes, int32_t len);
NOTA_API int32_t     nota_midi_test_take_sent(NotaEngine* engine, int32_t source, uint8_t* out, int32_t cap);
```

## Threading and real-time notes

- `nota_midi_set_claims` builds a new flat table (per source: whole-port flag + a
  `status × channel × data1` bitset) on the message thread and publishes it with an atomic
  pointer store; the previous table is retired on the message thread after the MIDI thread has
  moved on (epoch counter), mirroring the graph-snapshot pattern (AR-5).
- The MIDI-thread callback: `table->claimed(source, status, channel, d1)` → push to the SPSC
  surface queue (drop + count on overflow) **instead of** `MpeInput` and the MIDI-learn copy;
  otherwise unchanged. When the atomic `learnListening_` flag is set, a claimed CC / note-on is
  additionally pushed to `midiControl_` (the MIDI-learn copy). No allocation, no locks (AR-6).
- SysEx bytes accumulate in a fixed per-source 512 B buffer on the MIDI thread; a complete message
  is copied into a lock-free SysEx ring (16 slots × 512 B).
- `nota_midi_send` runs on the caller's thread (UI). CoreMIDI: `MIDISend` for short messages,
  `MIDISendSysex` with a request kept alive until its completion proc; RtMidi: `RtMidiOut::sendMessage`.

## Managed surface (`IAudioEngine`)

```csharp
uint MidiPortsGeneration { get; }
IReadOnlyList<MidiPortInfo> MidiPorts();                 // record (Uid, Name, Source, OutputUid)
void SetMidiClaims(ReadOnlySpan<MidiClaim> claims);
void SetMidiLearnListening(bool listening);
int PollMidiSurfaceEvents(Span<int> buffer);             // quints
int PollMidiSysex(out int source, Span<byte> buffer);
bool MidiSend(string outputUid, ReadOnlySpan<byte> bytes);
```
