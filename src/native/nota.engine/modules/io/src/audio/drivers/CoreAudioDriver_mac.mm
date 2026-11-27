// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#if defined(__APPLE__)

#include "audio/drivers/AudioDrivers.h"

#include "audio/drivers/CoreAudioDevice_mac.h"

#import <AudioToolbox/AudioToolbox.h>
#import <CoreAudio/CoreAudio.h>
#import <CoreFoundation/CoreFoundation.h>
#include <os/workgroup.h>

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <string>
#include <utility>
#include <vector>

namespace nota::io::audio {
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

UInt32 deviceChannels(AudioDeviceID dev, bool inputScope) {
    const AudioObjectPropertyAddress addr{kAudioDevicePropertyStreamConfiguration,
        inputScope ? kAudioDevicePropertyScopeInput : kAudioDevicePropertyScopeOutput, kAudioObjectPropertyElementMain};
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(dev, &addr, 0, nullptr, &size) != noErr || size == 0) return 0;
    std::vector<uint8_t> storage(size);
    auto* abl = reinterpret_cast<AudioBufferList*>(storage.data());
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, abl) != noErr) return 0;
    UInt32 channels = 0;
    for (UInt32 i = 0; i < abl->mNumberBuffers; ++i) channels += abl->mBuffers[i].mNumberChannels;
    return channels;
}

std::string deviceUidString(AudioDeviceID dev) {
    const AudioObjectPropertyAddress addr{
        kAudioDevicePropertyDeviceUID, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    CFStringRef uid = nullptr;
    UInt32 size = sizeof(uid);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &uid) != noErr || !uid) return {};
    std::string out = cfToString(uid);
    CFRelease(uid);
    return out;
}

std::string deviceNameString(AudioDeviceID dev) {
    const AudioObjectPropertyAddress addr{
        kAudioObjectPropertyName, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    CFStringRef name = nullptr;
    UInt32 size = sizeof(name);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &name) != noErr || !name) return {};
    std::string out = cfToString(name);
    CFRelease(name);
    return out;
}

AudioDeviceID defaultDeviceId(bool inputScope) {
    const AudioObjectPropertyAddress addr{
        inputScope ? kAudioHardwarePropertyDefaultInputDevice : kAudioHardwarePropertyDefaultOutputDevice,
        kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
    AudioDeviceID dev = kAudioObjectUnknown;
    UInt32 size = sizeof(dev);
    AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, nullptr, &size, &dev);
    return dev;
}

