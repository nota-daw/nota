# Bug Fix: Rack path alignment

- **Slug**: issue-1084
- **Fixed**: 2026-11-25
- **Assessment**: ./assessment.md
- **Status**: partial

## Summary

Prepare per-chain and dry delay lines on the message thread and align the rack's parallel and dry paths when compensation is enabled. Update track compensation after relevant rack edits.

## Changes and verification

`RackCore`, `RackDevice`, and `Engine_Rack.cpp` provide the lines and report the active latency. `--issue-1084` passed native impulse tests at 32 and 257 frames for parallel and dry/wet paths.

## Deviations and follow-ups

Delay lines use the engine's existing 65,535-sample compensation limit. Verify dynamic latency, bypass, switching, nested racks, performance and full-app behavior before marking complete.
