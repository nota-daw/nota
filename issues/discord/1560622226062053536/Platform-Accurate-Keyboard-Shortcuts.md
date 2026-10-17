**Platform-Accurate Keyboard Shortcuts and Key Symbols Throughout Nota**

Review all keyboard shortcuts, key labels, and shortcut icons throughout Nota so they accurately reflect the platform, active bindings, and relevant keyboard layout. This applies to Settings → Shortcuts, menus, tooltips, the Command Palette, and other shortcut hints.

**Reported Examples**
- On my MacBook, F11 is listed for entering/leaving full screen, but pressing it does not trigger that action in my setup.
- Page Up and Page Down are listed without explaining how to access them on the built-in keyboard, which has no dedicated keys for them.

These examples should prompt an application-wide review, not just an F11 fix.

**Expected Behavior**
- Use correct platform-specific modifier names and symbols, including Command, Option, Control, and Shift on macOS.
- Show the actual current binding, including user customizations, consistently across the interface.
- Explain compact-keyboard equivalents where relevant: on MacBook keyboards, Page Up/Down can be accessed with Fn+↑/↓, and Home/End with Fn+←/→.
- Do not hide valid commands simply because a keyboard lacks dedicated keys. Provide usable equivalents or allow reassignment.
- Explain when function keys require Fn/Globe, depending on system preferences, and when system shortcuts may intercept them.
- Investigate the F11 full-screen behavior and offer an appropriate platform-native default or alternative where needed.
- Distinguish platform detection from keyboard-layout and hardware detection. Where hardware cannot be identified reliably, provide clear help or a user-selectable keyboard profile rather than guessing.
- Support international layouts and clearly explain bindings that depend on physical key position rather than the printed character.
- Keep icons readable and unambiguous, with accessible text names for unfamiliar symbols.

**Validation**
Test shortcut display and execution across supported operating systems, built-in laptop keyboards, external keyboards, and representative international layouts. Verify that every displayed binding matches the action actually received in its intended context.

The goal is a trustworthy shortcut reference: users should understand exactly what to press on their setup, without misleading symbols or unexplained platform-specific limitations.
