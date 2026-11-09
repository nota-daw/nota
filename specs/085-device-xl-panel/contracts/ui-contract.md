# UI contract: Device cards XL panel

## Engine param contract (existing C ABI, no new entry points)

- `nota_device_param_max(track, device, View)` returns **2** for Nota EQ-8 and Nota
  Dynamic EQ-8, and stays 1 for every other device with a `View` param.
- `nota_device_set_param(..., View, 2)` stores 2 for those two devices. Get returns 2.
- Presets neither save nor apply `View` (unchanged). The smoke test checks both points
  above.

## Managed contracts

### `Nota.Application.DeviceView`

```text
enum CardSize { L = 0, S = 1, XL = 2 }
CardSize Size(engine, track, device)          // round(View); L when the device has no View
void     SetSize(engine, track, device, size) // writes View; XL only if param max ≥ 2
bool     SupportsXlParam(engine, track, device)  // param max ≥ 2
bool     IsMini(...) ⇔ Size == S              // unchanged callers keep working
```

### `IDeviceBody`

```text
bool SupportsXl => false   // EqDeviceBody, DynamicEqDeviceBody: true
```

### `DeviceCardContext`

```text
bool CanXl                 // true only for DeviceChainView's top-level chain
```

### `CardLayout.SizeToggle(ctx, device, xl)`

- `xl && ctx.CanXl && DeviceView.SupportsXlParam` → `S / L / XL`; otherwise `S / L`.
- Picking XL: every other XL device on the track → L, this one → XL, rebuild.

### `DeviceChainView`

```text
double PreferredHeight { get; }   // the chain view's content height, without the Detail toolbar
event Action? PreferredHeightChanged
```

`MainWindow.ShowDevices` sizes the docked Detail panel to `1 + 32 + PreferredHeight` and
re-applies it on `PreferredHeightChanged` while Devices is the shown tab.

## Visual contract

- The panel is a device card shell: radius, header, preset picker, type badge, the size
  toggle with XL picked, a Shrink key (not × — the header's × removes the device), and power. Its body is the device's L body
  stretched.
- The chain card in XL is 260 × 260, with a compact header without the preset picker and the toggle with XL picked.
  Its body shows a centred caption: "Open in XL above".
- Both follow the four themes through the existing tokens. No new colours.
