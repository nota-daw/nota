// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#if defined(_WIN32) || defined(__linux__)

#include "midi/drivers/MidiDrivers.h"

#include <RtMidi.h>

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::midi {
namespace {

struct PortTag {
    IMidiInput::MessageCallback* cb = nullptr;
    IMidiInput::SysexCallback* sysex = nullptr;
    int32_t source = 0;
};

void onRtMidiMessage(double, std::vector<unsigned char>* message, void* userData) {
    auto* tag = static_cast<PortTag*>(userData);
    if (!tag || !tag->cb || !*tag->cb || !message || message->empty()) return;
    const int status = (*message)[0];
    if (status == 0xF0) {
        const auto len = static_cast<int32_t>(message->size());
        if (tag->sysex && *tag->sysex && len <= MidiParser::kMaxSysex && message->back() == 0xF7) {
            (*tag->sysex)(tag->source, message->data(), len);
        }
        return;
    }
    const int n = IMidiInput::dataBytes(status);
    if (n == 0 || static_cast<int>(message->size()) < 1 + n) return;
    (*tag->cb)(tag->source, status & 0xF0, status & 0x0F, (*message)[1] & 0x7F, n > 1 ? (*message)[2] & 0x7F : 0);
}

class RtMidiInputImpl final : public IMidiInput {
  public:
    RtMidiInputImpl() = default;
    ~RtMidiInputImpl() override { close(); }

    bool open(MessageCallback cb, SysexCallback sysex, const std::vector<std::string>& disabledUids) override {
        close();
        cb_ = std::move(cb);
        sysex_ = std::move(sysex);
        try {
            RtMidiIn scanner(RtMidi::UNSPECIFIED, "Nota-scan");
            std::vector<std::string> outputs;
            try {
                RtMidiOut outScanner(RtMidi::UNSPECIFIED, "Nota-scan");
                for (unsigned int o = 0, m = outScanner.getPortCount(); o < m; ++o) {
                    outputs.push_back(outScanner.getPortName(o));
                }
            } catch (...) {}
            const unsigned int n = scanner.getPortCount();
            for (unsigned int i = 0; i < n; ++i) {
                const std::string uid = scanner.getPortName(i);
                if (!uid.empty() && std::find(disabledUids.begin(), disabledUids.end(), uid) != disabledUids.end()) {
                    continue;
                }
                auto port = std::make_unique<RtMidiIn>(RtMidi::UNSPECIFIED, "Nota In");
                port->openPort(i, "Nota In");
                port->ignoreTypes(false, true, true);
                auto tag = std::make_unique<PortTag>();
                tag->cb = &cb_;
                tag->sysex = &sysex_;
                tag->source = std::min<int32_t>(static_cast<int32_t>(ports_.size()), kMaxSources - 1);
                if (static_cast<int32_t>(uids_.size()) < kMaxSources) uids_.push_back(uid);
                PortInfo info;
                info.source = tag->source;
                info.uid = uid;
                info.name = uid;
                if (std::find(outputs.begin(), outputs.end(), uid) != outputs.end()) info.outputUid = uid;
                info_.push_back(std::move(info));
                port->setCallback(&onRtMidiMessage, tag.get());
                tags_.push_back(std::move(tag));
                ports_.push_back(std::move(port));
            }
        } catch (...) {}
        return true;
    }

    void close() override {
        ports_.clear();
        tags_.clear();
        info_.clear();
        uids_.clear();
    }

    const std::vector<std::string>& sourceUids() const override { return uids_; }
    const std::vector<PortInfo>& ports() const override { return info_; }

  private:
    MessageCallback cb_;
    SysexCallback sysex_;
    std::vector<std::unique_ptr<RtMidiIn>> ports_;
    std::vector<std::unique_ptr<PortTag>> tags_;
    std::vector<PortInfo> info_;
    std::vector<std::string> uids_;
};

class RtMidiOutputImpl final : public IMidiOutput {
  public:
    RtMidiOutputImpl() = default;
    ~RtMidiOutputImpl() override { close(); }

    bool open(const std::string& uid) override {
        close();
        try {
            auto out = std::make_unique<RtMidiOut>(RtMidi::UNSPECIFIED, "Nota Out");
            for (unsigned int i = 0, n = out->getPortCount(); i < n; ++i) {
                if (out->getPortName(i) == uid) {
                    out->openPort(i, "Nota Out");
                    out_ = std::move(out);
                    return true;
                }
            }
        } catch (...) {}
        return false;
    }

    bool send(const uint8_t* data, int32_t len) override {
        if (!out_ || !data || len <= 0) return false;
        try {
            int32_t i = 0;
            while (i < len) {
                int32_t j = i + 1;
                if (data[i] == 0xF0) {
                    while (j < len && data[j - 1] != 0xF7) ++j;
                    if (data[j - 1] != 0xF7) return false;
                } else {
                    if (!(data[i] & 0x80)) return false;
                    j = i + 1 + MidiParser::dataBytes(data[i]);
                    if (j > len) return false;
                }
                std::vector<unsigned char> msg(data + i, data + j);
                out_->sendMessage(&msg);
                i = j;
            }
        } catch (...) { return false; }
        return true;
    }

    void close() override { out_.reset(); }

  private:
    std::unique_ptr<RtMidiOut> out_;
};

class RtMidiDriver final : public IMidiDriver {
  public:
    std::string_view id() const noexcept override { return "rtmidi"; }
    std::string_view displayName() const noexcept override {
#if defined(_WIN32)
        return "WinMM MIDI";
#else
        return "ALSA MIDI";
#endif
    }

    std::vector<MidiDeviceInfo> enumerateInputs() const override {
        std::vector<MidiDeviceInfo> out;
        try {
            RtMidiIn in(RtMidi::UNSPECIFIED, "Nota-scan");
            const unsigned int n = in.getPortCount();
            for (unsigned int i = 0; i < n; ++i) {
                MidiDeviceInfo info;
                info.name = in.getPortName(i);
                info.uid = info.name;
                if (info.uid.empty()) continue;
                out.push_back(std::move(info));
            }
        } catch (...) {}
        return out;
    }

    std::string endpointUid(uint32_t endpoint) const override {
        try {
            RtMidiIn in(RtMidi::UNSPECIFIED, "Nota-scan");
            if (endpoint < in.getPortCount()) return in.getPortName(endpoint);
        } catch (...) {}
        return {};
    }

    std::string endpointName(uint32_t endpoint) const override { return endpointUid(endpoint); }

    uint32_t portsGeneration() const override {
        static std::mutex mx;
        static std::string last;
        static uint32_t gen = 0;
        static std::chrono::steady_clock::time_point lastLook{};
        std::lock_guard<std::mutex> lock(mx);
        const auto now = std::chrono::steady_clock::now();
        if (gen != 0 && now - lastLook < std::chrono::seconds(1)) return gen;
        lastLook = now;
        std::string sig;
        try {
            RtMidiIn in(RtMidi::UNSPECIFIED, "Nota-scan");
            for (unsigned int i = 0, n = in.getPortCount(); i < n; ++i) sig += in.getPortName(i) + ",";
            sig += "|";
            RtMidiOut out(RtMidi::UNSPECIFIED, "Nota-scan");
            for (unsigned int i = 0, n = out.getPortCount(); i < n; ++i) sig += out.getPortName(i) + ",";
        } catch (...) { return gen; }
        if (gen == 0 || sig != last) {
            last = std::move(sig);
            ++gen;
        }
        return gen;
    }

    std::unique_ptr<IMidiInput> createInput() const override { return std::make_unique<RtMidiInputImpl>(); }
    std::unique_ptr<IMidiOutput> createOutput() const override { return std::make_unique<RtMidiOutputImpl>(); }
};

} // namespace

namespace detail {

std::shared_ptr<const IMidiDriver> createRtMidiDriver() {
    return std::make_shared<RtMidiDriver>();
}

} // namespace detail
} // namespace nota::io::midi

#endif
