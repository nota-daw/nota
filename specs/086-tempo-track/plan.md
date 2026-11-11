# Implementation Plan: Tempo track and time scale

Date: 2026-11-11. Spec: [spec.md](spec.md). Approved issue: #535.

## Summary

Add an immutable tempo map to Graph and existing snapshot undo. Compile ramps
into invertible seconds/beat lookup data off the audio thread. Transport remains
the sole clock. Preserve the constant-BPM fast path. Add a pinned tempo lane to
ArrangementView using its existing scroll, zoom and Studio palette.

## Technical Context

C++20, C#/.NET 10, Avalonia; existing runtime dependencies; Avalonia.Headless for interaction tests (MIT). Project manifest v31
stores points and handles. Native queries cross only the pure C ABI. Windows is
the validation platform. Audio performs bounded lookups, no integration,
allocation, locks or I/O. Snapshot render scopes protect map lifetime.

## Constitution Check

Approved #535 verified. AR-4/5/6/7/9 retained. AGPL headers required. No runtime dependency
or JUCE change; test-only Avalonia.Headless registered. Smoke tests, docs, shortcuts and localization planned. No gate
violations before or after design. No push, merge or PR.

## Project Structure

- engine/TempoMap.h, Transport, Graph, Engine and ABI: compiled timing, edits,
  scheduling and history. Preserve physical placement of unwarped reference audio.
- Application/Engine: tempo values and port; Infrastructure/Interop: native calls.
- Infrastructure/Persistence: optional tempoPoints and migration.
- App/ArrangementView.Tempo.cs: lane, points, handles, marquee and time scale.
- tests/Nota.SmokeTest/TempoMapTests.cs: conversion, render, history, persistence.
- CHANGELOG, FEATURES, shortcuts and nota-docs: user documentation.

## Validation

Native and managed build, targeted tempo checks, then full smoke suite. Test
invalid input, all shapes, round trips, history, save/reload, reference audio,
multiple mapped sections, seek, loop, and mid-beat metronome start. Record actual
UI verification limits explicitly.
