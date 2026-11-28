// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginhost/PluginHostModule.h"

#include "nota/dsp/PluginContracts.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace {

#define NOTA_CHECK(cond)                                                                                               \
    do {                                                                                                               \
        if (!(cond)) {                                                                                                 \
            std::fprintf(stderr, "Check failed: %s (%s:%d)\n", #cond, __FILE__, __LINE__);                             \
            std::abort();                                                                                              \
        }                                                                                                              \
    } while (false)

class FakeCatalog final : public nota::dsp::IPluginCatalog {
  public:
    void addEntry(nota::dsp::PluginDescriptor desc) { entries_.push_back(std::move(desc)); }

    int32_t count() const override { return static_cast<int32_t>(entries_.size()); }

    int32_t indexOfId(const char* identifier) const override {
        if (identifier == nullptr) return -1;
        for (size_t i = 0; i < entries_.size(); ++i) {
            if (entries_[i].identifier == identifier) return static_cast<int32_t>(i);
        }
        return -1;
    }

    std::string descLine(int32_t index) const override {
        if (index < 0 || index >= count()) return {};
        return entries_[static_cast<size_t>(index)].name;
    }

    std::string idAt(int32_t index) const override {
        if (index < 0 || index >= count()) return {};
        return entries_[static_cast<size_t>(index)].identifier;
    }

    bool descriptorAt(int32_t index, nota::dsp::PluginDescriptor& out) const override {
        if (index < 0 || index >= count()) return false;
        out = entries_[static_cast<size_t>(index)];
        return true;
    }

    bool descriptorOfId(const char* identifier, nota::dsp::PluginDescriptor& out) const override {
        const int32_t idx = indexOfId(identifier);
        return idx >= 0 && descriptorAt(idx, out);
    }

    std::vector<std::string> scanPaths() const override { return {}; }
    void addScanPath(const std::string&) override {}
    bool removeScanPath(int32_t) override { return false; }
    int32_t scanPathCount() const override { return 0; }
    std::string scanPathAt(int32_t) const override { return {}; }
    bool isScanPathRemoved(const std::string&) const override { return false; }
    int32_t restoreDefaultScanPaths(const char*) override { return 0; }
    int32_t failedCount() const override { return 0; }
    void clearFailed() override {}

  private:
    std::vector<nota::dsp::PluginDescriptor> entries_;
};

void testHostWithNullAndFakeCatalog() {
    auto hostNoCat = nota::pluginhost::createPluginHost(nullptr);
    NOTA_CHECK(hostNoCat->catalog() == nullptr);
    NOTA_CHECK(hostNoCat->createInstrument(0, 48000.0, 512) == nullptr);
    NOTA_CHECK(hostNoCat->createEffect(0, 48000.0, 512) == nullptr);
    NOTA_CHECK(hostNoCat->createInstrumentById("missing", 48000.0, 512) == nullptr);
    NOTA_CHECK(hostNoCat->createEffectById("missing", 48000.0, 512) == nullptr);
    NOTA_CHECK(hostNoCat->pendingPluginLoads() == 0);
    const auto serviced = hostNoCat->servicePluginLoads();
    NOTA_CHECK(serviced.changed == 0);
    NOTA_CHECK(serviced.attached == 0);
    hostNoCat->setAudioWorkgroup(nullptr);
    hostNoCat->setPluginAutoloadScope(true);
    hostNoCat->setPluginAutoloadScope(false);
    NOTA_CHECK(hostNoCat->takeFrozenPluginNotice().empty());
    hostNoCat->pumpMessageThreadIfNeeded();

    auto fakeCatalog = std::make_shared<FakeCatalog>();
    nota::dsp::PluginDescriptor fxOnly;
    fxOnly.identifier = "VST3-FakeFx-0-1";
    fxOnly.name = "FakeFx";
    fxOnly.formatName = "VST3";
    fxOnly.fileOrIdentifier = "/nonexistent/FakeFx.vst3";
    fxOnly.isInstrument = false;
    fakeCatalog->addEntry(fxOnly);

    auto host = nota::pluginhost::createPluginHost(fakeCatalog);
    NOTA_CHECK(host->catalog() == fakeCatalog);
    // Asking for an instrument on an effect-only entry must cleanly reject without GUI/plugin instantiation.
    NOTA_CHECK(host->createInstrument(0, 48000.0, 512) == nullptr);
    NOTA_CHECK(host->createInstrumentById(fxOnly.identifier, 48000.0, 512) == nullptr);
    // Unknown indices / ids return null cleanly.
    NOTA_CHECK(host->createEffect(99, 48000.0, 512) == nullptr);
    NOTA_CHECK(host->createEffectById("unknown-id", 48000.0, 512) == nullptr);
}

} // namespace

int main() {
    testHostWithNullAndFakeCatalog();
    return 0;
}
