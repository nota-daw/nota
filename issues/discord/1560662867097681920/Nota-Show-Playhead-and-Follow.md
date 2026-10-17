**Show Playhead and Optional Playback Following**

When navigating the sequencer while playback continues, the playhead can end up outside the visible area. Finding it again by manually scrolling interrupts the workflow.

Please provide a clearly discoverable way to return the view to the current playhead position instantly. If this functionality already exists, make it easier to find and access consistently.

**1. One-shot “Show Playhead” command**
- Bring the current playhead into view with one keyboard shortcut, without changing the playback position.
- Provide a compact toolbar button, a menu entry and Command Palette access.
- Make the shortcut configurable, show it in the tooltip and use platform-appropriate key labels.
- Preserve horizontal zoom, vertical track position, selection and edit state.
- Avoid unnecessary scrolling if the playhead is already clearly visible.
- Position the playhead with useful context and some upcoming material visible, rather than at the extreme right edge.
- Work during playback, recording and while stopped. When stopped, reveal the current transport position.
- Do not restart playback, move clips, change the loop region or seek the audio transport.

**2. Optional “Follow Playhead” mode**
- Offer a separate toggle that keeps the playhead visible as playback advances.
- Keep one-shot navigation and continuous following independent: “Show Playhead” should not silently enable Follow.
- Support page-style scrolling as a straightforward default; optionally offer smooth continuous scrolling.
- Clearly indicate whether Follow is enabled, disabled or temporarily suspended.

**3. Respect manual navigation and editing**
- Allow users to inspect or edit another part of the arrangement without the view immediately snapping back.
- Temporarily suspend following when users deliberately navigate away, with a clear “Resume Follow” action.
- Never recenter the view in the middle of a clip drag, trim operation, marquee selection or text edit.
- Keep trackpad gestures, horizontal scrolling and zooming predictable.
- Make any automatic resumption behavior explicit and configurable rather than surprising.

**4. Off-screen position feedback**
- When the playhead is outside the viewport, show a subtle left/right indicator near the timeline or Show Playhead button.
- Clicking the indicator should reveal the playhead.
- Keep the interface uncluttered when the playhead is already visible.

**5. Reliability and accessibility**
- Handle loop wraparound, transport jumps and changing playback direction correctly where supported.
- Use the relevant timeline’s playhead if multiple editors are open; do not jump to an unrelated editor or change the selected clip.
- Keep the command accessible when ordinary editor panels have focus, while respecting text input and modal dialogs.
- Avoid distracting scroll animations; respect reduced-motion preferences.
- Keep this strictly a view operation, with no added audio latency, playback interruption or musical undo-history entry.

**Expected workflow**
Browse or edit elsewhere → press Show Playhead → immediately see the current playback position and continue working.

The essential feature is a reliable one-shortcut return to the playhead. Continuous following and off-screen indicators should complement it without adding friction.
