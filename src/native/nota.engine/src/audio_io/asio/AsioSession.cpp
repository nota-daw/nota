// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AsioSession.h"

#include <algorithm>
#include <thread>

namespace nota::asio {
namespace {

// ASIO callbacks carry no user data and a process opens one driver, so a single pointer
// routes them to the session that created the buffers.
std::atomic<AsioSession*> g_active{nullptr};

void onBufferSwitch(int32_t index, int64_t pos) {
    if (AsioSession* s = g_active.load(std::memory_order_acquire)) s->bufferSwitch(index, pos);
}
void onSampleRateDidChange(double rate) {
    if (AsioSession* s = g_active.load(std::memory_order_acquire)) s->sampleRateChanged(rate);
}
int32_t onMessage(int32_t selector, int32_t value) {
    AsioSession* s = g_active.load(std::memory_order_acquire);
    return s ? s->message(selector, value) : 0;
}

int64_t nowNs() {
    return std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch())
        .count();
}

// A switch in flight finishes within one buffer; bound the wait in case the driver is wedged.
void waitOutSwitch(const std::atomic<bool>& inSwitch) {
    for (int i = 0; i < 500 && inSwitch.load(std::memory_order_seq_cst); ++i)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
}

} // namespace

AsioSession::~AsioSession() {
    close();
}

void AsioSession::setRenderCallback(RenderCallback cb) {
    if (state_ != State::Running) render_ = std::move(cb);
}

void AsioSession::setXrunCallback(std::function<void()> cb) {
    if (state_ != State::Running) xrun_ = std::move(cb);
}

void AsioSession::setInputCallback(const InputCallback* cb) {
    input_.store(cb, std::memory_order_release);
    if (!cb) waitOutSwitch(inSwitch_);
}

bool AsioSession::failOpen(Failure why, std::string detail) {
    if (driver_ && driver_->timedOut()) why = Failure::Timeout;
    fail(why, std::move(detail));
    return false;
}

void AsioSession::fail(Failure why, std::string detail) {
    releaseDriver();
    failure_ = why;
    failureDetail_ = std::move(detail);
    state_ = State::Failed;
}

bool AsioSession::open(std::unique_ptr<AsioDriverApi> driver, const Options& opt) {
    close();
    state_ = State::Closed;
    failure_ = Failure::None;
    failureDetail_.clear();
    driverName_.clear();
    rateRefused_ = false;
    driver_ = std::move(driver);
    if (!driver_) return failOpen(Failure::LoadFailed, {});

    if (!driver_->init()) return failOpen(Failure::InitFailed, driver_->errorMessage());
    driverName_ = driver_->driverName();

    int32_t ins = 0, outs = 0;
    if (!driver_->getChannels(ins, outs)) return failOpen(Failure::InitFailed, driver_->errorMessage());
    if (outs <= 0) return failOpen(Failure::NoOutputs, {});

    // Sample rate: honour a request the driver supports; otherwise keep its rate (FR-007).
    double rate = 0.0;
    driver_->getSampleRate(rate);
    if (opt.sampleRate > 0.0 && opt.sampleRate != rate) {
        if (driver_->canSampleRate(opt.sampleRate) && driver_->setSampleRate(opt.sampleRate)) driver_->getSampleRate(rate);
        else rateRefused_ = true;
    }
    if (rate <= 0.0) return failOpen(Failure::InitFailed, "no sample clock");
    sampleRate_ = rate;

    // Buffer size: the driver's preferred one (FR-008: set in the driver's own panel).
    int32_t minSize = 0, maxSize = 0, preferred = 0, granularity = 0;
    if (!driver_->getBufferSize(minSize, maxSize, preferred, granularity))
        return failOpen(Failure::InitFailed, driver_->errorMessage());
    bufferFrames_ = preferred > 0 ? preferred : minSize;
    if (bufferFrames_ <= 0) return failOpen(Failure::InitFailed, "no buffer size");

    auto readChannels = [&](bool input, int32_t count, std::vector<ChannelInfo>& out) {
        out.clear();
        for (int32_t i = 0; i < count; ++i) {
            ChannelInfo ci;
            ci.index = i;
            ci.isInput = input;
            if (!driver_->getChannelInfo(ci)) return false;
            if (ci.name.empty()) ci.name = (input ? "In " : "Out ") + std::to_string(i + 1);
            out.push_back(std::move(ci));
        }
        return true;
    };
    if (!readChannels(false, outs, outChannels_) || !readChannels(true, ins, inChannels_))
        return failOpen(Failure::InitFailed, driver_->errorMessage());

    // Pairs: an out-of-range pair falls back to the first; a missing second channel = mono.
    outL_ = (opt.outputPair >= 0 && opt.outputPair < outs) ? opt.outputPair : 0;
    outR_ = outL_ + 1 < outs ? outL_ + 1 : -1;
    inL_ = ins > 0 ? ((opt.inputPair >= 0 && opt.inputPair < ins) ? opt.inputPair : 0) : -1;
    inR_ = (inL_ >= 0 && inL_ + 1 < ins) ? inL_ + 1 : -1;

    auto unsupported = [&](const ChannelInfo& c) { return !isSupported(c.type); };
    for (int32_t ch : {outL_, outR_})
        if (ch >= 0 && unsupported(outChannels_[ch]))
            return failOpen(Failure::UnsupportedFormat, sampleTypeName(outChannels_[ch].type));
    for (int32_t ch : {inL_, inR_})
        if (ch >= 0 && unsupported(inChannels_[ch]))
            return failOpen(Failure::UnsupportedFormat, sampleTypeName(inChannels_[ch].type));

    // Buffers for every output (so the unused ones can be held at zero, FR-006) and the
    // chosen input pair.
    buffers_.clear();
    for (int32_t i = 0; i < outs; ++i) buffers_.push_back(BufferInfo{false, i, {nullptr, nullptr}});
    outLBuf_ = outL_;
    outRBuf_ = outR_;
    inLBuf_ = inRBuf_ = -1;
    if (inL_ >= 0) {
        inLBuf_ = static_cast<int32_t>(buffers_.size());
        buffers_.push_back(BufferInfo{true, inL_, {nullptr, nullptr}});
    }
    if (inR_ >= 0) {
        inRBuf_ = static_cast<int32_t>(buffers_.size());
        buffers_.push_back(BufferInfo{true, inR_, {nullptr, nullptr}});
    }
    interleaved_.assign(static_cast<size_t>(bufferFrames_) * 2, 0.0f);

    g_active.store(this, std::memory_order_release); // the driver may message during createBuffers
    Callbacks cb;
    cb.bufferSwitch = &onBufferSwitch;
    cb.sampleRateDidChange = &onSampleRateDidChange;
    cb.message = &onMessage;
    if (!driver_->createBuffers(buffers_.data(), static_cast<int32_t>(buffers_.size()), bufferFrames_, cb))
        return failOpen(Failure::InitFailed, driver_->errorMessage());
    buffersCreated_ = true;
    for (const auto& b : buffers_)
        if (!b.isInput)
            for (void* half : b.buffers) clear(outChannels_[b.channel].type, half, bufferFrames_);

    inputLatency_ = outputLatency_ = 0;
    driver_->getLatencies(inputLatency_, outputLatency_);
    outputReady_ = driver_->outputReady(); // the probe call: ASE_OK means supported
    resetRequested_.store(false, std::memory_order_relaxed);
    state_ = State::Prepared;
    return true;
}

