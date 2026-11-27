// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#if defined(__APPLE__)

#include "midi/drivers/MidiDrivers.h"

#import <CoreFoundation/CoreFoundation.h>
#import <CoreMIDI/CoreMIDI.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"

namespace nota::io::midi {
namespace {

std::string cfToString(CFStringRef s) {
    if (!s) return {};
    if (const char* fast = CFStringGetCStringPtr(s, kCFStringEncodingUTF8)) return std::string(fast);
    const CFIndex len = CFStringGetLength(s);
    const CFIndex max = CFStringGetMaximumSizeForEncoding(len, kCFStringEncodingUTF8) + 1;
    std::string out(static_cast<std::size_t>(max), '\0');
    if (CFStringGetCString(s, out.data(), max, kCFStringEncodingUTF8)) {
        out.resize(std::char_traits<char>::length(out.c_str()));
    } else {
        out.clear();
    }
    return out;
}

std::string endpointDisplayName(MIDIEndpointRef ep) {
    CFStringRef name = nullptr;
    if (MIDIObjectGetStringProperty(ep, kMIDIPropertyDisplayName, &name) != noErr || !name) return {};
    std::string out = cfToString(name);
    CFRelease(name);
    return out;
}

std::string endpointUniqueId(MIDIEndpointRef ep) {
    SInt32 uid = 0;
    if (MIDIObjectGetIntegerProperty(static_cast<MIDIObjectRef>(ep), kMIDIPropertyUniqueID, &uid) != noErr) return {};
    return std::to_string(uid);
}

std::string pairedOutputUid(MIDIEndpointRef src) {
    MIDIEntityRef entity = 0;
    if (MIDIEndpointGetEntity(src, &entity) != noErr || !entity) return {};
    if (MIDIEntityGetNumberOfDestinations(entity) == 0) return {};
    const MIDIEndpointRef dst = MIDIEntityGetDestination(entity, 0);
    return dst ? endpointUniqueId(dst) : std::string{};
}

class CoreMidiInput final : public IMidiInput {
  public:
    CoreMidiInput() = default;
    ~CoreMidiInput() override { close(); }

    bool open(MessageCallback cb, SysexCallback sysex, const std::vector<std::string>& disabledUids) override {
        close();
        cb_ = std::move(cb);
        sysex_ = std::move(sysex);
        if (MIDIClientCreate(CFSTR("Nota"), nullptr, nullptr, &client_) != noErr) return false;
        if (MIDIInputPortCreate(client_, CFSTR("Nota In"), &readProc, this, &port_) != noErr) {
            close();
            return false;
        }

        for (auto& p : parsers_) p.reset();
        uids_.clear();
        ports_.clear();
        const ItemCount n = MIDIGetNumberOfSources();
        intptr_t index = 0;
        for (ItemCount i = 0; i < n; ++i) {
            const MIDIEndpointRef src = MIDIGetSource(i);
            if (!src) continue;
            const std::string uid = endpointUniqueId(src);
            if (!uid.empty() && std::find(disabledUids.begin(), disabledUids.end(), uid) != disabledUids.end()) {
                continue;
            }
            const intptr_t source = std::min<intptr_t>(index++, kMaxSources - 1);
            if (source == static_cast<intptr_t>(uids_.size())) uids_.push_back(uid);
            MIDIPortConnectSource(port_, src, reinterpret_cast<void*>(source));
            PortInfo info;
            info.source = static_cast<int32_t>(source);
            info.uid = uid;
            info.name = endpointDisplayName(src);
            if (info.name.empty()) info.name = "MIDI input " + uid;
            info.outputUid = pairedOutputUid(src);
            ports_.push_back(std::move(info));
        }
        return true;
    }

    void close() override {
        if (port_) {
            MIDIPortDispose(port_);
            port_ = 0;
        }
        if (client_) {
            MIDIClientDispose(client_);
            client_ = 0;
        }
        ports_.clear();
    }

    const std::vector<std::string>& sourceUids() const override { return uids_; }
    const std::vector<PortInfo>& ports() const override { return ports_; }

  private:
    static void readProc(const MIDIPacketList* pktlist, void* refCon, void* srcConn) {
        auto* self = static_cast<CoreMidiInput*>(refCon);
        if (!self || !self->cb_) return;
        const int32_t source =
            std::clamp(static_cast<int32_t>(reinterpret_cast<intptr_t>(srcConn)), 0, kMaxSources - 1);
        MidiParser& parser = self->parsers_[static_cast<std::size_t>(source)];
        const MIDIPacket* packet = &pktlist->packet[0];
        for (unsigned p = 0; p < pktlist->numPackets; ++p) {
            parser.feed(
                packet->data, packet->length,
                [&](int status, int channel, int d1, int d2) { self->cb_(source, status, channel, d1, d2); },
                [&](const uint8_t* data, int32_t len) {
                    if (self->sysex_) self->sysex_(source, data, len);
                });
            packet = MIDIPacketNext(packet);
        }
    }

