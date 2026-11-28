// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI — track modulators & CV links.

#include "nota/nota_engine.h"
#include "nota_engine_internal.h"
#include <cstdint>

extern "C" {

int32_t nota_track_add_modulator(NotaEngine* e, int32_t track_id, int32_t kind) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).addModulator(kind) : -1;
}

NotaResult nota_track_remove_modulator(NotaEngine* e, int32_t track_id, int32_t mod_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).removeModulator(mod_id) ? NOTA_OK
                                                                                           : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_modulator_count(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).modulatorCount() : 0;
}

int32_t nota_track_modulator_id_at(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).modulatorIdAt(index) : 0;
}

int32_t nota_track_modulator_kind(const NotaEngine* e, int32_t track_id, int32_t mod_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).modulator(mod_id).kind() : -1;
}

float nota_track_modulator_get(const NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t field) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).modulator(mod_id).get(field) : 0.0f;
}

NotaResult nota_track_modulator_set(NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t field, float value) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    auto mod = nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).modulator(mod_id);
    if (!mod.isValid()) return NOTA_ERR_INVALID_ARG;
    mod.set(field, value);
    return NOTA_OK;
}

float nota_track_modulator_value(const NotaEngine* e, int32_t track_id, int32_t mod_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).modulator(mod_id).value() : 0.0f;
}

int32_t nota_track_modulator_scope(const NotaEngine* e, int32_t track_id, int32_t mod_id, float* out, int32_t cap) {
    return (e && out && cap > 0)
               ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).modulator(mod_id).scope(out, nullptr, cap)
               : 0;
}

int32_t nota_track_modulator_scope_timed(
    const NotaEngine* e, int32_t track_id, int32_t mod_id, float* values, float* dts, int32_t cap) {
    return (e && values && cap > 0)
               ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).modulator(mod_id).scope(values, dts, cap)
               : 0;
}

int32_t nota_track_add_cv_link(
    NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).addCvLink(mod_id, device_index, param_index)
             : -1;
}

int32_t nota_track_add_cv_link_to(
    NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t target_track, int32_t device_index, int32_t param_index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .addCvLinkTo(mod_id, target_track, device_index, param_index)
             : -1;
}

int32_t nota_track_add_cv_link_from_param(NotaEngine* e, int32_t track_id, int32_t src_device, int32_t src_param,
    int32_t target_track, int32_t target_device, int32_t target_param) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .addCvLinkFromParam(src_device, src_param, target_track, target_device, target_param)
             : -1;
}

int32_t nota_track_add_cv_link_to_target(NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t target_kind,
    int32_t target_track, int32_t target_device, int32_t target_param) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .addCvLinkToTarget(mod_id, target_kind, target_track, target_device, target_param)
             : -1;
}

int32_t nota_track_add_cv_link_from_param_target(NotaEngine* e, int32_t track_id, int32_t src_device, int32_t src_param,
    int32_t target_kind, int32_t target_track, int32_t target_device, int32_t target_param) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .addCvLinkFromParamToTarget(
                       src_device, src_param, target_kind, target_track, target_device, target_param)
             : -1;
}

int32_t nota_track_add_nested_cv_link(NotaEngine* e, int32_t track_id, int32_t source_kind, int32_t mod_id,
    int32_t src_device, int32_t src_chain, int32_t src_slot, int32_t src_param, int32_t target_kind,
    int32_t target_track, int32_t target_device, int32_t tgt_chain, int32_t tgt_slot, int32_t target_param) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id)
                   .addNestedCvLink(source_kind, mod_id, src_device, src_chain, src_slot, src_param, target_kind,
                       target_track, target_device, tgt_chain, tgt_slot, target_param)
             : -1;
}

int32_t nota_track_cv_link_path(const NotaEngine* e, int32_t track_id, int32_t index, int32_t* out_src_chain,
    int32_t* out_src_slot, int32_t* out_tgt_chain, int32_t* out_tgt_slot) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                     .cvLink(index)
                     .path(out_src_chain, out_src_slot, out_tgt_chain, out_tgt_slot))
               ? 0
               : -1;
}

int32_t nota_track_cv_link_source_kind(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).sourceKind() : -1;
}

int32_t nota_track_cv_link_source_device(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).sourceDevice() : -1;
}

int32_t nota_track_cv_link_source_param(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).sourceParam() : -1;
}

int32_t nota_track_cv_link_target_kind(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).targetKind() : -1;
}

int32_t nota_track_cv_link_target_track(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).targetTrack() : -1;
}

int32_t nota_track_device_param_modulated(
    const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).paramModulated(0, device_index, param_index))
               ? 1
               : 0;
}

int32_t nota_track_param_modulated(
    const NotaEngine* e, int32_t target_kind, int32_t track_id, int32_t device_index, int32_t param_index) {
    return (e && nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id)
                     .paramModulated(target_kind, device_index, param_index))
               ? 1
               : 0;
}

NotaResult nota_track_remove_cv_link(NotaEngine* e, int32_t track_id, int32_t index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).removeCvLink(index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

int32_t nota_track_cv_link_count(const NotaEngine* e, int32_t track_id) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLinkCount() : 0;
}

int32_t nota_track_cv_link_source(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).source() : -1;
}

int32_t nota_track_cv_link_device(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).device() : -1;
}

int32_t nota_track_cv_link_param(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).param() : -1;
}

float nota_track_cv_link_depth(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).depth() : 0.0f;
}

int32_t nota_track_cv_link_mode(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).mode() : 0;
}

float nota_track_cv_link_base(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? nota::c_abi::trackHandle(nota::c_abi::ceng(e), track_id).cvLink(index).base() : 0.0f;
}

NotaResult nota_track_set_cv_link_depth(NotaEngine* e, int32_t track_id, int32_t index, float depth) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    auto link = nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).cvLink(index);
    if (!link.isValid()) return NOTA_ERR_INVALID_ARG;
    link.setDepth(depth);
    return NOTA_OK;
}

NotaResult nota_track_set_cv_link_mode(NotaEngine* e, int32_t track_id, int32_t index, int32_t mode) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    auto link = nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).cvLink(index);
    if (!link.isValid()) return NOTA_ERR_INVALID_ARG;
    link.setMode(mode);
    return NOTA_OK;
}

NotaResult nota_track_set_cv_link_base(NotaEngine* e, int32_t track_id, int32_t index, float base) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    auto link = nota::c_abi::trackHandle(nota::c_abi::eng(e), track_id).cvLink(index);
    if (!link.isValid()) return NOTA_ERR_INVALID_ARG;
    link.setBase(base);
    return NOTA_OK;
}

int32_t nota_engine_modulation_selftest(NotaEngine* e) {
    return (e && nota::c_abi::eng(e).diagnostics().modulationSelfTest()) ? 1 : 0;
}

} // extern "C"
