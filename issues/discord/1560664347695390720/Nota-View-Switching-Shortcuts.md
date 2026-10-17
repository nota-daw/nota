**Keyboard Shortcuts for Arrangement, Session and Modular Views**

Please add direct keyboard commands for switching between Arrangement, Session and Modular, so these views can be reached without clicking their tabs.

**Requested commands**
- Show Arrangement.
- Show Session.
- Show Modular.
- Optionally, Next View and Previous View for cycling between them.

**Shortcut assignment**
- List all commands in Settings > Shortcuts and the Command Palette.
- Provide simple, memorable, configurable shortcuts with conflict detection.
- Cmd+1/2/3 would be easy to remember, but these combinations are already shown for grid commands in my current setup. Please preserve existing assignments rather than silently replacing them.
- Choose available platform-appropriate defaults after checking existing bindings and OS conflicts. Users should still be able to assign their preferred combinations explicitly.
- Show each assigned shortcut in the corresponding view tab’s tooltip and relevant menus.

**Predictable behavior**
- Switch directly to the requested view; activating its shortcut again should not unexpectedly close it or cycle elsewhere.
- Preserve each view’s selection, zoom, scroll position and editing context when switching away and back.
- Keep playback and recording uninterrupted.
- Move keyboard focus into the selected view predictably, with a visible focus indicator, so keyboard navigation can continue immediately.
- Respect active text editing and modal dialogs; do not discard unfinished edits or unexpectedly switch views while typing.
- Make the underlying view-switching actions available for MIDI assignment as well.

The goal is fast, consistent navigation between Nota’s three main working views, without sacrificing existing shortcuts or the state of each view.
