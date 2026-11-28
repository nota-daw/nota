// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// macOS Objective-C++ bridge for EditorSnapshot: CGWindowListCreateImage content crop & downscale.

#if defined(__APPLE__)

#include "hosting/MacBridges.h"

#import <AppKit/AppKit.h>
#include <dlfcn.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota::pluginhost::detail::mac {
namespace {

using WindowImageFn = CGImageRef (*)(CGRect, uint32_t, uint32_t, uint32_t);

} // namespace

bool captureEditorImage(void* nsViewHandle, int bx, int by, int bw, int bh, int shortSide, int longSide,
    std::vector<uint8_t>& outBgraPremul, int& outWidth, int& outHeight) {
    static const auto windowImage = reinterpret_cast<WindowImageFn>(dlsym(RTLD_DEFAULT, "CGWindowListCreateImage"));
    auto* view = static_cast<NSView*>(nsViewHandle);
    NSWindow* win = view != nil ? view.window : nil;
    if (windowImage == nullptr || win == nil || !win.isVisible || win.isMiniaturized) return false;

    constexpr uint32_t kIncludingWindow = 1u << 3;
    constexpr uint32_t kIgnoreFraming = 1u << 0;
    constexpr uint32_t kBestResolution = 1u << 3;
    CGImageRef shot = windowImage(
        CGRectNull, kIncludingWindow, static_cast<uint32_t>(win.windowNumber), kIgnoreFraming | kBestResolution);
    if (shot == nullptr) return false;

    const NSRect frame = win.frame;
    const NSRect inner = [win contentRectForFrameRect:frame];
    const double sx = static_cast<double>(CGImageGetWidth(shot)) / frame.size.width;
    const double sy = static_cast<double>(CGImageGetHeight(shot)) / frame.size.height;
    const double ox = (inner.origin.x - frame.origin.x) * sx;
    const double oy = (NSMaxY(frame) - NSMaxY(inner)) * sy;
    const CGRect crop = CGRectIntersection(
        CGRectMake(std::round(ox + bx * sx), std::round(oy + by * sy), std::round(bw * sx), std::round(bh * sy)),
        CGRectMake(0.0, 0.0, static_cast<double>(CGImageGetWidth(shot)), static_cast<double>(CGImageGetHeight(shot))));
    CGImageRef editor = CGRectIsEmpty(crop) ? nullptr : CGImageCreateWithImageInRect(shot, crop);
    CGImageRelease(shot);
    if (editor == nullptr) return false;

    const int ew = static_cast<int>(CGImageGetWidth(editor));
    const int eh = static_cast<int>(CGImageGetHeight(editor));
    double scale = std::min(1.0, static_cast<double>(shortSide) / std::max(1, std::min(ew, eh)));
    if (std::max(ew, eh) * scale > longSide) { scale = static_cast<double>(longSide) / std::max(1, std::max(ew, eh)); }
    outWidth = std::max(1, static_cast<int>(std::lround(ew * scale)));
    outHeight = std::max(1, static_cast<int>(std::lround(eh * scale)));

    const size_t stride = static_cast<size_t>(outWidth) * 4u;
    outBgraPremul.assign(stride * static_cast<size_t>(outHeight), 0u);

    CGColorSpaceRef cs = CGColorSpaceCreateDeviceRGB();
    CGContextRef ctx = CGBitmapContextCreate(outBgraPremul.data(), static_cast<size_t>(outWidth),
        static_cast<size_t>(outHeight), 8, stride, cs,
        static_cast<uint32_t>(kCGImageAlphaPremultipliedFirst) | static_cast<uint32_t>(kCGBitmapByteOrder32Little));
    if (ctx != nullptr) {
        CGContextSetInterpolationQuality(ctx, kCGInterpolationHigh);
        CGContextDrawImage(ctx, CGRectMake(0.0, 0.0, outWidth, outHeight), editor);
        CGContextRelease(ctx);
    }
    CGColorSpaceRelease(cs);
    CGImageRelease(editor);
    return true;
}

} // namespace nota::pluginhost::detail::mac

#endif // defined(__APPLE__)
