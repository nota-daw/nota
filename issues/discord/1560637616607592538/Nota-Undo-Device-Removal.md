**Cmd+Z Does Not Restore a Removed Device**

After removing a device in Nota, pressing Cmd+Z does not restore it in my current experience on macOS.

**Observed sequence**
1. Add or use a device in a track’s device chain.
2. Remove the device.
3. Press Cmd+Z.
4. The removed device does not return.

I have not yet established whether this affects every device type or whether keyboard focus contributes to the problem.

**Expected behavior**
Undoing device removal should restore:
- The correct device in its original chain position.
- Its parameter values, preset and available saved internal state.
- Bypass/enabled state.
- Relevant routing, sidechains and connections.
- Associated automation and mappings without losing their relationship to the restored device.

It should restore the previous configuration rather than create a fresh instance with default settings.

**Additional checks**
- Compare Cmd+Z with Edit → Undo to distinguish shortcut/focus problems from an undo-history problem.
- Test built-in devices, third-party plugins and devices inside racks.
- Treat removal and restoration as a coherent undo/redo operation.
- Ensure redo removes the restored device correctly.
- Preserve references and offer a clear recoverable state if the plugin cannot be reloaded.
- Keep audio behavior safe during restoration, avoiding crashes, unexpected level spikes or unrelated project changes.

Device removal should be a reliably reversible edit. Losing a configured device because Undo does not restore it can undo substantial sound-design work.
