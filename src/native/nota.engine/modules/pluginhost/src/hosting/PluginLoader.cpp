// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "hosting/PluginLoader.h"

#include "hosting/IPluginFormatHost.h"
#include "nota/core/ConfigFile.h"

#include <algorithm>
#include <cstdint>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <thread>
#include <string>
#include <mutex>
#include <memory>
#include <utility>
#include <vector>

namespace nota::pluginhost::detail {
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
    if (const std::string over = core::cfgfile::dataDirOverride(); !over.empty()) {
        juce::File dir{juce::CharPointer_UTF8(over.c_str())};
        dir.createDirectory();
        return dir.getChildFile(name);
    }
    if (const char* env = std::getenv("NOTA_DATA_DIR"); env != nullptr && *env != '\0') {
        juce::File dir{juce::CharPointer_UTF8(env)};
        dir.createDirectory();
        return dir.getChildFile(name);
    }
#if defined(__APPLE__)
    auto dir = juce::File::getSpecialLocation(juce::File::userApplicationDataDirectory)
                   .getChildFile("Application Support")
                   .getChildFile("Nota");
    dir.createDirectory();
    return dir.getChildFile(name);
#else
    const std::string dir = core::cfgfile::dataDir();
    return dir.empty() ? juce::File{} : juce::File{juce::CharPointer_UTF8(dir.c_str())}.getChildFile(name);
#endif
}

constexpr const char* kLoadingFile = "plugin-loading.txt";
constexpr const char* kFrozeFile = "plugins-froze-nota.txt";

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

} // namespace

PluginLoader& PluginLoader::instance() {
    static PluginLoader* loader = new PluginLoader();
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

std::shared_ptr<LoadTicket> PluginLoader::request(const juce::PluginDescription& desc, LoadTicket::Finish finish) {
    auto t = std::make_shared<LoadTicket>(desc, std::move(finish));
    active_.push_back(t);
    if (autoloadScope_ > 0 && frozen(idOf(desc))) {
        fail(*t, LoadReason::FrozeNota);
        return t;
    }
    bool stuck = false;
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
        t->cancelled_ = false;
        t->gaveUp_ = false;
        t->binaryDone_ = false;
        t->binaryError_.clear();
        ++t->attempt_;
        queue_.push_back(t);
    }
    cv_.notify_one();
}

void PluginLoader::run() {
    for (;;) {
        std::shared_ptr<LoadTicket> t;
        uint32_t attempt = 0;
        {
            std::unique_lock<std::mutex> lock(mx_);
            cv_.wait(lock, [this] { return !queue_.empty() && loaded_.empty() && !finishing_; });
            t = queue_.front();
            queue_.pop_front();
            attempt = t->attempt_;
            current_ = t;
            currentStarted_ = Clock::now();
        }
        const auto t0 = Clock::now();
        std::string err;
        const bool ok = sharedFormatHost().preloadBinary(t->desc, err);
        const double secs = secondsSince(t0);
        if (!ok) {
            logLoad(t->desc, "the binary load", secs, "binary preload failed (" + err + "); the main thread will try");
        } else if (secs > 2.0) {
            logLoad(t->desc, "the binary load", secs, "binary loaded");
        }
        const std::lock_guard<std::mutex> lock(mx_);
        current_.reset();
        stuck_ = false;
        if (!t->cancelled_ && !t->gaveUp_ && t->attempt_ == attempt) {
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
    if (Clock::now() - currentStarted_ < timeout()) return behind;
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
        setFrozen(idOf(t->desc), false);
    } else {
        fail(*t, LoadReason::Failed, err);
    }
}

PluginLoader::Serviced PluginLoader::service() {
    Serviced s;
    std::vector<std::shared_ptr<LoadTicket>> ready;
    std::vector<std::shared_ptr<LoadTicket>> behind;
    std::shared_ptr<LoadTicket> current;
    {
        const std::lock_guard<std::mutex> lock(mx_);
        ready.swap(loaded_);
        finishing_ = !ready.empty();
        behind = poisonLocked();
        current = current_;
    }
    for (auto& q : behind) fail(*q, LoadReason::RestartRequired);
    for (const auto& t : ready) finishOne(t, s);
    if (!ready.empty()) {
        {
            const std::lock_guard<std::mutex> lock(mx_);
            finishing_ = false;
        }
        cv_.notify_one();
    }

    const auto now = Clock::now();
    const std::string currentName = current ? current->desc.name.toStdString() : std::string{};
    active_.erase(
        std::remove_if(active_.begin(), active_.end(), [](const std::weak_ptr<LoadTicket>& w) { return w.expired(); }),
        active_.end());
    for (const auto& w : active_) {
        auto t = w.lock();
        if (!t) continue;
        if (t->state_ == LoadState::Waiting || t->state_ == LoadState::Loading) {
            if (t == current) {
                if (t->state_ == LoadState::Waiting) t->deadline_ = now + timeout();
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
    for (const auto& w : active_) {
        if (auto t = w.lock(); t && t->state_ != LoadState::Loaded && t->state_ != LoadState::Failed) ++n;
    }
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
            if (current_ == t) behind = poisonLocked();
        }
        cv_.notify_one();
        fail(*t, a == LoadAction::Abandon ? LoadReason::Removed : LoadReason::GaveUp);
        for (auto& q : behind) fail(*q, LoadReason::RestartRequired);
        break;
    }
    case LoadAction::TryAgain: {
        if (t->state_ != LoadState::Failed || !t->finish) break;
        bool stuck = false;
        {
            const std::lock_guard<std::mutex> lock(mx_);
            stuck = stuck_;
        }
        if (stuck) fail(*t, LoadReason::RestartRequired);
        else
            enqueue(t);
        break;
    }
    }
}

void PluginLoader::cancel(const std::shared_ptr<LoadTicket>& t) {
    if (!t) return;
    t->finish = nullptr;
    const std::lock_guard<std::mutex> lock(mx_);
    t->cancelled_ = true;
    queue_.erase(std::remove(queue_.begin(), queue_.end(), t), queue_.end());
    loaded_.erase(std::remove(loaded_.begin(), loaded_.end(), t), loaded_.end());
    cv_.notify_one();
}

void PluginLoader::readLeftoverRecord() {
    const juce::File froze = guardFile(kFrozeFile);
    if (froze == juce::File{}) return;
    juce::StringArray ids;
    ids.addLines(froze.loadFileAsString());
    for (const auto& id : ids) {
        if (id.trim().isNotEmpty()) frozenIds_.insert(id.trim().toStdString());
    }
    const juce::File loading = guardFile(kLoadingFile);
    if (!loading.existsAsFile()) return;
    juce::StringArray rec;
    rec.addLines(loading.loadFileAsString());
    loading.deleteFile();
    if (rec.isEmpty() || rec[0].trim().isEmpty()) return;
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
    if (loading != juce::File{}) {
        loading.replaceWithText(juce::String::fromUTF8(idOf(t.desc).c_str()) + "\n" + t.desc.name + "\n");
    }
}

void PluginLoader::endMainThreadStep() {
    const juce::File loading = guardFile(kLoadingFile);
    if (loading != juce::File{}) loading.deleteFile();
}

std::string PluginLoader::takeFrozenNotice() {
    return std::exchange(frozenNotice_, std::string{});
}

} // namespace nota::pluginhost::detail
