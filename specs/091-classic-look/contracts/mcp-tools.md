# Contract: MCP theme tools, Look

Extends [087 MCP tools](../../087-user-skins/contracts/mcp-tools.md). All on the UI thread.

| Tool | Change |
|---|---|
| `list_themes` | `choice` gains `look`: `"new"` or `"classic"`. |
| `set_look` *(new)* | Input `look`: `"new"` \| `"classic"`. Wears it live, stores it as Preferences would, refreshes Preferences if open. Returns `{ look, worn }` (`worn` = theme id). Unknown value → error naming the allowed values; nothing changes. |
| `get_theme` | Optional `look`. With `"classic"` a built-in id returns its classic palette (format 1, same id); a user theme is returned unchanged. Default: the original theme file (unchanged behaviour). |
| `preview_theme` | Optional `look` (default: the worn look). Images render the theme under that look — colours (built-ins), radii and both typefaces; the live window is untouched. |
| `try_on_theme` | Unchanged; the try-on is worn under the current look. |
| `validate_theme`, `save_theme` | Accept format 3 (theme-file-v3). |
| `get_theme_catalogue` | Unchanged. |
