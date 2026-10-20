// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCatalog implementation — lossless JUCE KnownPluginList behind a
// shared_mutex, with a JUCE-free bridge snapshot for readers. Behavior matches
// pluginhost/PluginHost.cpp (M3-1) exactly; only the locking is new.

#include "PluginCatalog.h"

#include <cstdlib>

#include "audio_io/NotaConfigFile.h"

#include <juce_audio_processors/juce_audio_processors.h>

namespace nota {
namespace scanner {

struct PluginCatalog::Impl {
    juce::KnownPluginList list;
    std::vector<std::string> scanPaths;
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

catalog::PluginEntry toEntry(const juce::PluginDescription& d) {
    catalog::PluginEntry e;
    e.identifier = d.createIdentifierString().toStdString();
    e.name = d.name.toStdString();
    e.format = d.pluginFormatName.toStdString();
    e.manufacturer = d.manufacturerName.toStdString();
    e.category = d.category.toStdString();
    e.fileOrIdentifier = d.fileOrIdentifier.toStdString();
    e.isInstrument = d.isInstrument;
    return e;
}

} // namespace

PluginCatalog::PluginCatalog() : impl_(std::make_unique<Impl>()) {}
PluginCatalog::~PluginCatalog() = default;

PluginCatalog& PluginCatalog::instance() {
    static PluginCatalog inst;
    return inst;
}

std::vector<catalog::PluginEntry> PluginCatalog::snapshot() const {
    std::shared_lock lock(mutex_);
    std::vector<catalog::PluginEntry> out;
    for (const auto& t : impl_->list.getTypes()) out.push_back(toEntry(t));
    return out;
}

int32_t PluginCatalog::count() const {
    std::shared_lock lock(mutex_);
    return impl_->list.getNumTypes();
}

bool PluginCatalog::entryAt(int32_t index, catalog::PluginEntry& out) const {
    std::shared_lock lock(mutex_);
    const auto types = impl_->list.getTypes();
    if (index < 0 || index >= types.size()) return false;
    out = toEntry(types.getReference(index));
    return true;
}

std::string PluginCatalog::idAt(int32_t index) const {
    std::shared_lock lock(mutex_);
    const auto types = impl_->list.getTypes();
    if (index < 0 || index >= types.size()) return {};
    return types.getReference(index).createIdentifierString().toStdString();
}

int32_t PluginCatalog::indexOfId(const char* identifier) const {
    if (identifier == nullptr) return -1;
    const juce::String want{juce::CharPointer_UTF8(identifier)};
    std::shared_lock lock(mutex_);
    const auto types = impl_->list.getTypes();
    for (int i = 0; i < types.size(); ++i)
        if (types.getReference(i).createIdentifierString() == want) return i;

    // Fallback: JUCE ids are "<format>-<name>-<hex path hash>-<hex uid>", so the
    // same plugin at another path never matches exactly. Match format + name + uid.
    const int uidDash = want.lastIndexOfChar('-');
    const int hashDash = uidDash > 0 ? want.substring(0, uidDash).lastIndexOfChar('-') : -1;
    if (hashDash <= 0) return -1;
    const auto prefix = want.substring(0, hashDash);
    const auto uid = want.substring(uidDash + 1);
    for (int i = 0; i < types.size(); ++i) {
        const auto& t = types.getReference(i);
        if (t.pluginFormatName + "-" + t.name == prefix &&
            (juce::String::toHexString(t.uniqueId) == uid || juce::String::toHexString(t.deprecatedUid) == uid))
            return i;
    }
    return -1;
}

std::string PluginCatalog::descLine(int32_t index) const {
    std::shared_lock lock(mutex_);
    const auto types = impl_->list.getTypes();
    if (index < 0 || index >= types.size()) return {};
    const auto& d = types.getReference(index);
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
        const juce::String d{juce::CharPointer_UTF8(dir.c_str())};
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
}

bool PluginCatalog::hasFileOrIdentifier(const std::string& fileOrId) const {
    const juce::String want{juce::CharPointer_UTF8(fileOrId.c_str())};
    std::shared_lock lock(mutex_);
    for (const auto& t : impl_->list.getTypes())
        if (t.fileOrIdentifier == want) return true;
    return false;
}

void PluginCatalog::removeMissingVst3OnDisk() {
    std::unique_lock lock(mutex_);
    juce::Array<juce::PluginDescription> gone;
    for (const auto& t : impl_->list.getTypes())
        if (t.pluginFormatName == "VST3" && !juce::File(t.fileOrIdentifier).exists()) gone.add(t);
    for (const auto& t : gone) impl_->list.removeType(t);
}

std::vector<std::string> PluginCatalog::unknownIds(const std::vector<std::string>& discovered) const {
    std::shared_lock lock(mutex_);
    std::vector<std::string> out;
    for (const auto& id : discovered) {
        const juce::String want{juce::CharPointer_UTF8(id.c_str())};
        bool known = false;
        for (const auto& t : impl_->list.getTypes())
            if (t.fileOrIdentifier == want) {
                known = true;
                break;
            }
        if (!known) out.push_back(id);
    }
    return out;
}

std::vector<std::unique_ptr<juce::PluginDescription>> PluginCatalog::types() const {
    std::shared_lock lock(mutex_);
    std::vector<std::unique_ptr<juce::PluginDescription>> out;
    for (const auto& t : impl_->list.getTypes()) out.push_back(std::make_unique<juce::PluginDescription>(t));
    return out;
}

void PluginCatalog::loadOnce() {
    std::unique_lock lock(mutex_);
    if (catalogLoaded_) return;
    catalogLoaded_ = true;
    if (auto xml = juce::parseXML(catalogFile())) impl_->list.recreateFromXml(*xml);
}

void PluginCatalog::save() const {
    std::shared_lock lock(mutex_);
    if (auto xml = impl_->list.createXml()) xml->writeTo(catalogFile());
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

namespace catalog {

int32_t indexOfId(const PluginEntry* entries, int32_t count, const char* identifier) noexcept {
    if (entries == nullptr || identifier == nullptr || count <= 0) return -1;
    const std::string want(identifier);
    for (int32_t i = 0; i < count; ++i)
        if (entries[i].identifier == want) return i;
    const auto uidDash = want.rfind('-');
    if (uidDash == std::string::npos || uidDash == 0) return -1;
    const auto hashDash = want.rfind('-', uidDash - 1);
    if (hashDash == std::string::npos || hashDash == 0) return -1;
    const std::string prefix = want.substr(0, hashDash);
    const std::string uid = want.substr(uidDash + 1);
    for (int32_t i = 0; i < count; ++i) {
        if (entries[i].format + "-" + entries[i].name == prefix) {
            if (entries[i].identifier.size() >= uid.size() &&
                entries[i].identifier.compare(entries[i].identifier.size() - uid.size(), uid.size(), uid) == 0)
                return i;
        }
    }
    return -1;
}

} // namespace catalog
} // namespace nota
