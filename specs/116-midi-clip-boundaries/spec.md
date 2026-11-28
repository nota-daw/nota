# Feature Specification: MIDI clip boundaries in the Piano Roll

**Issue**: [#1121](https://github.com/nota-daw/nota/issues/1121)
**Status**: Draft

## User Scenarios & Testing

### US1: Resize a clip from its ruler (P1)

Drag the start or end handle of the active Arrangement MIDI clip. Its duration
updates during the drag. With snap enabled the boundary follows the displayed
grid, with a temporary bypass. On release, the Arrangement and playback use the
same bounds. Escape cancels. One undo or redo restores the whole edit.

### US2: Expand a clip while editing notes (P1)

Enable Auto Expand MIDI Clip, then create, move, resize, duplicate, or paste a
note across either boundary. The clip grows to contain the complete affected
note. Existing notes keep their absolute positions unless they were the notes
being moved. One undo restores both clip bounds and note content. With the
setting off, existing edit limits apply.

## Requirements

- Show dedicated handles for the active clip and keep the grid visible beyond it.
- Respect timeline zero and the minimum valid clip length.
- Rebase clip-relative note, expression, controller, and envelope data as needed
  to preserve absolute timing when the clip start changes.
- Preserve hidden content on trim where the model supports it. Otherwise stop
  the trim before it would discard content, including controller or envelope data.
- Keep unrelated clips unchanged, including linked and neighboring clips.
- Make expansion optional and off by default. Cancelled gestures must not change
  the clip or create undo history.
- Keep clip-level and note-level gestures distinguishable. Apply Nota's existing
  design and shortcut conventions.

## Edge Cases

- At timeline zero, expansion cannot extend left.
- A multi-clip edit must not enlarge an unrelated clip. Existing clip routing
  continues to apply when Auto Expand is off.
- A trim cannot change the source content of other clip instances.
- Undo after a left expansion must restore both bounds and local timestamps.

## Success Criteria

- Both ruler handles can resize a valid Arrangement MIDI clip without leaving
  the Piano Roll, and each accepted gesture takes one undo step.
- Each of the five note edits that can trigger expansion contains the full
  affected note afterward, and one undo reverses the entire gesture.
- Existing note and controller positions match their pre-edit absolute positions
  when a left edge changes without moving those events.

## Assumptions

- Initial scope is Arrangement MIDI clips. Session slot length and recording
  behavior are separate workflows.
- Preserve existing loop semantics. If a specific clip cannot be resized
  safely, make its boundary handles unavailable.
- Draft PR #1124 is parallel work. This branch starts from develop and uses
  its existing editor behavior.
