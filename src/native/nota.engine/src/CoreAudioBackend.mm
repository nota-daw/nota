// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "CoreAudioBackend.h"

#import <AudioToolbox/AudioToolbox.h>
#import <CoreAudio/CoreAudio.h>

namespace nota {

struct CoreAudioBackend::Impl {
    AudioUnit       outputUnit = nullptr;
    RenderCallback  render;              // copied from the backend on start()
    AudioDeviceID   device = kAudioObjectUnknown; // for the overload listener (M7-8)
};

// CoreAudio fires this (off the RT thread) when the device drops a buffer /
// overloads. We only bump the engine's counter — never block or allocate.
static const AudioObjectPropertyAddress kOverloadAddr{
    kAudioDeviceProcessorOverload, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain };

static OSStatus overloadProc(AudioObjectID, UInt32, const AudioObjectPropertyAddress*, void* refCon) {
    auto* self = static_cast<CoreAudioBackend*>(refCon);
    if (self && self->onXrun_) self->onXrun_();
    return noErr;
}

// Real-time audio callback. Runs on the CoreAudio thread — no allocation,
// no locks, no I/O (AR-6). We set up an interleaved stereo float stream, so
// ioData has a single buffer of frames*2.
static OSStatus renderCallback(void*                       inRefCon,
                               AudioUnitRenderActionFlags* /*ioActionFlags*/,
                               const AudioTimeStamp*        /*inTimeStamp*/,
                               UInt32                       /*inBusNumber*/,
                               UInt32                       inNumberFrames,
                               AudioBufferList*             ioData) {
    auto* impl = static_cast<CoreAudioBackend::Impl*>(inRefCon);
    float* out = static_cast<float*>(ioData->mBuffers[0].mData);
    if (impl->render)
        impl->render(out, static_cast<int32_t>(inNumberFrames));
    else
        for (UInt32 i = 0; i < inNumberFrames * 2; ++i) out[i] = 0.0f;
    return noErr;
}

CoreAudioBackend::CoreAudioBackend() : impl_(new Impl()) {}

CoreAudioBackend::~CoreAudioBackend() {
    stop();
    delete impl_;
}

namespace {

AudioDeviceID defaultOutputDevice() {
    AudioObjectPropertyAddress addr{ kAudioHardwarePropertyDefaultOutputDevice,
                                     kAudioObjectPropertyScopeGlobal,
                                     kAudioObjectPropertyElementMain };
    AudioDeviceID dev = kAudioObjectUnknown;
    UInt32 size = sizeof(dev);
    AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, nullptr, &size, &dev);
    return dev;
}

double deviceNominalSampleRate(AudioDeviceID dev) {
    if (dev == kAudioObjectUnknown) return 0.0;
    AudioObjectPropertyAddress addr{ kAudioDevicePropertyNominalSampleRate,
                                     kAudioObjectPropertyScopeGlobal,
                                     kAudioObjectPropertyElementMain };
    Float64 sr = 0.0;
    UInt32 size = sizeof(sr);
    if (AudioObjectGetPropertyData(dev, &addr, 0, nullptr, &size, &sr) != noErr) return 0.0;
    return sr;
}

} // namespace

