<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# UI contract

Logic Grey appears in Settings -> Appearance -> Dark and the dark Follow-system choice.
Studio Dark stays the default; every old preset remains named and selectable.
Brush.Playhead is live. Brush.RaisedFill is a computed solid/gradient resource; its palette
key represents SurfaceRaised while NotaThemeService publishes the actual brush to XAML.
No view constructs a gradient or snapshots colours. Custom views use live slots.
Old themes' new slot values preserve previous rendering exactly.
Every T3 readability pairing applies, including text on the lighter raised top.
No new settings, native ABI or shortcuts.
