**Section Renaming: Cancel Edits and Restore Automatic Names**

Double-clicking a section bar to rename it works well. However, there should also be a clear way to discard an unwanted edit or restore the section’s automatic name.

**Cancel the current edit**
- Escape should cancel an uncommitted rename and restore the previously saved name.
- Enter should confirm the new name.
- If the previous name was custom, Escape should preserve it rather than unexpectedly replace it with a default.

**Restore the default name**
- Add a clearly accessible “Reset to Default” command, ideally with a small button in the rename interface and a context-menu entry.
- Make this command keyboard-accessible and assignable in the shortcut settings.
- Resetting should return the section to automatic naming mode, not merely insert a fixed text label.

**Accurate automatic numbering**
- Use “Section 1”, “Section 2”, “Section 3”, etc., based on each section’s current position in the arrangement, from left to right.
- Count all sections, including those with custom names. For example, if the second section is named “Chorus”, the third section’s default remains “Section 3”.
- Update automatically named sections after inserting, deleting, duplicating or reordering sections. Preserve custom names.
- Base numbering on the complete arrangement, regardless of zoom, visibility or selection.
- Keep internal section identities and references stable when displayed names or positions change.

**Safety and consistency**
- Undo/redo should restore both the name and its automatic/custom naming state.
- Clearly distinguish automatic names from custom names in the rename interface without cluttering the arrangement.
- Handle empty or whitespace-only names predictably, preferably by returning to automatic naming when confirmed.

The goal is fast renaming with a reliable escape route and a one-step return to a correctly numbered default name.
