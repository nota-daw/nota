// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginscanner/PluginScannerImpl.h"

#include "nota/pluginscanner/IPluginFormatBackend.h"
#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/OutOfProcessScanner.h"
#include "nota/pluginscanner/PluginFormatRegistry.h"
#include "nota/pluginscanner/PluginCatalogImpl.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <memory>
#include <ios>
#include <mutex>
#include <string>
#include <string_view>
#include <system_error>
#include <thread>
#include <utility>
#include <vector>

namespace nota::pluginscanner {
namespace {

using Outcome = OutOfProcessScanner::Outcome;

const char* outcomeLabel(Outcome o) noexcept {
    switch (o) {
    case Outcome::Ok: return "ok";
    case Outcome::Timeout: return "timed out, worker killed";
    case Outcome::NoAnswer: return "no answer (worker crashed or exited)";
    case Outcome::StartFailed: return "worker failed to start";
    }
    return "?";
}

std::string isoTimestampNow() {
    const auto now = std::chrono::system_clock::now();
    const std::time_t t = std::chrono::system_clock::to_time_t(now);
    std::tm tmBuf{};
#if defined(_WIN32)
    localtime_s(&tmBuf, &t);
#else
    localtime_r(&t, &tmBuf);
#endif
    const auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch()).count() % 1000;
    char buf[40];
    std::snprintf(buf, sizeof(buf), "%04d-%02d-%02dT%02d:%02d:%02d.%03lld", tmBuf.tm_year + 1900, tmBuf.tm_mon + 1,
        tmBuf.tm_mday, tmBuf.tm_hour, tmBuf.tm_min, tmBuf.tm_sec, static_cast<long long>(ms));
    return std::string(buf);
}

class ScanLog {
  public:
    explicit ScanLog(const std::string& path) {
        if (path.empty()) return;
        std::error_code ec;
        const std::filesystem::path fsPath(path);
        if (fsPath.has_parent_path()) { std::filesystem::create_directories(fsPath.parent_path(), ec); }
        stream_.open(fsPath, std::ios::out | std::ios::trunc);
    }

    void line(const std::string& text) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!stream_.is_open()) return;
        stream_ << isoTimestampNow() << ' ' << text << '\n';
        stream_.flush();
    }

  private:
    std::mutex mutex_;
    std::ofstream stream_;
};

size_t probeThreads(size_t jobs) {
    const unsigned hw = std::max(2u, std::thread::hardware_concurrency());
    return std::min<size_t>(jobs, static_cast<size_t>(std::clamp(hw / 2, 1u, 4u)));
}

struct ProbeJob {
    std::shared_ptr<IPluginFormatBackend> backend;
    std::string formatId;
    std::string candidateId;
};

} // namespace

PluginScannerImpl::PluginScannerImpl(std::shared_ptr<PluginCatalogImpl> catalog, const PluginFormatRegistry* registry,
    std::string workerPath, int probeTimeoutMs, int finalRetryTimeoutMs)
    : catalog_(std::move(catalog)), registry_(registry != nullptr ? registry : &PluginFormatRegistry::instance()),
      workerPath_(std::move(workerPath)),
      probeTimeoutMs_(probeTimeoutMs > 0 ? probeTimeoutMs : OutOfProcessScanner::kDefaultTimeoutMs),
      finalRetryTimeoutMs_(finalRetryTimeoutMs > 0 ? finalRetryTimeoutMs : 120000) {}

PluginScannerImpl::~PluginScannerImpl() = default;

dsp::IPluginCatalog& PluginScannerImpl::catalog() {
    return *catalog_;
}

const dsp::IPluginCatalog& PluginScannerImpl::catalog() const {
    return *catalog_;
}

void PluginScannerImpl::setWorkerPath(std::string workerPath) {
    std::lock_guard<std::mutex> lock(scanMutex_);
    workerPath_ = std::move(workerPath);
}

int32_t PluginScannerImpl::scan(bool rescanFailed) {
    std::string wp;
    {
        std::lock_guard<std::mutex> lock(scanMutex_);
        wp = workerPath_;
    }
    return scanEx(wp, nullptr, rescanFailed ? kScanRetryFailedFlag : 0);
}

int32_t PluginScannerImpl::scanDirectory(const std::string& dir) {
    std::string wp;
    {
        std::lock_guard<std::mutex> lock(scanMutex_);
        wp = workerPath_;
    }
    return scanEx(wp, dir.empty() ? nullptr : dir.c_str(), 0);
}

