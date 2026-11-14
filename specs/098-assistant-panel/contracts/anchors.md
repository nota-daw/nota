# Contract: Show me anchors

## Markup (nota-docs)

```mdx
import At from '@components/At.astro';

## Sidechain

1. <At id="ui:device.compressor.sidechain" /> Turn on **Sidechain** at the top of the card.
2. <At id="ui:device.compressor.sidechain-source" /> Pick the kick's track in **Source**.
```

- One `<At>` per step, at the start of the list item or sentence it belongs to.
- `<At id="none" />` anywhere in a section marks it explanatory (it tells the user nothing to
  do in the app). Not allowed together with other anchors in the same section.
- On the web `<At>` renders a small "Show in Nota" marker with a tooltip; nothing else.
- Translations copy the `<At>` tags unchanged and in the same order; only the words around
  them are translated.

## Anchor ids

| Form | Meaning | Example |
|---|---|---|
| `ui:<area>.<name>` | a control in the registry | `ui:transport.mixer`, `ui:browser.tab.files`, `ui:mixer.strip.sends` |
| `cmd:<command id>` | a command / key action | `cmd:view.mixer`, `cmd:edit.split` |
| `none` | explanatory section | — |

Areas: `transport`, `menu`, `browser`, `arrangement`, `session`, `modular`, `mixer`, `detail`,
`clip`, `pianoroll`, `device.<kind>`, `prefs.<page>`, `palette`, `status`, `assistant`.

## Registry entry (Nota.Presentation.Assistant.AnchorRegistry)

```csharp
A("ui:transport.mixer", locator: "MixerToggle");
A("ui:mixer.strip.sends", locator: "Mixer.SendsRow", reveal: [Panel("mixer")]);
A("ui:device.compressor.sidechain", locator: "Device.compressor.Sidechain",
  reveal: [Panel("detail"), DetailTab("devices")], requires: "device:compressor");
Cmd("view.mixer", "ui:transport.mixer");
```

- `reveal` steps run in order and only change what is visible: switch view, show panel, pick
  a Detail tab or browser tab, open a Preferences page. They never create, delete, select or
  edit anything, never start or stop the transport (FR-018).
- `requires` unmet → the step shows "First: <requirement in words>" and highlights where that
  is done (e.g. the browser's Audio effects tab for a missing device), not the control itself.
- A `cmd:` anchor with a mapped `ui:` id behaves like that id; without one, the step shows
  the user's current binding (`KeyBindings`) and its menu path.

## How-to rule

A section is a how-to when it contains a `<Steps>` block, an ordered list, or a sentence that
starts with an action verb — EN: Click, Double-click, Right-click, Press, Hold, Drag, Drop,
Choose, Select, Open, Turn, Type, Set, Pick; RU: Нажмите, Щёлкните, Дважды щёлкните, Перетащите,
Выберите, Откройте, Включите, Удерживайте, Введите, Задайте. A how-to needs ≥ 1 anchor or
`none`.
