// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// External MIDI (specs/115): the engine's instrument calls back into MIDI bytes. Every call
// below runs on the audio thread except allNotesOff (any thread: it only raises a flag) and the
// state / clone calls (message thread). Nothing here allocates or locks while rendering (AR-6):
// a full ring drops the message and counts it.

#include "devices/instruments/ExternalMidi.h"

#include "engine/MidiController.h"
#include "engine/NoteExpression.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace nota {

namespace {
uint8_t u7(float v) { return static_cast<uint8_t>(std::clamp(static_cast<int>(std::lround(v * 127.0f)), 0, 127)); }
// −1..+1 → a 14-bit pitch-bend value.
int bend14(float v) { return std::clamp(static_cast<int>(std::lround((v + 1.0f) * 8191.5f)), 0, 16383); }
} // namespace

ExternalMidi::ExternalMidi(std::string outputUid, int32_t channel, std::string outputName, std::string roleId)
    : uid_(std::move(outputUid)), name_(std::move(outputName)), role_(std::move(roleId)) {
    sLive.fetch_add(1, std::memory_order_relaxed);
    channel_ = channel >= 0 && channel <= 16 ? channel : 1;
    for (uint8_t i = 0; i < 15; ++i) lru_[i] = static_cast<uint8_t>(i + 1); // member channels 2..16 (0-based 1..15)
}

// ---- timing --------------------------------------------------------------------------------

// When an event at eventOffset is due: the block's start, plus the frames rendered so far this
// block, plus the offset. A new block (the clock's serial moved on) starts counting again.
uint64_t ExternalMidi::dueNow() {
    const MidiRenderClock* c = clock_.load(std::memory_order_acquire);
    if (!c) return 0;
    const uint32_t s = c->serial.load(std::memory_order_acquire);
    if (s != serialSeen_) {
        serialSeen_ = s;
        framesIntoBlock_ = 0;
    }
    const double sr = c->sampleRate.load(std::memory_order_relaxed);
    const double frames = static_cast<double>(framesIntoBlock_ + std::max(0, eventOffset));
    return c->blockNs.load(std::memory_order_relaxed) + static_cast<uint64_t>(frames * 1e9 / (sr > 0.0 ? sr : sr_));
}

void ExternalMidi::push(uint8_t s, uint8_t d1, uint8_t d2, uint8_t len) {
    takePendingFlush();
    if (channel_ == kKeepChannels && !mpeConfigured_) sendMpeConfig();
    OutMidiEv e;
    e.dueNs = dueNow();
    const MidiRenderClock* c = clock_.load(std::memory_order_relaxed);
    e.realtime = c && c->realtime.load(std::memory_order_relaxed) ? 1 : 0;
    e.len = len;
    e.b[0] = s;
    e.b[1] = d1;
    e.b[2] = d2;
    if (!ring_.push(e)) dropped.fetch_add(1, std::memory_order_relaxed);
}

void ExternalMidi::pushFlush() {
    OutMidiEv e;
    e.dueNs = dueNow();
    const MidiRenderClock* c = clock_.load(std::memory_order_relaxed);
    e.realtime = c && c->realtime.load(std::memory_order_relaxed) ? 1 : 0;
    e.len = 0;
    if (!ring_.push(e)) dropped.fetch_add(1, std::memory_order_relaxed);
    // The MIDI out thread releases what it sent; the channels are free again here.
    mpeChan_.fill(0);
    chanUse_.fill(0);
}

void ExternalMidi::takePendingFlush() {
    if (pendingFlush_.exchange(false, std::memory_order_acq_rel)) pushFlush();
}

void ExternalMidi::flushFromAudio() {
    pendingFlush_.store(false, std::memory_order_relaxed);
    pushFlush();
}

void ExternalMidi::allNotesOff() { pendingFlush_.store(true, std::memory_order_release); }

void ExternalMidi::render(float* out, int32_t frames) {
    takePendingFlush();
    if (out && frames > 0) std::memset(out, 0, sizeof(float) * static_cast<size_t>(frames) * 2);
    (void)dueNow(); // syncs the block counter before advancing it
    framesIntoBlock_ += std::max(0, frames);
}

// ---- channels ------------------------------------------------------------------------------

uint8_t ExternalMidi::master() const {
    if (eventChannel >= 1 && eventChannel <= 16) return static_cast<uint8_t>(eventChannel - 1); // an articulation's channel
    return channel_ == kKeepChannels ? 0 : static_cast<uint8_t>(channel_ - 1);
}

uint8_t ExternalMidi::chanFor(int32_t pitch) const {
    if (channel_ != kKeepChannels || eventChannel != 0) return master();
    const uint8_t c = pitch >= 0 && pitch < 128 ? mpeChan_[static_cast<size_t>(pitch)] : 0;
    return c; // 0 = not sounding: the master channel
}

// The MPE Configuration Message (RPN 6) on channel 1: a lower zone of 15 member channels.
void ExternalMidi::sendMpeConfig() {
    mpeConfigured_ = true;
    push(0xB0, 101, 0, 3);
    push(0xB0, 100, 6, 3);
    push(0xB0, 6, 15, 3);
    push(0xB0, 101, 127, 3); // RPN null: later data entry goes nowhere
    push(0xB0, 100, 127, 3);
}

// ---- events --------------------------------------------------------------------------------

