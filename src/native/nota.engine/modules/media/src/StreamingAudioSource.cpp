// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/media/StreamingAudioSource.h"
#include "nota/media/AudioCodecRegistry.h"
#include "nota/media/AudioSource.h"
#include "nota/media/AudioCodecTypes.h"
#include "nota/media/IAudioDecoder.h"
#include "nota/media/SampleBuffer.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <vector>
#include <utility>
#include <thread>
#include <string>
#include <mutex>
#include <memory>

namespace nota::media {

namespace {

std::mutex g_schedMu;
std::weak_ptr<StreamingIoScheduler> g_sharedSched;

} // namespace

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
        if (std::this_thread::get_id() == worker_.get_id()) worker_.detach();
        else
            worker_.join();
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
    list.erase(
        std::remove_if(list.begin(), list.end(), [src](const Entry& e) { return e.key == src || e.weak.expired(); }),
        list.end());
}

void StreamingIoScheduler::wake() noexcept {
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

        bool anyWork = false;
        bool anyPlaying = false;
        snapshot.clear();
        snapshot.reserve(pending.size());
        for (const auto& w : pending) {
            if (auto sp = w.lock()) snapshot.push_back(std::move(sp));
        }

        for (auto& src : snapshot) {
            if (src->hasPendingPrefetch() && src->servicePrefetchStep()) anyWork = true;
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
        if (anyPlaying) {
            st->cv.wait_for(lk, std::chrono::milliseconds(8), [&] { return st->stop || st->dirty; });
            continue;
        }
        st->cv.wait(lk, [&] { return st->stop || st->dirty; });
    }
}

StreamingAudioSource::StreamingAudioSource(PrivateTag) {}

StreamingAudioSource::~StreamingAudioSource() {
    if (scheduler_) scheduler_->unregisterSource(this);
}

bool StreamingAudioSource::probeHeader(
    const std::string& path, int32_t& outChannels, int64_t& outFrames, double& outSampleRate) {
    auto dec = AudioCodecRegistry::instance().openDecoder(path);
    if (!dec) return false;
    const AudioStreamInfo info = dec->info();
    if (!info.valid() || !info.seekable || info.totalFrames <= 0) return false;
    outChannels = info.channels;
    outFrames = info.totalFrames;
    outSampleRate = info.sampleRate;
    return true;
}

std::shared_ptr<StreamingAudioSource> StreamingAudioSource::open(const std::string& path,
    std::vector<float>* outPeakTable, std::vector<float>* outRmsTable, const float* seededPeaks,
    int64_t seededPeakCount) {
    auto dec = AudioCodecRegistry::instance().openDecoder(path);
    if (!dec) return nullptr;
    const AudioStreamInfo info = dec->info();
    if (!info.valid() || !info.seekable || info.totalFrames <= 0) return nullptr;

    auto src = std::make_shared<StreamingAudioSource>(PrivateTag{});
    src->path_ = path;
    src->asyncDec_ = std::move(dec);
    src->channels_ = info.channels;
    src->frames_ = info.totalFrames;
    src->sampleRate_ = info.sampleRate;

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
        else
            outPeakTable->assign(static_cast<size_t>(nBlocks) * 2, 0.0f);
    }
    if (outRmsTable) outRmsTable->assign(static_cast<size_t>(nBlocks), 0.0f);

    constexpr int64_t kScanFrames = 16384;
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
                if (f1 > f0) {
                    float mn = 0.0f;
                    float mx = 0.0f;
                    float meanSq = 0.0f;
                    SampleBuffer::computeBlockPeakAndMeanSq(
                        scan.data() + static_cast<size_t>(f0 - pos) * static_cast<size_t>(src->channels_),
                        src->channels_, f1 - f0, mn, mx, meanSq);
                    if (outPeakTable) {
                        (*outPeakTable)[static_cast<size_t>(b) * 2] = mn;
                        (*outPeakTable)[static_cast<size_t>(b) * 2 + 1] = mx;
                    }
                    if (outRmsTable) (*outRmsTable)[static_cast<size_t>(b)] = meanSq;
                }
            }
            pos += got;
        }
    } else if (totalHead > 0) {
        src->asyncDec_->seekToFrame(0);
        int64_t pos = 0;
        while (pos < totalHead) {
            const int64_t got =
                src->asyncDec_->readFrames(totalHead - pos, src->headSamples_.data() + pos * src->channels_);
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
        if (p < 0 || p == page) return base + w;
        const int64_t dist = std::abs(p - focusPage);
        const uint64_t age = accessStamp_.load(std::memory_order_relaxed) - sl.stamp.load(std::memory_order_relaxed);
        const int64_t score = (dist << 16) + static_cast<int64_t>(std::min<uint64_t>(age, 65535));
        if (score > worstScore) {
            worstScore = score;
            best = base + w;
        }
    }
    return best;
}

