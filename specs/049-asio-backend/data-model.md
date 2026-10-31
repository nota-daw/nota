# Data model: ASIO audio on Windows

Engine-side state; nothing here is stored in the project file.

## AudioConfig (extended, `src/audio_io/AudioConfig.h`, persisted in `audio.json`)

| Field | Type | Default | Notes |
|---|---|---|---|
| outputDeviceUid, inputDeviceUid, sampleRate, bufferFrames, wasapiExclusive | — | — | unchanged |
| `audioSystem` | enum `AudioSystem { Wasapi, Asio }` | `Wasapi` | Windows only; read as `Wasapi` elsewhere |
| `asioDriverClsid` | string | `""` | primary key of the chosen driver |
| `asioDriverName` | string | `""` | fallback key if the CLSID is gone |
| `asioOutputPair` | int32 | 0 | first channel index of the master pair; even numbers in UI |
| `asioInputPair` | int32 | 0 | first channel index of the recording pair |

Validation: an out-of-range pair is clamped to 0 at open; an unknown driver → fallback
(FR-011). `bufferFrames` is ignored while `audioSystem == Asio` (driver decides, FR-008);
`sampleRate` is requested via `setSampleRate` if non-zero and supported.

## AsioDriverInfo (enumeration result, not persisted)

`clsid`, `name` (registry key), `displayName` (name + ` (2)` suffix for duplicates).

## AsioChannelInfo (per open driver)

`index`, `isInput`, `name` (from `getChannelInfo`), `sampleType` (ASIO enum).
Pairs shown to the user are derived: `(i, i+1)` for even `i`; a trailing odd channel is a
mono "pair".

## AsioSession state machine

```text
Closed ──load(clsid)──▶ Loaded ──init()+createBuffers()──▶ Prepared ──start()──▶ Running
  ▲                       │ fail                             │ fail                 │
  │                       ▼                                  ▼                      │
  └────────────── Failed(reason) ◀───────────────────────────┴──── watchdog / error ┘
Running ──resetRequested──▶ (event to app) ──apply──▶ Closed ──▶ … ──▶ Running
```

- `Failed(reason)`: reason is one of `NotInstalled`, `LoadFailed`, `InitFailed`
  (driver returned false / busy), `UnsupportedFormat(type)`, `NoOutputs`,
  `Timeout`, `Stopped` (no buffer callback for 2 s while running). The engine then starts
  the miniaudio WASAPI backend with system defaults and keeps `audioSystem = Asio` in the
  saved config (FR-011).
- Atomics set from `asioMessage` / `bufferSwitch`: `resetRequested`, `lastCallbackTick`.
  Overloads call the engine's xrun counter directly (an atomic increment). The watchdog
  runs inside `nota_audio_poll_device_event` (UI tick, ≈ 33 ms) rather than on its own
  thread: a running driver silent for 2 s moves to `Failed(Stopped)` on the next poll.
  The restart that `DRIVER_FAILED` triggers runs WASAPI straight away (a driver that starts
  but stays silent would otherwise fail forever); the user's next apply tries ASIO again.
  Events are deferred while the driver's own panel is open.
- A driver call that times out abandons the driver: its callbacks are cut off and no other
  ASIO driver opens until Nota restarts (ASIO callbacks can't tell two drivers apart).
- A sample rate the driver refuses is dropped from the saved config (back to device default).

## Device event (C ABI poll)

`NotaAudioDeviceEvent { NONE = 0, RESET_REQUESTED = 1, DRIVER_FAILED = 2 }` — returned by
`nota_audio_poll_device_event`, cleared on read.

## Latency

`AudioBackend::inputLatencyFrames()`, `outputLatencyFrames()` (default 0). Recording
offset = input + output; the Latency line shows output (and round trip when recording).
