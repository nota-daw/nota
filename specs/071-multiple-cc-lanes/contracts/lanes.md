<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# UI and editing contract

+ appends a controller view. Each picker changes only its lane. Minus hides that view
and preserves all MIDI. Clearing is the existing explicit controller context action.
Every lane shares BeatToX/XToBeat, active clip selection and cache reloads.
StoreController(controller, points) remains the sole existing commit boundary.

Upper dividers resize only their own primary/extra row. Up grows, down shrinks;
release saves heights. Resizing never changes notes, CC values, selection or undo.
Overflow scrolls while notes and the primary lane remain usable. The Shortcuts pane
must describe the divider. All controls retain the app's themes and localization.
