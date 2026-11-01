<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Research

## Existing theme path
Decision: complete version-1 dark preset plus BuiltInOrder.
Rationale: Preferences enumerates presets; Appearance.Store/Resolve and settings already
persist the choice and support Follow system. Shared brushes and repaint service cover custom views.
Alternative rejected: a duplicate two-option selector would lose existing choices.

## Static depth and playhead
Decision: Playhead and SurfaceRaisedTop slots with compatible original-theme defaults.
One shared VGradient, exposed as computed RaisedFill; equal stops choose the original
SurfaceRaised brush. KeyMap's RaisedFill is its representative solid colour; the theme
service publishes the computed XAML brush. Generator mirrors it.
Alternative rejected: per-view gradients, literal colours or theme-ID branches.

## Contrast and data preservation
The speckit-plan research agent checked actual T3 pairings. On hover #535358, supplied
secondary #ACACB1 is only 3.38:1. Use secondary #CBCBD0 (4.73), muted #C8C8CD (4.59),
tertiary #A3A3A9 (3.05), disabled/axis/faint #96969C (2.60). Primary #E6E6E8 is 6.14.
Raised top #505054 remains below hover and passes these pairings.
Accent #E6A14B retains required state separation (DeltaE 15.51 against existing Mute);
playhead #F1C45D is 8.02:1 on background. Preserve all Studio Dark musical/status hues.
Do not lower contrast checks; add the raised top to checked surfaces.

## Validation/documentation
Use existing smoke checks plus scratch-settings real App/window diagnostics and captures.
No screenshot/timing thresholds in CI. No new dependency.
Sibling nota-docs is absent; supply an in-repo manual update handoff.
