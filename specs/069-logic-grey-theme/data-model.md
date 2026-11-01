<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Data model

Preset: formatVersion 1, id logic-grey, name Logic Grey, kind dark, complete semantic colors.
New slots: Playhead (old Accent); SurfaceRaisedTop (old SurfaceRaised).
Derived RaisedFill: original solid brush if stops equal; otherwise shared static gradient.
Existing preference transitions: pick -> Store/Save -> Set/repaint; process restart -> Resolve.
Follow system remembers the dark choice. No migration/new setting or project field.
Track indices, notes and automation are independent of theme state.