bool CoreAudioBackend::start(RenderCallback render, const BackendConfig& cfg) {
    if (running_) return true;
    impl_->render = std::move(render);

    // HALOutput lets us target a specific device and set buffer/rate; the
    // default-output subtype cannot. A zeroed cfg resolves to the default device.
    AudioComponentDescription desc{};
    desc.componentType    = kAudioUnitType_Output;
    desc.componentSubType = kAudioUnitSubType_HALOutput;
    desc.componentManufacturer = kAudioUnitManufacturer_Apple;

    AudioComponent comp = AudioComponentFindNext(nullptr, &desc);
    if (!comp) return false;
    if (AudioComponentInstanceNew(comp, &impl_->outputUnit) != noErr) return false;

    // Enable output IO (bus 0), disable input (bus 1).
    UInt32 one = 1, zero = 0;
    if (AudioUnitSetProperty(impl_->outputUnit, kAudioOutputUnitProperty_EnableIO,
                             kAudioUnitScope_Output, 0, &one, sizeof(one)) != noErr) { stop(); return false; }
    AudioUnitSetProperty(impl_->outputUnit, kAudioOutputUnitProperty_EnableIO,
                         kAudioUnitScope_Input, 1, &zero, sizeof(zero));

    // Choose the device: requested, else the current system default.
    AudioDeviceID dev = cfg.device != 0 ? static_cast<AudioDeviceID>(cfg.device)
                                        : defaultOutputDevice();
    if (dev != kAudioObjectUnknown) {
        if (AudioUnitSetProperty(impl_->outputUnit, kAudioOutputUnitProperty_CurrentDevice,
                                 kAudioUnitScope_Global, 0, &dev, sizeof(dev)) != noErr) { stop(); return false; }
        // Listen for device overloads/dropouts (M7-8); non-fatal, off the RT thread.
        impl_->device = dev;
        AudioObjectAddPropertyListener(dev, &kOverloadAddr, &overloadProc, this);
    }

    // Apply requested nominal sample rate + buffer size on the device (best effort).
    if (cfg.sampleRate > 0.0 && dev != kAudioObjectUnknown) {
        Float64 sr = cfg.sampleRate;
        AudioObjectPropertyAddress a{ kAudioDevicePropertyNominalSampleRate,
                                      kAudioObjectPropertyScopeGlobal,
                                      kAudioObjectPropertyElementMain };
        AudioObjectSetPropertyData(dev, &a, 0, nullptr, sizeof(sr), &sr);
    }
    if (cfg.bufferFrames > 0 && dev != kAudioObjectUnknown) {
        UInt32 bf = static_cast<UInt32>(cfg.bufferFrames);
        AudioObjectPropertyAddress a{ kAudioDevicePropertyBufferFrameSize,
                                      kAudioObjectPropertyScopeGlobal,
                                      kAudioObjectPropertyElementMain };
        AudioObjectSetPropertyData(dev, &a, 0, nullptr, sizeof(bf), &bf);
    }

    // Interleaved stereo 32-bit float client format. Run the graph at the
    // requested rate (falling back to the device's nominal rate) so no hidden
    // sample-rate conversion sneaks in.
    double clientSr = cfg.sampleRate > 0.0 ? cfg.sampleRate : deviceNominalSampleRate(dev);
    if (!(clientSr > 0.0)) clientSr = 44100.0;

    AudioStreamBasicDescription fmt{};
    fmt.mSampleRate       = clientSr;
    fmt.mFormatID         = kAudioFormatLinearPCM;
    fmt.mFormatFlags      = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
    fmt.mFramesPerPacket  = 1;
    fmt.mChannelsPerFrame = 2;
    fmt.mBitsPerChannel   = 32;
    fmt.mBytesPerFrame    = fmt.mChannelsPerFrame * sizeof(float);
    fmt.mBytesPerPacket   = fmt.mBytesPerFrame;

    if (AudioUnitSetProperty(impl_->outputUnit, kAudioUnitProperty_StreamFormat,
                             kAudioUnitScope_Input, 0, &fmt, sizeof(fmt)) != noErr) {
        stop();
        return false;
    }

    AURenderCallbackStruct cb{};
    cb.inputProc       = &renderCallback;
    cb.inputProcRefCon = impl_;
    if (AudioUnitSetProperty(impl_->outputUnit, kAudioUnitProperty_SetRenderCallback,
                             kAudioUnitScope_Input, 0, &cb, sizeof(cb)) != noErr) {
        stop();
        return false;
    }

    if (AudioUnitInitialize(impl_->outputUnit) != noErr) { stop(); return false; }

    // Read back the actual negotiated sample rate.
    AudioStreamBasicDescription actual{};
    UInt32 size = sizeof(actual);
    if (AudioUnitGetProperty(impl_->outputUnit, kAudioUnitProperty_StreamFormat,
                             kAudioUnitScope_Input, 0, &actual, &size) == noErr) {
        sampleRate_ = actual.mSampleRate;
    } else {
        sampleRate_ = clientSr;
    }

    // Read back the actual buffer frame size (0 if unavailable).
    bufferFrames_ = 0;
    if (dev != kAudioObjectUnknown) {
        UInt32 bf = 0; UInt32 bsz = sizeof(bf);
        AudioObjectPropertyAddress a{ kAudioDevicePropertyBufferFrameSize,
                                      kAudioObjectPropertyScopeGlobal,
                                      kAudioObjectPropertyElementMain };
        if (AudioObjectGetPropertyData(dev, &a, 0, nullptr, &bsz, &bf) == noErr)
            bufferFrames_ = static_cast<int32_t>(bf);
    }

    if (AudioOutputUnitStart(impl_->outputUnit) != noErr) { stop(); return false; }

    running_ = true;
    return true;
}

void CoreAudioBackend::stop() {
    if (impl_->device != kAudioObjectUnknown) {
        AudioObjectRemovePropertyListener(impl_->device, &kOverloadAddr, &overloadProc, this);
        impl_->device = kAudioObjectUnknown;
    }
    if (impl_->outputUnit) {
        AudioOutputUnitStop(impl_->outputUnit);
        AudioUnitUninitialize(impl_->outputUnit);
        AudioComponentInstanceDispose(impl_->outputUnit);
        impl_->outputUnit = nullptr;
    }
    running_ = false;
}

} // namespace nota
