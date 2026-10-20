// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCatalog — thread-safe plugin registry.
// Owns plugins.xml + scanpaths.txt in the per-user data dir. Background scans
// (C# Task.Run -> nota_pluginhost_scan) take the unique lock; UI reads
// (count/desc/index_of_id) take the shared lock — no data races, no iterator
// invalidation. Internally keeps full JUCE PluginDescriptions (uid, path hash,
// category) so persistence is lossless, plus an indexed copy rebuilt on every
// mutation so a read never copies the whole list. Zero dependencies on
// Engine/Track/Graph/Device (AR-5/AR-6 safe: all file I/O runs on
// message/background threads, never the audio thread).

#pragma once

#include <cstdint>
#include <memory>
#include <mutex>
#include <shared_mutex>
#include <string>
#include <vector>

namespace juce {
class AudioPluginFormatManager;
class PluginDescription;
} // namespace juce

namespace nota {
namespace scanner {

// The process's one plugin format manager (VST3 everywhere, AU on macOS), shared by
// scanning and in-process hosting. Created on first use; thread-safe to obtain.
juce::AudioPluginFormatManager& sharedFormatManager();

class PluginCatalog {
  public:
    static PluginCatalog& instance();

    PluginCatalog(const PluginCatalog&) = delete;
    PluginCatalog& operator=(const PluginCatalog&) = delete;

    // Read API (shared lock). Results are copied out — safe to hold across
    // a background scan.
    int32_t count() const;
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
    void removeMissingVst3OnDisk();
    std::vector<std::string> unknownIds(const std::vector<std::string>& discovered) const;

    // Full JUCE description of entry `index` for in-process instantiation, copied
    // under one lock so the index and the entry can't drift apart mid-scan. False
    // when `index` is out of range.
    bool typeAt(int32_t index, juce::PluginDescription& out) const;

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
