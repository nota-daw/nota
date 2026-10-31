# Data Model: Plug-in card preview

## EditorPreview (engine, on a device)

| Field | Type | Notes |
|---|---|---|
| `jpeg` | `shared_ptr<const vector<uint8_t>>` | JPEG bytes; null / empty = no preview. Immutable once made, so clones share it |
| `serial` | `uint32_t` | Unique per preview **made** (a process-wide counter, starting at 1); 0 = none. A clone keeps the source's serial (same image) |

Lives on `Device` (accessors, like `folded()`) and on `Instrument` (a public field, like
`folded`). Message thread only.

**Lifecycle**

```text
none ──(editor hidden, capture ok and not blank)──▶ preview(serial n)
preview(n) ──(editor hidden, capture ok)──▶ preview(n+k)
preview(n) ──(capture failed / blank)──▶ preview(n)            (FR-010)
any ──(ABI set, size 0)──▶ none
any ──(ABI set, bytes — project load)──▶ preview(new serial)
device cloned (copy/paste/duplicate/duplicate track) ──▶ clone has the same preview
device replaced (new plug-in in the slot) ──▶ new device: none
```

Validation: `set` accepts at most 4 MB and stores the bytes as given (the app only ever
passes back what `get` returned). The engine never decodes them.

## Project document (managed)

`DeviceDto.Preview : string?` and `InstrumentDto.Preview : string?`: the bundle-relative
path of a state blob holding the JPEG bytes, written only when a preview exists. See
[contracts/project-format.md](contracts/project-format.md).

## Card view (managed, not persisted)

`PluginPreviewBackdrop`: `Bitmap? Image`, which draws nothing when null. The card
remembers the serial it last showed and swaps the image when
`PluginPreviewSerial(track, di)` returns a different value. Decoded bitmaps are cached
by serial (last 32).
