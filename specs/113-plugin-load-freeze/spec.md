# Feature Specification: A plug-in that hangs while loading doesn't freeze Nota

**Feature Branch**: `113-plugin-load-freeze`

**Created**: 2026-11-23

**Status**: Draft

**Input**: User description: "#979: a hosted plug-in that blocks while loading (SSL / Slate Digital PACE-wrapped plug-ins on macOS) must not freeze Nota — keep the UI responsive with a placeholder device while it loads, bound the wait with a timeout and a clear error, remember a plug-in that froze Nota so it isn't auto-loaded again, and name the likely causes of the PACE hang."

Issue: [nota-daw/nota#979](https://github.com/nota-daw/nota/issues/979) (approved).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — the insert / project-load / preset paths and the placeholder device row
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — the "loading" and "couldn't load" device states
- [x] Plug-in hosting (`pluginhost/`) — the only place JUCE is allowed; main area
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Diagnosis *(why a PACE-wrapped plug-in freezes Nota)*

### What the report shows

The reporter's process sample has the **main thread** in
`Engine::addTrackEffectPlugin → VST3PluginFormat::createPluginInstance → DLLHandle::open →
SSL 4K E → PaceProtectionWrapper`. Both the VST3 and the AU builds freeze; the same
plug-ins work in Ableton Live on the same Mac; other third-party plug-ins load in Nota.
The sample file itself isn't attached to the issue, so we can't yet tell a **deadlock**
(main thread parked in a wait / `mach_msg` / semaphore) from a **spin** (main thread busy
inside PACE code).

### How Nota loads a plug-in today

- Every in-process instantiation goes through `hosting::createInstance`
  (`pluginhost/hosting/PluginCommon.cpp`), which calls the **synchronous**
  `AudioPluginFormatManager::createPluginInstance` on the caller's thread. Callers are
  all on the UI thread: `DeviceInsertService` (drag from the browser),
  `ProjectService.ApplyDevices` (project open), `PresetService`, MCP `PluginTools`,
  plus `cloneInstance` (duplicate / copy).
- On macOS the UI thread **is** the process main thread and JUCE's message thread
  (JUCE shares Avalonia's `CFRunLoop`; see `PluginHost.cpp`). While a load runs, the run
  loop is not serviced at all — no Avalonia rendering, no JUCE messages, no main-queue
  blocks, no window-server events.
- For VST3, `DLLHandle::open` is `CFBundleLoadExecutableAndReturnError` + `bundleEntry`.
  A PACE (iLok / "Eden") wrapped binary decrypts and verifies itself in its load-time
  initialisers and licence check, i.e. exactly there.
- Scanning already runs out of process (`nota-scanworker`), but a VST3 that ships
  `moduleinfo.json` is listed **without loading its binary** (JUCE's fast path), so the
  scan proves nothing about whether the binary loads.
- Nota's macOS bundle is ad-hoc signed **without** the hardened runtime
  (`scripts/package-dmg.sh`: `codesign --force --deep --sign -`), so library validation
  and missing `cs.*` entitlements are **not** the cause.

### Likely causes, most to least likely

1. **PACE needs the main run loop while the main thread is blocked loading it.**
   PACE's loader / licence check can hand work to the main queue or put up licence UI
   (activation, "iLok not found", subscription check — SSL Complete Access is a
   subscription) and wait for it. With the load itself on the main thread, nothing ever
   services that work: a self-deadlock that lasts until force-quit. This has precedent:
   PACE-wrapped AUs hanging with a PACE dialog whose buttons never respond when loaded
   before the host's window-server connection / run loop was ready
   ([coreaudio-api, 2010](https://lists.apple.com/archives/coreaudio-api/2010/Jul/msg00143.html)).
   Why Ableton escapes it: it loads in a state where its run loop (or the licence
   service round trip) still gets serviced, or its licence is already cached for that
   host — not confirmable without the plug-in.
2. **The .NET runtime in-process looks like a debugger to PACE.** Nota is the only host
   in the comparison that runs CoreCLR in the same process. On macOS CoreCLR's PAL
   installs **Mach exception ports** on its threads to turn hardware faults into managed
   exceptions (`CPalThread::EnableMachExceptions` → `thread_swap_exception_ports`, see
   [dotnet/diagnostics#4551](https://github.com/dotnet/diagnostics/issues/4551)). Anti-tamper
   code commonly treats non-default exception ports, or the loss of its own deliberately
   raised exceptions to someone else's handler, as a debugger and stalls or loops. This
   fits "works in Ableton, freezes only in Nota" and a sample that ends inside the PACE
   wrapper; it would be a spin (100% CPU on the main thread), not a wait.
3. **A slow first-time licence check that isn't actually infinite.** An online
   subscription check can take tens of seconds; with no UI feedback it reads as a freeze
   and gets force-quit. The reporter force-quit; we don't know how long they waited.
4. **Running the x86_64 build under Rosetta on an M2.** Nota ships separate arm64 and
   x86_64 DMGs; PACE under Rosetta is a known trouble spot. Cheap to rule out
   (Activity Monitor → "Kind").

What is **not** the cause: the hardened runtime / library validation (Nota doesn't opt
in), the scanner (out of process, and it never loaded the binary), and the audio thread
(the stack is the main thread).

### How to confirm (needs a Mac with an SSL licence — not available to the team)

- Ask the reporter for the attached sample text: main-thread top frames (wait vs spin),
  CPU use during the freeze, any other thread inside PACE or waiting on the main queue,
  whether a PACE / iLok / SSL window appeared behind Nota, how long they waited, and
  Activity Monitor's "Kind" for Nota (Apple vs Intel).
- With the fix in this spec, the logs and the "couldn't load" device tell us which case
  it was: a load that finishes once it runs off the main thread confirms cause 1; one
  that still never finishes off the main thread points at cause 2 or a licence problem.

## Chosen approach *(summary; the plan decides the mechanics)*

Nota cannot run a hosted plug-in out of process today and this spec does not add that.
Within in-process hosting:

1. **Load the risky part off the main thread, with a deadline.** The binary load (where
   PACE runs — bundle load / library load and its initialisers) happens on a background
   loader thread while the UI thread keeps servicing its run loop. Only the quick part
   that must be on the message thread (creating the instance from the already-loaded
   module) runs there. This alone fixes cause 1 (the main queue / licence UI is serviced
   while PACE waits) and keeps Nota responsive for causes 2–3.
2. **A placeholder device** sits in the chain at once and turns into the real plug-in
   when it is ready; the chain, undo, save and the device's saved state all work while it
   loads.
3. **A timeout with a clear error.** If loading doesn't finish within the deadline, the
   placeholder becomes a "couldn't load" device that says so and why, keeps the saved
   state, and can be retried or removed.
4. **A freeze guard for whatever still blocks the main thread.** Nota notes which plug-in
   it is loading before any step that runs on the main thread; if Nota is force-quit or
   crashes during that step, the next start names the plug-in, and projects open it as a
   "couldn't load" device instead of loading it again.

## Clarifications

### Session 2026-11-23

- Q: When a plug-in is still loading after 30 seconds, should Nota give up automatically or ask? → A: Ask — the card turns "not responding" with **Keep waiting** / **Give up**; loading continues until Give up, and a late success always lands.
- Q: Should a plug-in marked as having frozen Nota be loaded when a project that uses it opens? → A: No — it opens as a "couldn't load" card with **Try again**; inserting it from the browser loads it.
- Q: While one plug-in's binary load is stuck, what happens to the next plug-in loads? → A: They wait in line, their cards saying they're waiting for the stuck plug-in; after **Give up** on a load whose loader never returns, they end as "couldn't load — restart Nota to load plug-ins".
- Q: Is project open in scope, or only interactive inserts? → A: In scope — project open, presets, MCP and duplicate/copy all load through the same placeholder path.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inserting a plug-in that hangs doesn't freeze Nota (Priority: P1)

A producer drags SSL 4K E onto an audio track's device chain. A device card appears at
once in a "loading" state. Nota keeps playing, scrolling and editing. Either the plug-in
finishes loading and the card becomes the real plug-in, or after the timeout the card
says the plug-in couldn't load and why; the producer can retry or remove it. Nota never
has to be force-quit.

**Why this priority**: This is #979 itself — a force-quit loses unsaved work and is the
worst outcome a host can give.

**Independent Test**: On any OS, with a test plug-in whose load blocks for a configurable
time (or forever), insert it from the browser; the UI stays responsive (playback keeps
running, a track can be renamed) and the card ends in "loaded" or "couldn't load".

**Acceptance Scenarios**:

1. **Given** a plug-in whose load takes 5 s, **When** it is inserted on a track,
   **Then** a "loading" card appears immediately, the UI responds throughout, and the
   card becomes the working plug-in when the load finishes.
2. **Given** a plug-in whose load never finishes, **When** it is inserted,
   **Then** after the timeout the card says the plug-in isn't responding and offers
   **Keep waiting** and **Give up**; Give up turns it into a "couldn't load" card naming
   the plug-in, and Nota keeps working throughout.
3. **Given** a "couldn't load" card, **When** the user removes it, **Then** the chain is
   as before the insert and undo / redo behave as for any device.
4. **Given** a plug-in that loads normally in under a second, **When** it is inserted,
   **Then** the result looks the same as today (at most a brief "loading" state).

---

### User Story 2 - Opening a project with such a plug-in doesn't freeze Nota (Priority: P1)

A project saved with SSL plug-ins on several tracks opens. Every track and built-in
device appears; each hosted plug-in appears as a loading card and fills in as it loads;
those that can't load end as "couldn't load" cards that keep their saved settings, so
saving the project again doesn't lose them.

**Why this priority**: The project-open path calls the same synchronous load; without it
a project with one bad plug-in can't be opened at all.

**Independent Test**: Open a project containing the blocking test plug-in plus a normal
plug-in; the project is usable at once, the normal plug-in loads, the blocking one ends
as "couldn't load"; save and reopen — the blocking plug-in's saved state is unchanged.

**Acceptance Scenarios**:

1. **Given** a project with a hosted plug-in that never loads, **When** it is opened,
   **Then** the project is editable without waiting for the plug-in, and the plug-in's
   card ends as "couldn't load".
2. **Given** a "couldn't load" card from a project, **When** the project is saved and
   reopened, **Then** the plug-in's identity, position in the chain and saved state are
   preserved byte for byte.
3. **Given** a project with several hosted plug-ins, **When** it is opened, **Then** they
   load one after another and one that hangs doesn't stop the rest from loading.

---

### User Story 3 - A plug-in that froze Nota isn't loaded again blindly (Priority: P2)

If a load still manages to block Nota and the user force-quits (or Nota crashes while
loading a plug-in), the next start tells them which plug-in it was. Projects that use it
open with a "couldn't load" card for it instead of freezing again. The user can choose to
try it again.

**Why this priority**: The off-main load (stories 1–2) covers the known case; this is the
safety net for a plug-in that blocks in the part that has to stay on the main thread.

**Independent Test**: Simulate a load that blocks on the main thread, kill Nota during it,
restart; Nota names the plug-in, and opening a project that uses it shows a
"couldn't load" card without loading it. "Try again" loads it.

**Acceptance Scenarios**:

1. **Given** Nota was force-quit while loading plug-in X, **When** Nota starts,
   **Then** it shows a one-time notice naming X and saying it will not load X
   automatically.
2. **Given** X is marked as having frozen Nota, **When** a project using X is opened,
   **Then** X appears as a "couldn't load" card with a "try again" action, and is not
   loaded.
3. **Given** X is marked, **When** the user inserts X from the browser or chooses
   "try again", **Then** Nota loads it (the user's explicit choice) and clears the mark
   if it loads.

---

### User Story 4 - Enough information to fix the real cause (Priority: P3)

When a plug-in couldn't load, the log says which plug-in, which format, which load step
it was in (binary load vs instance creation), and how long it ran — enough for a bug
report to tell cause 1 from cause 2 in the Diagnosis above.

**Why this priority**: The root cause can't be confirmed without the reporter's machine;
the next report must carry the answer.

**Independent Test**: Trigger a timeout with the test plug-in; the log line names the
plug-in, format, step and elapsed time.

**Acceptance Scenarios**:

1. **Given** a load that timed out, **When** the user opens the log, **Then** it states
   the plug-in, format, the step that didn't finish and the elapsed time.

### Edge Cases

- **A load that finishes after the timeout.** A "not responding" card keeps loading, so
  the late result lands and the card becomes the working plug-in; if the user gave up,
  removed the card or closed the project meanwhile, the late instance is discarded
  cleanly.
- **A loader thread that never returns.** It can't be killed safely, and a stuck binary
  load may hold the system's library-loading lock, so any later load would block too. While a binary load
  is stuck, further hosted-plug-in loads wait in the queue and end as "couldn't load"
  ("another plug-in is still loading — restart Nota to retry"), and Nota must not touch
  that lock on the main thread (no main-thread load of a binary not already loaded).
- **Quitting while a plug-in is loading.** Quit is not held up by a stuck loader beyond a
  short grace period; the project-save prompt still works.
- **The same plug-in inserted twice while the first is loading.** The second waits for
  the first's binary load rather than starting another.
- **Duplicate track / copy device of a loading or "couldn't load" device.** The copy is
  the same placeholder with the same saved state, not a fresh load of nothing.
- **Parameter automation, MIDI-learn, controller mappings or sidechain routes that target
  a placeholder.** They are kept with the project and take effect once the plug-in loads;
  on a "couldn't load" card they do nothing and are kept.
- **Instrument plug-ins.** An instrument slot holding a loading plug-in is silent until it
  loads; the same states apply.
- **Plug-in editor windows.** Opening the editor of a loading card does nothing (or waits)
  instead of failing.
- **A licence / activation dialog shown by the plug-in during load.** It must be able to
  appear and take input while Nota waits (this is the point of loading off the main
  thread); while it is up, the timeout does not count as a failure the user can't recover
  from — the "couldn't load" card offers "try again".
- **Plug-ins that load fine today.** No behaviour change beyond, at most, a momentary
  loading state; nothing is loaded twice.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Loading a hosted plug-in's binary (the step where copy-protection wrappers
  run) MUST NOT happen on the UI / main thread; the UI MUST keep responding (input,
  redraw, playback control) for the whole load.
- **FR-002**: The step that must run on the message thread (creating the instance from an
  already-loaded binary, and preparing it) MUST be the only part of a load done there.
- **FR-003**: Inserting a hosted plug-in (browser, preset, MCP tool, duplicate / copy) MUST
  put a placeholder device in the chain immediately, at the position the plug-in will
  occupy; the placeholder passes audio through unchanged (effect) or is silent
  (instrument).
- **FR-004**: When the load succeeds the placeholder MUST be replaced in place by the
  plug-in, with any saved state applied, without an extra undo step and without an
  audible glitch on other tracks.
- **FR-005**: A load that hasn't finished within 30 s MUST turn the placeholder to
  "not responding" with **Keep waiting** (restarts the 30 s) and **Give up**; loading
  continues until Give up. Give up, or a load that fails, MUST turn the placeholder into a
  "couldn't load" device that names the plug-in and the reason ("stopped responding while
  loading", "not installed", "failed to load: <format's message>") and offers
  **Try again**; **Remove** is the card's own delete, as for any device.
- **FR-006**: Opening a project MUST NOT wait for hosted plug-ins: tracks and built-in
  devices are usable at once; hosted plug-ins load one after another in the background as
  placeholders, and one that fails or hangs MUST NOT stop the others.
- **FR-007**: A placeholder or "couldn't load" device MUST keep the plug-in's identity,
  chain position, saved state, automation, mappings and routes, and save them back
  unchanged.
- **FR-008**: Before any load step that runs on the main thread, Nota MUST record which
  plug-in it is loading in a way that survives a force-quit or crash, and clear it when
  the step returns.
- **FR-009**: On start, a leftover record MUST mark that plug-in as having frozen Nota,
  show a one-time notice naming it, and from then on projects MUST open it as a
  "couldn't load" device without loading it, until the user chooses **Try again** or
  inserts it explicitly; a successful load clears the mark.
- **FR-010**: Binary loads MUST run one at a time; while one is running, the next loads
  wait in line (their cards say which plug-in they're waiting for) and none starts a
  binary load on the main thread. After **Give up** on a load whose loader never returns,
  the waiting and later loads end as "couldn't load" with the reason "restart Nota to load
  plug-ins".
- **FR-011**: Every load that fails or times out MUST log the plug-in name, format,
  identifier, the step that didn't finish and the elapsed time.
- **FR-012**: The engine's C ABI MUST expose the load as a request plus a result the app
  can observe (no managed call that blocks on a plug-in load), and the existing
  synchronous calls MUST keep working for tests and tools that need them, with the same
  timeout.
- **FR-013**: The behaviour MUST be the same on macOS, Windows and Linux (the binary load
  moves off the main thread on all three), so it can be developed and tested without a Mac.

### Key Entities

- **Plug-in load request**: one plug-in to load into one slot — catalog entry, target
  (track / rack chain, slot), saved state to apply, and its state: queued, loading
  binary, creating instance, loaded, couldn't load (with reason).
- **Placeholder device**: what occupies the slot until the plug-in arrives: plug-in
  identity and name, saved state, the load request's state; audio pass-through / silent.
- **Frozen-plug-in record**: plug-in identifier, when it froze Nota; persisted per user
  next to the plug-in catalog.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a test plug-in that never finishes loading, Nota responds to input
  within 100 ms throughout an insert and a project open, and never needs a force-quit.
- **SC-002**: A device card appears within 100 ms of dropping a plug-in on a chain, for
  any plug-in.
- **SC-003**: A project containing a never-loading plug-in becomes editable as fast as
  the same project without it (within 10%).
- **SC-004**: Saving and reopening a project with a "couldn't load" device preserves the
  plug-in's saved state byte for byte.
- **SC-005**: Plug-ins that load today in under a second still load with no extra user
  step and no visible difference beyond a momentary loading state.
- **SC-006**: After a force-quit during a main-thread load step, the next open of the same
  project does not freeze.

## Assumptions

- The reporter's licence and plug-ins are valid (they work in Ableton Live); the fix is
  about how Nota loads them, not about the licence.
- We can't reproduce #979 here (no SSL / iLok licence, no Mac on the team's dev machine);
  verification uses a purpose-built test plug-in whose load blocks for a configurable
  time, on Windows and in CI, plus a request to the reporter to try a test build.
- Loading a plug-in binary off the main thread is supported by the OS loaders on all
  three platforms; the instance itself is still created and used on the message thread as
  JUCE expects.
- Out-of-process plug-in hosting (sandboxing each plug-in in its own process) is out of
  scope; it would also fix cause 2 but is a separate architecture change.
- Asking the .NET runtime not to install Mach exception ports (cause 2) is not part of
  this spec: it changes how managed null references and crashes are handled process-wide
  and needs confirmation on a Mac first; it's a follow-up if the logs from FR-011 point
  there.
- The 30 s timeout is a default chosen to cover a slow online licence check; it can be
  tuned in the plan.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [ ] Audio-thread impact considered: placeholder swap publishes a new immutable graph snapshot (AR-5); no alloc / lock / I/O on the audio thread (AR-6)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` (async load, placeholder, timeout with the blocking test plug-in)
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned, FEATURES.md / user docs via nota-features / nota-docs
- [ ] UI change → loading / "couldn't load" card follows `DESIGN.md` (Studio)
