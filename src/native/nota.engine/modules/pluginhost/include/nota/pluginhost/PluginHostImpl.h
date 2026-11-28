// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginHostImpl — in-process JUCE plugin host implementing nota::dsp::IPluginHost.
// Consumes an injected IPluginCatalog with zero dependency on nota_pluginscanner.

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginhost/Export.h"

#include <cstdint>
#include <memory>
#include <mutex>
#include <string>

namespace nota::pluginhost {

class NOTA_PLUGINHOST_API PluginHostImpl final : public dsp::IPluginHost {
  public:
    explicit PluginHostImpl(std::shared_ptr<dsp::IPluginCatalog> catalog = nullptr);
    ~PluginHostImpl() override;

    PluginHostImpl(const PluginHostImpl&) = delete;
    PluginHostImpl& operator=(const PluginHostImpl&) = delete;

    void setCatalog(std::shared_ptr<dsp::IPluginCatalog> catalog);
    std::shared_ptr<dsp::IPluginCatalog> catalog() const;

    [[nodiscard]] std::shared_ptr<dsp::Instrument> createInstrument(
        int32_t catalogIndex, double sampleRate, int32_t maxBlock) override;
    [[nodiscard]] std::shared_ptr<dsp::Device> createEffect(
        int32_t catalogIndex, double sampleRate, int32_t maxBlock) override;
    [[nodiscard]] std::shared_ptr<dsp::Instrument> createInstrumentById(
        const std::string& identifier, double sampleRate, int32_t maxBlock) override;
    [[nodiscard]] std::shared_ptr<dsp::Device> createEffectById(
        const std::string& identifier, double sampleRate, int32_t maxBlock) override;

    dsp::PluginLoadsServiced servicePluginLoads() override;
    int32_t pendingPluginLoads() const override;
    void setPluginAutoloadScope(bool enabled) override;
    std::string takeFrozenPluginNotice() override;
    void setAudioWorkgroup(void* workgroup) override;
    void pumpMessageThreadIfNeeded() override;

    bool openTestWindow();
    int32_t pumpMessages();

  private:
    mutable std::mutex catalogMutex_;
    std::shared_ptr<dsp::IPluginCatalog> catalog_;
};

} // namespace nota::pluginhost
