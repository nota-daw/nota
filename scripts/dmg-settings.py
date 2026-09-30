# SPDX-License-Identifier: AGPL-3.0-only
#
# dmgbuild settings for the Nota installer window (used by scripts/package-dmg.sh).
# Values come in via `dmgbuild -D key=value`:
#   app        path to the built Nota.app
#   background HiDPI .tiff built from assets/macos/dmg-background{,@2x}.png
#   icon       volume icon (.icns)
#
# Geometry matches the background art (660x400 pt): the dashed arrow sits at
# y=190 between the two icon slots, all inside the rounded card.

import os.path

app = defines["app"]  # noqa: F821 — injected by dmgbuild
app_name = os.path.basename(app)

format = "UDZO"
filesystem = "HFS+"
files = [app]
symlinks = {"Applications": "/Applications"}
icon = defines.get("icon")  # noqa: F821

background = defines["background"]  # noqa: F821
window_rect = ((200, 120), (660, 400))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False

icon_size = 128
text_size = 13
arrange_by = None
icon_locations = {
    app_name: (165, 190),
    "Applications": (495, 190),
}
