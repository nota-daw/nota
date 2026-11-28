// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginscanner/PluginCatalogImpl.h"

#include "nota/dsp/PluginContracts.h"
#include "nota/core/ConfigFile.h"
#include "nota/pluginscanner/PluginFormatRegistry.h"
#include "nota/pluginscanner/PluginScannerModule.h"
#include "xml/SimpleXml.h"

#include <algorithm>
#include <charconv>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <mutex>
#include <shared_mutex>
#include <sstream>
#include <string_view>
#include <string>
#include <system_error>
#include <utility>
#include <vector>

namespace nota::pluginscanner {
namespace {

bool isAbsolutePluginPath(const std::string& path) {
    if (path.empty()) return false;
    return std::filesystem::path(path).is_absolute();
}

int64_t filesystemStampMs(const std::string& path) {
    if (!isAbsolutePluginPath(path)) return 0;
    std::error_code ec;
    const std::filesystem::path p(path);
    if (!std::filesystem::exists(p, ec)) return 0;
    const auto ftime = std::filesystem::last_write_time(p, ec);
    if (ec) return 0;
    // Match epoch-independent invalidation semantics: any mtime change invalidates failed cache.
    const auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(ftime.time_since_epoch());
    return static_cast<int64_t>(ms.count());
}

std::filesystem::path childIfDirNonEmpty(const std::string& dir, const char* name) {
    if (dir.empty()) return {};
    return std::filesystem::path(dir) / name;
}

bool descriptorsMatchDuplicate(const dsp::PluginDescriptor& a, const dsp::PluginDescriptor& b) noexcept {
    return a.fileOrIdentifier == b.fileOrIdentifier && a.deprecatedUid == b.deprecatedUid && a.uniqueId == b.uniqueId;
}

std::vector<std::string> splitLines(const std::string& text, bool skipEmpty) {
    std::vector<std::string> lines;
    std::istringstream ss(text);
    std::string line;
    while (std::getline(ss, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (skipEmpty && line.empty()) continue;
        lines.push_back(std::move(line));
    }
    return lines;
}

std::string joinLines(const std::vector<std::string>& lines) {
    std::string out;
    for (size_t i = 0; i < lines.size(); ++i) {
        if (i > 0) out.push_back('\n');
        out += lines[i];
    }
    return out;
}

void writeLinesFile(const std::filesystem::path& filePath, const std::vector<std::string>& lines) {
    if (filePath.empty()) return;
    std::error_code ec;
    if (filePath.has_parent_path()) { std::filesystem::create_directories(filePath.parent_path(), ec); }
    (void)core::cfgfile::writeFileAtomic(filePath.string(), joinLines(lines));
}

} // namespace

PluginCatalogImpl::PluginCatalogImpl(
    const PluginFormatRegistry* registry, std::string supportDirOverride, bool autoLoadOnRead)
    : registry_(registry != nullptr ? registry : &PluginFormatRegistry::instance()),
      supportDirOverride_(std::move(supportDirOverride)), autoLoadOnRead_(autoLoadOnRead) {}

PluginCatalogImpl::~PluginCatalogImpl() = default;

std::string PluginCatalogImpl::effectiveSupportDir() const {
    if (!supportDirOverride_.empty()) {
        std::error_code ec;
        std::filesystem::create_directories(supportDirOverride_, ec);
        return supportDirOverride_;
    }
    return supportDirPath();
}

std::string PluginCatalogImpl::scanLogPath() const {
    const std::string dir = effectiveSupportDir();
    if (dir.empty()) return {};
    return (std::filesystem::path(dir) / "logs" / "plugin-scan.log").string();
}

void PluginCatalogImpl::ensureCatalogLoadedIfAuto() const {
    if (!autoLoadOnRead_) return;
    {
        std::shared_lock<std::shared_mutex> readLock(mutex_);
        if (catalogLoaded_) return;
    }
    const_cast<PluginCatalogImpl*>(this)->loadOnce();
}

void PluginCatalogImpl::ensureScanPathsLoadedIfAuto() const {
    if (!autoLoadOnRead_) return;
    {
        std::shared_lock<std::shared_mutex> readLock(mutex_);
        if (scanPathsLoaded_) return;
    }
    const_cast<PluginCatalogImpl*>(this)->loadScanPathsOnce();
}

void PluginCatalogImpl::ensureFailedLoadedIfAuto() const {
    if (!autoLoadOnRead_) return;
    {
        std::shared_lock<std::shared_mutex> readLock(mutex_);
        if (failedLoaded_) return;
    }
    const_cast<PluginCatalogImpl*>(this)->loadFailedOnce();
}

int32_t PluginCatalogImpl::count() const {
    ensureCatalogLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    return static_cast<int32_t>(entries_.size());
}

int32_t PluginCatalogImpl::indexOfIdLocked(const char* identifier) const {
    if (identifier == nullptr) return -1;
    const std::string_view want(identifier);
    const size_t n = entries_.size();
    for (size_t i = 0; i < n; ++i) {
        if (entries_[i].identifier == want) return static_cast<int32_t>(i);
    }

    // Fallback: "<format>-<name>-<hex path hash>-<hex uid>" when moved across paths.
    const size_t uidDash = want.rfind('-');
    if (uidDash == std::string_view::npos || uidDash == 0) return -1;
    const size_t hashDash = want.substr(0, uidDash).rfind('-');
    if (hashDash == std::string_view::npos || hashDash == 0) return -1;

    const std::string_view prefix = want.substr(0, hashDash);
    const std::string_view uid = want.substr(uidDash + 1);
    for (size_t i = 0; i < n; ++i) {
        const auto& e = entries_[i];
        if (e.formatName.size() + 1 + e.name.size() == prefix.size() && prefix.starts_with(e.formatName) &&
            prefix[e.formatName.size()] == '-' && prefix.ends_with(e.name)) {
            if (detail::toHexString32(e.uniqueId) == uid || detail::toHexString32(e.deprecatedUid) == uid) {
                return static_cast<int32_t>(i);
            }
        }
    }
    return -1;
}

int32_t PluginCatalogImpl::indexOfId(const char* identifier) const {
    ensureCatalogLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    return indexOfIdLocked(identifier);
}

std::string PluginCatalogImpl::idAt(int32_t index) const {
    ensureCatalogLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    if (index < 0 || index >= static_cast<int32_t>(entries_.size())) return {};
    return entries_[static_cast<size_t>(index)].identifier;
}

std::string PluginCatalogImpl::descLine(int32_t index) const {
    ensureCatalogLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    if (index < 0 || index >= static_cast<int32_t>(entries_.size())) return {};
    const auto& d = entries_[static_cast<size_t>(index)];
    std::string category = d.category;
    std::replace(category.begin(), category.end(), '|', '/');
    return d.name + " | " + d.formatName + " | " + (d.isInstrument ? "inst" : "fx") + " | " +
           (d.manufacturerName.empty() ? std::string("?") : d.manufacturerName) + " | " + category;
}

bool PluginCatalogImpl::descriptorAt(int32_t index, dsp::PluginDescriptor& out) const {
    ensureCatalogLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    if (index < 0 || index >= static_cast<int32_t>(entries_.size())) return false;
    out = entries_[static_cast<size_t>(index)];
    return true;
}

bool PluginCatalogImpl::descriptorOfId(const char* identifier, dsp::PluginDescriptor& out) const {
    ensureCatalogLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    const int32_t idx = indexOfIdLocked(identifier);
    if (idx < 0) return false;
    out = entries_[static_cast<size_t>(idx)];
    return true;
}

std::vector<std::string> PluginCatalogImpl::scanPaths() const {
    ensureScanPathsLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    return scanPaths_;
}

void PluginCatalogImpl::addScanPath(const std::string& dir) {
    if (dir.empty()) return;
    ensureScanPathsLoadedIfAuto();
    bool unblocked = false;
    bool appended = false;
    {
        std::unique_lock<std::shared_mutex> lock(mutex_);
        for (auto it = removedScanPaths_.begin(); it != removedScanPaths_.end(); ++it) {
            if (*it == dir) {
                removedScanPaths_.erase(it);
                unblocked = true;
                break;
            }
        }
        bool known = false;
        for (const auto& p : scanPaths_) {
            if (p == dir) {
                known = true;
                break;
            }
        }
        if (!known) {
            scanPaths_.push_back(dir);
            appended = true;
        }
    }
    if (unblocked) saveRemoved();
    if (appended) saveScanPaths();
}

bool PluginCatalogImpl::removeScanPath(int32_t index) {
    ensureScanPathsLoadedIfAuto();
    std::string gone;
    {
        std::unique_lock<std::shared_mutex> lock(mutex_);
        if (index < 0 || index >= static_cast<int32_t>(scanPaths_.size())) return false;
        gone = scanPaths_[static_cast<size_t>(index)];
        scanPaths_.erase(scanPaths_.begin() + index);
    }
    saveScanPaths();
    if (!gone.empty()) {
        bool fresh = false;
        {
            std::unique_lock<std::shared_mutex> lock(mutex_);
            bool known = false;
            for (const auto& p : removedScanPaths_) {
                if (p == gone) {
                    known = true;
                    break;
                }
            }
            if (!known) {
                removedScanPaths_.push_back(gone);
                fresh = true;
            }
        }
        if (fresh) saveRemoved();
    }
    return true;
}

int32_t PluginCatalogImpl::scanPathCount() const {
    ensureScanPathsLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    return static_cast<int32_t>(scanPaths_.size());
}

std::string PluginCatalogImpl::scanPathAt(int32_t index) const {
    ensureScanPathsLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    if (index < 0 || index >= static_cast<int32_t>(scanPaths_.size())) return {};
    return scanPaths_[static_cast<size_t>(index)];
}

bool PluginCatalogImpl::isScanPathRemoved(const std::string& dir) const {
    if (dir.empty()) return false;
    ensureScanPathsLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    for (const auto& p : removedScanPaths_) {
        if (p == dir) return true;
    }
    return false;
}

std::vector<std::string> PluginCatalogImpl::defaultScanPathsFor(const char* format) const {
    if (format == nullptr || *format == '\0') return {};
    const auto backend = registry_->find(format);
    if (!backend) return {};
    std::vector<std::string> out;
    for (const auto& path : backend->defaultSearchPaths()) {
        if (path.empty()) continue;
        bool known = false;
        for (const auto& existing : out) {
            if (existing == path) {
                known = true;
                break;
            }
        }
        if (!known) out.push_back(path);
    }
    return out;
}

int32_t PluginCatalogImpl::restoreDefaultScanPaths(const char* format) {
    ensureScanPathsLoadedIfAuto();
    const auto defaults = defaultScanPathsFor(format);
    if (defaults.empty()) return 0;
    int32_t added = 0;
    bool unblocked = false;
    {
        std::unique_lock<std::shared_mutex> lock(mutex_);
        for (const auto& p : defaults) {
            for (auto it = removedScanPaths_.begin(); it != removedScanPaths_.end(); ++it) {
                if (*it == p) {
                    removedScanPaths_.erase(it);
                    unblocked = true;
                    break;
                }
            }
            bool known = false;
            for (const auto& q : scanPaths_) {
                if (q == p) {
                    known = true;
                    break;
                }
            }
            if (!known) {
                scanPaths_.push_back(p);
                ++added;
            }
        }
    }
    if (unblocked) saveRemoved();
    if (added > 0) saveScanPaths();
    return added;
}

void PluginCatalogImpl::addDescriptor(const dsp::PluginDescriptor& desc) {
    dsp::PluginDescriptor normalized = desc;
    if (normalized.format == dsp::PluginFormatKind::Unknown) {
        normalized.format = detail::inferFormatKind(normalized.formatName);
    }
    if (normalized.identifier.empty()) { normalized.identifier = detail::computePluginIdentifier(normalized); }
    if (normalized.serializedXml.empty()) {
        normalized.serializedXml = detail::pluginDescriptorToXmlNode(normalized).toString(0);
    }

    std::unique_lock<std::shared_mutex> lock(mutex_);
    for (auto& existing : entries_) {
        if (descriptorsMatchDuplicate(existing, normalized) || existing.identifier == normalized.identifier) {
            existing = std::move(normalized);
            return;
        }
    }
    entries_.push_back(std::move(normalized));
}

void PluginCatalogImpl::removeMissingByBackends() {
    std::unique_lock<std::shared_mutex> lock(mutex_);
    for (auto it = entries_.begin(); it != entries_.end();) {
        const auto backend = registry_->find(it->formatName);
        if (backend && !backend->candidateExists(it->fileOrIdentifier)) {
            it = entries_.erase(it);
        } else {
            ++it;
        }
    }

    for (auto it = failed_.begin(); it != failed_.end();) {
        std::error_code ec;
        if (isAbsolutePluginPath(it->first) && !std::filesystem::exists(it->first, ec)) {
            it = failed_.erase(it);
        } else {
            ++it;
        }
    }
}

std::vector<std::string> PluginCatalogImpl::unknownCandidates(
    const std::string& formatId, const std::vector<std::string>& discovered) const {
    const auto backend = registry_->find(formatId);
    std::shared_lock<std::shared_mutex> lock(mutex_);
    std::vector<std::string> out;
    for (const auto& candidate : discovered) {
        bool known = false;
        for (const auto& entry : entries_) {
            if (entry.formatName == formatId && entry.fileOrIdentifier == candidate) {
                known = true;
                break;
            }
        }
        if (known) continue;

        if (const auto f = failed_.find(candidate); f != failed_.end()) {
            const int64_t stamp = backend ? backend->candidateTimestampMs(candidate) : filesystemStampMs(candidate);
            if (f->second == stamp) continue;
        }
        out.push_back(candidate);
    }
    return out;
}

void PluginCatalogImpl::markFailed(const std::string& formatId, const std::string& candidateId) {
    const auto backend = registry_->find(formatId);
    const int64_t stamp = backend ? backend->candidateTimestampMs(candidateId) : filesystemStampMs(candidateId);
    std::unique_lock<std::shared_mutex> lock(mutex_);
    failed_[candidateId] = stamp;
}

void PluginCatalogImpl::forgetFailed(const std::string& candidateId) {
    std::unique_lock<std::shared_mutex> lock(mutex_);
    failed_.erase(candidateId);
}

void PluginCatalogImpl::clearFailed() {
    std::unique_lock<std::shared_mutex> lock(mutex_);
    failed_.clear();
}

int32_t PluginCatalogImpl::failedCount() const {
    ensureFailedLoadedIfAuto();
    std::shared_lock<std::shared_mutex> lock(mutex_);
    return static_cast<int32_t>(failed_.size());
}

void PluginCatalogImpl::loadOnce() {
    const std::string dir = effectiveSupportDir();
    std::unique_lock<std::shared_mutex> lock(mutex_);
    if (catalogLoaded_) return;
    catalogLoaded_ = true;

    const auto file = childIfDirNonEmpty(dir, "plugins.xml");
    if (file.empty()) return;
    const std::string content = core::cfgfile::readFile(file.string());
    if (content.empty()) return;

    detail::XmlNode root;
    if (!detail::parseXmlDocument(content, root)) return;

    entries_.clear();
    for (const auto& child : root.children) {
        dsp::PluginDescriptor desc;
        if (detail::pluginDescriptorFromXmlNode(child, desc)) {
            bool duplicate = false;
            for (auto& existing : entries_) {
                if (descriptorsMatchDuplicate(existing, desc)) {
                    existing = desc;
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate) entries_.push_back(std::move(desc));
        }
    }
}

void PluginCatalogImpl::save() const {
    const std::string dir = effectiveSupportDir();
    const auto file = childIfDirNonEmpty(dir, "plugins.xml");
    if (file.empty()) return;

    detail::XmlNode root;
    root.tag = "KNOWNPLUGINS";
    {
        std::shared_lock<std::shared_mutex> lock(mutex_);
        root.children.reserve(entries_.size());
        for (const auto& entry : entries_) { root.children.push_back(detail::pluginDescriptorToXmlNode(entry)); }
    }

    std::string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n\n";
    xml += root.toString(0);
    xml.push_back('\n');
    (void)core::cfgfile::writeFileAtomic(file.string(), xml);
}

void PluginCatalogImpl::loadFailedOnce() {
    const std::string dir = effectiveSupportDir();
    std::unique_lock<std::shared_mutex> lock(mutex_);
    if (failedLoaded_) return;
    failedLoaded_ = true;

    const auto file = childIfDirNonEmpty(dir, "scan-failed.txt");
    if (file.empty()) return;
    const std::string content = core::cfgfile::readFile(file.string());
    if (content.empty()) return;

    for (const auto& line : splitLines(content, false)) {
        const size_t tab = line.find('\t');
        if (tab == std::string::npos || tab == 0) continue;
        int64_t stamp = 0;
        const auto res = std::from_chars(line.data(), line.data() + tab, stamp, 10);
        if (res.ec != std::errc{}) continue;
        failed_[line.substr(tab + 1)] = stamp;
    }
}

void PluginCatalogImpl::saveFailed() const {
    const std::string dir = effectiveSupportDir();
    const auto file = childIfDirNonEmpty(dir, "scan-failed.txt");
    if (file.empty()) return;

    std::vector<std::string> lines;
    {
        std::shared_lock<std::shared_mutex> lock(mutex_);
        lines.reserve(failed_.size());
        for (const auto& [id, stamp] : failed_) { lines.push_back(std::to_string(stamp) + "\t" + id); }
    }
    writeLinesFile(file, lines);
}

void PluginCatalogImpl::loadScanPathsOnce() {
    const std::string dir = effectiveSupportDir();
    bool changed = false;
    {
        std::unique_lock<std::shared_mutex> lock(mutex_);
        if (scanPathsLoaded_) return;
        scanPathsLoaded_ = true;

        if (!dir.empty()) {
            const auto pathsFile = childIfDirNonEmpty(dir, "scanpaths.txt");
            if (const std::string content = core::cfgfile::readFile(pathsFile.string()); !content.empty()) {
                for (auto& line : splitLines(content, true)) { scanPaths_.push_back(std::move(line)); }
            }
            if (!removedLoaded_) {
                removedLoaded_ = true;
                const auto removedFile = childIfDirNonEmpty(dir, "scanpaths-removed.txt");
                if (const std::string content = core::cfgfile::readFile(removedFile.string()); !content.empty()) {
                    for (auto& line : splitLines(content, true)) { removedScanPaths_.push_back(std::move(line)); }
                }
            }
        }

        for (const auto& backend : registry_->orderedBackends()) {
            if (!backend || !backend->hasDefaultScanPathBackfill()) continue;
            for (const auto& path : backend->defaultSearchPaths()) {
                if (path.empty()) continue;
                bool skipped = false;
                for (const auto& removed : removedScanPaths_) {
                    if (removed == path) {
                        skipped = true;
                        break;
                    }
                }
                if (skipped) continue;
                bool known = false;
                for (const auto& existing : scanPaths_) {
                    if (existing == path) {
                        known = true;
                        break;
                    }
                }
                if (!known) {
                    scanPaths_.push_back(path);
                    changed = true;
                }
            }
        }
    }
    if (changed) saveScanPaths();
}

void PluginCatalogImpl::saveScanPaths() const {
    const auto file = childIfDirNonEmpty(effectiveSupportDir(), "scanpaths.txt");
    if (file.empty()) return;
    std::vector<std::string> copy;
    {
        std::shared_lock<std::shared_mutex> lock(mutex_);
        copy = scanPaths_;
    }
    writeLinesFile(file, copy);
}

void PluginCatalogImpl::saveRemoved() const {
    const auto file = childIfDirNonEmpty(effectiveSupportDir(), "scanpaths-removed.txt");
    if (file.empty()) return;
    std::vector<std::string> copy;
    {
        std::shared_lock<std::shared_mutex> lock(mutex_);
        copy = removedScanPaths_;
    }
    writeLinesFile(file, copy);
}

} // namespace nota::pluginscanner
