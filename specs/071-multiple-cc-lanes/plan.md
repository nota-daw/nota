<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Plan: multiple MIDI CC lanes and saved heights

Date: 2026-11-02. Approved issue: #422. Spec: spec.md.
C# / .NET 10, existing Avalonia and settings services; no new dependencies.
Audio-thread impact: none. No native, DSP, ABI, routing or file-format changes.

## Decisions and implementation

Give ControllerLane an optional per-instance controller identity; preserve its
fallback for the primary lane. Parameterize the existing picker by a callback.
Reuse StoreController and the shared cache, cancelling pending gestures/capture on
reload, change or removal. Invalidate all lanes on data/view changes. Register each
extra gutter with the existing key-column registry and remove it with the lane.
Use a native plus toolbar, per-lane picker/minus and bounded vertical scroll area.

Wrap the primary and extra lanes in captured upper dividers. Store stable logical
height keys for the primary mode and extra slot/controller; clamp finite heights to
48–480 and use original defaults otherwise. Save on release, not on every move.
Fit lanes to the available height while retaining note editing. Inject the existing
settings service into both Arrangement and Session editor construction paths.

Update CHANGELOG [Unreleased], FEATURES, French strings and the divider row in
Preferences > Shortcuts. The external manual repo is absent; include paired EN/RU
handoff. Preserve native/theme/identity, workspace, zoom and velocity source behavior.

## Validation and evidence

A real Windows Avalonia probe exercises add/hide/reload/cache/commits and pointer
resizing, independent saved heights, old settings and short-window overflow. The
smoke test covers normalization and preference serialization. Run build-win.ps1.
Capture the same controller data, size, scale and Studio Dark theme at the official
base and on this branch. Put images on the fork's existing pr-assets branch; exclude
images, logs, benchmark output and harness binaries from the contribution.

## Constitution check

Approved open issue #422 verified via GitHub API before this branch's changes.
Existing audio and pure-C boundaries untouched; AGPL headers on new files; no new
dependency. One bounded logical change; fresh build/test and convergence required.
No extension hooks are configured. Docs site absence is disclosed in the PR.
