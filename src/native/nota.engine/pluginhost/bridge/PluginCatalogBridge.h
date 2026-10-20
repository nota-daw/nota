// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCatalogBridge — pure C++ (JUCE-free) snapshot view over the plugin
// catalog . The engine and hosting layer read catalog
// entries through this struct; only pluginhost/scanner/ touches JUCE types.
// Lets nota_pluginhost instantiate plugins without depending on Engine/Track.

#pragma once

#include <cstdint>
#include <string>

namespace nota {
namespace catalog {

struct PluginEntry {
    std::string identifier; // JUCE "<format>-<name>-<hash>-<uid>" id string
    std::string name;
    std::string format; // "VST3" | "AudioUnit"
    std::string manufacturer;
    std::string category;
    std::string fileOrIdentifier;
    bool isInstrument = false;
};

// ID fallback: exact match first, then format+name+uid (same plugin at another
// path — see PluginHost.cpp nota_pluginhost_index_of_id).
int32_t indexOfId(const PluginEntry* entries, int32_t count, const char* identifier) noexcept;

} // namespace catalog
} // namespace nota
