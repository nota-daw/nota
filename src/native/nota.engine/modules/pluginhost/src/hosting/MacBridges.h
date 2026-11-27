// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MacBridges — pure C++ declarations for Objective-C++ macOS bridges implemented in *_mac.mm.

#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

namespace nota::pluginhost::detail::mac {

void cacheCurrentApplication();
void retainWorkgroup(void* osWorkgroup);
void* copyAuWorkgroupObserver(void* audioUnitPtr);
void releaseWorkgroupObserver(void* observerBlock);
void invokeWorkgroupObserver(void* observerBlock, void* osWorkgroup);

void activateApplication();
using EditorKeyCallback = std::function<bool(int32_t ch, int keyCode, bool isKeyDown, bool hasModifiers)>;
void* installEditorKeyMonitor(void* nsViewHandle, EditorKeyCallback callback);
void removeEditorKeyMonitor(void* monitor);

bool captureEditorImage(void* nsViewHandle, int bx, int by, int bw, int bh, int shortSide, int longSide,
    std::vector<uint8_t>& outBgraPremul, int& outWidth, int& outHeight);

bool preloadVst3Bundle(const std::string& utf8Path, bool isDir, std::string& error);
bool preloadAudioUnitComponent(const std::string& componentId, std::string& error);

} // namespace nota::pluginhost::detail::mac