const AudioObjectPropertyAddress kOverloadAddr{
    kAudioDeviceProcessorOverload, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
const AudioObjectPropertyAddress kAliveAddr{
    kAudioDevicePropertyDeviceIsAlive, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};

class CoreAudioOutputStream final : public IAudioOutputStream {
  public:
    CoreAudioOutputStream() = default;
    ~CoreAudioOutputStream() override { stop(); }

    bool start(RenderCallback render, const OutputStreamConfig& cfg) override {
        if (running_) return true;
        render_ = std::move(render);

        AudioComponentDescription desc{};
        desc.componentType = kAudioUnitType_Output;
        desc.componentSubType = kAudioUnitSubType_HALOutput;
        desc.componentManufacturer = kAudioUnitManufacturer_Apple;

        AudioComponent comp = AudioComponentFindNext(nullptr, &desc);
        if (!comp) return false;
        if (AudioComponentInstanceNew(comp, &outputUnit_) != noErr) return false;

        UInt32 one = 1;
        UInt32 zero = 0;
        if (AudioUnitSetProperty(outputUnit_, kAudioOutputUnitProperty_EnableIO, kAudioUnitScope_Output, 0, &one,
                sizeof(one)) != noErr) {
            stop();
            return false;
        }
        AudioUnitSetProperty(
            outputUnit_, kAudioOutputUnitProperty_EnableIO, kAudioUnitScope_Input, 1, &zero, sizeof(zero));

        const AudioDeviceID dev = cfg.device != 0 ? static_cast<AudioDeviceID>(cfg.device) : defaultDeviceId(false);
        if (dev != kAudioObjectUnknown) {
            AudioDeviceID bindDev = dev;
            if (AudioUnitSetProperty(outputUnit_, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0,
                    &bindDev, sizeof(bindDev)) != noErr) {
                stop();
                return false;
            }
            device_ = dev;
            AudioObjectAddPropertyListener(dev, &kOverloadAddr, &overloadProc, this);
            deviceDied_.store(false, std::memory_order_relaxed);
            AudioObjectAddPropertyListener(dev, &kAliveAddr, &aliveProc, this);
        }

        if (cfg.sampleRate > 0.0 && dev != kAudioObjectUnknown &&
            coreaudio::deviceNominalSampleRate(dev) != cfg.sampleRate && !coreaudio::isBluetoothDevice(dev) &&
            coreaudio::deviceSupportsSampleRate(dev, cfg.sampleRate)) {
            Float64 sr = cfg.sampleRate;
            const AudioObjectPropertyAddress addr{kAudioDevicePropertyNominalSampleRate,
                kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
            AudioObjectSetPropertyData(dev, &addr, 0, nullptr, sizeof(sr), &sr);
        }
        if (cfg.bufferFrames > 0 && dev != kAudioObjectUnknown) {
            UInt32 bf = static_cast<UInt32>(cfg.bufferFrames);
            const AudioObjectPropertyAddress addr{
                kAudioDevicePropertyBufferFrameSize, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
            AudioObjectSetPropertyData(dev, &addr, 0, nullptr, sizeof(bf), &bf);
        }

        channels_ = 2;
        {
            AudioStreamBasicDescription hw{};
            UInt32 hsz = sizeof(hw);
            if (AudioUnitGetProperty(
                    outputUnit_, kAudioUnitProperty_StreamFormat, kAudioUnitScope_Output, 0, &hw, &hsz) == noErr &&
                hw.mChannelsPerFrame > 0) {
                channels_ = std::min<int32_t>(static_cast<int32_t>(hw.mChannelsPerFrame), kMaxOutputChannels);
            }
        }
        names_.assign(static_cast<std::size_t>(channels_), std::string{});
        for (int32_t c = 0; c < channels_ && dev != kAudioObjectUnknown; ++c) {
            const AudioObjectPropertyAddress na{
                kAudioObjectPropertyElementName, kAudioDevicePropertyScopeOutput, static_cast<UInt32>(c + 1)};
            CFStringRef name = nullptr;
            UInt32 nsz = sizeof(name);
            if (AudioObjectHasProperty(dev, &na) &&
                AudioObjectGetPropertyData(dev, &na, 0, nullptr, &nsz, &name) == noErr && name) {
                char buf[256] = {};
                if (CFStringGetCString(name, buf, sizeof(buf), kCFStringEncodingUTF8)) {
                    names_[static_cast<std::size_t>(c)] = buf;
                }
                CFRelease(name);
            }
        }

        double clientSr = cfg.sampleRate > 0.0 ? cfg.sampleRate : coreaudio::deviceNominalSampleRate(dev);
        if (!(clientSr > 0.0)) clientSr = 44100.0;

        AudioStreamBasicDescription fmt{};
        fmt.mSampleRate = clientSr;
        fmt.mFormatID = kAudioFormatLinearPCM;
        fmt.mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
        fmt.mFramesPerPacket = 1;
        fmt.mChannelsPerFrame = static_cast<UInt32>(channels_);
        fmt.mBitsPerChannel = 32;
        fmt.mBytesPerFrame = fmt.mChannelsPerFrame * sizeof(float);
        fmt.mBytesPerPacket = fmt.mBytesPerFrame;

        if (AudioUnitSetProperty(
                outputUnit_, kAudioUnitProperty_StreamFormat, kAudioUnitScope_Input, 0, &fmt, sizeof(fmt)) != noErr) {
            stop();
            return false;
        }

        AURenderCallbackStruct cb{};
        cb.inputProc = &renderCallback;
        cb.inputProcRefCon = this;
        if (AudioUnitSetProperty(outputUnit_, kAudioUnitProperty_SetRenderCallback, kAudioUnitScope_Input, 0, &cb,
                sizeof(cb)) != noErr) {
            stop();
            return false;
        }

        if (AudioUnitInitialize(outputUnit_) != noErr) {
            stop();
            return false;
        }

        AudioStreamBasicDescription actual{};
        UInt32 size = sizeof(actual);
        if (AudioUnitGetProperty(
                outputUnit_, kAudioUnitProperty_StreamFormat, kAudioUnitScope_Input, 0, &actual, &size) == noErr) {
            sampleRate_ = actual.mSampleRate;
        } else {
            sampleRate_ = clientSr;
        }

        bufferFrames_ = 0;
        if (dev != kAudioObjectUnknown) {
            UInt32 bf = 0;
            UInt32 bsz = sizeof(bf);
            const AudioObjectPropertyAddress addr{
                kAudioDevicePropertyBufferFrameSize, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
            if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &bsz, &bf) == noErr) {
                bufferFrames_ = static_cast<int32_t>(bf);
            }
        }

        if (AudioOutputUnitStart(outputUnit_) != noErr) {
            stop();
            return false;
        }

        if (@available(macOS 11.0, *)) {
            if (dev != kAudioObjectUnknown) {
                const AudioObjectPropertyAddress addr{kAudioDevicePropertyIOThreadOSWorkgroup,
                    kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
                os_workgroup_t wg = nullptr;
                UInt32 sz = sizeof(wg);
                if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &sz, &wg) == noErr && wg) workgroup_ = wg;
            }
        }

        running_ = true;
        return true;
    }

    void stop() override {
        if (device_ != kAudioObjectUnknown) {
            AudioObjectRemovePropertyListener(device_, &kOverloadAddr, &overloadProc, this);
            AudioObjectRemovePropertyListener(device_, &kAliveAddr, &aliveProc, this);
            device_ = kAudioObjectUnknown;
        }
        if (outputUnit_) {
            AudioOutputUnitStop(outputUnit_);
            AudioUnitUninitialize(outputUnit_);
            AudioComponentInstanceDispose(outputUnit_);
            outputUnit_ = nullptr;
        }
        if (workgroup_) {
            if (@available(macOS 11.0, *)) os_release(static_cast<os_workgroup_t>(workgroup_));
            workgroup_ = nullptr;
        }
        running_ = false;
    }

    double sampleRate() const override { return sampleRate_; }
    int32_t bufferFrames() const override { return bufferFrames_; }
    bool isRunning() const override { return running_; }
    void setXrunCallback(XrunCallback cb) override { onXrun_ = std::move(cb); }
    void* ioWorkgroup() const override { return workgroup_; }
    int32_t outputChannelCount() const override { return running_ ? channels_ : 0; }
    std::string outputChannelName(int32_t index) const override {
        if (index < 0 || index >= static_cast<int32_t>(names_.size())) return {};
        return names_[static_cast<std::size_t>(index)];
    }
    void setOutputRouting(const OutputRouting& r) override { stage_.setRouting(r); }
    int32_t pollDeviceEvent() override {
        return deviceDied_.exchange(false, std::memory_order_relaxed)
                   ? static_cast<int32_t>(AudioDeviceEvent::ResetRequested)
                   : static_cast<int32_t>(AudioDeviceEvent::None);
    }

  private:
    static OSStatus overloadProc(AudioObjectID, UInt32, const AudioObjectPropertyAddress*, void* refCon) {
        auto* self = static_cast<CoreAudioOutputStream*>(refCon);
        if (self && self->onXrun_) self->onXrun_();
        return noErr;
    }

    static OSStatus aliveProc(AudioObjectID dev, UInt32, const AudioObjectPropertyAddress*, void* refCon) {
        auto* self = static_cast<CoreAudioOutputStream*>(refCon);
        UInt32 alive = 1;
        UInt32 size = sizeof(alive);
        if (self && AudioObjectGetPropertyData(dev, &kAliveAddr, 0, nullptr, &size, &alive) == noErr && !alive) {
            self->deviceDied_.store(true, std::memory_order_relaxed);
        }
        return noErr;
    }

    static OSStatus renderCallback(void* inRefCon, AudioUnitRenderActionFlags*, const AudioTimeStamp*, UInt32,
        UInt32 inNumberFrames, AudioBufferList* ioData) {
        auto* self = static_cast<CoreAudioOutputStream*>(inRefCon);
        float* out = static_cast<float*>(ioData->mBuffers[0].mData);
        if (self && self->render_) {
            self->stage_.process(self->render_, out, self->channels_, static_cast<int32_t>(inNumberFrames));
        } else {
            const UInt32 count = inNumberFrames * static_cast<UInt32>(self ? self->channels_ : 2);
            for (UInt32 i = 0; i < count; ++i) out[i] = 0.0f;
        }
        return noErr;
    }

    AudioUnit outputUnit_ = nullptr;
    RenderCallback render_;
    XrunCallback onXrun_;
    OutputStage stage_;
    int32_t channels_ = 2;
    std::vector<std::string> names_;
    std::atomic<bool> deviceDied_{false};
    AudioDeviceID device_ = kAudioObjectUnknown;
    void* workgroup_ = nullptr;
    double sampleRate_ = 0.0;
    int32_t bufferFrames_ = 0;
    bool running_ = false;
};

class CoreAudioInputStream final : public IAudioInputStream {
  public:
    CoreAudioInputStream() = default;
    ~CoreAudioInputStream() override { stop(); }

    bool start(InputCallback cb, uint32_t device, double preferredRate) override {
        if (running_) return true;
        cb_ = std::move(cb);

        AudioComponentDescription desc{};
        desc.componentType = kAudioUnitType_Output;
        desc.componentSubType = kAudioUnitSubType_HALOutput;
        desc.componentManufacturer = kAudioUnitManufacturer_Apple;
        AudioComponent comp = AudioComponentFindNext(nullptr, &desc);
        if (!comp) return false;
        if (AudioComponentInstanceNew(comp, &unit_) != noErr) return false;

        UInt32 one = 1;
        UInt32 zero = 0;
        if (AudioUnitSetProperty(
                unit_, kAudioOutputUnitProperty_EnableIO, kAudioUnitScope_Input, 1, &one, sizeof(one)) != noErr) {
            stop();
            return false;
        }
        AudioUnitSetProperty(unit_, kAudioOutputUnitProperty_EnableIO, kAudioUnitScope_Output, 0, &zero, sizeof(zero));

        AudioDeviceID dev = static_cast<AudioDeviceID>(device);
        if (dev == kAudioObjectUnknown) {
            const AudioObjectPropertyAddress addr{kAudioHardwarePropertyDefaultInputDevice,
                kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
            UInt32 sz = sizeof(dev);
            if (AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, nullptr, &sz, &dev) != noErr ||
                dev == kAudioObjectUnknown) {
                stop();
                return false;
            }
        }
        if (preferredRate > 0.0) coreaudio::setDeviceNominalSampleRate(dev, preferredRate);

        if (AudioUnitSetProperty(
                unit_, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &dev, sizeof(dev)) != noErr) {
            stop();
            return false;
        }

        AudioStreamBasicDescription hw{};
        UInt32 hwSize = sizeof(hw);
        if (AudioUnitGetProperty(unit_, kAudioUnitProperty_StreamFormat, kAudioUnitScope_Input, 1, &hw, &hwSize) !=
            noErr) {
            stop();
            return false;
        }
        const UInt32 ch = hw.mChannelsPerFrame > 0 ? hw.mChannelsPerFrame : 1;
        sampleRate_ = hw.mSampleRate > 0.0 ? hw.mSampleRate : 44100.0;
        channels_ = ch;

        AudioStreamBasicDescription client{};
        client.mSampleRate = sampleRate_;
        client.mFormatID = kAudioFormatLinearPCM;
        client.mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
        client.mFramesPerPacket = 1;
        client.mChannelsPerFrame = ch;
        client.mBitsPerChannel = 32;
        client.mBytesPerFrame = ch * sizeof(float);
        client.mBytesPerPacket = client.mBytesPerFrame;
        if (AudioUnitSetProperty(
                unit_, kAudioUnitProperty_StreamFormat, kAudioUnitScope_Output, 1, &client, sizeof(client)) != noErr) {
            stop();
            return false;
        }

        constexpr UInt32 kMaxFrames = 16384;
        scratch_.assign(static_cast<std::size_t>(kMaxFrames) * ch, 0.0f);

        names_.assign(ch, std::string{});
        for (UInt32 c = 0; c < ch; ++c) {
            const AudioObjectPropertyAddress na{kAudioObjectPropertyElementName, kAudioDevicePropertyScopeInput, c + 1};
            CFStringRef name = nullptr;
            UInt32 nsz = sizeof(name);
            if (AudioObjectHasProperty(dev, &na) &&
                AudioObjectGetPropertyData(dev, &na, 0, nullptr, &nsz, &name) == noErr && name) {
                char buf[256] = {};
                if (CFStringGetCString(name, buf, sizeof(buf), kCFStringEncodingUTF8)) names_[c] = buf;
                CFRelease(name);
            }
        }

        AURenderCallbackStruct cbs{};
        cbs.inputProc = &inputProc;
        cbs.inputProcRefCon = this;
        if (AudioUnitSetProperty(unit_, kAudioOutputUnitProperty_SetInputCallback, kAudioUnitScope_Global, 0, &cbs,
                sizeof(cbs)) != noErr) {
            stop();
            return false;
        }

        if (AudioUnitInitialize(unit_) != noErr || AudioOutputUnitStart(unit_) != noErr) {
            stop();
            return false;
        }

        running_ = true;
        return true;
    }

    void stop() override {
        if (unit_) {
            AudioOutputUnitStop(unit_);
            AudioUnitUninitialize(unit_);
            AudioComponentInstanceDispose(unit_);
            unit_ = nullptr;
        }
        running_ = false;
    }

    bool isRunning() const override { return running_; }
    double sampleRate() const override { return sampleRate_; }
    int32_t channelCount() const override {
        if (!running_) return 0;
        return static_cast<int32_t>(std::min<UInt32>(channels_, static_cast<UInt32>(kMaxInputChannels)));
    }
    std::string channelName(int32_t index) const override {
        if (index < 0 || index >= static_cast<int32_t>(names_.size())) return {};
        return names_[static_cast<std::size_t>(index)];
    }

  private:
    static OSStatus inputProc(void* inRefCon, AudioUnitRenderActionFlags* ioActionFlags,
        const AudioTimeStamp* inTimeStamp, UInt32 inBusNumber, UInt32 inNumberFrames, AudioBufferList*) {
        auto* self = static_cast<CoreAudioInputStream*>(inRefCon);
        const UInt32 ch = self->channels_;
        if (inNumberFrames * ch > self->scratch_.size()) return noErr;

        AudioBufferList abl;
        abl.mNumberBuffers = 1;
        abl.mBuffers[0].mNumberChannels = ch;
        abl.mBuffers[0].mDataByteSize = inNumberFrames * ch * sizeof(float);
        abl.mBuffers[0].mData = self->scratch_.data();

        if (AudioUnitRender(self->unit_, ioActionFlags, inTimeStamp, inBusNumber, inNumberFrames, &abl) != noErr) {
            return noErr;
        }
        if (self->cb_) {
            self->cb_(self->scratch_.data(), static_cast<int32_t>(inNumberFrames), static_cast<int32_t>(ch));
        }
        return noErr;
    }

    AudioUnit unit_ = nullptr;
    UInt32 channels_ = 2;
    std::vector<float> scratch_;
    std::vector<std::string> names_;
    InputCallback cb_;
    double sampleRate_ = 0.0;
    bool running_ = false;
};

class CoreAudioDriver final : public IAudioDriver {
  public:
    std::string_view id() const noexcept override { return "coreaudio"; }
    std::string_view displayName() const noexcept override { return "CoreAudio"; }
    DriverCapabilities capabilities() const noexcept override {
        return DriverCapabilities{true, true, false, false, false};
    }

    std::vector<AudioDeviceInfo> enumerateDevices(bool inputScope) const override {
        std::vector<AudioDeviceInfo> out;
        const AudioObjectPropertyAddress addr{
            kAudioHardwarePropertyDevices, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
        UInt32 size = 0;
        if (AudioObjectGetPropertyDataSize(kAudioObjectSystemObject, &addr, 0, nullptr, &size) != noErr || size == 0) {
            return out;
        }
        std::vector<AudioDeviceID> ids(size / sizeof(AudioDeviceID));
        if (AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, nullptr, &size, ids.data()) != noErr) {
            return out;
        }
        for (AudioDeviceID dev : ids) {
            if (deviceChannels(dev, inputScope) == 0) continue;
            AudioDeviceInfo info;
            info.uid = deviceUidString(dev);
            info.name = deviceNameString(dev);
            if (info.uid.empty()) continue;
            if (info.name.empty()) info.name = info.uid;
            out.push_back(std::move(info));
        }
        return out;
    }

    uint32_t resolveDeviceId(std::string_view uid, bool inputScope) const override {
        if (!uid.empty()) {
            const std::string uidStr(uid);
            CFStringRef cf = CFStringCreateWithCString(kCFAllocatorDefault, uidStr.c_str(), kCFStringEncodingUTF8);
            if (cf) {
                const AudioObjectPropertyAddress addr{kAudioHardwarePropertyTranslateUIDToDevice,
                    kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
                AudioDeviceID dev = kAudioObjectUnknown;
                UInt32 size = sizeof(dev);
                const OSStatus st =
                    AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, sizeof(cf), &cf, &size, &dev);
                CFRelease(cf);
                if (st == noErr && dev != kAudioObjectUnknown) return dev;
            }
        }
        return defaultDeviceId(inputScope);
    }

    std::string effectiveDeviceUid(std::string_view uid, bool inputScope) const override {
        const AudioDeviceID dev = static_cast<AudioDeviceID>(resolveDeviceId(uid, inputScope));
        return dev == kAudioObjectUnknown ? std::string{} : deviceUidString(dev);
    }

    std::unique_ptr<IAudioOutputStream> createOutputStream() const override {
        return std::make_unique<CoreAudioOutputStream>();
    }

    std::unique_ptr<IAudioInputStream> createInputStream() const override {
        return std::make_unique<CoreAudioInputStream>();
    }
};

} // namespace

namespace detail {

std::shared_ptr<const IAudioDriver> createCoreAudioDriver() {
    return std::make_shared<CoreAudioDriver>();
}

} // namespace detail
} // namespace nota::io::audio

#endif
