# Contract: MCP tools (`Nota.Mcp/Tools/TrackTools.cs`)

| Tool | Parameters | Returns | Description (as registered) |
|------|------------|---------|-----------------------------|
| `set_group_link` | `trackId` (group track id), `on` (bool) | bool — false when the track is not a group | "Link a group's tracks for editing and recording (Link): arming one arms them all, and clips selected or edited in the Arrangement take the clips at the same position on the other tracks along. One undo step." |
| `get_group_link` | `trackId` | bool | "Whether a group's Link is on." |

MCP clip-edit tools keep acting on exactly the clips they name — the link is an Arrangement
interaction rule, so an agent edits each track explicitly (stated in the `set_group_link`
description: "MCP edits are not linked; they act on the clips they name").
