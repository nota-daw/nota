# Contract: MCP tools (`Nota.Mcp/Tools/DeviceTools.cs`)

Changed:

- `get_device_sidechain` → adds `sourcePart` (int: -1 whole track, -2 the keyed part is gone) and
  `sourceName` ("Drums · Kick").
- `set_device_sidechain` → optional `sourcePart` (int, default -1 = whole track; the part index from
  `list_sidechain_sources`). Fails with a clear message when no route is free.

New:

- `list_sidechain_sources` → every track that can be a source: `{ trackId, name, frozen,
  routeInUse, parts: [{ index, name, routeInUse }] }`, plus `routesFree` / `routeCapacity`.
- `get_instrument_sidechain` / `set_instrument_sidechain` (Nota Flux React): `trackId`,
  `sourceTrackId`, optional `sourcePart`; React always taps Post.

Descriptions mention the "duck the bass from the kick pad" recipe so an agent finds it.
