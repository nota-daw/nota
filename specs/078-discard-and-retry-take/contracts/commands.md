# Contract: App command, shortcut, MCP

## Command catalog

| Id | Title (en key) | Group | Default shortcut | Keywords |
|----|----------------|-------|------------------|----------|
| `transport.retryTake` | `commands.discard_and_retry_take` — "Discard and retry take" | Transport | `⌘.` (macOS) / `Ctrl+.` | `undo restart retry discard take again redo record over` |

- Also in the Transport menu, after Record.
- Listed on Preferences → Shortcuts (nota-shortcuts skill).
- Status line after the command: `transport.take_discarded_retrying` "Take discarded — recording
  again" / `transport.no_take_to_discard` "Nothing is recording — no take to discard" /
  the existing record-start failure messages for `Failed`.
- All new strings go to `en.json` (and the other bundled languages via the localization flow).

## MCP

`retry_take` (TransportTools): "Discard the take being recorded and record it again from the
same place (Arrangement) or the next launch quantum (Session slot). Leaves no undo step."
Returns `{ "result": "none" | "arrangement" | "session" | "failed" }`.
