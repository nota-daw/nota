// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCatalog — thread-safe plugin registry.
// Owns plugins.xml + scanpaths.txt + scan-failed.txt in the per-user data dir. Background scans
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
    // User-removed scan paths (scanpaths-removed.txt, one dir per line).
    // Backfill skips them; an explicit add or a restore clears them.
    bool isScanPathRemoved(const std::string& dir) const;

    // Default scan folders for a plugin format ("VST3" today; "CLAP"/"LV2"
    // add entries to kFormatsWithDefaults in the .cpp — no other code
    // changes). Read live from the format's own standard locations, so the
    // listing can never drift from what the scan searches. Empty for
    // unknown formats. No persistence here — backfill and restore decide.
    static std::vector<std::string> defaultScanPathsFor(const char* format);
    // Appends the missing OS defaults for `format`, keeps existing entries
    // (defaults and user-added) untouched and ordered. Returns how many
    // were added; 0 for unknown formats or when complete.
    int32_t restoreDefaultScanPaths(const char* format);

    // Mutation from the scan coordinator (unique lock).
    void addType(const juce::PluginDescription& desc);
    void removeMissingVst3OnDisk();
    // Discovered ids not in the catalog and not failed before (an id that failed is
    // skipped until its file changes — see markFailed).
    std::vector<std::string> unknownIds(const std::vector<std::string>& discovered) const;

    // Probes that found nothing usable (no answer, timeout, zero types), keyed by id
    // with the file's modification time: a plugin that hung once costs its timeout on
    // one scan, not on every scan. clearFailed() makes the next scan retry them all.
    void markFailed(const std::string& id);
    void forgetFailed(const std::string& id);
    void clearFailed();
    int32_t failedCount() const;

    // Full JUCE description of entry `index` for in-process instantiation, copied
    // under one lock so the index and the entry can't drift apart mid-scan. False
    // when `index` is out of range.
    bool typeAt(int32_t index, juce::PluginDescription& out) const;
    // The same for the entry indexOfId(identifier) finds, looked up and copied under one
    // lock (a scan between the two steps could move the index onto another plug-in).
    bool typeOfId(const char* identifier, juce::PluginDescription& out) const;

    // Persistence (message/background threads only — file I/O, never RT).
    void loadOnce();
    void save() const;
    void loadScanPathsOnce();
    void saveScanPaths() const;
    void saveRemoved() const;
    void loadFailedOnce();
    void saveFailed() const;

    // <data dir>/logs/plugin-scan.log — one line per probe, rewritten each scan.
    // Empty when there is no data dir (persistence off).
    static std::string scanLogPath();

  private:
    PluginCatalog();
    ~PluginCatalog();
    struct Impl;
    int32_t indexOfIdLocked(const char* identifier) const; // caller holds mutex_
    std::unique_ptr<Impl> impl_;
    mutable std::shared_mutex mutex_;
    bool catalogLoaded_ = false;
    bool scanPathsLoaded_ = false;
    bool removedLoaded_ = false;
    bool failedLoaded_ = false;
};

} // namespace scanner
} // namespace nota
