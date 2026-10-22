// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// nota-scanner-selftest — OutOfProcessScanner against misbehaving workers.
// The binary is its own stub worker: run as `<self> STUB <mode>` (the worker's
// <format> <id> argument shape) it plays a plugin that hangs, answers then hangs
// at exit, prints noise, or dies without answering. Run with no arguments it drives the scanner
// at those stubs and checks every probe ends on time. Exit code 0 = all passed.

#include "../OutOfProcessScanner.h"

#include <juce_audio_processors/juce_audio_processors.h>

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

#if !JUCE_WINDOWS
#include <cerrno>
#include <sys/wait.h>
#endif

namespace {

using nota::scanner::OutOfProcessScanner;
using Outcome = OutOfProcessScanner::Outcome;

void printAnswer() {
    juce::PluginDescription d;
    d.name = "Stub";
    d.pluginFormatName = "VST3";
    d.fileOrIdentifier = "stub.vst3";
    d.uniqueId = 42;
    juce::XmlElement root("plugins");
    root.addChildElement(d.createXml().release());
    std::cout << "<<<NOTA_PLUGINS_BEGIN>>>\n" << root.toString() << "\n<<<NOTA_PLUGINS_END>>>" << std::endl;
}

[[noreturn]] void hang() {
    for (;;) std::this_thread::sleep_for(std::chrono::hours(1));
}

int runStub(const std::string& mode) {
    if (mode == "hang") hang();                       // stuck probing, never answers
    if (mode == "answer-then-hang") { printAnswer(); hang(); } // deadlocks on unload
    if (mode == "noisy") {                            // chatty plugin, > the old 8 KB read
        for (int i = 0; i < 400; ++i) std::cout << "plugin log line " << i << " ........................\n";
        printAnswer();
        return 0;
    }
    if (mode == "die") std::_Exit(7);                 // crash stand-in (no WER/ReportCrash dialog)
    if (mode == "ok") { printAnswer(); return 0; }
    return 3;
}

int failures = 0;

void check(bool ok, const std::string& label) {
    std::cout << "  [" << (ok ? "PASS" : "FAIL") << "] " << label << "\n";
    if (!ok) ++failures;
}

void probe(const OutOfProcessScanner& scanner, const char* mode, Outcome want, size_t wantTypes, int maxMs) {
    const auto t0 = std::chrono::steady_clock::now();
    Outcome got = Outcome::StartFailed;
    const auto descs = scanner.scanDescriptions("STUB", mode, &got);
    const auto ms =
        (int)std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - t0).count();
    check(got == want && descs.size() == wantTypes && ms <= maxMs,
          std::string(mode) + ": outcome " + std::to_string((int)got) + " (want " + std::to_string((int)want) + "), " +
              std::to_string(descs.size()) + " types (want " + std::to_string(wantTypes) + "), " + std::to_string(ms) +
              " ms (max " + std::to_string(maxMs) + ")");
}

} // namespace

int main(int argc, char** argv) {
    if (argc >= 3 && std::strcmp(argv[1], "STUB") == 0) return runStub(argv[2]);

    const auto self = juce::File::getSpecialLocation(juce::File::currentExecutableFile).getFullPathName().toStdString();
    constexpr int kTimeoutMs = 1500;
    const OutOfProcessScanner scanner(self, kTimeoutMs);
    // Allowance over the timeout: process start-up plus the post-kill join (<= 2 s).
    constexpr int kSlack = 3000;

    std::cout << "-- out-of-process scanner --\n";
    probe(scanner, "ok", Outcome::Ok, 1, kTimeoutMs);
    probe(scanner, "hang", Outcome::Timeout, 0, kTimeoutMs + kSlack);
    probe(scanner, "answer-then-hang", Outcome::Ok, 1, kTimeoutMs);
    probe(scanner, "noisy", Outcome::Ok, 1, kTimeoutMs);
    probe(scanner, "die", Outcome::NoAnswer, 0, kTimeoutMs);

    // A missing worker: Windows fails CreateProcess; POSIX forks and the exec fails.
    const OutOfProcessScanner missing("/nonexistent/nota-scanworker", kTimeoutMs);
    Outcome got = Outcome::Ok;
    const auto descs = missing.scanDescriptions("STUB", "ok", &got);
    check(descs.empty() && (got == Outcome::StartFailed || got == Outcome::NoAnswer), "missing worker: no types, no hang");

    // Parallel probes, as the scan runs them: a dying or hung sibling must not
    // delay or confuse the others (inherited pipe ends), and all finish on time.
    {
        const char* modes[] = {"ok", "die", "hang", "answer-then-hang", "noisy", "die", "ok", "hang"};
        std::vector<std::thread> pool;
        std::vector<std::string> bad(8);
        const auto t0 = std::chrono::steady_clock::now();
        for (int i = 0; i < 8; ++i)
            pool.emplace_back([&, i] {
                Outcome got = Outcome::StartFailed;
                const auto d = scanner.scanDescriptions("STUB", modes[i], &got);
                const std::string m = modes[i];
                const Outcome want = m == "hang" ? Outcome::Timeout : m == "die" ? Outcome::NoAnswer : Outcome::Ok;
                if (got != want || d.size() != (want == Outcome::Ok ? 1u : 0u)) bad[(size_t)i] = m;
            });
        for (auto& t : pool) t.join();
        const auto ms =
            (int)std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - t0).count();
        std::string wrong;
        for (const auto& b : bad)
            if (!b.empty()) wrong += " " + b;
        check(wrong.empty() && ms <= kTimeoutMs + kSlack,
              "8 parallel probes end right and on time (" + std::to_string(ms) + " ms)" +
                  (wrong.empty() ? "" : ", wrong:" + wrong));
    }

#if !JUCE_WINDOWS
    // Every worker was reaped: no zombies piling up over a thousand-plugin scan.
    int status = 0;
    const pid_t left = ::waitpid(-1, &status, WNOHANG);
    check(left == -1 && errno == ECHILD, "no unreaped worker processes left");
#endif

    std::cout << (failures == 0 ? "SCANNER SELFTEST PASSED" : "SCANNER SELFTEST FAILED") << "\n";
    return failures == 0 ? 0 : 1;
}
