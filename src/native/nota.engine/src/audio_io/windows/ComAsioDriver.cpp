// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "ComAsioDriver.h"

#if defined(_WIN32)

#include <windows.h>
#include <objbase.h>
#include <avrt.h>

// Steinberg ASIO SDK headers (GPLv3 option; LICENSES/third-party.md note 8).
#include "asiosys.h"
#include "asio.h"
#include "iasiodrv.h"

#include <atomic>
#include <cstdio>
#include <cstring>
#include <deque>
#include <functional>
#include <future>
#include <mutex>
#include <vector>

namespace nota::asio {

struct ComAsioDriver::Shared {
    std::mutex m;
    std::deque<std::function<void()>> queue;
    HANDLE wake = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    bool quit = false;           // control thread only
    IASIO* drv = nullptr;        // created, used and released on the control thread
    HWND window = nullptr;       // the driver's sysHandle (parent of its panel)
    std::atomic<bool> abandoned{false};
    std::atomic<bool> panelOpen{false}; // inside drv->controlPanel()
    std::promise<void> exited;          // set by the thread after Release() + CoUninitialize()

    ~Shared() {
        if (wake) CloseHandle(wake);
    }
    void post(std::function<void()> fn) {
        {
            std::lock_guard<std::mutex> lock(m);
            queue.push_back(std::move(fn));
        }
        SetEvent(wake);
    }
};

namespace {

constexpr unsigned kLoadTimeoutMs = 5000;
constexpr unsigned kCallTimeoutMs = 2000;

// The SDK's ASIOCallbacks hold plain function pointers and must stay valid while buffers
// exist; one driver per process, so one static set forwards to the session's callbacks.
Callbacks g_cb;
// Open only between createBuffers and disposeBuffers of a live driver. An abandoned driver
// (a call timed out) is cut off here, and no other driver opens until Nota restarts: its
// thread may still be inside the driver and call back later, and ASIO callbacks carry
// nothing that tells two drivers apart.
std::atomic<bool> g_routeOpen{false};
std::atomic<bool> g_driverAbandoned{false};

void abandon(ComAsioDriver::Shared& sh) {
    sh.abandoned.store(true);
    g_routeOpen.store(false, std::memory_order_release);
    g_driverAbandoned.store(true);
}

void ensureProAudioThread() {
    // The buffer thread is the engine's audio thread: make sure it runs under MMCSS
    // "Pro Audio" if the driver didn't already raise it (ASIO SDK 2.3 § MMCSS). Once per thread.
    thread_local bool done = false;
    if (done) return;
    done = true;
    if (GetThreadPriority(GetCurrentThread()) >= THREAD_PRIORITY_TIME_CRITICAL) return;
    DWORD task = 0;
    AvSetMmThreadCharacteristicsW(L"Pro Audio", &task);
}

void sdkBufferSwitch(long index, ASIOBool) {
    if (!g_routeOpen.load(std::memory_order_acquire)) return;
    ensureProAudioThread();
    if (g_cb.bufferSwitch) g_cb.bufferSwitch(static_cast<int32_t>(index), -1);
}

ASIOTime* sdkBufferSwitchTimeInfo(ASIOTime* t, long index, ASIOBool) {
    if (!g_routeOpen.load(std::memory_order_acquire)) return t;
    ensureProAudioThread();
    int64_t pos = -1;
    if (t && (t->timeInfo.flags & kSamplePositionValid))
        pos = static_cast<int64_t>((static_cast<uint64_t>(t->timeInfo.samplePosition.hi) << 32) |
                                   static_cast<uint64_t>(t->timeInfo.samplePosition.lo));
    if (g_cb.bufferSwitch) g_cb.bufferSwitch(static_cast<int32_t>(index), pos);
    return t;
}

void sdkSampleRateDidChange(ASIOSampleRate rate) {
    if (g_routeOpen.load(std::memory_order_acquire) && g_cb.sampleRateDidChange) g_cb.sampleRateDidChange(rate);
}

long sdkAsioMessage(long selector, long value, void*, double*) {
    // Messages also arrive inside createBuffers (time-info queries), before the route opens.
    return g_cb.message && !g_driverAbandoned.load() ? g_cb.message(static_cast<int32_t>(selector), static_cast<int32_t>(value)) : 0;
}

ASIOCallbacks g_sdkCallbacks = {&sdkBufferSwitch, &sdkSampleRateDidChange, &sdkAsioMessage,
                                &sdkBufferSwitchTimeInfo};

std::string ansiToUtf8(const char* s) {
    if (!s || !*s) return {};
    const int wn = MultiByteToWideChar(CP_ACP, 0, s, -1, nullptr, 0);
    std::wstring w(static_cast<size_t>(wn), L'\0');
    MultiByteToWideChar(CP_ACP, 0, s, -1, w.data(), wn);
    const int n = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string out(static_cast<size_t>(n), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, out.data(), n, nullptr, nullptr);
    while (!out.empty() && out.back() == '\0') out.pop_back();
    return out;
}

void controlThread(std::shared_ptr<ComAsioDriver::Shared> sh) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    sh->window = CreateWindowExW(0, L"STATIC", L"Nota ASIO", WS_POPUP, 0, 0, 0, 0, nullptr, nullptr,
                                 GetModuleHandleW(nullptr), nullptr);
    while (!sh->quit) {
        MsgWaitForMultipleObjects(1, &sh->wake, FALSE, INFINITE, QS_ALLINPUT);
        for (;;) {
            std::function<void()> fn;
            {
                std::lock_guard<std::mutex> lock(sh->m);
                if (sh->queue.empty()) break;
                fn = std::move(sh->queue.front());
                sh->queue.pop_front();
            }
            fn();
        }
        MSG msg;
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    if (sh->drv) sh->drv->Release();
    sh->drv = nullptr;
    if (sh->window) DestroyWindow(sh->window);
    CoUninitialize();
    sh->exited.set_value();
}

} // namespace

ComAsioDriver::ComAsioDriver()
    : sh_(std::make_shared<Shared>()), exited_(sh_->exited.get_future()), thread_(controlThread, sh_) {}

ComAsioDriver::~ComAsioDriver() {
    if (!sh_->abandoned.load()) {
        // The thread releases the driver on its way out; wait for that, not just for the
        // quit task, so a Release() that hangs can't hang the caller in join().
        sh_->post([sh = sh_] { sh->quit = true; });
        if (exited_.wait_for(std::chrono::milliseconds(kCallTimeoutMs)) == std::future_status::ready) {
            thread_.join();
            return;
        }
        abandon(*sh_);
    }
    thread_.detach(); // stuck inside the driver: leave it (and the driver object) behind
}

// Run `fn` on the control thread and wait. Everything `fn` touches must be captured by
// value or shared_ptr: after a timeout the caller is gone but the task may still run.
template <class F> bool ComAsioDriver::call(F fn, unsigned timeoutMs) {
    if (sh_->abandoned.load()) return false;
    auto done = std::make_shared<std::promise<void>>();
    auto fut = done->get_future();
    sh_->post([fn = std::move(fn), done]() mutable {
        fn();
        done->set_value();
    });
    // Never wait open-ended here: the caller is the UI thread. While the driver's panel is open
    // the app holds restarts back (nota_audio_asio_panel_open), so a call stuck behind the
    // panel only happens on the way out of Nota — where giving up on the driver is fine.
    if (fut.wait_for(std::chrono::milliseconds(timeoutMs)) == std::future_status::ready) return true;
    abandon(*sh_);
    return false;
}

std::unique_ptr<ComAsioDriver> ComAsioDriver::create(const std::string& clsid, bool& timedOut, std::string& error) {
    timedOut = false;
    if (g_driverAbandoned.load()) {
        error = "an earlier ASIO driver stopped responding — restart Nota to use ASIO again";
        return nullptr;
    }
    const int wn = MultiByteToWideChar(CP_UTF8, 0, clsid.c_str(), -1, nullptr, 0);
    std::wstring w(static_cast<size_t>(wn > 0 ? wn : 1), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, clsid.c_str(), -1, w.data(), wn);
    CLSID id{};
    if (CLSIDFromString(w.c_str(), &id) != NOERROR) {
        error = "bad CLSID";
        return nullptr;
    }
    std::unique_ptr<ComAsioDriver> d(new ComAsioDriver());
    auto hr = std::make_shared<HRESULT>(E_FAIL);
    // ASIO drivers register their CLSID as the interface id too.
    if (!d->call(
            [sh = d->sh_, id, hr] {
                IASIO* p = nullptr;
                *hr = CoCreateInstance(id, nullptr, CLSCTX_INPROC_SERVER, id, reinterpret_cast<void**>(&p));
                if (SUCCEEDED(*hr)) sh->drv = p;
            },
            kLoadTimeoutMs)) {
        timedOut = true;
        error = "timed out";
        return nullptr;
    }
    if (FAILED(*hr)) {
        char buf[32];
        snprintf(buf, sizeof buf, "0x%08lX", static_cast<unsigned long>(*hr));
        error = buf;
        return nullptr;
    }
    return d;
}

bool ComAsioDriver::timedOut() const {
    return sh_->abandoned.load();
}

bool ComAsioDriver::panelOpen() const {
    return sh_->panelOpen.load();
}

bool ComAsioDriver::init() {
    auto ok = std::make_shared<bool>(false);
    return call([sh = sh_, ok] { *ok = sh->drv && sh->drv->init(sh->window) == ASIOTrue; }, kLoadTimeoutMs) && *ok;
}

std::string ComAsioDriver::driverName() {
    auto s = std::make_shared<std::string>();
    call(
        [sh = sh_, s] {
            char name[128] = {};
            if (sh->drv) sh->drv->getDriverName(name);
            *s = ansiToUtf8(name);
        },
        kCallTimeoutMs);
    return *s;
}

std::string ComAsioDriver::errorMessage() {
    auto s = std::make_shared<std::string>();
    call(
        [sh = sh_, s] {
            char text[256] = {};
            if (sh->drv) sh->drv->getErrorMessage(text);
            *s = ansiToUtf8(text);
        },
        kCallTimeoutMs);
    return *s;
}

bool ComAsioDriver::getChannels(int32_t& inputs, int32_t& outputs) {
    struct R {
        long in = 0, out = 0;
        ASIOError e = ASE_NotPresent;
    };
    auto r = std::make_shared<R>();
    if (!call([sh = sh_, r] { if (sh->drv) r->e = sh->drv->getChannels(&r->in, &r->out); }, kCallTimeoutMs) ||
        r->e != ASE_OK)
        return false;
    inputs = static_cast<int32_t>(r->in);
    outputs = static_cast<int32_t>(r->out);
    return true;
}

bool ComAsioDriver::getBufferSize(int32_t& minSize, int32_t& maxSize, int32_t& preferred, int32_t& granularity) {
    struct R {
        long mn = 0, mx = 0, pref = 0, gran = 0;
        ASIOError e = ASE_NotPresent;
    };
    auto r = std::make_shared<R>();
    if (!call([sh = sh_, r] { if (sh->drv) r->e = sh->drv->getBufferSize(&r->mn, &r->mx, &r->pref, &r->gran); },
              kCallTimeoutMs) ||
        r->e != ASE_OK)
        return false;
    minSize = static_cast<int32_t>(r->mn);
    maxSize = static_cast<int32_t>(r->mx);
    preferred = static_cast<int32_t>(r->pref);
    granularity = static_cast<int32_t>(r->gran);
    return true;
}

bool ComAsioDriver::canSampleRate(double rate) {
    auto e = std::make_shared<ASIOError>(ASE_NotPresent);
    return call([sh = sh_, e, rate] { if (sh->drv) *e = sh->drv->canSampleRate(rate); }, kCallTimeoutMs) &&
           *e == ASE_OK;
}

bool ComAsioDriver::getSampleRate(double& rate) {
    struct R {
        ASIOSampleRate rate = 0.0;
        ASIOError e = ASE_NotPresent;
    };
    auto r = std::make_shared<R>();
    if (!call([sh = sh_, r] { if (sh->drv) r->e = sh->drv->getSampleRate(&r->rate); }, kCallTimeoutMs) ||
        r->e != ASE_OK)
        return false;
    rate = r->rate;
    return true;
}

bool ComAsioDriver::setSampleRate(double rate) {
    auto e = std::make_shared<ASIOError>(ASE_NotPresent);
    return call([sh = sh_, e, rate] { if (sh->drv) *e = sh->drv->setSampleRate(rate); }, kLoadTimeoutMs) &&
           *e == ASE_OK;
}

bool ComAsioDriver::getChannelInfo(ChannelInfo& info) {
    auto ci = std::make_shared<ASIOChannelInfo>();
    ci->channel = info.index;
    ci->isInput = info.isInput ? ASIOTrue : ASIOFalse;
    auto e = std::make_shared<ASIOError>(ASE_NotPresent);
    if (!call([sh = sh_, ci, e] { if (sh->drv) *e = sh->drv->getChannelInfo(ci.get()); }, kCallTimeoutMs) ||
        *e != ASE_OK)
        return false;
    info.type = static_cast<SampleType>(ci->type);
    char name[33] = {};
    memcpy(name, ci->name, 32);
    info.name = ansiToUtf8(name);
    return true;
}

bool ComAsioDriver::createBuffers(BufferInfo* infos, int32_t count, int32_t bufferSize, const Callbacks& cb) {
    if (!infos || count <= 0) return false;
    g_cb = cb;
    auto sdk = std::make_shared<std::vector<ASIOBufferInfo>>(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) {
        (*sdk)[i].isInput = infos[i].isInput ? ASIOTrue : ASIOFalse;
        (*sdk)[i].channelNum = infos[i].channel;
        (*sdk)[i].buffers[0] = (*sdk)[i].buffers[1] = nullptr;
    }
    auto e = std::make_shared<ASIOError>(ASE_NotPresent);
    if (!call(
            [sh = sh_, sdk, e, bufferSize] {
                if (sh->drv)
                    *e = sh->drv->createBuffers(sdk->data(), static_cast<long>(sdk->size()), bufferSize,
                                                &g_sdkCallbacks);
            },
            kLoadTimeoutMs) ||
        *e != ASE_OK)
        return false;
    for (int32_t i = 0; i < count; ++i) {
        infos[i].buffers[0] = (*sdk)[i].buffers[0];
        infos[i].buffers[1] = (*sdk)[i].buffers[1];
    }
    g_routeOpen.store(true, std::memory_order_release);
    return true;
}

bool ComAsioDriver::disposeBuffers() {
    g_routeOpen.store(false, std::memory_order_release); // the session has already stopped the driver
    auto e = std::make_shared<ASIOError>(ASE_NotPresent);
    return call([sh = sh_, e] { if (sh->drv) *e = sh->drv->disposeBuffers(); }, kCallTimeoutMs) && *e == ASE_OK;
}

bool ComAsioDriver::start() {
    auto e = std::make_shared<ASIOError>(ASE_NotPresent);
    return call([sh = sh_, e] { if (sh->drv) *e = sh->drv->start(); }, kLoadTimeoutMs) && *e == ASE_OK;
}

bool ComAsioDriver::stop() {
    auto e = std::make_shared<ASIOError>(ASE_NotPresent);
    return call([sh = sh_, e] { if (sh->drv) *e = sh->drv->stop(); }, kCallTimeoutMs) && *e == ASE_OK;
}

bool ComAsioDriver::getLatencies(int32_t& input, int32_t& output) {
    struct R {
        long in = 0, out = 0;
        ASIOError e = ASE_NotPresent;
    };
    auto r = std::make_shared<R>();
    if (!call([sh = sh_, r] { if (sh->drv) r->e = sh->drv->getLatencies(&r->in, &r->out); }, kCallTimeoutMs) ||
        r->e != ASE_OK)
        return false;
    input = static_cast<int32_t>(r->in);
    output = static_cast<int32_t>(r->out);
    return true;
}

bool ComAsioDriver::controlPanel() {
    // Posted, not waited on: the panel may be modal and stay open for as long as the user likes.
    if (sh_->abandoned.load()) return false;
    sh_->panelOpen.store(true); // before posting: a reset sent at once must already wait
    sh_->post([sh = sh_] {
        if (sh->drv) sh->drv->controlPanel();
        sh->panelOpen.store(false);
    });
    return true;
}

bool ComAsioDriver::probeOutputReady() {
    auto e = std::make_shared<ASIOError>(ASE_NotPresent);
    return call([sh = sh_, e] { if (sh->drv) *e = sh->drv->outputReady(); }, kCallTimeoutMs) && *e == ASE_OK;
}

bool ComAsioDriver::outputReady() {
    // From the buffer switch (and once from the session to probe support): a direct call.
    IASIO* d = sh_->drv;
    return d && !sh_->abandoned.load(std::memory_order_relaxed) && d->outputReady() == ASE_OK;
}

} // namespace nota::asio

#endif // defined(_WIN32)
