// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "AsioDriverList.h"

#include <windows.h>

#include <algorithm>
#include <cwctype>

namespace nota::asio {
namespace {

std::string toUtf8(const std::wstring& w) {
    if (w.empty()) return {};
    const int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), static_cast<int>(w.size()), nullptr, 0, nullptr, nullptr);
    std::string s(static_cast<size_t>(n), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), static_cast<int>(w.size()), s.data(), n, nullptr, nullptr);
    return s;
}

std::wstring readString(HKEY key, const wchar_t* value) {
    DWORD type = 0, bytes = 0;
    if (RegQueryValueExW(key, value, nullptr, &type, nullptr, &bytes) != ERROR_SUCCESS || type != REG_SZ || bytes == 0)
        return {};
    std::wstring s(bytes / sizeof(wchar_t), L'\0');
    if (RegQueryValueExW(key, value, nullptr, nullptr, reinterpret_cast<LPBYTE>(s.data()), &bytes) != ERROR_SUCCESS)
        return {};
    while (!s.empty() && s.back() == L'\0') s.pop_back();
    return s;
}

bool sameClsid(const std::string& a, const std::string& b) {
    return a.size() == b.size() && std::equal(a.begin(), a.end(), b.begin(), [](char x, char y) {
               return std::towlower(static_cast<unsigned char>(x)) == std::towlower(static_cast<unsigned char>(y));
           });
}

} // namespace

std::vector<AsioDriverInfo> enumerateAsioDrivers() {
    std::vector<AsioDriverInfo> list;
    HKEY root = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\ASIO", 0, KEY_READ, &root) != ERROR_SUCCESS) return list;
    for (DWORD i = 0;; ++i) {
        wchar_t keyName[256];
        DWORD len = 256;
        const LONG r = RegEnumKeyExW(root, i, keyName, &len, nullptr, nullptr, nullptr, nullptr);
        if (r == ERROR_NO_MORE_ITEMS) break;
        if (r != ERROR_SUCCESS) continue;
        HKEY sub = nullptr;
        if (RegOpenKeyExW(root, keyName, 0, KEY_READ, &sub) != ERROR_SUCCESS) continue;
        const std::wstring clsid = readString(sub, L"CLSID");
        const std::wstring description = readString(sub, L"Description");
        RegCloseKey(sub);
        if (clsid.empty()) continue;
        AsioDriverInfo d;
        d.clsid = toUtf8(clsid);
        d.name = toUtf8(std::wstring(keyName, len));
        d.displayName = description.empty() ? d.name : toUtf8(description);
        list.push_back(std::move(d));
    }
    RegCloseKey(root);

    // Two drivers may share a display name (two interfaces of one maker): number the rest.
    std::vector<std::string> base;
    for (const auto& d : list) base.push_back(d.displayName);
    for (size_t i = 0; i < list.size(); ++i) {
        const auto same = 1 + std::count(base.begin(), base.begin() + static_cast<std::ptrdiff_t>(i), base[i]);
        if (same > 1) list[i].displayName += " (" + std::to_string(same) + ")";
    }
    return list;
}

const AsioDriverInfo* findAsioDriver(const std::vector<AsioDriverInfo>& drivers, const std::string& clsid,
                                     const std::string& name) {
    if (!clsid.empty())
        for (const auto& d : drivers)
            if (sameClsid(d.clsid, clsid)) return &d;
    if (!name.empty())
        for (const auto& d : drivers)
            if (d.name == name) return &d;
    return nullptr;
}

} // namespace nota::asio
