# Data model: Device cards XL panel

## CardSize (per device, editor state)

| Value | `View` param | Card in the chain | Panel |
|-------|--------------|-------------------|-------|
| L     | 0 (default)  | full L layout     | —     |
| S     | 1            | 260 mini layout   | —     |
| XL    | 2            | 260 × 260 placeholder ("open in XL") | the device's L body, stretched |

- Stored in the device's `View` param, so it saves with the project and is skipped by
  presets, A/B and the automation list.
- XL is valid only for devices whose body has `SupportsXl` and whose engine `paramMax(View)`
  is 2 (EQ-8, Dynamic EQ-8). Elsewhere `View` stays 0..1.
- Reading: `Size = round(View)`. `IsMini ⇔ Size == S`.

## Invariant: at most one XL device per track

- Setting XL on device *i* sets every other device on that track with Size XL to L.
- On rebuild with more than one XL: the device whose `CardExtra.XlOwner` is set keeps XL,
  or the first in the chain if none is set. The rest are set to L.

## Transitions

```text
L ──XL──▶ XL     S ──XL──▶ XL
XL ──S──▶ S      XL ──L──▶ L
XL ──close / Esc──▶ L
XL(device A) ──XL on device B, same track──▶ A: L, B: XL
XL ──select another track──▶ XL (panel hidden; shown again on return)
XL ──device removed──▶ (panel gone)
```

## View state (DeviceChainView, not persisted)

- `_xlIndex`: the device index shown in the panel for the current track, or -1.
- `PreferredHeight`: `2·ChainInset + CardH`, plus `XlHeight (460) + ChainInset` while `_xlIndex ≥ 0`.
