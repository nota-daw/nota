// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OutOfProcessScanner — child-process scan coordinator.
// Spawns nota-scanworker per undiscovered plugin id, enforces a per-plugin
// timeout (the pipe is drained on a reader thread, so a worker that never exits
// is killed on time; once the end sentinel is in, the worker is not waited for), slices the <<<NOTA_PLUGINS_BEGIN>>>..<<<NOTA_PLUGINS_END>>> sentinel
// (noisy plugins print to stdout), and parses PluginDescriptions. A crashing or
// hanging plugin never takes down the DAW: kill, skip, continue.
// No Engine/Track dependency.

#pragma once

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace juce {
class PluginDescription;
} // namespace juce

namespace nota {
namespace scanner {

class OutOfProcessScanner {
  public:
    static constexpr int kDefaultTimeoutMs = 30000;

    // How a probe ended, for the scan log.
    enum class Outcome {
        Ok,          // worker answered (possibly with zero types)
        Timeout,     // no answer within the timeout — worker killed
        NoAnswer,    // worker exited (or crashed) without the sentinel block
        StartFailed, // worker could not be started
    };

    explicit OutOfProcessScanner(std::string workerPath, int timeoutMs = kDefaultTimeoutMs) noexcept;

    // Scan one plugin id (format + fileOrIdentifier) via the worker: full JUCE
    // descriptions (uid, path hash) for catalog storage. Never throws; returns
    // empty on any failure; *outcome (optional) says why, *detail (optional) adds
    // what the log needs to tell a crash from a cut-off pipe.
    std::vector<std::unique_ptr<juce::PluginDescription>> scanDescriptions(const std::string& formatName,
        const std::string& fileOrId, Outcome* outcome = nullptr, std::string* detail = nullptr) const noexcept;

    // Returns false when the worker binary is missing.
    bool workerAvailable() const noexcept;

    const std::string& workerPath() const noexcept { return workerPath_; }
    int timeoutMs() const noexcept { return timeoutMs_; }

  private:
    std::string workerPath_;
    int timeoutMs_;
};

} // namespace scanner
} // namespace nota
