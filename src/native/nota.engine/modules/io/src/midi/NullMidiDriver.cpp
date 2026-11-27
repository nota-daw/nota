// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/io/midi/NullMidiDriver.h"
#include "nota/io/midi/MidiTypes.h"
#include "nota/io/midi/IMidiOutput.h"
#include "nota/io/midi/IMidiInput.h"

#include <atomic>
#include <memory>
#include <cstdint>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::midi {
namespace {

class NullMidiInput final : public IMidiInput {
  public:
    bool open(MessageCallback cb, SysexCallback sysex, const std::vector<std::string>&) override {
        cb_ = std::move(cb);
        sysex_ = std::move(sysex);
        uids_.clear();
        ports_.clear();
        return true;
    }
    void close() override {
        cb_ = nullptr;
        sysex_ = nullptr;
        uids_.clear();
        ports_.clear();
    }
    const std::vector<std::string>& sourceUids() const override { return uids_; }
    const std::vector<PortInfo>& ports() const override { return ports_; }

  private:
    MessageCallback cb_;
    SysexCallback sysex_;
    std::vector<std::string> uids_;
    std::vector<PortInfo> ports_;
};

class NullMidiOutput final : public IMidiOutput {
  public:
    bool open(const std::string& uid) override {
        open_ = !uid.empty();
        return open_;
    }
    bool send(const uint8_t* data, int32_t len) override { return open_ && data != nullptr && len > 0; }
    void close() override { open_ = false; }

  private:
    bool open_ = false;
};

} // namespace

std::vector<MidiDeviceInfo> NullMidiDriver::enumerateInputs() const {
    return {};
}

std::string NullMidiDriver::endpointUid(uint32_t) const {
    return {};
}

std::string NullMidiDriver::endpointName(uint32_t) const {
    return {};
}

uint32_t NullMidiDriver::portsGeneration() const {
    static std::atomic<uint32_t> gen{1};
    return gen.load(std::memory_order_relaxed);
}

std::unique_ptr<IMidiInput> NullMidiDriver::createInput() const {
    return std::make_unique<NullMidiInput>();
}

std::unique_ptr<IMidiOutput> NullMidiDriver::createOutput() const {
    return std::make_unique<NullMidiOutput>();
}

} // namespace nota::io::midi
