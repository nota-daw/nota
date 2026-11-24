# Contract: MCP tools (FR-029)

New `Nota.Mcp/Tools/ControllerTools.cs` over an `IControllerAccess` abstraction (implemented in
`Nota.App/Mcp/McpAppServices.cs` over `ControllerService`), next to the existing
`MidiControlTools`. All calls run on the UI thread like the other tools.

| Tool | Args | Returns |
|------|------|---------|
| `list_controllers` | – | `[{ portUid, portName, outputUid, profileId?, profileName?, protocol, enabled, pickup, bankOrigin, knobMode, page }]` |
| `set_controller` | `portUid`, `profileId?` (`"none"`, `"mcu"`, `"hui"`, or an id), `enabled?`, `pickup?` | updated entry |
| `list_controller_profiles` | – | `[{ id, name, origin, protocol, source, errors[] }]` plus file-level errors |
| `reload_controller_profiles` | – | same as `list_controller_profiles` |
| `get_controller_focus` | `portUid?` | `{ track, device: { section, index, rackPath? }, typeKey, page, pageCount, params: [{ knob, name, value }] }` |
| `controller_inject` | `portName`, `bytes` (hex) | number of messages accepted — **only when `NOTA_CONTROLLER_TEST=1`**; registers a virtual port with that name (and a loopback output whose sent bytes `controller_sent` returns) and feeds bytes through the same surface path as a real port |
| `controller_sent` | `portName`, `clear?` | hex of bytes Nota sent to that virtual port since the last clear — test-gated like `controller_inject` |

`controller_inject` / `controller_sent` exist so headless runs (and agents verifying the feature)
can exercise recognition, roles, device-follow, pickup and MCU feedback without hardware. Tools are
discovered from the assembly, so without the env var they are listed but answer with an error.
