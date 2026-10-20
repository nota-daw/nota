# Quickstart: validating multi-clip editing

## Automated

```bash
dotnet run --project tests/Nota.SmokeTest -- --multiclip
```

Expect all `multiclip:` checks green: owner reassignment across a seam, a gap and a
reference clip as walls, Split/Merge round-trip, per-clip overwrite, paste routing, and
one grouped undo restoring two clips.

```bash
dotnet run --project tests/Nota.SmokeTest -- --notes
```

Spec 007 checks must stay green unchanged (single-clip parity, R3).

```bash
scripts/build.sh
```

The PR gate (native + managed build and the full smoke test).

## Manual / headless scenarios

Setup: one MIDI track with clips A (bars 1–4) and B (bars 5–8) holding a bass line, and a
second track with a chord clip C (bars 1–8). Use `NOTA_DATA_DIR` to isolate data.

1. Select A, B, C; double-click B → three clips open, strip shows three bars, B active,
   ruler reads arrangement bars 1–8. (Story 1)
2. Click one clip only in the arrangement → editor shows that clip alone; ⇧-click a second
   → both at once. (FR-001a)
3. Rubber-band notes over A and B, press ↑ → both clips change; ⌘Z once → both restored.
   (Story 2, FR-011)
4. Drag a note of A so it starts in bar 5 → it lands in B; ⌘Z → back in A. (FR-006)
5. Lock C in the strip; Select All → no chord notes selected; chords still play. (Story 3)
6. Click a note of C after unlocking → C active; inspector shows C's start/length;
   Envelopes tab edits C. (Story 4)
7. Clip tools → Arpeggiate with notes selected in A and B → each clip gets its own
   result; Apply → one undo step. (R6)
8. Delete clip B in the arrangement → B drops out of the editor; A stays active-able.
   (FR-013)
9. Open a single clip → no strip, behaviour as before. (FR-002)
