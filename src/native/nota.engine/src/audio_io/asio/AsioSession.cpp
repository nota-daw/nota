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

// A switch in flight finishes within one buffer; bound the wait in case it never does.
// False = it is still running: whatever it reads must not be freed.
bool waitOutSwitch(const std::atomic<bool>& inSwitch) {
    for (int i = 0; i < 500 && inSwitch.load(std::memory_order_seq_cst); ++i)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    return !inSwitch.load(std::memory_order_seq_cst);
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

bool AsioSession::setInputCallback(const InputCallback* cb) {
    input_.store(cb, std::memory_order_seq_cst);
    return cb || waitOutSwitch(inSwitch_);
}

bool AsioSession::quiesce() {
    live_.store(nullptr, std::memory_order_seq_cst); // late switches are ignored from here on
    if (waitOutSwitch(inSwitch_)) return true;
    wedged_ = true;
    return false;
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
    if (wedged_) {
        // The callbacks can't tell an old driver from a new one: never open next to a switch
        // that is still stuck. The driver is released (its thread is the driver's problem).
        driver.reset();
        return failOpen(Failure::Timeout, "an earlier audio callback never returned — restart Nota to use ASIO again");
    }
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
    io_ = std::make_unique<Io>();
    Io& io = *io_;
    io.driver = driver_.get();
    io.frames = bufferFrames_;
    for (int32_t i = 0; i < outs; ++i) {
        io.buffers.push_back(BufferInfo{false, i, {nullptr, nullptr}});
        io.outTypes.push_back(outChannels_[i].type);
    }
    io.outL = outL_;
    io.outR = outR_;
    if (inL_ >= 0) {
        io.inLBuf = static_cast<int32_t>(io.buffers.size());
        io.inLType = inChannels_[inL_].type;
        io.buffers.push_back(BufferInfo{true, inL_, {nullptr, nullptr}});
    }
    if (inR_ >= 0) {
        io.inRBuf = static_cast<int32_t>(io.buffers.size());
        io.inRType = inChannels_[inR_].type;
        io.buffers.push_back(BufferInfo{true, inR_, {nullptr, nullptr}});
    }
    io.interleaved.assign(static_cast<size_t>(bufferFrames_) * 2, 0.0f);

    g_active.store(this, std::memory_order_release); // the driver may message during createBuffers
    Callbacks cb;
    cb.bufferSwitch = &onBufferSwitch;
    cb.sampleRateDidChange = &onSampleRateDidChange;
    cb.message = &onMessage;
    if (!driver_->createBuffers(io.buffers.data(), static_cast<int32_t>(io.buffers.size()), bufferFrames_, cb))
        return failOpen(Failure::InitFailed, driver_->errorMessage());
    buffersCreated_ = true;
    for (const auto& b : io.buffers)
        if (!b.isInput)
            for (void* half : b.buffers) clear(io.outTypes[static_cast<size_t>(b.channel)], half, bufferFrames_);

    inputLatency_ = outputLatency_ = 0;
    driver_->getLatencies(inputLatency_, outputLatency_);
    io.outputReady = driver_->outputReady(); // the probe call: ASE_OK means supported
    resetRequested_.store(false, std::memory_order_relaxed);
    state_ = State::Prepared;
    return true;
}

bool AsioSession::start() {
    if (state_ == State::Running) return true;
    if (state_ != State::Prepared || !driver_ || !io_) return false;
    lastPosition_ = -1;
    lastTickNs_.store(nowNs(), std::memory_order_relaxed);
    io_->render = render_;
    live_.store(io_.get(), std::memory_order_seq_cst); // drivers may call back from inside start()
    if (!driver_->start()) {
        quiesce();
        return failOpen(Failure::InitFailed, driver_->errorMessage());
    }
    started_ = true;
    state_ = State::Running;
    return true;
}

void AsioSession::stop() {
    quiesce();
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
    if (quiesce()) {
        if (driver_) {
            if (started_) driver_->stop();
            if (buffersCreated_) driver_->disposeBuffers();
        }
        driver_.reset();
        io_.reset();
    } else {
        // A switch is still inside the render (a stalled plug-in) or the driver: freeing the
        // buffers, the driver or the scratch now would pull them out from under it. Leak them
        // on purpose; open() refuses from here on.
        (void)driver_.release();
        (void)io_.release();
    }
    started_ = false;
    buffersCreated_ = false;
    AsioSession* self = this;
    g_active.compare_exchange_strong(self, nullptr, std::memory_order_acq_rel);
    inChannels_.clear();
    outChannels_.clear();
    outL_ = outR_ = inL_ = inR_ = -1;
    sampleRate_ = 0.0;
    bufferFrames_ = 0;
    inputLatency_ = outputLatency_ = 0;
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
    // While the driver's own panel is open, events wait: restarting now would queue behind the
    // panel. The watchdog waits too — a driver may pause its buffers while its panel
    // reconfigures it, and that pause must not count as a failure once the panel closes.
    if (driver_ && driver_->panelOpen()) {
        lastTickNs_.store(nowNs(), std::memory_order_relaxed);
        return kEventNone;
    }
    // A pending reset comes first: it explains a silent spell (the driver stopped its buffers
    // to reconfigure) and reopening the driver is the right answer, not WASAPI.
    if (resetRequested_.exchange(false, std::memory_order_acq_rel)) return kEventResetRequested;
    if (state_ == State::Running) {
        const int64_t silentNs = nowNs() - lastTickNs_.load(std::memory_order_relaxed);
        if (silentNs > std::chrono::duration_cast<std::chrono::nanoseconds>(watchdog_).count()) {
            quiesce();
            failure_ = Failure::Stopped;
            failureDetail_.clear();
            state_ = State::Failed; // the driver is released by the next close() (the app re-applies)
            return kEventDriverFailed;
        }
    }
    return kEventNone;
}

// ---- audio thread ------------------------------------------------------------------------

void AsioSession::bufferSwitch(int32_t index, int64_t pos) {
    inSwitch_.store(true, std::memory_order_seq_cst);
    Io* const live = live_.load(std::memory_order_seq_cst); // pairs with quiesce()
    if (!live || index < 0 || index > 1) {
        inSwitch_.store(false, std::memory_order_release);
        return;
    }
    Io& s = *live; // everything below goes through this one object
    lastTickNs_.store(nowNs(), std::memory_order_relaxed);
    const int32_t n = s.frames;
    // A driver that changed its buffer size without a reset message shows up as a position
    // step that isn't a whole number of buffers: treat it as a reset request.
    if (pos >= 0) {
        if (lastPosition_ >= 0) {
            const int64_t step = pos - lastPosition_;
            if (step > 0 && step % n != 0) resetRequested_.store(true, std::memory_order_relaxed);
        }
        lastPosition_ = pos;
    }

    float* io = s.interleaved.data();
    // Input first, so the engine's input rings hold block n before block n renders (R2).
    if (const InputCallback* in = input_.load(std::memory_order_seq_cst); in && s.inLBuf >= 0) {
        toFloat(s.buffers[static_cast<size_t>(s.inLBuf)].buffers[index], s.inLType, io, n, 2);
        if (s.inRBuf >= 0) toFloat(s.buffers[static_cast<size_t>(s.inRBuf)].buffers[index], s.inRType, io + 1, n, 2);
        else
            for (int32_t i = 0; i < n; ++i) io[i * 2 + 1] = io[i * 2]; // mono input: both sides
        (*in)(io, n);
    }

    if (s.render) s.render(io, n);
    else std::fill(io, io + static_cast<size_t>(n) * 2, 0.0f);

    const auto outBuf = [&](int32_t ch) { return s.buffers[static_cast<size_t>(ch)].buffers[index]; };
    const auto outType = [&](int32_t ch) { return s.outTypes[static_cast<size_t>(ch)]; };
    if (s.outR >= 0) {
        fromFloat(io, 2, outType(s.outL), outBuf(s.outL), n);
        fromFloat(io + 1, 2, outType(s.outR), outBuf(s.outR), n);
    } else {
        for (int32_t i = 0; i < n; ++i) io[i * 2] = 0.5f * (io[i * 2] + io[i * 2 + 1]); // mono output: down-sum
        fromFloat(io, 2, outType(s.outL), outBuf(s.outL), n);
    }
    // Drivers may not keep buffer contents: hold every other output at zero each switch.
    const auto outs = static_cast<int32_t>(s.outTypes.size());
    for (int32_t ch = 0; ch < outs; ++ch)
        if (ch != s.outL && ch != s.outR) clear(outType(ch), outBuf(ch), n);

    if (s.outputReady) s.driver->outputReady();
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
