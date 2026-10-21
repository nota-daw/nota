# Quickstart: verifying typed value entry

## Prerequisites

- Release engine build in `src/native/nota.engine/build` (see BUILD.md; `build/` stays Release).
- A scratch data dir so the run does not touch your library: `export NOTA_DATA_DIR=$(mktemp -d)`.

## 1. Build and smoke test (the PR gate)

```bash
scripts/build.sh
```

Expect green. The parser / solver tests run in the smoke test's own section:

```bash
dotnet run --project tests/Nota.SmokeTest -c Release -- --valueentry
```

Expect every check `ok`: the cases in [contracts/value-entry.md § Parser](contracts/value-entry.md#parser-valuetextread--valuetextsolve)
— units, fractions, pan, ∞, words, decimal comma, clamping, stepped read-outs landing mid-step,
unit switch (ms ↔ s, Hz ↔ kHz), unreadable text → null.

The design checks (`-- design: controls --`, `-- design: menus --`) must stay green: the box
uses tokens only and the parameter menu goes through `MenuKit`.

## 2. In the app — one control of each family

Open a project with a Volt track (amp + filter env, mod matrix), an EQ-8 and a Compressor,
and the mixer.

| Step | Expect |
|---|---|
| Double-click a device knob, type `1.2k`, Enter | read-out shows 1.20 kHz; ⌘Z once restores it |
| Double-click a knob, type `abc`, Enter | nothing changes; history unchanged |
| Double-click, type a value, Esc | unchanged, no history entry |
| ⌥-click a moved knob | back to default; one history entry |
| Right-click a device knob | menu: Type value…, Reset to default |
| Mixer fader: double-click, `-6`, Enter | fader at −6.0 dB |
| Mixer pan: `30L` | 30 % left; `C` centres |
| On a card, open a box, Tab ×N | commits each, walks the card left→right, top→bottom, wraps; Shift+Tab reverses |
| With a box open press Space / letters | typed into the box; transport and note keys do nothing |
| Box closed, click a knob, press Return | transport stops (unchanged) |
| Volt env: double-click the attack node, `12ms` | attack 12 ms; A read-out shows it |
| Volt env: double-click the D/S node, type D, Tab, type S | two history entries, both values set |
| Volt matrix: double-click empty cell, `-35` | route at −35 %; ⌥-click clears; ⌘Z brings it back |
| EQ-8: double-click a band handle; empty space | handle → freq box, Tab → gain → Q; empty space still adds a band |
| Automation armed (write), type a value during playback | one gesture written, like a drag |
| Every colour theme (Studio Dark / Light, Ember Graphite / Paper) | the box looks the same: well fill, rule edge, accent edge, mono |

## 3. Audit

Walk [contracts/control-audit.md](contracts/control-audit.md): every row ☑ (converted /
covered) or exempt with its reason. `/speckit-converge` must report **Converged**.

## 4. Docs

- Preferences → Shortcuts shows: double-click = type a value, ⌥-click = reset, Enter / Tab /
  Esc in the box; the rows that said "double-click resets" (preview volume, clip ADSR handle)
  are updated (`nota-shortcuts`).
- DESIGN.md § Interaction contract: double-click types, ⌥-click resets, right-click opens the
  parameter menu.
- FEATURES.md (`nota-features`), user docs EN + RU (`nota-docs`), CHANGELOG `[Unreleased]`
  ("Changed: double-click on a knob, fader or slider now types a value; ⌥-click resets").