bool AsioSession::start() {
    if (state_ == State::Running) return true;
    if (state_ != State::Prepared || !driver_) return false;
    lastPosition_ = -1;
    lastTickNs_.store(nowNs(), std::memory_order_relaxed);
    active_.store(true, std::memory_order_seq_cst); // drivers may call back from inside start()
    if (!driver_->start()) {
        active_.store(false, std::memory_order_seq_cst);
        waitOutSwitch(inSwitch_);
        return failOpen(Failure::InitFailed, driver_->errorMessage());
    }
    started_ = true;
    state_ = State::Running;
    return true;
}

void AsioSession::stop() {
    active_.store(false, std::memory_order_seq_cst); // late switches are ignored from here on
    waitOutSwitch(inSwitch_);
    if (started_ && driver_) driver_->stop();
    started_ = false;
    if (state_ == State::Running) state_ = State::Prepared;
}

void AsioSession::close() {
    stop();
    releaseDriver();
    if (state_ != State::Failed) state_ = State::Closed;
}

void AsioSession::releaseDriver() {
    active_.store(false, std::memory_order_seq_cst);
    waitOutSwitch(inSwitch_);
    if (driver_) {
        if (started_) driver_->stop();
        if (buffersCreated_) driver_->disposeBuffers();
    }
    started_ = false;
    buffersCreated_ = false;
    AsioSession* self = this;
    g_active.compare_exchange_strong(self, nullptr, std::memory_order_acq_rel);
    driver_.reset();
    buffers_.clear();
    inChannels_.clear();
    outChannels_.clear();
    outL_ = outR_ = inL_ = inR_ = -1;
    outLBuf_ = outRBuf_ = inLBuf_ = inRBuf_ = -1;
    sampleRate_ = 0.0;
    bufferFrames_ = 0;
    inputLatency_ = outputLatency_ = 0;
    outputReady_ = false;
    if (state_ == State::Running || state_ == State::Prepared) state_ = State::Closed;
}

std::string AsioSession::failureText() const {
    std::string s;
    switch (failure_) {
    case Failure::None: return {};
    case Failure::NotInstalled: s = "the driver isn't installed"; break;
    case Failure::LoadFailed: s = "the driver couldn't be loaded"; break;
    case Failure::InitFailed: s = "the driver didn't start — another program may be using it"; break;
    case Failure::UnsupportedFormat: s = "the driver uses a sample format Nota can't read"; break;
    case Failure::NoOutputs: s = "the driver has no outputs"; break;
    case Failure::Timeout: s = "the driver stopped responding"; break;
    case Failure::Stopped: s = "the driver stopped sending audio"; break;
    }
    if (!failureDetail_.empty()) s += " (" + failureDetail_ + ")";
    return s;
}

