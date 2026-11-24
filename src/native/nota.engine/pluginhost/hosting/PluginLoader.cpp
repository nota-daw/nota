// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginLoader implementation (#979, specs/113). See PluginLoader.h for why it exists.

#include "PluginLoader.h"

#include "scanner/PluginCatalog.h"

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <utility>

#if JUCE_WINDOWS
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#elif JUCE_LINUX || JUCE_BSD
#include <dlfcn.h>
#include <sys/utsname.h>
#elif JUCE_MAC
#include <AudioToolbox/AudioToolbox.h>
#include <CoreFoundation/CoreFoundation.h>
#endif

namespace nota {
namespace hosting {

namespace {

using Clock = std::chrono::steady_clock;


double secondsSince(Clock::time_point t) {
    return t == Clock::time_point{} ? 0.0 : std::chrono::duration<double>(Clock::now() - t).count();
}

std::string idOf(const juce::PluginDescription& d) {
    return d.createIdentifierString().toStdString();
}

void logLoad(const juce::PluginDescription& d, const char* step, double secs, const std::string& what) {
    std::fprintf(stderr, "[nota plugin] '%s' (%s, %s): %s after %.1f s in %s\n", d.name.toRawUTF8(),
        d.pluginFormatName.toRawUTF8(), d.fileOrIdentifier.toRawUTF8(), what.c_str(), secs, step);
}

juce::File guardFile(const char* name) {
    const std::string dir = scanner::supportDirPath();
    return dir.empty() ? juce::File{} : juce::File{juce::CharPointer_UTF8(dir.c_str())}.getChildFile(name);
}

constexpr const char* kLoadingFile = "plugin-loading.txt"; // id \n name, while a main-thread step runs
constexpr const char* kFrozeFile = "plugins-froze-nota.txt"; // one id per line

#if JUCE_MAC
OSType osType(const juce::String& s) {
    OSType t = 0;
    for (int i = 0; i < 4; ++i) t = (t << 8) | (OSType)(uint8_t)(i < s.length() ? (char)s[i] : ' ');
    return t;
}
#endif

} // namespace

// ---- binary preload (loader thread) -----------------------------------------------------

bool preloadBinary(const juce::PluginDescription& desc, std::string& error) {
    const juce::String path = desc.fileOrIdentifier;
    if (desc.pluginFormatName == "VST3") {
#if JUCE_WINDOWS
        // The file JUCE's DLLHandle opens: the .vst3 itself, or the module inside a bundle folder.
        juce::File f{path};
        if (f.isDirectory()) {
#if defined(_M_ARM64) || defined(_M_ARM64EC)
            const char* arch = "arm64-win";
#else
            const char* arch = "x86_64-win";
#endif
            f = f.getChildFile("Contents").getChildFile(arch).getChildFile(f.getFileName());
        }
        // Kept loaded for the process lifetime: JUCE's own LoadLibrary then only bumps the count.
        if (LoadLibraryW(f.getFullPathName().toWideCharPointer()) != nullptr) return true;
        error = "LoadLibrary failed (" + std::to_string(GetLastError()) + ")";
        return false;
#elif JUCE_LINUX || JUCE_BSD
        struct utsname u {};
        if (uname(&u) != 0) return true; // JUCE couldn't name the module either: leave it to the main step
        const juce::File bundle{path};
        const juce::File so = bundle.getChildFile("Contents")
                                  .getChildFile(juce::String(u.machine) + "-linux")
                                  .getChildFile(bundle.getFileNameWithoutExtension() + ".so");
        if (dlopen(so.getFullPathName().toRawUTF8(), RTLD_LOCAL | RTLD_NOW) != nullptr) return true;
        const char* why = dlerror();
        error = why != nullptr ? why : "dlopen failed";
        return false;
#elif JUCE_MAC
        // CFBundleCreate hands JUCE the same bundle object later; loading the executable here runs
        // its initialisers (and a copy-protection wrapper) off the main thread. bundleEntry is
        // left to JUCE, on the message thread.
        const auto* utf8 = path.toRawUTF8();
        CFURLRef url = CFURLCreateFromFileSystemRepresentation(
            nullptr, (const UInt8*)utf8, (CFIndex)std::strlen(utf8), juce::File{path}.isDirectory());
        if (url == nullptr) return true;
        CFBundleRef bundle = CFBundleCreate(kCFAllocatorDefault, url); // kept: one per plug-in binary
        CFRelease(url);
        if (bundle == nullptr) return true;
        CFErrorRef err = nullptr;
        if (CFBundleLoadExecutableAndReturnError(bundle, &err)) return true;
        error = "the bundle's executable didn't load";
        if (err != nullptr) {
            if (CFStringRef why = CFErrorCopyFailureReason(err)) {
                error = juce::String::fromCFString(why).toStdString();
                CFRelease(why);
            }
            CFRelease(err);
        }
        return false;
#else
        return true;
#endif
    }
#if JUCE_MAC
    if (desc.pluginFormatName == "AudioUnit") {
        // "AudioUnit:Effects/aufx,Sub ,Manu": an instance made and dropped here loads the
        // component's bundle on this thread. An AUv3 runs out of process: nothing to load.
        const juce::String s = path.substring(juce::jmax(path.lastIndexOfChar(':'), path.lastIndexOfChar('/')) + 1);
        juce::StringArray tokens;
        tokens.addTokens(s, ",", juce::StringRef());
        tokens.removeEmptyStrings();
        if (tokens.size() != 3) return true;
        AudioComponentDescription cd{};
        cd.componentType = osType(tokens[0]);
        cd.componentSubType = osType(tokens[1]);
        cd.componentManufacturer = osType(tokens[2]);
        AudioComponent comp = AudioComponentFindNext(nullptr, &cd);
        if (comp == nullptr) return true;
        AudioComponentDescription found{};
        if (AudioComponentGetDescription(comp, &found) == noErr && (found.componentFlags & kAudioComponentFlag_IsV3AudioUnit) != 0)
            return true;
        AudioComponentInstance inst = nullptr;
        const OSStatus st = AudioComponentInstanceNew(comp, &inst);
        if (st == noErr && inst != nullptr) {
            AudioComponentInstanceDispose(inst);
            return true;
        }
        error = "AudioComponentInstanceNew failed (" + std::to_string((int)st) + ")";
        return false;
    }
#endif
    return true; // other formats: the main-thread step loads them
}

// ---- the loader --------------------------------------------------------------------------

PluginLoader& PluginLoader::instance() {
    static PluginLoader* loader = new PluginLoader(); // never destroyed (see header)
    return *loader;
}

PluginLoader::PluginLoader() {
    readLeftoverRecord();
    thread_ = std::thread([this] { run(); });
    thread_.detach();
}

std::chrono::milliseconds PluginLoader::timeout() {
    if (const char* env = std::getenv("NOTA_PLUGIN_LOAD_TIMEOUT_MS"); env != nullptr && *env != '\0') {
        const long ms = std::strtol(env, nullptr, 10);
        if (ms > 0) return std::chrono::milliseconds(ms);
    }
    return std::chrono::seconds(30);
}

// English for the log only; the app words the reason itself (LoadReason -> Loc).
std::string reasonText(LoadReason r, const std::string& arg) {
    switch (r) {
    case LoadReason::WaitingFor: return "waiting for " + arg;
    case LoadReason::NotResponding: return "not responding";
    case LoadReason::GaveUp: return "given up: it stopped responding while loading";
    case LoadReason::RestartRequired: return "a load that never returned blocks loading until a restart";
    case LoadReason::FrozeNota: return "it was loading when Nota last ended; held back";
    case LoadReason::Failed: return arg.empty() ? "it failed to load" : "it failed to load: " + arg;
    case LoadReason::Removed: return "it left the project while it loaded";
    case LoadReason::None: break;
    }
    return {};
}

std::shared_ptr<LoadTicket> PluginLoader::request(const juce::PluginDescription& desc, LoadTicket::Finish finish) {
    auto t = std::make_shared<LoadTicket>(desc, std::move(finish));
    active_.push_back(t);
    if (autoloadScope_ > 0 && frozen(idOf(desc))) {
        fail(*t, LoadReason::FrozeNota);
        return t;
    }
    bool stuck;
    {
        const std::lock_guard<std::mutex> lock(mx_);
        stuck = stuck_;
    }
    if (stuck) {
        fail(*t, LoadReason::RestartRequired);
        return t;
    }
    enqueue(t);
    return t;
}

void PluginLoader::enqueue(const std::shared_ptr<LoadTicket>& t) {
    t->state_ = LoadState::Waiting;
    t->reason_ = LoadReason::None;
    t->reasonArg_.clear();
    t->started_ = Clock::now();
    t->deadline_ = {};
    {
        const std::lock_guard<std::mutex> lock(mx_);
        t->cancelled_ = t->gaveUp_ = t->binaryDone_ = false;
        t->binaryError_.clear();
        queue_.push_back(t);
    }
    cv_.notify_one();
}

void PluginLoader::run() {
    for (;;) {
        std::shared_ptr<LoadTicket> t;
        {
            std::unique_lock<std::mutex> lock(mx_);
            cv_.wait(lock, [this] { return !queue_.empty(); });
            t = queue_.front();
            queue_.pop_front();
            current_ = t;
            currentStarted_ = Clock::now();
        }
        const auto t0 = Clock::now();
        std::string err;
        const bool ok = preloadBinary(t->desc, err);
        const double secs = secondsSince(t0);
        if (!ok) logLoad(t->desc, "the binary load", secs, "binary preload failed (" + err + "); the main thread will try");
        else if (secs > 2.0) logLoad(t->desc, "the binary load", secs, "binary loaded");
        const std::lock_guard<std::mutex> lock(mx_);
        current_.reset();
        stuck_ = false; // whatever it was, the loader thread is free again
        if (!t->cancelled_ && !t->gaveUp_) {
            t->binaryDone_ = true;
            t->binaryError_ = ok ? std::string{} : err;
            loaded_.push_back(t);
        }
    }
}

void PluginLoader::fail(LoadTicket& t, LoadReason reason, std::string arg) {
    t.state_ = LoadState::Failed;
    t.reason_ = reason;
    t.reasonArg_ = std::move(arg);
    logLoad(t.desc, "loading", secondsSince(t.started_), "couldn't load: " + reasonText(t.reason_, t.reasonArg_));
}

std::vector<std::shared_ptr<LoadTicket>> PluginLoader::poisonLocked() {
    std::vector<std::shared_ptr<LoadTicket>> behind;
    if (!current_ || stuck_ || !(current_->cancelled_ || current_->gaveUp_)) return behind;
    if (Clock::now() - currentStarted_ < timeout()) return behind; // it may still come back in time
    // Its loader thread can't be stopped and may hold the system's library lock, so nothing
    // behind it can load until it returns (FR-010): those loads end instead of waiting for a
    // card that may be gone.
    stuck_ = true;
    behind.assign(queue_.begin(), queue_.end());
    for (auto& q : behind) q->gaveUp_ = true;
    queue_.clear();
    return behind;
}

void PluginLoader::finishOne(const std::shared_ptr<LoadTicket>& t, Serviced& s) {
    {
        const std::lock_guard<std::mutex> lock(mx_);
        if (t->cancelled_ || t->gaveUp_) return;
    }
    if (!t->finish || t->state_ == LoadState::Failed) return;
    const auto t0 = Clock::now();
    std::string err;
    beginMainThreadStep(*t);
    const bool ok = t->finish(err);
    endMainThreadStep();
    const double secs = secondsSince(t0);
    if (secs > 2.0) logLoad(t->desc, "instance creation", secs, ok ? "created" : "failed");
    if (ok) {
        t->state_ = LoadState::Loaded;
        t->reason_ = LoadReason::None;
        t->reasonArg_.clear();
        ++s.attached;
        setFrozen(idOf(t->desc), false); // it loads now: no longer held back on project open
    } else {
        fail(*t, LoadReason::Failed, err);
    }
}

PluginLoader::Serviced PluginLoader::service() {
    Serviced s;
    std::vector<std::shared_ptr<LoadTicket>> ready, behind;
    std::shared_ptr<LoadTicket> current;
    {
        const std::lock_guard<std::mutex> lock(mx_);
        ready.swap(loaded_);
        behind = poisonLocked();
        current = current_;
    }
    for (auto& q : behind) fail(*q, LoadReason::RestartRequired);
    for (const auto& t : ready) finishOne(t, s);

    const auto now = Clock::now();
    const std::string currentName = current ? current->desc.name.toStdString() : std::string{};
    active_.erase(std::remove_if(active_.begin(), active_.end(), [](const std::weak_ptr<LoadTicket>& w) { return w.expired(); }),
        active_.end());
    for (const auto& w : active_) {
        auto t = w.lock();
        if (!t) continue;
        if (t->state_ == LoadState::Waiting || t->state_ == LoadState::Loading) {
            if (t == current) {
                if (t->state_ == LoadState::Waiting) t->deadline_ = now + timeout(); // its turn just came
                t->state_ = LoadState::Loading;
                t->reason_ = LoadReason::None;
                t->reasonArg_.clear();
            } else if (t->state_ == LoadState::Waiting) {
                t->reason_ = currentName.empty() ? LoadReason::None : LoadReason::WaitingFor;
                t->reasonArg_ = currentName;
            }
            if (t->state_ == LoadState::Loading && t->deadline_ != Clock::time_point{} && now > t->deadline_) {
                t->state_ = LoadState::NotResponding;
                t->reason_ = LoadReason::NotResponding;
                t->reasonArg_ = t->desc.name.toStdString();
                logLoad(t->desc, "the binary load", secondsSince(t->started_), "not responding");
            }
        }
        if (t->state_ != t->reported_) {
            t->reported_ = t->state_;
            ++s.changed;
        }
    }
    return s;
}

int32_t PluginLoader::pending() const {
    int32_t n = 0;
    for (const auto& w : active_)
        if (auto t = w.lock(); t && t->state_ != LoadState::Loaded && t->state_ != LoadState::Failed) ++n;
    return n;
}

void PluginLoader::act(const std::shared_ptr<LoadTicket>& t, LoadAction a) {
    if (!t) return;
    switch (a) {
    case LoadAction::KeepWaiting:
        if (t->state_ == LoadState::NotResponding) {
            t->state_ = LoadState::Loading;
            t->reason_ = LoadReason::None;
            t->reasonArg_.clear();
            t->deadline_ = Clock::now() + timeout();
        }
        break;
    case LoadAction::GiveUp:
    case LoadAction::Abandon: {
        if (t->state_ == LoadState::Loaded || t->state_ == LoadState::Failed) break;
        std::vector<std::shared_ptr<LoadTicket>> behind;
        {
            const std::lock_guard<std::mutex> lock(mx_);
            t->gaveUp_ = true;
            queue_.erase(std::remove(queue_.begin(), queue_.end(), t), queue_.end());
            loaded_.erase(std::remove(loaded_.begin(), loaded_.end(), t), loaded_.end());
            // The load in progress: once past its deadline nothing behind it can load; before
            // that it may still come back, and service() checks again every tick.
            if (current_ == t) behind = poisonLocked();
        }
        fail(*t, a == LoadAction::Abandon ? LoadReason::Removed : LoadReason::GaveUp);
        for (auto& q : behind) fail(*q, LoadReason::RestartRequired);
        break;
    }
    case LoadAction::TryAgain: {
        if (t->state_ != LoadState::Failed || !t->finish) break;
        bool stuck;
        {
            const std::lock_guard<std::mutex> lock(mx_);
            stuck = stuck_;
        }
        if (stuck) fail(*t, LoadReason::RestartRequired);
        else enqueue(t);
        break;
    }
    }
}

void PluginLoader::cancel(const std::shared_ptr<LoadTicket>& t) {
    if (!t) return;
    t->finish = nullptr;
    const std::lock_guard<std::mutex> lock(mx_);
    // The adapter is gone. A load in progress stays current_ until its loader thread returns;
    // service() stops anything waiting behind it once it is past its deadline (poisonLocked).
    t->cancelled_ = true;
    queue_.erase(std::remove(queue_.begin(), queue_.end(), t), queue_.end());
    loaded_.erase(std::remove(loaded_.begin(), loaded_.end(), t), loaded_.end());
}

// ---- freeze guard (FR-008, FR-009) -------------------------------------------------------

void PluginLoader::readLeftoverRecord() {
    const juce::File froze = guardFile(kFrozeFile);
    if (froze == juce::File{}) return;
    juce::StringArray ids;
    ids.addLines(froze.loadFileAsString());
    for (const auto& id : ids)
        if (id.trim().isNotEmpty()) frozenIds_.insert(id.trim().toStdString());
    const juce::File loading = guardFile(kLoadingFile);
    if (!loading.existsAsFile()) return;
    juce::StringArray rec;
    rec.addLines(loading.loadFileAsString());
    loading.deleteFile();
    if (rec.isEmpty() || rec[0].trim().isEmpty()) return;
    // Nota ended while this plug-in's main-thread step ran: it froze (or crashed) Nota.
    setFrozen(rec[0].trim().toStdString(), true);
    frozenNotice_ = (rec.size() > 1 && rec[1].trim().isNotEmpty() ? rec[1] : rec[0]).trim().toStdString();
    std::fprintf(stderr, "[nota plugin] '%s' was loading when Nota last ended; it won't load on project open\n",
        frozenNotice_.c_str());
}

void PluginLoader::setFrozen(const std::string& id, bool on) {
    if (on == frozen(id)) return;
    if (on) frozenIds_.insert(id);
    else
        frozenIds_.erase(id);
    const juce::File froze = guardFile(kFrozeFile);
    if (froze == juce::File{}) return;
    juce::String text;
    for (const auto& f : frozenIds_) text << juce::String::fromUTF8(f.c_str()) << "\n";
    froze.replaceWithText(text);
}

void PluginLoader::beginMainThreadStep(const LoadTicket& t) {
    const juce::File loading = guardFile(kLoadingFile);
    if (loading != juce::File{})
        loading.replaceWithText(juce::String::fromUTF8(idOf(t.desc).c_str()) + "\n" + t.desc.name + "\n");
}

void PluginLoader::endMainThreadStep() {
    const juce::File loading = guardFile(kLoadingFile);
    if (loading != juce::File{}) loading.deleteFile();
}

std::string PluginLoader::takeFrozenNotice() {
    return std::exchange(frozenNotice_, std::string{});
}

} // namespace hosting
} // namespace nota
