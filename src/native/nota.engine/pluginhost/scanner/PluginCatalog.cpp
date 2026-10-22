// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCatalog implementation — lossless JUCE KnownPluginList behind a
// shared_mutex. Behavior matches pluginhost/PluginHost.cpp (M3-1) exactly;
// only the locking is new.

#include "PluginCatalog.h"

#include <cstdlib>
#include <map>

#include "audio_io/NotaConfigFile.h"

#include <juce_audio_processors/juce_audio_processors.h>

namespace nota {
namespace scanner {

struct PluginCatalog::Impl {
    juce::KnownPluginList list;
    // list's entries in list order. KnownPluginList only hands out whole-list copies
    // (getTypes()), so reads index this instead; sync() after every mutation.
    std::vector<juce::PluginDescription> types;
    std::vector<std::string> scanPaths;
    std::map<std::string, int64_t> failed; // id -> file stamp when it failed
    void sync() {
        const auto all = list.getTypes();
        types.assign(all.begin(), all.end());
    }
};

namespace {

juce::File supportDir() {
    if (std::string over = nota::cfgfile::dataDirOverride(); !over.empty()) {
        juce::File dir{juce::CharPointer_UTF8(over.c_str())};
        dir.createDirectory();
        return dir;
    }
    if (const char* env = std::getenv("NOTA_DATA_DIR"); env != nullptr && *env != '\0') {
        juce::File dir{juce::CharPointer_UTF8(env)};
        dir.createDirectory();
        return dir;
    }
#if JUCE_MAC
    auto dir = juce::File::getSpecialLocation(juce::File::userApplicationDataDirectory)
                   .getChildFile("Application Support")
                   .getChildFile("Nota");
    dir.createDirectory();
    return dir;
#else
    return juce::File{juce::CharPointer_UTF8(nota::cfgfile::dataDir().c_str())};
#endif
}

juce::File catalogFile() {
    return supportDir().getChildFile("plugins.xml");
}
juce::File scanPathsFile() {
    return supportDir().getChildFile("scanpaths.txt");
}
juce::File failedFile() {
    return supportDir().getChildFile("scan-failed.txt");
}

// Modification time of a file-based plugin (VST3 path), 0 for registry ids (AU).
int64_t fileStamp(const std::string& id) {
    const juce::String path{juce::CharPointer_UTF8(id.c_str())};
    if (!juce::File::isAbsolutePath(path)) return 0;
    const juce::File f{path};
    return f.exists() ? f.getLastModificationTime().toMilliseconds() : 0;
}

} // namespace

juce::AudioPluginFormatManager& sharedFormatManager() {
    static juce::AudioPluginFormatManager fm;
    static const bool once = [] {
        juce::addDefaultFormatsToManager(fm);
        return true;
    }();
    juce::ignoreUnused(once);
    return fm;
}

PluginCatalog::PluginCatalog() : impl_(std::make_unique<Impl>()) {}
PluginCatalog::~PluginCatalog() = default;

PluginCatalog& PluginCatalog::instance() {
    static PluginCatalog inst;
    return inst;
}

int32_t PluginCatalog::count() const {
    std::shared_lock lock(mutex_);
    return static_cast<int32_t>(impl_->types.size());
}

std::string PluginCatalog::idAt(int32_t index) const {
    std::shared_lock lock(mutex_);
    const auto& types = impl_->types;
    if (index < 0 || index >= static_cast<int32_t>(types.size())) return {};
    return types[static_cast<size_t>(index)].createIdentifierString().toStdString();
}

int32_t PluginCatalog::indexOfId(const char* identifier) const {
    if (identifier == nullptr) return -1;
    const juce::String want{juce::CharPointer_UTF8(identifier)};
    std::shared_lock lock(mutex_);
    const auto& types = impl_->types;
    const int n = static_cast<int>(types.size());
    for (int i = 0; i < n; ++i)
        if (types[static_cast<size_t>(i)].createIdentifierString() == want) return i;

    // Fallback: JUCE ids are "<format>-<name>-<hex path hash>-<hex uid>", so the
    // same plugin at another path never matches exactly. Match format + name + uid.
    const int uidDash = want.lastIndexOfChar('-');
    const int hashDash = uidDash > 0 ? want.substring(0, uidDash).lastIndexOfChar('-') : -1;
    if (hashDash <= 0) return -1;
    const auto prefix = want.substring(0, hashDash);
    const auto uid = want.substring(uidDash + 1);
    for (int i = 0; i < n; ++i) {
        const auto& t = types[static_cast<size_t>(i)];
        if (t.pluginFormatName + "-" + t.name == prefix &&
            (juce::String::toHexString(t.uniqueId) == uid || juce::String::toHexString(t.deprecatedUid) == uid))
            return i;
    }
    return -1;
}

std::string PluginCatalog::descLine(int32_t index) const {
    std::shared_lock lock(mutex_);
    const auto& types = impl_->types;
    if (index < 0 || index >= static_cast<int32_t>(types.size())) return {};
    const auto& d = types[static_cast<size_t>(index)];
    return (d.name + " | " + d.pluginFormatName + " | " + (d.isInstrument ? "inst" : "fx") + " | " +
            (d.manufacturerName.isEmpty() ? juce::String("?") : d.manufacturerName) + " | " +
            d.category.replaceCharacter('|', '/'))
        .toStdString();
}

std::vector<std::string> PluginCatalog::scanPaths() const {
    std::shared_lock lock(mutex_);
    return impl_->scanPaths;
}

void PluginCatalog::addScanPath(const std::string& dir) {
    if (dir.empty()) return;
    {
        std::unique_lock lock(mutex_);
        for (const auto& p : impl_->scanPaths)
            if (p == dir) return;
        impl_->scanPaths.push_back(dir);
    }
    saveScanPaths();
}

bool PluginCatalog::removeScanPath(int32_t index) {
    {
        std::unique_lock lock(mutex_);
        if (index < 0 || index >= static_cast<int32_t>(impl_->scanPaths.size())) return false;
        impl_->scanPaths.erase(impl_->scanPaths.begin() + index);
    }
    saveScanPaths();
    return true;
}

int32_t PluginCatalog::scanPathCount() const {
    std::shared_lock lock(mutex_);
    return static_cast<int32_t>(impl_->scanPaths.size());
}

std::string PluginCatalog::scanPathAt(int32_t index) const {
    std::shared_lock lock(mutex_);
    if (index < 0 || index >= static_cast<int32_t>(impl_->scanPaths.size())) return {};
    return impl_->scanPaths[static_cast<size_t>(index)];
}

void PluginCatalog::addType(const juce::PluginDescription& desc) {
    std::unique_lock lock(mutex_);
    impl_->list.addType(desc);
    impl_->sync();
}

void PluginCatalog::removeMissingVst3OnDisk() {
    std::unique_lock lock(mutex_);
    juce::Array<juce::PluginDescription> gone;
    for (const auto& t : impl_->types)
        if (t.pluginFormatName == "VST3" && !juce::File(t.fileOrIdentifier).exists()) gone.add(t);
    for (const auto& t : gone) impl_->list.removeType(t);
    if (!gone.isEmpty()) impl_->sync();
    // Failed bundles that were deleted are no longer worth remembering.
    for (auto it = impl_->failed.begin(); it != impl_->failed.end();) {
        const juce::String path{juce::CharPointer_UTF8(it->first.c_str())};
        if (juce::File::isAbsolutePath(path) && !juce::File(path).exists()) it = impl_->failed.erase(it);
        else ++it;
    }
}

std::vector<std::string> PluginCatalog::unknownIds(const std::vector<std::string>& discovered) const {
    std::shared_lock lock(mutex_);
    std::vector<std::string> out;
    for (const auto& id : discovered) {
        const juce::String want{juce::CharPointer_UTF8(id.c_str())};
        bool known = false;
        for (const auto& t : impl_->types)
            if (t.fileOrIdentifier == want) {
                known = true;
                break;
            }
        if (known) continue;
        if (auto f = impl_->failed.find(id); f != impl_->failed.end() && f->second == fileStamp(id)) continue;
        out.push_back(id);
    }
    return out;
}

void PluginCatalog::markFailed(const std::string& id) {
    const auto stamp = fileStamp(id);
    std::unique_lock lock(mutex_);
    impl_->failed[id] = stamp;
}

void PluginCatalog::forgetFailed(const std::string& id) {
    std::unique_lock lock(mutex_);
    impl_->failed.erase(id);
}

void PluginCatalog::clearFailed() {
    std::unique_lock lock(mutex_);
    impl_->failed.clear();
}

int32_t PluginCatalog::failedCount() const {
    std::shared_lock lock(mutex_);
    return static_cast<int32_t>(impl_->failed.size());
}

bool PluginCatalog::typeAt(int32_t index, juce::PluginDescription& out) const {
    std::shared_lock lock(mutex_);
    const auto& types = impl_->types;
    if (index < 0 || index >= static_cast<int32_t>(types.size())) return false;
    out = types[static_cast<size_t>(index)];
    return true;
}

void PluginCatalog::loadOnce() {
    std::unique_lock lock(mutex_);
    if (catalogLoaded_) return;
    catalogLoaded_ = true;
    if (auto xml = juce::parseXML(catalogFile())) impl_->list.recreateFromXml(*xml);
    impl_->sync();
}

void PluginCatalog::save() const {
    std::shared_lock lock(mutex_);
    if (auto xml = impl_->list.createXml()) xml->writeTo(catalogFile());
}

void PluginCatalog::loadFailedOnce() {
    std::unique_lock lock(mutex_);
    if (failedLoaded_) return;
    failedLoaded_ = true;
    const auto f = failedFile();
    if (!f.existsAsFile()) return;
    juce::StringArray lines;
    lines.addLines(f.loadFileAsString());
    for (const auto& l : lines) {
        const int tab = l.indexOfChar('\t');
        if (tab <= 0) continue;
        impl_->failed[l.substring(tab + 1).toStdString()] = l.substring(0, tab).getLargeIntValue();
    }
}

void PluginCatalog::saveFailed() const {
    juce::StringArray lines;
    {
        std::shared_lock lock(mutex_);
        for (const auto& [id, stamp] : impl_->failed)
            lines.add(juce::String(stamp) + "\t" + juce::String::fromUTF8(id.c_str()));
    }
    failedFile().replaceWithText(lines.joinIntoString("\n"));
}

std::string PluginCatalog::scanLogPath() {
    const auto dir = supportDir();
    if (dir.getFullPathName().isEmpty()) return {};
    return dir.getChildFile("logs").getChildFile("plugin-scan.log").getFullPathName().toStdString();
}

void PluginCatalog::loadScanPathsOnce() {
    std::unique_lock lock(mutex_);
    if (scanPathsLoaded_) return;
    scanPathsLoaded_ = true;
    const auto f = scanPathsFile();
    if (!f.existsAsFile()) return;
    juce::StringArray lines;
    lines.addLines(f.loadFileAsString());
    lines.removeEmptyStrings();
    for (const auto& l : lines) impl_->scanPaths.push_back(l.toStdString());
}

void PluginCatalog::saveScanPaths() const {
    std::vector<std::string> copy;
    {
        std::shared_lock lock(mutex_);
        copy = impl_->scanPaths;
    }
    juce::StringArray lines;
    for (const auto& p : copy) lines.add(juce::String::fromUTF8(p.c_str()));
    scanPathsFile().replaceWithText(lines.joinIntoString("\n"));
}

} // namespace scanner
} // namespace nota