bool AsioSession::openControlPanel() {
    return driver_ && (state_ == State::Prepared || state_ == State::Running) && driver_->controlPanel();
}

int32_t AsioSession::pollEvent() {
    // A reset sent from the driver's open panel waits until the panel closes: restarting now
    // would queue behind the panel and look like a hung driver.
    if (driver_ && driver_->panelOpen()) return kEventNone;
    if (state_ == State::Running) {
        const int64_t silentNs = nowNs() - lastTickNs_.load(std::memory_order_relaxed);
        if (silentNs > std::chrono::duration_cast<std::chrono::nanoseconds>(watchdog_).count()) {
            active_.store(false, std::memory_order_seq_cst);
            waitOutSwitch(inSwitch_);
            failure_ = Failure::Stopped;
            failureDetail_.clear();
            state_ = State::Failed; // the driver is released by the next close() (the app re-applies)
            return kEventDriverFailed;
        }
    }
    if (resetRequested_.exchange(false, std::memory_order_acq_rel)) return kEventResetRequested;
    return kEventNone;
}

// ---- audio thread ------------------------------------------------------------------------

void AsioSession::bufferSwitch(int32_t index, int64_t pos) {
    inSwitch_.store(true, std::memory_order_seq_cst);
    if (!active_.load(std::memory_order_seq_cst) || index < 0 || index > 1) {
        inSwitch_.store(false, std::memory_order_release);
        return;
    }
    lastTickNs_.store(nowNs(), std::memory_order_relaxed);
    const int32_t n = bufferFrames_;
    // A driver that changed its buffer size without a reset message shows up as a position
    // step that isn't a whole number of buffers: treat it as a reset request.
    if (pos >= 0) {
        if (lastPosition_ >= 0) {
            const int64_t step = pos - lastPosition_;
            if (step > 0 && step % n != 0) resetRequested_.store(true, std::memory_order_relaxed);
        }
        lastPosition_ = pos;
    }

    float* io = interleaved_.data();
    // Input first, so the engine's input rings hold block n before block n renders (R2).
    if (const InputCallback* in = input_.load(std::memory_order_acquire); in && inLBuf_ >= 0) {
        toFloat(buffers_[inLBuf_].buffers[index], inChannels_[inL_].type, io, n, 2);
        if (inRBuf_ >= 0) toFloat(buffers_[inRBuf_].buffers[index], inChannels_[inR_].type, io + 1, n, 2);
        else
            for (int32_t i = 0; i < n; ++i) io[i * 2 + 1] = io[i * 2]; // mono input: both sides
        (*in)(io, n);
    }

    if (render_) render_(io, n);
    else std::fill(io, io + static_cast<size_t>(n) * 2, 0.0f);

    if (outRBuf_ >= 0) {
        fromFloat(io, 2, outChannels_[outL_].type, buffers_[outLBuf_].buffers[index], n);
        fromFloat(io + 1, 2, outChannels_[outR_].type, buffers_[outRBuf_].buffers[index], n);
    } else {
        for (int32_t i = 0; i < n; ++i) io[i * 2] = 0.5f * (io[i * 2] + io[i * 2 + 1]); // mono output: down-sum
        fromFloat(io, 2, outChannels_[outL_].type, buffers_[outLBuf_].buffers[index], n);
    }
    // Drivers may not keep buffer contents: hold every other output at zero each switch.
    const auto outs = static_cast<int32_t>(outChannels_.size());
    for (int32_t ch = 0; ch < outs; ++ch)
        if (ch != outL_ && ch != outR_) clear(outChannels_[ch].type, buffers_[ch].buffers[index], n);

    if (outputReady_) driver_->outputReady();
    inSwitch_.store(false, std::memory_order_release);
}

// Driver threads (any): only flags and the lightweight xrun counter.
void AsioSession::sampleRateChanged(double) {
    resetRequested_.store(true, std::memory_order_relaxed);
}

int32_t AsioSession::message(int32_t selector, int32_t value) {
    switch (selector) {
    case kSelectorSupported:
        switch (value) {
        case kEngineVersion:
        case kResetRequest:
        case kBufferSizeChange:
        case kResyncRequest:
        case kLatenciesChanged:
        case kSupportsTimeInfo:
        case kOverload: return 1;
        default: return 0;
        }
    case kEngineVersion: return 2;
    case kResetRequest:
    case kBufferSizeChange:
    case kLatenciesChanged: resetRequested_.store(true, std::memory_order_relaxed); return 1;
    case kResyncRequest:
    case kOverload:
        if (xrun_) xrun_();
        return 1;
    case kSupportsTimeInfo: return 1;
    default: return 0;
    }
}

} // namespace nota::asio