int32_t PluginScannerImpl::scanEx(std::string_view workerPath, const char* onlyDir, int32_t flags) {
    if (workerPath.empty() || !catalog_) return -1;

    catalog_->loadOnce();
    catalog_->loadScanPathsOnce();
    catalog_->loadFailedOnce();

    OutOfProcessScanner worker{std::string(workerPath), probeTimeoutMs_};
    if (!worker.workerAvailable()) return -2;
    OutOfProcessScanner patient{std::string(workerPath), finalRetryTimeoutMs_};

    std::lock_guard<std::mutex> scanLock(scanMutex_);
    stopRequested_.store(false, std::memory_order_relaxed);
    done_.store(0, std::memory_order_relaxed);
    total_.store(0, std::memory_order_relaxed);
    {
        std::lock_guard<std::mutex> curLock(currentMutex_);
        current_.clear();
    }
    scanning_.store(true, std::memory_order_release);
    struct IdleGuard {
        std::atomic<bool>& flag;
        ~IdleGuard() { flag.store(false, std::memory_order_release); }
    } idle{scanning_};

    const std::string onlyDirStr = (onlyDir != nullptr) ? std::string(onlyDir) : std::string{};
    ScanLog log(catalog_->scanLogPath());
    log.line("scan start, worker " + std::string(workerPath) +
             (onlyDirStr.empty() ? std::string{} : ", only " + onlyDirStr));

    if ((flags & kScanRetryFailedFlag) != 0) catalog_->clearFailed();

    catalog_->removeMissingByBackends();

    const std::vector<std::string> userScanPaths = catalog_->scanPaths();
    std::vector<ProbeJob> probes;
    for (const auto& backend : registry_->orderedBackends()) {
        if (!backend) continue;
        // Single-folder scan only inspects file-based formats; skip registry backends cleanly.
        if (!onlyDirStr.empty() && !backend->isFileBased()) continue;
        const std::string formatId = backend->descriptor().id;
        const std::vector<std::string> discovered = backend->discoverCandidates(userScanPaths, onlyDirStr);
        const std::vector<std::string> unknown = catalog_->unknownCandidates(formatId, discovered);
        log.line(formatId + ": " + std::to_string(discovered.size()) + " found, " + std::to_string(unknown.size()) +
                 " to probe");
        for (const auto& candidateId : unknown) { probes.push_back(ProbeJob{backend, formatId, candidateId}); }
    }

    total_.store(static_cast<int32_t>(probes.size()), std::memory_order_relaxed);

    using Clock = std::chrono::steady_clock;
    std::mutex saveMutex;
    auto lastSave = Clock::now();
    auto saveNow = [&]() {
        catalog_->save();
        catalog_->saveFailed();
        lastSave = Clock::now();
    };

    auto probeOne = [&](const ProbeJob& probe, bool finalAttempt) -> bool {
        if (stopRequested_.load(std::memory_order_acquire)) return true;

        const std::string disp = probe.backend->candidateDisplayName(probe.candidateId);
        {
            std::lock_guard<std::mutex> curLock(currentMutex_);
            current_ = disp;
        }
        log.line("probe " + probe.formatId + " " + probe.candidateId + (finalAttempt ? " (alone)" : ""));
        const auto t0 = Clock::now();
        Outcome outcome = Outcome::StartFailed;
        std::string detail;
        const auto descs =
            (finalAttempt ? patient : worker).scanDescriptions(probe.formatId, probe.candidateId, &outcome, &detail);
        for (const auto& desc : descs) catalog_->addDescriptor(desc);

        const auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - t0).count();
        log.line("  " + probe.candidateId + ": " + outcomeLabel(outcome) + ", " + std::to_string(descs.size()) +
                 " types, " + std::to_string(ms) + " ms" + (detail.empty() ? std::string{} : " (" + detail + ")"));

        const bool retry = !finalAttempt && (outcome == Outcome::Timeout || outcome == Outcome::NoAnswer);
        if (!descs.empty()) {
            catalog_->forgetFailed(probe.candidateId);
        } else if (!retry && outcome != Outcome::StartFailed) {
            catalog_->markFailed(probe.formatId, probe.candidateId);
        }
        done_.fetch_add(1, std::memory_order_relaxed);
        std::lock_guard<std::mutex> lock(saveMutex);
        if (Clock::now() - lastSave > std::chrono::seconds(5)) saveNow();
        return !retry;
    };

    std::vector<ProbeJob> retries;
    std::mutex retriesMutex;
    std::atomic<size_t> next{0};
    auto work = [&]() {
        for (size_t i = 0; (i = next.fetch_add(1, std::memory_order_relaxed)) < probes.size();) {
            if (stopRequested_.load(std::memory_order_acquire)) break;
            if (!probeOne(probes[i], probes.size() == 1)) {
                std::lock_guard<std::mutex> lock(retriesMutex);
                retries.push_back(probes[i]);
            }
        }
    };

    const size_t threads = probeThreads(probes.size());
    log.line("probing " + std::to_string(probes.size()) + " with " + std::to_string(threads) + " workers");
    std::vector<std::thread> pool;
    for (size_t t = 1; t < threads; ++t) pool.emplace_back(work);
    if (threads > 0) work();
    for (auto& t : pool) t.join();

    if (!retries.empty() && !stopRequested_.load(std::memory_order_acquire)) {
        log.line("retrying " + std::to_string(retries.size()) + " one at a time");
        total_.fetch_add(static_cast<int32_t>(retries.size()), std::memory_order_relaxed);
        for (const auto& probe : retries) {
            if (stopRequested_.load(std::memory_order_acquire)) break;
            (void)probeOne(probe, true);
        }
    }

    catalog_->save();
    catalog_->saveFailed();
    const int32_t totalKnown = catalog_->count();
    log.line("scan done, " + std::to_string(totalKnown) + " plugins known, " + std::to_string(catalog_->failedCount()) +
             " failed");
    return totalKnown;
}

dsp::PluginScanProgress PluginScannerImpl::progress() const {
    dsp::PluginScanProgress out;
    out.scanning = scanning_.load(std::memory_order_acquire);
    out.done = done_.load(std::memory_order_relaxed);
    out.total = total_.load(std::memory_order_relaxed);
    out.currentPlugin = currentPlugin();
    return out;
}

std::string PluginScannerImpl::currentPlugin() const {
    std::lock_guard<std::mutex> lock(currentMutex_);
    return current_;
}

bool PluginScannerImpl::isScanning() const {
    return scanning_.load(std::memory_order_acquire);
}

void PluginScannerImpl::stopScan() {
    stopRequested_.store(true, std::memory_order_release);
}

} // namespace nota::pluginscanner