    MIDIClientRef client_ = 0;
    MIDIPortRef port_ = 0;
    MessageCallback cb_;
    SysexCallback sysex_;
    std::array<MidiParser, kMaxSources> parsers_{};
    std::vector<std::string> uids_;
    std::vector<PortInfo> ports_;
};

struct SysexJob {
    MIDISysexSendRequest req{};
    std::vector<uint8_t> bytes;
};

void sysexDone(MIDISysexSendRequest* req) {
    delete static_cast<SysexJob*>(req->completionRefCon);
}

class CoreMidiOutput final : public IMidiOutput {
  public:
    CoreMidiOutput() = default;
    ~CoreMidiOutput() override { close(); }

    bool open(const std::string& uid) override {
        close();
        for (ItemCount i = 0, n = MIDIGetNumberOfDestinations(); i < n; ++i) {
            const MIDIEndpointRef d = MIDIGetDestination(i);
            if (d && endpointUniqueId(d) == uid) {
                dest_ = d;
                break;
            }
        }
        if (!dest_) return false;
        if (MIDIClientCreate(CFSTR("Nota Out"), nullptr, nullptr, &client_) != noErr) {
            close();
            return false;
        }
        if (MIDIOutputPortCreate(client_, CFSTR("Nota Out"), &port_) != noErr) {
            close();
            return false;
        }
        return true;
    }

    bool send(const uint8_t* data, int32_t len) override {
        if (!port_ || !dest_ || !data || len <= 0) return false;
        int32_t i = 0;
        while (i < len) {
            if (data[i] == 0xF0) {
                int32_t j = i;
                while (j < len && data[j] != 0xF7) ++j;
                if (j >= len) return false;
                auto* job = new SysexJob();
                job->bytes.assign(data + i, data + j + 1);
                job->req.destination = dest_;
                job->req.data = job->bytes.data();
                job->req.bytesToSend = static_cast<UInt32>(job->bytes.size());
                job->req.completionProc = sysexDone;
                job->req.completionRefCon = job;
                if (MIDISendSysex(&job->req) != noErr) {
                    delete job;
                    return false;
                }
                i = j + 1;
                continue;
            }
            int32_t j = i;
            while (j < len && data[j] != 0xF0) ++j;
            Byte buffer[1024]{};
            auto* list = reinterpret_cast<MIDIPacketList*>(buffer);
            MIDIPacket* pkt = MIDIPacketListInit(list);
            pkt = MIDIPacketListAdd(list, sizeof(buffer), pkt, 0, static_cast<ByteCount>(j - i), data + i);
            if (!pkt || MIDISend(port_, dest_, list) != noErr) return false;
            i = j;
        }
        return true;
    }

    void close() override {
        if (port_) {
            MIDIPortDispose(port_);
            port_ = 0;
        }
        if (client_) {
            MIDIClientDispose(client_);
            client_ = 0;
        }
        dest_ = 0;
    }

  private:
    MIDIClientRef client_ = 0;
    MIDIPortRef port_ = 0;
    MIDIEndpointRef dest_ = 0;
};

class CoreMidiDriver final : public IMidiDriver {
  public:
    std::string_view id() const noexcept override { return "coremidi"; }
    std::string_view displayName() const noexcept override { return "CoreMIDI"; }

    std::vector<MidiDeviceInfo> enumerateInputs() const override {
        std::vector<MidiDeviceInfo> out;
        const ItemCount n = MIDIGetNumberOfSources();
        for (ItemCount i = 0; i < n; ++i) {
            const MIDIEndpointRef src = MIDIGetSource(i);
            if (!src) continue;
            MidiDeviceInfo info;
            info.uid = endpointUniqueId(src);
            info.name = endpointDisplayName(src);
            if (info.uid.empty()) continue;
            if (info.name.empty()) info.name = "MIDI input " + info.uid;
            out.push_back(std::move(info));
        }
        return out;
    }

    std::string endpointUid(uint32_t endpoint) const override {
        return endpointUniqueId(static_cast<MIDIEndpointRef>(endpoint));
    }

    std::string endpointName(uint32_t endpoint) const override {
        return endpointDisplayName(static_cast<MIDIEndpointRef>(endpoint));
    }

    uint32_t portsGeneration() const override {
        static std::mutex mx;
        static std::string last;
        static uint32_t gen = 0;
        std::lock_guard<std::mutex> lock(mx);
        std::string sig;
        for (ItemCount i = 0, n = MIDIGetNumberOfSources(); i < n; ++i) sig += endpointUniqueId(MIDIGetSource(i)) + ",";
        sig += "|";
        for (ItemCount i = 0, n = MIDIGetNumberOfDestinations(); i < n; ++i) {
            sig += endpointUniqueId(MIDIGetDestination(i)) + ",";
        }
        if (sig != last) {
            last = std::move(sig);
            ++gen;
        }
        return gen;
    }

    std::unique_ptr<IMidiInput> createInput() const override { return std::make_unique<CoreMidiInput>(); }
    std::unique_ptr<IMidiOutput> createOutput() const override { return std::make_unique<CoreMidiOutput>(); }
};

} // namespace

namespace detail {

std::shared_ptr<const IMidiDriver> createCoreMidiDriver() {
    return std::make_shared<CoreMidiDriver>();
}

} // namespace detail
} // namespace nota::io::midi

#pragma clang diagnostic pop

#endif
