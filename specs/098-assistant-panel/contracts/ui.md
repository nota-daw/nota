# Contract: panel, command, settings, replies

## Command

| Id | Title | Category | Default (mac / win+linux) | Aliases |
|---|---|---|---|---|
| `view.assistant` | Show Assistant | View | ⌘⇧H / Ctrl+Shift+H | help manual docs chat ask how |

Also: View menu item "Assistant", toolbar `AssistantToggle` (tooltip with the bound key),
`ToolbarPart.Assistant` (sheds right after `Mixer`, listed after `Mixer` in the ⋯ menu).

## Panel

- Column right of `BodyGrid`, full work-area height; header 32 px: title "Assistant",
  Clear, Close. Width 280–560 (default 360), splitter on the seam, double-click resets.
- Body: conversation (scrolls; new messages scroll into view), input box at the bottom
  (placeholder "Ask how to do something in Nota"), Send button. Return sends; Shift+Return
  new line; Escape in the input stops a running walkthrough, else returns focus to the main view.
- Empty state: a short intro and three example questions (click to ask).
- Index not ready: the first reply waits (spinner in the reply bubble). Index unavailable:
  reply "The manual isn't available in this installation" + link to the online manual.
- Walkthrough bar (inside the panel, above the input): "Step k of N", step text, Back, Next,
  Done.

## Reply (ManualResponder)

```markdown
Here is what the manual says:

### Nota Compressor › Sidechain
Turn on **Sidechain** and pick the source…

[Open in the manual](nota-manual:devices/audio-effects/compressor#sidechain) · [Show me](nota:showme/0)
```

- Up to 3 sections; text cut at ~600 characters on a paragraph or sentence end, "…" added.
- `nota:showme/<n>` → walkthrough n of the message. `nota-manual:` → browser (manual).
- Nothing matches: "Nothing in the manual matches that. Try other words, or [open the manual](nota-manual:)."

## Settings

`AssistantShown` (restored on launch), `AssistantWidth`.

## Strings

`assistant.*` keys in `en.json` (English reference); Russian through nota-translations.
