// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Small helpers shared by the config layer on all platforms (AudioConfig and
// MidiConfig persistence): the per-user data directory plus atomic file read/write.
// JSON itself is handled by nlohmann/json (fetched via CPM, see cmake/dependencies.cmake)
// in ConfigJson.cpp — this header stays JSON-free. The engine never infers platform
// paths: the directory is set once at startup from the managed side
// (nota_set_data_dir <- NotaPaths.DataDir); tests use NOTA_DATA_DIR instead.
// Unset (empty) means persistence is off. The plugin host's catalog lives here too.

#pragma once

#include <string>

namespace nota::cfgfile {

// Explicit override set once at startup via nota_set_data_dir (C ABI), fed from
// NotaPaths.DataDir on the managed side. Empty = unset (persistence off).
// Thread-safe; never called on the audio thread (config IO is message-thread).
void setDataDirOverride(const std::string& dir);
std::string dataDirOverride();

// Per-user config dir, created on first use. Empty when unset (no override and
// no NOTA_DATA_DIR): loads return defaults, saves are skipped.
std::string dataDir();

std::string readFile(const std::string& path);
bool writeFileAtomic(const std::string& path, const std::string& content);

} // namespace nota::cfgfile
