// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/core/ConfigFile.h"
#include "nota/core/EditorKeys.h"

#include <array>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <string>

namespace {

int gFail = 0;

#define CHECK(cond)                                                                                                    \
    do {                                                                                                               \
        if (!(cond)) {                                                                                                 \
            std::fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond);                                       \
            ++gFail;                                                                                                   \
        }                                                                                                              \
    } while (false)

void testConfigFile() {
    const auto tmpDir = std::filesystem::temp_directory_path() / "nota_core_test_cfg";
    std::filesystem::remove_all(tmpDir);
    const std::string prev = nota::core::cfgfile::dataDirOverride();
    nota::core::cfgfile::setDataDirOverride(tmpDir.string());
    CHECK(nota::core::cfgfile::dataDir() == tmpDir.string());
    const std::string file = (tmpDir / "test.json").string();
    CHECK(nota::core::cfgfile::writeFileAtomic(file, "{\"ok\":true}\n"));
    CHECK(nota::core::cfgfile::readFile(file) == "{\"ok\":true}\n");
    nota::core::cfgfile::setDataDirOverride(prev);
    std::filesystem::remove_all(tmpDir);
}

void testEditorKeys() {
    const std::array<int32_t, 2> keys = {' ', 'a'};
    nota::core::editorkeys::setClaimed(keys.data(), static_cast<int32_t>(keys.size()));
    CHECK(nota::core::editorkeys::claims(' '));
    CHECK(nota::core::editorkeys::claims('a'));
    CHECK(nota::core::editorkeys::claims('A'));
    CHECK(!nota::core::editorkeys::claims('b'));
    CHECK(!nota::core::editorkeys::claims(0));

    nota::core::editorkeys::push('a', true);
    nota::core::editorkeys::push(' ', false);

    std::array<int32_t, 4> outChars{};
    std::array<uint8_t, 4> outDown{};
    const int32_t n =
        nota::core::editorkeys::drain(outChars.data(), outDown.data(), static_cast<int32_t>(outChars.size()));
    CHECK(n == 2);
    CHECK(outChars[0] == 'A' && outDown[0] == 1);
    CHECK(outChars[1] == ' ' && outDown[1] == 0);
    CHECK(nota::core::editorkeys::drain(outChars.data(), outDown.data(), static_cast<int32_t>(outChars.size())) == 0);

    nota::core::editorkeys::setClaimed(nullptr, 0);
    CHECK(!nota::core::editorkeys::claims('a'));
}

} // namespace

int main() {
    testConfigFile();
    testEditorKeys();
    return gFail == 0 ? 0 : 1;
}
