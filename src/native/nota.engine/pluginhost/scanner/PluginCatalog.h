// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCatalog — thread-safe plugin registry.
// Owns plugins.xml + scanpaths.txt in the per-user data dir. Background scans
// (C# Task.Run -> nota_pluginhost_scan) take the unique lock; UI reads
// (count/desc/index_of_id) take the shared lock — no data races, no iterator
// invalidation. Internally keeps full JUCE PluginDescriptions (uid, path hash,
// category) so persistence is lossless; the JUCE-free bridge snapshot
// (PluginCatalogBridge.h) is derived on read. Zero dependencies on
// Engine/Track/Graph/Device (AR-5/AR-6 safe: all file I/O runs on
// message/background threads, never the audio thread).

#pragma once

#include <cstdint>
#include <memory>
#include <mutex>
#include <shared_mutex>
#include <string>
#include <vector>

#include "bridge/PluginCatalogBridge.h"

namespace juce {
class PluginDescription;
class KnownPluginList;
} // namespace juce

namespace nota {
namespace scanner {

class PluginCatalog {
  public:
    static PluginCatalog& instance();

    PluginCatalog(const PluginCatalog&) = delete;
    PluginCatalog& operator=(const PluginCatalog&) = delete;

    // Read API (shared lock). Snapshots are copied out — safe to hold across
    // a background scan.
    std::vector<catalog::PluginEntry> snapshot() const;
    int32_t count() const;
    bool entryAt(int32_t index, catalog::PluginEntry& out) const;
    int32_t indexOfId(const char* identifier) const;
    std::string descLine(int32_t index) const;
    std::string idAt(int32_t index) const;

    // Scan-path management (scanpaths.txt, one dir per line).
    std::vector<std::string> scanPaths() const;
    void addScanPath(const std::string& dir);
    bool removeScanPath(int32_t index);
    int32_t scanPathCount() const;
    std::string scanPathAt(int32_t index) const;

    // Mutation from the scan coordinator (unique lock).
    void addType(const juce::PluginDescription& desc);
    bool hasFileOrIdentifier(const std::string& fileOrId) const;
    void removeMissingVst3OnDisk();
    std::vector<std::string> unknownIds(const std::vector<std::string>& discovered) const;

    // Full JUCE types for in-process instantiation (message thread). Returned
    // by value so the caller never touches the locked list.
    std::vector<std::unique_ptr<juce::PluginDescription>> types() const;

    // Persistence (message/background threads only — file I/O, never RT).
    void loadOnce();
    void save() const;
    void loadScanPathsOnce();
    void saveScanPaths() const;

  private:
    PluginCatalog();
    ~PluginCatalog();
    struct Impl;
    std::unique_ptr<Impl> impl_;
    mutable std::shared_mutex mutex_;
    bool catalogLoaded_ = false;
    bool scanPathsLoaded_ = false;
};

} // namespace scanner
} // namespace nota
