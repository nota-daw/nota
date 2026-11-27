// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — parameter automation lanes, master volume automation & write/latch.

#include "nota/nota_engine.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Engine.h"
#include "nota_engine_internal.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstddef>
#include <vector>

namespace {

constexpr size_t kStackPointLimit = 128;

std::vector<nota::engine::AutomationPointData> readTempAutoPoints(const NotaAutomationPoint* pts, int32_t count) {
    std::vector<nota::engine::AutomationPointData> out;
    if (!pts || count <= 0) return out;
    out.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) out.push_back(nota::c_abi::toCppAutoPoint(pts[i]));
    return out;
}

int32_t readMasterAutoPoints(const nota::Engine::Mixer& mixer, NotaAutomationPoint* out, int32_t cap) {
    if (!out || cap <= 0) return mixer.getMasterVolumeAutomation(nullptr, 0);
    std::array<nota::engine::AutomationPointData, kStackPointLimit> stack{};
    std::vector<nota::engine::AutomationPointData> heap;
    nota::engine::AutomationPointData* dst = nullptr;
    if (static_cast<size_t>(cap) <= stack.size()) {
        dst = stack.data();
    } else {
        heap.resize(static_cast<size_t>(cap));
        dst = heap.data();
    }
    const int32_t n = mixer.getMasterVolumeAutomation(dst, cap);
    const int32_t written = std::min(n, cap);
    for (int32_t i = 0; i < written; ++i) out[i] = nota::c_abi::toCAutoPoint(dst[i]);
    return n;
}

} // namespace

extern "C" {

int32_t nota_engine_automation_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().automationSelfTest()) ? 1 : 0;
}
int32_t nota_engine_automation_curve_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().automationCurveSelfTest()) ? 1 : 0;
}
int32_t nota_engine_plugin_automation_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().pluginAutomationSelfTest()) ? 1 : 0;
}
int32_t nota_engine_rack_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().rackSelfTest()) ? 1 : 0;
}
int32_t nota_engine_rack_device_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().rackDeviceSelfTest()) ? 1 : 0;
}
int32_t nota_engine_timed_events_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().timedEventsSelfTest()) ? 1 : 0;
}
int32_t nota_engine_articulation_selftest(NotaEngine* e) {
    return e ? nota::c_abi::eng(e).diagnostics().articulationSelfTest() : -1;
}
int32_t nota_engine_drum_rack_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().drumRackSelfTest()) ? 1 : 0;
}

int32_t nota_track_add_automation_lane(
    NotaEngine* e, int32_t track_id, int32_t target, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .addAutomationLane(target, device_index, param_index)
             : -1;
}

int32_t nota_track_add_plugin_automation_lane(
    NotaEngine* e, int32_t track_id, int32_t device_index, const char* param_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).addPluginAutomationLane(device_index, param_id)
             : -1;
}

int32_t nota_track_add_nested_automation_lane(NotaEngine* e, int32_t track_id, int32_t target, int32_t device_index,
    int32_t chain, int32_t slot, int32_t param_index, const char* param_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .addNestedAutomationLane(target, device_index, chain, slot, param_index, param_id)
             : -1;
}

const char* nota_track_automation_lane_param_id(const NotaEngine* e, int32_t track_id, int32_t lane_index) {
    if (!e) return "";
    return nota::c_abi::cacheThreadString(
        nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).automationLane(lane_index).paramId());
}

NotaResult nota_track_automation_lane_path(
    const NotaEngine* e, int32_t track_id, int32_t lane_index, int32_t* out_chain, int32_t* out_slot) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).automationLane(lane_index).path(out_chain, out_slot)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_automation_lane_count(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).automationLaneCount() : 0;
}