bool StreamingAudioSource::fillPageLocked(IAudioDecoder& dec, int64_t page, bool isAsync) {
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
    sl.seq.store(curSeq + 1, std::memory_order_release);
    std::atomic_thread_fence(std::memory_order_release);
    std::memcpy(sl.data.data(), scratch.data(), sizeof(float) * scratch.size());
    sl.pageIdx.store(page, std::memory_order_relaxed);
    sl.stamp.store(accessStamp_.fetch_add(1, std::memory_order_relaxed), std::memory_order_relaxed);
    sl.seq.store(curSeq + 2, std::memory_order_release);

    pagesFaulted_.fetch_add(1, std::memory_order_relaxed);
    if (isAsync) pagesServedAsync_.fetch_add(1, std::memory_order_relaxed);
    else
        pagesServedSync_.fetch_add(1, std::memory_order_relaxed);
    return true;
}

bool StreamingAudioSource::fillPageSync(int64_t page, int32_t* outSlot) const {
    if (page < kHeadPages) return true;
    std::lock_guard<std::mutex> lk(ioMu_);
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
        syncDec_ = AudioCodecRegistry::instance().openDecoder(path_);
        if (!syncDec_) return false;
    }
    auto* mut = const_cast<StreamingAudioSource*>(this);
    const bool ok = mut->fillPageLocked(*syncDec_, page, false);
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

    int32_t filledAny = 0;
    const int32_t span = kLookaheadPages + kTrailingPages;
    for (int32_t step = -kTrailingPages; step <= span && filledAny < 4; ++step) {
        const int64_t p = rev ? (cur - step) : (cur + step);
        if (p < kHeadPages || p >= totalPages) continue;
        if (isPageResident(p)) continue;
        if (fillPageLocked(*asyncDec_, p, true)) ++filledAny;
    }

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
    if (frame < headFrames_) {
        return headSamples_[static_cast<size_t>(frame) * static_cast<size_t>(channels_) + static_cast<size_t>(ch)];
    }
    const int64_t page = frame / kPageFrames;
    const int64_t inPage = frame - page * kPageFrames;
    float val = 0.0f;
    if (findSlotExactCh(page, inPage, ch, val)) return val;
    if (!playbackActive_.load(std::memory_order_relaxed)) {
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
    readStereoHinted(frame, hint, !playbackActive_.load(std::memory_order_relaxed), l, r);
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

    const int64_t totalPages = (frames_ + kPageFrames - 1) / kPageFrames;
    const int64_t lookAheadEnd =
        reverse ? std::max<int64_t>(kHeadPages, p0 - 4) : std::min<int64_t>(totalPages - 1, p1 + 6);
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

    auto dec = AudioCodecRegistry::instance().openDecoder(path_);
    if (!dec) return 0;

    const int64_t wantFrames = std::min<int64_t>(frames_, capFloats / std::max<int32_t>(1, channels_));
    int64_t done = 0;
    while (done < wantFrames) {
        const int64_t chunk = std::min<int64_t>(16384, wantFrames - done);
        const int64_t got = dec->readFrames(chunk, out + done * channels_);
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
    playbackActive_.store(active, std::memory_order_release);
}

} // namespace nota::media
