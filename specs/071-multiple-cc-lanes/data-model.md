<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Data model

ExtraControllerLane owns a ControllerLane, Grid row, divider container and reusable
slot index. Controller points remain in the active clip and existing shared cache.
Removing the row releases its gesture/gutter references; it never edits the model.

Settings.PianoRollLaneHeights maps mode/controller keys to logical pixel heights.
Defaults remain 72/112; finite heights clamp to 48–480. Missing or malformed values
fall back safely. Extra keys contain slot plus controller, allowing duplicates to
retain independent sizes. Save after a completed divider gesture. The visible extra
lane list is editor-local and is not persisted by this contribution.
