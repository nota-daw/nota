// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Small helpers shared by the non-Apple config layer (AudioConfig_miniaudio.cpp and
// MidiConfig_rtmidi.cpp, on Windows + Linux): the per-user data directory and a minimal
// JSON read/write for the tiny audio.json / midi.json config files. These files are
// engine-internal (the managed layer reads them only through C ABI getters), so a
// hand-rolled parser is sufficient — but string values (WASAPI endpoint ids can contain
// backslashes/braces) are properly escaped. The directory mirrors NotaPaths.DataDir on
// the managed side: %APPDATA%\Nota on Windows, $XDG_CONFIG_HOME/Nota (or ~/.config/Nota)
// on Linux.

#pragma once

#include <string>
#include <vector>

namespace nota::cfgfile {

// Per-user config dir, created on first use. Empty string if it can't be resolved.
std::string dataDir();

std::string readFile(const std::string& path);
bool        writeFileAtomic(const std::string& path, const std::string& content);

std::string jsonEscape(const std::string& s);

// Extract "key":"value" (JSON-unescaped). False if the key is absent.
bool jsonGetString(const std::string& json, const std::string& key, std::string& out);
// Extract "key": <number>. False if the key is absent / unparseable.
bool jsonGetNumber(const std::string& json, const std::string& key, double& out);
// Extract "key":["a","b",...]; appends unescaped elements. False if key absent.
bool jsonGetStringArray(const std::string& json, const std::string& key,
                        std::vector<std::string>& out);

} // namespace nota::cfgfile
