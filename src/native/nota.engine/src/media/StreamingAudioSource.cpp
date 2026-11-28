// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "media/StreamingAudioSource.h"
#include "media/AudioFile.h"
#include "media/SampleBuffer.h"

// Declarations only — implementations live in AudioFile.cpp.
#include <dr_wav.h>
#include <dr_flac.h>
#include <dr_mp3.h>

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cmath>
#include <cstring>

namespace nota {

namespace {

std::string extLowerStr(const std::string& path) {
    const auto dot = path.find_last_of('.');
    if (dot == std::string::npos) return {};
    std::string ext = path.substr(dot + 1);
    std::transform(
        ext.begin(), ext.end(), ext.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return ext;
}

std::mutex g_schedMu;
std::weak_ptr<StreamingIoScheduler> g_sharedSched;

} // namespace

struct StreamingAudioSource::DecoderHandle {
    enum class Kind { None, Wav, Flac, Mp3 } kind = Kind::None;
    drwav wav{};
    drflac* flac = nullptr;
    drmp3 mp3{};
    int64_t cursorFrame = -1;

    ~DecoderHandle() { close(); }

    bool open(const std::string& path, int32_t& outChannels, int64_t& outFrames, double& outSampleRate) {
        close();
        const std::string ext = extLowerStr(path);
        if (ext == "wav") {
            kind = Kind::Wav;
#ifdef _WIN32
            if (!drwav_init_file_w(&wav, widePath(path).c_str(), nullptr)) {
#else
            if (!drwav_init_file(&wav, path.c_str(), nullptr)) {
#endif
                kind = Kind::None;
                return false;
            }
            outChannels = static_cast<int32_t>(wav.channels);
            outFrames = static_cast<int64_t>(wav.totalPCMFrameCount);
            outSampleRate = static_cast<double>(wav.sampleRate);
            cursorFrame = 0;
            return outChannels > 0 && outFrames > 0 && outSampleRate > 0.0;
        }
        if (ext == "flac") {
            kind = Kind::Flac;
#ifdef _WIN32
            flac = drflac_open_file_w(widePath(path).c_str(), nullptr);
#else
            flac = drflac_open_file(path.c_str(), nullptr);
#endif
            if (!flac) {
                kind = Kind::None;
                return false;
            }
            outChannels = static_cast<int32_t>(flac->channels);
            outFrames = static_cast<int64_t>(flac->totalPCMFrameCount);
            outSampleRate = static_cast<double>(flac->sampleRate);
            cursorFrame = 0;
            return outChannels > 0 && outFrames > 0 && outSampleRate > 0.0;
        }
        if (ext == "mp3") {
            kind = Kind::Mp3;
#ifdef _WIN32
            if (!drmp3_init_file_w(&mp3, widePath(path).c_str(), nullptr)) {
#else
            if (!drmp3_init_file(&mp3, path.c_str(), nullptr)) {
#endif
                kind = Kind::None;
                return false;
            }
            outChannels = static_cast<int32_t>(mp3.channels);
            outSampleRate = static_cast<double>(mp3.sampleRate);
            outFrames = static_cast<int64_t>(drmp3_get_pcm_frame_count(&mp3));
            drmp3_seek_to_pcm_frame(&mp3, 0);
            cursorFrame = 0;
            return outChannels > 0 && outFrames > 0 && outSampleRate > 0.0;
        }
        return false;
    }

    void close() {
        switch (kind) {
        case Kind::Wav: drwav_uninit(&wav); break;
        case Kind::Flac:
            if (flac) {
                drflac_close(flac);
                flac = nullptr;
            }
            break;
        case Kind::Mp3: drmp3_uninit(&mp3); break;
        case Kind::None: break;
        }
        kind = Kind::None;
        cursorFrame = -1;
    }

    bool seekToFrame(int64_t frame) {
        if (frame < 0) frame = 0;
        if (cursorFrame == frame) return true;
        bool ok = false;
        switch (kind) {
        case Kind::Wav: ok = drwav_seek_to_pcm_frame(&wav, static_cast<drwav_uint64>(frame)) == DRWAV_TRUE; break;
        case Kind::Flac: ok = drflac_seek_to_pcm_frame(flac, static_cast<drflac_uint64>(frame)) == DRFLAC_TRUE; break;
        case Kind::Mp3: ok = drmp3_seek_to_pcm_frame(&mp3, static_cast<drmp3_uint64>(frame)) == DRMP3_TRUE; break;
        case Kind::None: return false;
        }
        cursorFrame = ok ? frame : -1;
        return ok;
    }

    int64_t readFrames(int64_t count, float* dst) {
        if (count <= 0 || !dst) return 0;
        uint64_t got = 0;
        switch (kind) {
        case Kind::Wav: got = drwav_read_pcm_frames_f32(&wav, static_cast<drwav_uint64>(count), dst); break;
        case Kind::Flac: got = drflac_read_pcm_frames_f32(flac, static_cast<drflac_uint64>(count), dst); break;
        case Kind::Mp3: got = drmp3_read_pcm_frames_f32(&mp3, static_cast<drmp3_uint64>(count), dst); break;
        case Kind::None: return 0;
        }
        if (cursorFrame >= 0) cursorFrame += static_cast<int64_t>(got);
        return static_cast<int64_t>(got);
    }
};

// ---- StreamingIoScheduler --------------------------------------------------

std::shared_ptr<StreamingIoScheduler> StreamingIoScheduler::shared() {
    std::lock_guard<std::mutex> lk(g_schedMu);
    auto sp = g_sharedSched.lock();
    if (!sp) {
        sp = std::make_shared<StreamingIoScheduler>();
        g_sharedSched = sp;
    }
    return sp;
}

StreamingIoScheduler::StreamingIoScheduler()
    : state_(std::make_shared<State>()), worker_([st = state_] { runLoop(st); }) {}

StreamingIoScheduler::~StreamingIoScheduler() {
    {
        std::lock_guard<std::mutex> lk(state_->mu);
        state_->stop = true;
        state_->dirty = true;
    }
    state_->cv.notify_all();
    if (worker_.joinable()) {
        // ~StreamingAudioSource running on worker_ may drop the final reference to scheduler_;
        // detaching avoids EDEADLK self-join while runLoop still holds its own shared_ptr<State>.
        if (std::this_thread::get_id() == worker_.get_id()) worker_.detach();
        else worker_.join();
    }
}

void StreamingIoScheduler::registerSource(const std::shared_ptr<StreamingAudioSource>& src) {
    if (!src) return;
    {
        std::lock_guard<std::mutex> lk(state_->mu);
        state_->sources.push_back(Entry{src, src.get()});
        state_->dirty = true;
    }
    state_->cv.notify_one();
}

void StreamingIoScheduler::unregisterSource(const StreamingAudioSource* src) {
    std::lock_guard<std::mutex> lk(state_->mu);
    auto& list = state_->sources;
    list.erase(std::remove_if(list.begin(), list.end(),
                   [src](const Entry& e) { return e.key == src || e.weak.expired(); }),
        list.end());
}

void StreamingIoScheduler::wake() noexcept {
    // Non-RT callers only, OR if called externally under lock.
    {
        std::lock_guard<std::mutex> lk(state_->mu);
        state_->dirty = true;
    }
    state_->cv.notify_one();
}

void StreamingIoScheduler::runLoop(std::shared_ptr<State> st) {
    std::vector<std::weak_ptr<StreamingAudioSource>> pending;
    std::vector<std::shared_ptr<StreamingAudioSource>> snapshot;
    for (;;) {
        {
            std::lock_guard<std::mutex> lk(st->mu);
            if (st->stop) break;
            st->dirty = false;
            pending.clear();
            auto& list = st->sources;
            for (auto it = list.begin(); it != list.end();) {
                if (it->weak.expired()) {
                    it = list.erase(it);
                } else {
                    pending.push_back(it->weak);
                    ++it;
                }
            }
        }

        // Lock weak_ptrs and run ~StreamingAudioSource (if last ref drops) strictly outside st->mu.
        bool anyWork = false;
        bool anyPlaying = false;
        snapshot.clear();
        snapshot.reserve(pending.size());
        for (const auto& w : pending) {
            if (auto sp = w.lock()) snapshot.push_back(std::move(sp));
        }

        for (auto& src : snapshot) {
            if (src->hasPendingPrefetch()) {
                if (src->servicePrefetchStep()) anyWork = true;
            }
        }

        for (auto& src : snapshot) {
            if (src->hasPendingPrefetch()) anyWork = true;
            if (src->isPlaybackActive()) anyPlaying = true;
        }
        snapshot.clear();
        pending.clear();

        if (anyWork) continue;

        std::unique_lock<std::mutex> lk(st->mu);
        if (st->stop) break;
        if (st->dirty) continue;
        // While actively rolling, poll lookahead deltas every 8 ms without locking mu on the RT thread;
        // when stopped/idle, park indefinitely -> 0.0% idle CPU.
        if (anyPlaying) {
            st->cv.wait_for(lk, std::chrono::milliseconds(8), [&] { return st->stop || st->dirty; });
            continue;
        }
        st->cv.wait(lk, [&] { return st->stop || st->dirty; });
    }
}

// ---- StreamingAudioSource --------------------------------------------------

StreamingAudioSource::~StreamingAudioSource() {
    if (scheduler_) scheduler_->unregisterSource(this);
}

bool StreamingAudioSource::probeHeader(
    const std::string& path, int32_t& outChannels, int64_t& outFrames, double& outSampleRate) {
    DecoderHandle dec;
    return dec.open(path, outChannels, outFrames, outSampleRate);
}

std::shared_ptr<StreamingAudioSource> StreamingAudioSource::open(const std::string& path,
    std::vector<float>* outPeakTable, std::vector<float>* outRmsTable, const float* seededPeaks,
    int64_t seededPeakCount) {
    auto src = std::shared_ptr<StreamingAudioSource>(new StreamingAudioSource());
    src->path_ = path;
    src->asyncDec_ = std::make_unique<DecoderHandle>();
    if (!src->asyncDec_->open(path, src->channels_, src->frames_, src->sampleRate_)) return nullptr;

    // Pre-allocate wait-free 4-way set-associative ring once up front (never grown on RT).
    const size_t pageFloats = static_cast<size_t>(kPageFrames) * static_cast<size_t>(src->channels_);
    src->slots_ = std::vector<PageSlot>(static_cast<size_t>(kRingPages));
    for (auto& s : src->slots_) s.data.assign(pageFloats, 0.0f);
    src->asyncScratch_.assign(pageFloats, 0.0f);
    src->syncScratch_.assign(pageFloats, 0.0f);

    const int64_t totalHead = std::min<int64_t>(src->frames_, static_cast<int64_t>(kHeadPages) * kPageFrames);
    src->headFrames_ = totalHead;
    src->headSamples_.assign(static_cast<size_t>(totalHead) * static_cast<size_t>(src->channels_), 0.0f);

    const bool needOverview = (outPeakTable != nullptr || outRmsTable != nullptr);
    const int64_t nBlocks = (src->frames_ + SampleBuffer::kPeakBlock - 1) / SampleBuffer::kPeakBlock;
    const bool useSeeded = !outRmsTable && seededPeaks && seededPeakCount == nBlocks * 2;
    if (outPeakTable) {
        if (useSeeded) outPeakTable->assign(seededPeaks, seededPeaks + seededPeakCount);
        else outPeakTable->assign(static_cast<size_t>(nBlocks) * 2, 0.0f);
    }
    if (outRmsTable) outRmsTable->assign(static_cast<size_t>(nBlocks), 0.0f);

    // Bounded sequential pass: fills pinned head frames AND computes exact peak/RMS tables if needed,
    // holding only a 16384-frame reusable scratch buffer (~128 KiB transient, freeing multi-100 MiB files).
    constexpr int64_t kScanFrames = 16384; // multiple of SampleBuffer::kPeakBlock (512)
    static_assert(kScanFrames % SampleBuffer::kPeakBlock == 0, "kScanFrames must align to peak block size");

    if (needOverview && !useSeeded) {
        std::vector<float> scan(static_cast<size_t>(kScanFrames) * static_cast<size_t>(src->channels_));
        src->asyncDec_->seekToFrame(0);
        int64_t pos = 0;
        while (pos < src->frames_) {
            const int64_t want = std::min<int64_t>(kScanFrames, src->frames_ - pos);
            const int64_t got = src->asyncDec_->readFrames(want, scan.data());
            if (got <= 0) break;

            if (pos < totalHead) {
                const int64_t copyFrames = std::min<int64_t>(got, totalHead - pos);
                std::memcpy(src->headSamples_.data() + pos * src->channels_, scan.data(),
                    sizeof(float) * static_cast<size_t>(copyFrames) * static_cast<size_t>(src->channels_));
            }

            const int64_t blockStart = pos / SampleBuffer::kPeakBlock;
            const int64_t blockEnd = (pos + got + SampleBuffer::kPeakBlock - 1) / SampleBuffer::kPeakBlock;
            for (int64_t b = blockStart; b < blockEnd && b < nBlocks; ++b) {
                const int64_t f0 = std::max<int64_t>(pos, b * SampleBuffer::kPeakBlock);
                const int64_t f1 = std::min<int64_t>(pos + got, (b + 1) * SampleBuffer::kPeakBlock);
                float mn = 1.0f, mx = -1.0f;
                double sq = 0.0;
                const int64_t cnt = f1 - f0;
                for (int64_t f = f0; f < f1; ++f) {
                    const size_t base = static_cast<size_t>(f - pos) * static_cast<size_t>(src->channels_);
                    const float l = scan[base];
                    const float r = src->channels_ >= 2 ? scan[base + 1] : l;
                    const float mid = 0.5f * (l + r);
                    mn = std::min(mn, mid);
                    mx = std::max(mx, mid);
                    const double dmid = 0.5 * (static_cast<double>(l) + static_cast<double>(r));
                    sq += dmid * dmid;
                }
                if (cnt > 0) {
                    if (outPeakTable) {
                        (*outPeakTable)[static_cast<size_t>(b) * 2] = mn;
                        (*outPeakTable)[static_cast<size_t>(b) * 2 + 1] = mx;
                    }
                    if (outRmsTable)
                        (*outRmsTable)[static_cast<size_t>(b)] = static_cast<float>(sq / static_cast<double>(cnt));
                }
            }
            pos += got;
        }
    } else if (totalHead > 0) {
        src->asyncDec_->seekToFrame(0);
        int64_t pos = 0;
        while (pos < totalHead) {
            const int64_t got = src->asyncDec_->readFrames(totalHead - pos, src->headSamples_.data() + pos * src->channels_);
            if (got <= 0) break;
            pos += got;
        }
    }

    src->scheduler_ = StreamingIoScheduler::shared();
    src->scheduler_->registerSource(src);
    return src;
}

int64_t StreamingAudioSource::memoryBytes() const noexcept {
    int64_t bytes = static_cast<int64_t>(headSamples_.capacity() * sizeof(float));
    bytes += static_cast<int64_t>(asyncScratch_.capacity() * sizeof(float));
    bytes += static_cast<int64_t>(syncScratch_.capacity() * sizeof(float));
    for (const auto& s : slots_) bytes += static_cast<int64_t>(s.data.capacity() * sizeof(float));
    return bytes;
}

bool StreamingAudioSource::isPageResident(int64_t page) const noexcept {
    if (page < 0 || page * kPageFrames >= frames_) return true;
    if (page < kHeadPages) return true;
    const int32_t set = static_cast<int32_t>(page & (kNumSets - 1));
    const int32_t base = set * kWays;
    for (int32_t w = 0; w < kWays; ++w) {
        const PageSlot& sl = slots_[static_cast<size_t>(base + w)];
        const uint64_t s1 = sl.seq.load(std::memory_order_acquire);
        if ((s1 & 1u) == 0 && s1 != 0 && sl.pageIdx.load(std::memory_order_relaxed) == page &&
            sl.seq.load(std::memory_order_acquire) == s1)
            return true;
    }
    return false;
}

bool StreamingAudioSource::hasPendingPrefetch() const noexcept {
    return needsPrefetch_.load(std::memory_order_acquire);
}

int32_t StreamingAudioSource::selectVictimSlot(int64_t page, int64_t focusPage) const noexcept {
    const int32_t set = static_cast<int32_t>(page & (kNumSets - 1));
    const int32_t base = set * kWays;
    int32_t best = base;
    int64_t worstScore = -1;
    for (int32_t w = 0; w < kWays; ++w) {
        const PageSlot& sl = slots_[static_cast<size_t>(base + w)];
        const int64_t p = sl.pageIdx.load(std::memory_order_relaxed);
        if (p < 0) return base + w;
        if (p == page) return base + w;
        const int64_t dist = std::abs(p - focusPage);
        // Prefer evicting pages outside the active window; tie-break by LRU stamp.
        const uint64_t age = accessStamp_.load(std::memory_order_relaxed) - sl.stamp.load(std::memory_order_relaxed);
        const int64_t score = (dist << 16) + static_cast<int64_t>(std::min<uint64_t>(age, 65535));
        if (score > worstScore) {
            worstScore = score;
            best = base + w;
        }
    }
    return best;
}

bool StreamingAudioSource::fillPageLocked(DecoderHandle& dec, int64_t page, bool isAsync) {
    if (page < 0 || page * kPageFrames >= frames_) return false;
    const int64_t totalPages = (frames_ + kPageFrames - 1) / kPageFrames;
    if (page >= totalPages) return false;
    const int64_t startFrame = page * kPageFrames;
    const int64_t want = std::min<int64_t>(kPageFrames, frames_ - startFrame);
    auto& scratch = isAsync ? asyncScratch_ : syncScratch_;
    std::fill(scratch.begin(), scratch.end(), 0.0f);
    if (!dec.seekToFrame(startFrame)) return false;
    int64_t readTotal = 0;
    while (readTotal < want) {
        const int64_t got = dec.readFrames(want - readTotal, scratch.data() + readTotal * channels_);
        if (got <= 0) break;
        readTotal += got;
    }

    const int64_t focus = cursorPage_.load(std::memory_order_relaxed);
    const int32_t slotIdx = selectVictimSlot(page, focus);
    PageSlot& sl = slots_[static_cast<size_t>(slotIdx)];
    const uint64_t curSeq = sl.seq.load(std::memory_order_relaxed);
    sl.seq.store(curSeq + 1, std::memory_order_release); // mark writer-busy (odd)
    std::atomic_thread_fence(std::memory_order_release);
    std::memcpy(sl.data.data(), scratch.data(), sizeof(float) * scratch.size());
    sl.pageIdx.store(page, std::memory_order_relaxed);
    sl.stamp.store(accessStamp_.fetch_add(1, std::memory_order_relaxed), std::memory_order_relaxed);
    sl.seq.store(curSeq + 2, std::memory_order_release); // even = valid & stable

    pagesFaulted_.fetch_add(1, std::memory_order_relaxed);
    if (isAsync) pagesServedAsync_.fetch_add(1, std::memory_order_relaxed);
    else pagesServedSync_.fetch_add(1, std::memory_order_relaxed);
    return true;
}

bool StreamingAudioSource::fillPageSync(int64_t page, int32_t* outSlot) const {
    if (page < kHeadPages) return true;
    std::lock_guard<std::mutex> lk(ioMu_);
    // Re-check once lock acquired in case background worker just filled it.
    const int32_t set = static_cast<int32_t>(page & (kNumSets - 1));
    const int32_t base = set * kWays;
    for (int32_t w = 0; w < kWays; ++w) {
        const PageSlot& sl = slots_[static_cast<size_t>(base + w)];
        const uint64_t s = sl.seq.load(std::memory_order_acquire);
        if ((s & 1u) == 0 && s != 0 && sl.pageIdx.load(std::memory_order_relaxed) == page) {
            if (outSlot) *outSlot = base + w;
            return true;
        }
    }
    if (!syncDec_) {
        syncDec_ = std::make_unique<DecoderHandle>();
        int32_t ch = 0;
        int64_t fr = 0;
        double sr = 0.0;
        if (!syncDec_->open(path_, ch, fr, sr)) {
            syncDec_.reset();
            return false;
        }
    }
    StreamingAudioSource* mut = const_cast<StreamingAudioSource*>(this);
    const bool ok = mut->fillPageLocked(*syncDec_, page, /*isAsync=*/false);
    if (ok && outSlot) {
        for (int32_t w = 0; w < kWays; ++w)
            if (slots_[static_cast<size_t>(base + w)].pageIdx.load(std::memory_order_relaxed) == page) {
                *outSlot = base + w;
                break;
            }
    }
    return ok;
}

bool StreamingAudioSource::servicePrefetchStep() {
    std::lock_guard<std::mutex> lk(ioMu_);
    if (!asyncDec_) return false;
    const int64_t cur = cursorPage_.load(std::memory_order_acquire);
    const bool rev = cursorReverse_.load(std::memory_order_acquire);
    const int64_t totalPages = (frames_ + kPageFrames - 1) / kPageFrames;

    // Fetch missing pages outward in playback direction first.
    int32_t filledAny = 0;
    const int32_t span = kLookaheadPages + kTrailingPages;
    for (int32_t step = -kTrailingPages; step <= span && filledAny < 4; ++step) {
        const int64_t p = rev ? (cur - step) : (cur + step);
        if (p < kHeadPages || p >= totalPages) continue;
        if (isPageResident(p)) continue;
        if (fillPageLocked(*asyncDec_, p, /*isAsync=*/true)) ++filledAny;
    }

    // Check if the full lookahead window is now satisfied; if yes, allow worker to sleep cleanly.
    bool stillMissing = false;
    for (int32_t step = -kTrailingPages; step <= kLookaheadPages; ++step) {
        const int64_t p = rev ? (cur - step) : (cur + step);
        if (p >= kHeadPages && p < totalPages && !isPageResident(p)) {
            stillMissing = true;
            break;
        }
    }
    needsPrefetch_.store(stillMissing, std::memory_order_release);
    return filledAny > 0;
}

bool StreamingAudioSource::findSlotLockFree(
    int64_t page, int32_t& slotHint, int64_t inPage, float& l, float& r) const noexcept {
    const int32_t ch = channels_;
    if (inPage < 0 || inPage >= kPageFrames || ch <= 0) return false;
    const size_t off = static_cast<size_t>(inPage) * static_cast<size_t>(ch);

    if (slotHint >= 0 && slotHint < kRingPages) {
        const PageSlot& sl = slots_[static_cast<size_t>(slotHint)];
        const uint64_t s1 = sl.seq.load(std::memory_order_acquire);
        if ((s1 & 1u) == 0 && s1 != 0 && sl.pageIdx.load(std::memory_order_relaxed) == page) {
            const float vL = sl.data[off];
            const float vR = ch >= 2 ? sl.data[off + 1] : vL;
            if (sl.seq.load(std::memory_order_acquire) == s1) {
                l = vL;
                r = vR;
                return true;
            }
        }
    }

    const int32_t set = static_cast<int32_t>(page & (kNumSets - 1));
    const int32_t base = set * kWays;
    for (int32_t w = 0; w < kWays; ++w) {
        const int32_t idx = base + w;
        const PageSlot& sl = slots_[static_cast<size_t>(idx)];
        const uint64_t s1 = sl.seq.load(std::memory_order_acquire);
        if ((s1 & 1u) == 0 && s1 != 0 && sl.pageIdx.load(std::memory_order_relaxed) == page) {
            const float vL = sl.data[off];
            const float vR = ch >= 2 ? sl.data[off + 1] : vL;
            if (sl.seq.load(std::memory_order_acquire) == s1) {
                slotHint = idx;
                l = vL;
                r = vR;
                return true;
            }
        }
    }
    return false;
}

bool StreamingAudioSource::findSlotExactCh(int64_t page, int64_t inPage, int32_t ch, float& outVal) const noexcept {
    if (inPage < 0 || inPage >= kPageFrames || ch < 0 || ch >= channels_) return false;
    const size_t off = static_cast<size_t>(inPage) * static_cast<size_t>(channels_) + static_cast<size_t>(ch);
    const int32_t set = static_cast<int32_t>(page & (kNumSets - 1));
    const int32_t base = set * kWays;
    for (int32_t w = 0; w < kWays; ++w) {
        const PageSlot& sl = slots_[static_cast<size_t>(base + w)];
        const uint64_t s1 = sl.seq.load(std::memory_order_acquire);
        if ((s1 & 1u) == 0 && s1 != 0 && sl.pageIdx.load(std::memory_order_relaxed) == page) {
            const float v = sl.data[off];
            if (sl.seq.load(std::memory_order_acquire) == s1) {
                outVal = v;
                return true;
            }
        }
    }
    return false;
}

float StreamingAudioSource::at(int64_t frame, int32_t ch) const noexcept {
    if (frame < 0 || frame >= frames_ || ch < 0 || ch >= channels_) return 0.0f;
    if (frame < headFrames_) return headSamples_[static_cast<size_t>(frame) * channels_ + static_cast<size_t>(ch)];
    const int64_t page = frame / kPageFrames;
    const int64_t inPage = frame - page * kPageFrames;
    float val = 0.0f;
    if (findSlotExactCh(page, inPage, ch, val)) return val;
    if (!playbackActive_.load(std::memory_order_relaxed)) {
        // Non-RT callers (Warp build / TempoDetect / SampleAnalysis) when playback isn't active sync-fill safely.
        if (fillPageSync(page) && findSlotExactCh(page, inPage, ch, val)) return val;
    } else {
        cursorPage_.store(page, std::memory_order_relaxed);
        needsPrefetch_.store(true, std::memory_order_release);
        rtUnderruns_.fetch_add(1, std::memory_order_relaxed);
    }
    return 0.0f;
}

void StreamingAudioSource::readStereo(int64_t frame, float& l, float& r) const noexcept {
    int32_t hint = -1;
    readStereoHinted(frame, hint, /*allowSyncFill=*/!playbackActive_.load(std::memory_order_relaxed), l, r);
}

void StreamingAudioSource::readStereoHinted(
    int64_t frame, int32_t& slotHint, bool allowSyncFill, float& l, float& r) const noexcept {
    if (frame < 0 || frame >= frames_ || channels_ <= 0) {
        l = 0.0f;
        r = 0.0f;
        return;
    }
    if (frame < headFrames_) {
        const size_t base = static_cast<size_t>(frame) * static_cast<size_t>(channels_);
        l = headSamples_[base];
        r = channels_ >= 2 ? headSamples_[base + 1] : l;
        return;
    }
    const int64_t page = frame / kPageFrames;
    const int64_t inPage = frame - page * kPageFrames;
    if (findSlotLockFree(page, slotHint, inPage, l, r)) return;
    if (allowSyncFill) {
        if (fillPageSync(page, &slotHint) && findSlotLockFree(page, slotHint, inPage, l, r)) return;
    } else {
        cursorPage_.store(page, std::memory_order_relaxed);
        needsPrefetch_.store(true, std::memory_order_release);
        rtUnderruns_.fetch_add(1, std::memory_order_relaxed);
    }
    l = 0.0f;
    r = 0.0f;
}

void StreamingAudioSource::preparePlaybackWindow(
    int64_t minFrame, int64_t maxFrame, bool reverse, bool allowSyncFill) const noexcept {
    if (frames_ <= 0) return;
    minFrame = std::clamp<int64_t>(minFrame, 0, frames_ - 1);
    maxFrame = std::clamp<int64_t>(maxFrame, 0, frames_ - 1);
    if (minFrame > maxFrame) std::swap(minFrame, maxFrame);
    const int64_t p0 = minFrame / kPageFrames;
    const int64_t p1 = maxFrame / kPageFrames;
    const int64_t focus = reverse ? p1 : p0;
    cursorReverse_.store(reverse, std::memory_order_relaxed);
    cursorPage_.store(focus, std::memory_order_relaxed);
    targetEndPage_.store(reverse ? p0 : p1, std::memory_order_relaxed);

    if (allowSyncFill) {
        for (int64_t p = p0; p <= p1; ++p)
            if (!isPageResident(p)) fillPageSync(p);
        return;
    }

    // Strict AR-6 RT path: only inspect lock-free atomic flags to request worker fill if deficit exists.
    const int64_t totalPages = (frames_ + kPageFrames - 1) / kPageFrames;
    const int64_t lookAheadEnd = reverse ? std::max<int64_t>(kHeadPages, p0 - 4) : std::min<int64_t>(totalPages - 1, p1 + 6);
    const int64_t checkLo = reverse ? lookAheadEnd : p0;
    const int64_t checkHi = reverse ? p1 : lookAheadEnd;
    for (int64_t p = checkLo; p <= checkHi; ++p) {
        if (!isPageResident(p)) {
            needsPrefetch_.store(true, std::memory_order_release);
            break;
        }
    }
}

void StreamingAudioSource::primeAround(int64_t frame, bool reverse) const {
    if (frames_ <= 0) return;
    frame = std::clamp<int64_t>(frame, 0, frames_ - 1);
    const int64_t focus = frame / kPageFrames;
    cursorReverse_.store(reverse, std::memory_order_relaxed);
    cursorPage_.store(focus, std::memory_order_relaxed);
    const int64_t totalPages = (frames_ + kPageFrames - 1) / kPageFrames;
    const int64_t pLo = std::max<int64_t>(kHeadPages, reverse ? focus - 2 : focus);
    const int64_t pHi = std::min<int64_t>(totalPages - 1, reverse ? focus : focus + 2);
    for (int64_t p = pLo; p <= pHi; ++p)
        if (!isPageResident(p)) fillPageSync(p);
    needsPrefetch_.store(true, std::memory_order_release);
    if (scheduler_) scheduler_->wake();
}

int64_t StreamingAudioSource::readAllInterleaved(float* out, int64_t capFloats) const {
    const int64_t total = frames_ * static_cast<int64_t>(channels_);
    if (!out || capFloats <= 0) return total;

    DecoderHandle dec;
    int32_t ch = 0;
    int64_t fr = 0;
    double sr = 0.0;
    if (!dec.open(path_, ch, fr, sr)) return 0;

    const int64_t wantFrames = std::min<int64_t>(frames_, capFloats / std::max<int32_t>(1, channels_));
    int64_t done = 0;
    while (done < wantFrames) {
        const int64_t chunk = std::min<int64_t>(16384, wantFrames - done);
        const int64_t got = dec.readFrames(chunk, out + done * channels_);
        if (got <= 0) break;
        done += got;
    }
    return total;
}

StreamRuntimeStats StreamingAudioSource::runtimeStats() const noexcept {
    StreamRuntimeStats st;
    st.cacheBytes = memoryBytes();
    st.pagesFaulted = pagesFaulted_.load(std::memory_order_relaxed);
    st.pagesServedAsync = pagesServedAsync_.load(std::memory_order_relaxed);
    st.pagesServedSync = pagesServedSync_.load(std::memory_order_relaxed);
    st.rtUnderruns = rtUnderruns_.load(std::memory_order_relaxed);
    return st;
}

void StreamingAudioSource::setPlaybackActive(bool active) const noexcept {
    playbackActive_.store(active, std::memory_order_relaxed);
}

} // namespace nota