void ExternalMidi::noteOn(int32_t pitch, float velocity) {
    if (pitch < 0 || pitch > 127) return;
    takePendingFlush();
    if (channel_ == kKeepChannels && eventChannel == 0) {
        const auto p = static_cast<size_t>(pitch);
        if (mpeChan_[p] == 0) {
            // The member channel freed longest ago that no note uses; all busy: share the oldest.
            size_t pick = 0;
            for (size_t i = 0; i < lru_.size(); ++i)
                if (chanUse_[lru_[i]] == 0) {
                    pick = i;
                    break;
                }
            const uint8_t ch = lru_[pick];
            std::rotate(lru_.begin() + static_cast<std::ptrdiff_t>(pick), lru_.begin() + static_cast<std::ptrdiff_t>(pick) + 1,
                lru_.end());
            mpeChan_[p] = ch;
            ++chanUse_[ch];
        }
    }
    const uint8_t v = static_cast<uint8_t>(std::clamp(static_cast<int>(std::lround(velocity * 127.0f)), 1, 127));
    push(static_cast<uint8_t>(0x90 | chanFor(pitch)), static_cast<uint8_t>(pitch), v, 3);
}

void ExternalMidi::noteOffVelocity(int32_t pitch, float velocity) {
    if (pitch < 0 || pitch > 127) return;
    push(static_cast<uint8_t>(0x80 | chanFor(pitch)), static_cast<uint8_t>(pitch), u7(velocity), 3);
    if (channel_ == kKeepChannels && eventChannel == 0) {
        const auto p = static_cast<size_t>(pitch);
        if (const uint8_t ch = mpeChan_[p]; ch != 0) {
            mpeChan_[p] = 0;
            if (chanUse_[ch] > 0) --chanUse_[ch];
        }
    }
}

void ExternalMidi::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch < 0) { // instrument-wide: the wheel (−1..+1), channel pressure, slide
        if (dim == ExprBend) controller(kCtlPitchWheel, value);
        else if (dim == ExprPressure) controller(kCtlChannelPressure, value);
        else if (dim == ExprSlide) controller(74, value);
        return;
    }
    if (pitch > 127) return;
    const bool member = channel_ == kKeepChannels && eventChannel == 0 && mpeChan_[static_cast<size_t>(pitch)] != 0;
    const uint8_t ch = chanFor(pitch);
    if (member) { // the note's own channel carries its expression (MPE)
        if (dim == ExprBend) {
            const int b = bend14(std::clamp(value / kMpeBendRange, -1.0f, 1.0f));
            push(static_cast<uint8_t>(0xE0 | ch), static_cast<uint8_t>(b & 0x7F), static_cast<uint8_t>(b >> 7), 3);
        } else if (dim == ExprPressure) {
            push(static_cast<uint8_t>(0xD0 | ch), u7(value), 0, 2);
        } else if (dim == ExprSlide) {
            push(static_cast<uint8_t>(0xB0 | ch), 74, u7(value), 3);
        }
        return;
    }
    // One channel for every note: only pressure has a per-note message (poly aftertouch).
    if (dim == ExprPressure) push(static_cast<uint8_t>(0xA0 | ch), static_cast<uint8_t>(pitch), u7(value), 3);
}

void ExternalMidi::controller(int32_t ctl, float value) {
    const uint8_t ch = master();
    if (ctl == kCtlPitchWheel) {
        const int b = bend14(std::clamp(value, -1.0f, 1.0f));
        push(static_cast<uint8_t>(0xE0 | ch), static_cast<uint8_t>(b & 0x7F), static_cast<uint8_t>(b >> 7), 3);
    } else if (ctl == kCtlChannelPressure) {
        push(static_cast<uint8_t>(0xD0 | ch), u7(value), 0, 2);
    } else if (ctl >= 0 && ctl < 128) {
        push(static_cast<uint8_t>(0xB0 | ch), static_cast<uint8_t>(ctl), u7(value), 3);
    }
}

void ExternalMidi::programChange(int32_t program) {
    if (program >= 0 && program < 128) push(static_cast<uint8_t>(0xC0 | master()), static_cast<uint8_t>(program), 0, 2);
}

// ---- state ---------------------------------------------------------------------------------

// Version 1: [1][channel] then the output uid, its name and the role id, each as a little-endian
// u16 length and its bytes. A newer version's blob keeps what this one knows.
std::vector<uint8_t> ExternalMidi::getState() const {
    std::vector<uint8_t> s;
    s.reserve(8 + uid_.size() + name_.size() + role_.size());
    s.push_back(1);
    s.push_back(static_cast<uint8_t>(channel_));
    for (const std::string* str : {&uid_, &name_, &role_}) {
        const auto n = static_cast<uint16_t>(std::min<size_t>(str->size(), 0xFFFF));
        s.push_back(static_cast<uint8_t>(n & 0xFF));
        s.push_back(static_cast<uint8_t>(n >> 8));
        s.insert(s.end(), str->begin(), str->begin() + n);
    }
    return s;
}

void ExternalMidi::setState(const uint8_t* data, int32_t size) {
    if (!data || size < 2 || data[0] < 1) return;
    channel_ = data[1] <= 16 ? data[1] : 1;
    int32_t at = 2;
    for (std::string* str : {&uid_, &name_, &role_}) {
        str->clear();
        if (at + 2 > size) continue;
        const int32_t n = data[at] | (data[at + 1] << 8);
        at += 2;
        if (at + n > size) return;
        str->assign(reinterpret_cast<const char*>(data + at), static_cast<size_t>(n));
        at += n;
    }
}

std::shared_ptr<Instrument> ExternalMidi::clone() const {
    auto c = std::make_shared<ExternalMidi>(uid_, channel_, name_, role_);
    c->setSampleRate(sr_);
    c->folded = folded;
    c->presetTag = presetTag;
    c->setBypassed(bypassed());
    return c;
}

} // namespace nota
