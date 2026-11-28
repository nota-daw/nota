// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IPluginFormatBackend — format-agnostic plugin discovery, existence & probe contract.

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/Export.h"

#include <cstdint>
#include <string>
#include <vector>

namespace nota::pluginscanner {

class NOTA_PLUGINSCANNER_API IPluginFormatBackend {
  public:
    virtual ~IPluginFormatBackend();

    virtual dsp::PluginFormatDescriptor descriptor() const = 0;

    // Whether this backend scans file-system folders (true for VST3, false for OS registry formats like AudioUnit).
    virtual bool isFileBased() const { return true; }

    // Whether defaultSearchPaths() should automatically backfill user scanpaths.
    virtual bool hasDefaultScanPathBackfill() const { return false; }

    // OS-standard locations for this format (empty for registry-based formats like AU).
    virtual std::vector<std::string> defaultSearchPaths() const = 0;

    // Discover candidate identifiers (paths or registry IDs). When `onlyDir` is non-empty,
    // file-based formats restrict search to `onlyDir`, while registry formats return empty.
    virtual std::vector<std::string> discoverCandidates(
        const std::vector<std::string>& searchPaths, const std::string& onlyDir) const = 0;

    // Returns false when a previously scanned file/bundle was deleted from disk.
    // Registry-backed formats (e.g. AudioUnit) return true so transient registry shifts don't wipe entries.
    virtual bool candidateExists(const std::string& candidateId) const = 0;

    // Last-modified stamp in ms for cache invalidation of failed candidates; 0 for non-file IDs.
    virtual int64_t candidateTimestampMs(const std::string& candidateId) const = 0;

    // Human-readable name shown while probing (e.g. bundle stem), empty when unavailable pre-probe.
    virtual std::string candidateDisplayName(const std::string& candidateId) const = 0;

    // Probe `candidateId` inside `nota-scanworker`, appending zero or more descriptors.
    // Returns false only if the backend could not run the probe cleanly.
    virtual bool probeInWorker(
        const std::string& candidateId, std::vector<dsp::PluginDescriptor>& outDescriptors) const = 0;
};

} // namespace nota::pluginscanner

namespace nota {
namespace scanner = pluginscanner;
} // namespace nota
