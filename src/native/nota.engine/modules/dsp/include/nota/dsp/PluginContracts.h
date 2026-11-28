// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginContracts — JUCE-free contracts for plugin discovery, catalog storage & hosting.

#pragma once

#include "nota/dsp/Export.h"

#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace nota::dsp {

class Device;
class Instrument;

enum class PluginLoadState : int32_t {
    Loaded = 0,
    WaitingForSlot = 1,
    Loading = 2,
    Unresponsive = 3,
    Failed = 4,
};

enum class PluginLoadAction : int32_t {
    KeepWaiting = 1,
    GiveUp = 2,
    Retry = 3,
    Abandon = 4,
};

enum class PluginFormatKind : int32_t {
    Unknown = 0,
    Vst3 = 1,
    AudioUnit = 2,
    Clap = 3,
    Lv2 = 4,
    Vst2 = 5,
};

struct PluginFormatDescriptor {
    PluginFormatKind kind = PluginFormatKind::Unknown;
    std::string id;          // Canonical backend key: "VST3", "AudioUnit", "CLAP", ...
    std::string displayName; // Human-readable label
    bool supportsInProcessScan = false;
    bool supportsOutOfProcessScan = true;
};

struct PluginDescriptor {
    std::string identifier; // Stable unique identifier (compatible with catalog indexOfId)
    std::string name;
    std::string descriptiveName;
    std::string formatName;
    PluginFormatKind format = PluginFormatKind::Unknown;
    std::string category;
    std::string manufacturerName;
    std::string version;
    std::string fileOrIdentifier;
    std::string serializedXml; // Lossless round-trip payload for legacy plugins.xml compatibility
    int32_t uniqueId = 0;
    int32_t deprecatedUid = 0;
    bool isInstrument = false;
    int32_t numInputChannels = 0;
    int32_t numOutputChannels = 0;
    bool hasSharedContainer = false;
};

struct PluginScanProgress {
    bool scanning = false;
    int32_t done = 0;
    int32_t total = 0;
    std::string currentPlugin;
};

struct PluginLoadsServiced {
    int32_t changed = 0;
    int32_t attached = 0;
};

struct HostedPluginFactory {
    std::function<std::shared_ptr<Instrument>(const std::string& identifier)> createInstrumentById;
    std::function<std::shared_ptr<Device>(const std::string& identifier)> createEffectById;
};

class NOTA_DSP_API IPluginCatalog {
  public:
    virtual ~IPluginCatalog();

    virtual int32_t count() const = 0;
    virtual int32_t indexOfId(const char* identifier) const = 0;
    virtual std::string descLine(int32_t index) const = 0;
    virtual std::string idAt(int32_t index) const = 0;
    virtual bool descriptorAt(int32_t index, PluginDescriptor& out) const = 0;
    virtual bool descriptorOfId(const char* identifier, PluginDescriptor& out) const = 0;

    virtual std::vector<std::string> scanPaths() const = 0;
    virtual void addScanPath(const std::string& dir) = 0;
    virtual bool removeScanPath(int32_t index) = 0;
    virtual int32_t scanPathCount() const = 0;
    virtual std::string scanPathAt(int32_t index) const = 0;
    virtual bool isScanPathRemoved(const std::string& dir) const = 0;
    virtual int32_t restoreDefaultScanPaths(const char* format) = 0;

    virtual int32_t failedCount() const = 0;
    virtual void clearFailed() = 0;
};

class NOTA_DSP_API IPluginScanner {
  public:
    virtual ~IPluginScanner();

    virtual IPluginCatalog& catalog() = 0;
    virtual const IPluginCatalog& catalog() const = 0;

    virtual int32_t scan(bool rescanFailed) = 0;
    virtual int32_t scanDirectory(const std::string& dir) = 0;
    virtual PluginScanProgress progress() const = 0;
    virtual bool isScanning() const = 0;
    virtual void stopScan() = 0;
};

class NOTA_DSP_API IPluginHost {
  public:
    virtual ~IPluginHost();

    [[nodiscard]] virtual std::shared_ptr<Instrument> createInstrument(
        int32_t catalogIndex, double sampleRate, int32_t maxBlock) = 0;
    [[nodiscard]] virtual std::shared_ptr<Device> createEffect(
        int32_t catalogIndex, double sampleRate, int32_t maxBlock) = 0;
    [[nodiscard]] virtual std::shared_ptr<Instrument> createInstrumentById(
        const std::string& identifier, double sampleRate, int32_t maxBlock) = 0;
    [[nodiscard]] virtual std::shared_ptr<Device> createEffectById(
        const std::string& identifier, double sampleRate, int32_t maxBlock) = 0;

    virtual PluginLoadsServiced servicePluginLoads() = 0;
    virtual int32_t pendingPluginLoads() const = 0;
    virtual void setPluginAutoloadScope(bool enabled) = 0;
    virtual std::string takeFrozenPluginNotice() = 0;
    virtual void setAudioWorkgroup(void* workgroup) = 0;
    virtual void pumpMessageThreadIfNeeded() = 0;
};

} // namespace nota::dsp
