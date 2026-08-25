// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "CoreAudioInput.h"

#import <AudioToolbox/AudioToolbox.h>
#import <CoreAudio/CoreAudio.h>

#include <vector>

namespace nota {

struct CoreAudioInput::Impl {
    AudioUnit          unit = nullptr;
    UInt32             channels = 2;       // device input channels (client format)
    std::vector<float> scratch;            // device-format frames (channels interleaved)
    std::vector<float> stereo;             // downmixed stereo scratch
    CoreAudioInput*    owner = nullptr;
};

// CoreAudio input thread: pull captured frames, downmix to stereo, hand to owner.
static OSStatus inputProc(void*                       inRefCon,
                          AudioUnitRenderActionFlags* ioActionFlags,
                          const AudioTimeStamp*        inTimeStamp,
                          UInt32                       inBusNumber,
                          UInt32                       inNumberFrames,
                          AudioBufferList*             /*ioData*/) {
    auto* impl = static_cast<CoreAudioInput::Impl*>(inRefCon);
    const UInt32 ch = impl->channels;
    if (inNumberFrames * ch > impl->scratch.size()) return noErr; // guard (no alloc on RT thread)

    AudioBufferList abl;
    abl.mNumberBuffers = 1;
    abl.mBuffers[0].mNumberChannels = ch;
    abl.mBuffers[0].mDataByteSize   = inNumberFrames * ch * sizeof(float);
    abl.mBuffers[0].mData           = impl->scratch.data();

    if (AudioUnitRender(impl->unit, ioActionFlags, inTimeStamp, inBusNumber,
                        inNumberFrames, &abl) != noErr)
        return noErr;

    const float* src = impl->scratch.data();
    float* dst = impl->stereo.data();
    for (UInt32 i = 0; i < inNumberFrames; ++i) {
        const float l = src[i * ch];
        const float r = ch > 1 ? src[i * ch + 1] : l;
        dst[i * 2]     = l;
        dst[i * 2 + 1] = r;
    }
    if (impl->owner->cb_) impl->owner->cb_(dst, static_cast<int32_t>(inNumberFrames));
    return noErr;
}

CoreAudioInput::CoreAudioInput() : impl_(new Impl()) { impl_->owner = this; }

CoreAudioInput::~CoreAudioInput() { stop(); delete impl_; }

bool CoreAudioInput::start(InputCallback cb, uint32_t device) {
    if (running_) return true;
    cb_ = std::move(cb);

    AudioComponentDescription desc{};
    desc.componentType         = kAudioUnitType_Output;
    desc.componentSubType      = kAudioUnitSubType_HALOutput;
    desc.componentManufacturer = kAudioUnitManufacturer_Apple;
    AudioComponent comp = AudioComponentFindNext(nullptr, &desc);
    if (!comp) return false;
    if (AudioComponentInstanceNew(comp, &impl_->unit) != noErr) return false;

    // Enable input (bus 1), disable output (bus 0).
    UInt32 one = 1, zero = 0;
    if (AudioUnitSetProperty(impl_->unit, kAudioOutputUnitProperty_EnableIO,
                             kAudioUnitScope_Input, 1, &one, sizeof(one)) != noErr) { stop(); return false; }
    AudioUnitSetProperty(impl_->unit, kAudioOutputUnitProperty_EnableIO,
                         kAudioUnitScope_Output, 0, &zero, sizeof(zero));

    // Bind to the requested input device, or the system default when unset.
    AudioDeviceID dev = static_cast<AudioDeviceID>(device);
    if (dev == kAudioObjectUnknown) {
        AudioObjectPropertyAddress addr{ kAudioHardwarePropertyDefaultInputDevice,
                                         kAudioObjectPropertyScopeGlobal,
                                         kAudioObjectPropertyElementMain };
        UInt32 sz = sizeof(dev);
        if (AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, nullptr, &sz, &dev) != noErr
            || dev == kAudioObjectUnknown) { stop(); return false; }
    }
    if (AudioUnitSetProperty(impl_->unit, kAudioOutputUnitProperty_CurrentDevice,
                             kAudioUnitScope_Global, 0, &dev, sizeof(dev)) != noErr) { stop(); return false; }

    // Read the device's hardware input format (channels + sample rate).
    AudioStreamBasicDescription hw{};
    UInt32 hwSize = sizeof(hw);
    if (AudioUnitGetProperty(impl_->unit, kAudioUnitProperty_StreamFormat,
                             kAudioUnitScope_Input, 1, &hw, &hwSize) != noErr) { stop(); return false; }
    UInt32 ch = hw.mChannelsPerFrame > 0 ? hw.mChannelsPerFrame : 1;
    sampleRate_ = hw.mSampleRate > 0 ? hw.mSampleRate : 44100.0;
    impl_->channels = ch;

    // Ask the unit to convert to interleaved float, same channel count / rate.
    AudioStreamBasicDescription client{};
    client.mSampleRate       = sampleRate_;
    client.mFormatID         = kAudioFormatLinearPCM;
    client.mFormatFlags      = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
    client.mFramesPerPacket  = 1;
    client.mChannelsPerFrame = ch;
    client.mBitsPerChannel   = 32;
    client.mBytesPerFrame    = ch * sizeof(float);
    client.mBytesPerPacket   = client.mBytesPerFrame;
    if (AudioUnitSetProperty(impl_->unit, kAudioUnitProperty_StreamFormat,
                             kAudioUnitScope_Output, 1, &client, sizeof(client)) != noErr) { stop(); return false; }

    // Preallocate RT scratch (generous cap so the input proc never allocates).
    constexpr UInt32 kMaxFrames = 16384;
    impl_->scratch.assign(kMaxFrames * ch, 0.0f);
    impl_->stereo.assign(kMaxFrames * 2, 0.0f);

    AURenderCallbackStruct cbs{};
    cbs.inputProc       = &inputProc;
    cbs.inputProcRefCon = impl_;
    if (AudioUnitSetProperty(impl_->unit, kAudioOutputUnitProperty_SetInputCallback,
                             kAudioUnitScope_Global, 0, &cbs, sizeof(cbs)) != noErr) { stop(); return false; }

    if (AudioUnitInitialize(impl_->unit) != noErr) { stop(); return false; }
    if (AudioOutputUnitStart(impl_->unit) != noErr) { stop(); return false; }

    running_ = true;
    return true;
}

void CoreAudioInput::stop() {
    if (impl_->unit) {
        AudioOutputUnitStop(impl_->unit);
        AudioUnitUninitialize(impl_->unit);
        AudioComponentInstanceDispose(impl_->unit);
        impl_->unit = nullptr;
    }
    running_ = false;
}

} // namespace nota
