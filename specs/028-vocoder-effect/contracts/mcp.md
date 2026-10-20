# Contract: Nota Vocoder over MCP

No new MCP tools. The vocoder is reachable through the existing generic ones.

| Task | Tool | Notes |
|---|---|---|
| Find it | effect list (from `BuiltinDeviceCatalog`) | Entry "Nota Vocoder", kind 26, role / aliases: vocoder, robot voice, talk box, choir. |
| Add it | the existing add-built-in-device tool with kind 26 | Same as any audio effect. |
| Read / set params | the existing device param tools | Names and ranges per [device-params.md](device-params.md). |
| Use another track as the carrier | set param **Carrier** to 1.0 (Track), then `set_device_sidechain(trackId, deviceIndex, sourceTrackId, tapPre?, gainDb?)` | The vocoder's own track is the voice. `gainDb` trims the carrier track. |
| Watch it | `get_device_sidechain` | No vocoder-specific reader in v1. |

Only change: the descriptions of `get_device_sidechain` / `set_device_sidechain` list
"Nota Vocoder (kind 26) — its carrier track" among the devices that take a key, so an agent
knows to route one.
