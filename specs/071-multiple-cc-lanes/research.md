<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Research

- Reuse ControllerLane and StoreController: they already implement switch blocks,
  continuous points and the clip's undo boundary. Duplicate editing code would drift.
- Use optional per-instance controller plus callback picker; primary behavior stays
  unchanged. Generation checks prevent stale picker/context actions after reload.
- Capture lane divider pointers independently from note/CC edit gestures. Persist
  only on release; layout changes must never commit MIDI.
- Application preferences are the current persistence mechanism. Key an extra
  height by its lane slot and controller so duplicate controller views stay independent.
- Reuse existing CPU-safe presentation behavior. No playhead, engine or theme changes
  are needed. Workspace, deeper zoom and portable app naming are separate local work.