NotaResult nota_track_automation_lane_info(const NotaEngine* e, int32_t track_id, int32_t lane_index,
    int32_t* out_target, int32_t* out_device_index, int32_t* out_param_index, int32_t* out_point_count) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                   .automationLane(lane_index)
                   .info(out_target, out_device_index, out_param_index, out_point_count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_automation_get_points(
    const NotaEngine* e, int32_t track_id, int32_t lane_index, NotaAutomationPoint* out, int32_t cap) {
    if (!e) return 0;
    const auto lane = nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).automationLane(lane_index);
    if (!out || cap <= 0) return lane.points(nullptr, 0);
    std::array<nota::engine::AutomationPointData, kStackPointLimit> stack{};
    std::vector<nota::engine::AutomationPointData> heap;
    nota::engine::AutomationPointData* dst = nullptr;
    if (static_cast<size_t>(cap) <= stack.size()) {
        dst = stack.data();
    } else {
        heap.resize(static_cast<size_t>(cap));
        dst = heap.data();
    }
    const int32_t n = lane.points(dst, cap);
    const int32_t written = std::min(n, cap);
    for (int32_t i = 0; i < written; ++i) out[i] = nota::c_abi::toCAutoPoint(dst[i]);
    return n;
}

NotaResult nota_track_automation_set_points(
    NotaEngine* e, int32_t track_id, int32_t lane_index, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    const auto v = readTempAutoPoints(pts, count);
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .automationLane(lane_index)
                   .setPoints(v.empty() ? nullptr : v.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_track_automation_set_points_live(
    NotaEngine* e, int32_t track_id, int32_t lane_index, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    const auto v = readTempAutoPoints(pts, count);
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .automationLane(lane_index)
                   .setPointsLive(v.empty() ? nullptr : v.data(), count)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_track_remove_automation_lane(NotaEngine* e, int32_t track_id, int32_t lane_index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).removeAutomationLane(lane_index)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

void nota_engine_set_automation_record(NotaEngine* e, int32_t on) {
    if (e) nota::Engine::AutomationLane::setRecordEnabled(nota::c_abi::eng(e), on != 0);
}

int32_t nota_engine_automation_record(const NotaEngine* e) {
    return (e && nota::Engine::AutomationLane::recordEnabled(nota::c_abi::ceng(e))) ? 1 : 0;
}

void nota_track_begin_automation_write(NotaEngine* e, int32_t track_id, int32_t target, int32_t device_index,
    int32_t param_index, const char* param_id, int32_t latch) {
    if (e)
        nota::Engine::AutomationLane::beginWrite(
            nota::c_abi::eng(e), track_id, target, device_index, param_index, param_id, latch != 0);
}

void nota_track_end_automation_write(
    NotaEngine* e, int32_t track_id, int32_t target, int32_t device_index, int32_t param_index, const char* param_id) {
    if (e)
        nota::Engine::AutomationLane::endWrite(
            nota::c_abi::eng(e), track_id, target, device_index, param_index, param_id);
}

void nota_engine_reenable_automation(NotaEngine* e) {
    if (e) nota::Engine::AutomationLane::reenableAll(nota::c_abi::eng(e));
}

int32_t nota_engine_automation_overridden(const NotaEngine* e) {
    return (e && nota::Engine::AutomationLane::hasOverrides(nota::c_abi::ceng(e))) ? 1 : 0;
}

int32_t nota_engine_automation_write_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().automationWriteSelfTest()) ? 1 : 0;
}

int32_t nota_engine_automation_device_remap_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().automationDeviceRemapSelfTest()) ? 1 : 0;
}

int32_t nota_engine_master_volume_automation_count(const NotaEngine* e) {
    return e ? nota::c_abi::ceng(e).mixer().masterVolumeAutomationCount() : 0;
}

int32_t nota_engine_master_volume_automation_get(const NotaEngine* e, NotaAutomationPoint* out, int32_t cap) {
    return e ? readMasterAutoPoints(nota::c_abi::ceng(e).mixer(), out, cap) : 0;
}

NotaResult nota_engine_master_volume_automation_set(NotaEngine* e, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    const auto v = readTempAutoPoints(pts, count);
    return nota::c_abi::eng(e).mixer().setMasterVolumeAutomation(v.empty() ? nullptr : v.data(), count, false)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

NotaResult nota_engine_master_volume_automation_set_live(NotaEngine* e, const NotaAutomationPoint* pts, int32_t count) {
    if (!e || count < 0 || (count > 0 && !pts)) return NOTA_ERR_INVALID_ARG;
    const auto v = readTempAutoPoints(pts, count);
    return nota::c_abi::eng(e).mixer().setMasterVolumeAutomation(v.empty() ? nullptr : v.data(), count, true)
               ? NOTA_OK
               : NOTA_ERR_INVALID_ARG;
}

} // extern "C"
