# Contract: UI — loop record mode

- **Transport console**: a `cell` ToggleButton right after the Loop cell, inside the same recess. It shows the label **Overdub** when off and **Replace** when on. It uses the accent colour only when on (Studio: the accent is for what is chosen and non-default). Tooltip: "Loop recording: Overdub adds every pass · Replace keeps the latest pass (⌘⇧R)". Bound to `Transport.LoopRecordReplace`.
- **Command**: `transport.loopRecordMode`, "Loop record: overdub / replace", category Transport, shortcut **⌘⇧R** (Ctrl+Shift+R on Windows/Linux through the existing mapping). It appears in the command palette and in Preferences → Shortcuts (nota-shortcuts skill).
- **Menu**: Transport → "Loop record: overdub / replace" (sentence case per the design-token check), next to Loop, bound to the same command.
- **Persistence**: `Settings.Current.LoopRecordReplace` is written on every change and restored at startup before the engine is used, the same way as `CountInBars`.
- The control is enabled at all times. It does not change appearance with loop on or off, so the choice stays visible.
