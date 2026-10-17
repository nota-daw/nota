**Add a Clearly Visible Remove Button to Device Headers**

I cannot find an X or another clearly visible button for removing a device directly from its device card in Nota.

**Suggested behavior**
- Add a compact Remove Device button to each applicable device header.
- Use a clear icon with a tooltip stating “Remove device” so it is not confused with closing the plugin window or hiding its interface.
- Make it discoverable with a mouse, trackpad and keyboard; do not rely exclusively on a context menu.
- Keep it visually separate from bypass, enable/disable and window-close controls.
- Provide a suitable hit target without making accidental removal likely.
- Keep existing removal methods available.

**Safety and consistency**
Removing a device should be fully undoable. Undo must restore the same instance’s position, settings and relevant connections, rather than inserting a fresh device with default values.

Apply the same clear interaction consistently to built-in devices and supported third-party plugins, including devices inside racks where applicable.

The goal is a simple, discoverable way to remove a device directly from the chain without unnecessary clicks.
