// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — tracks: add/remove, mixer state, grouping, sends.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class TrackTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    [McpServerTool(Name = "add_audio_track"), Description("Add an audio track. Returns its track id.")]
    public Task<int> AddAudioTrack() => Mutate(() => E.AddAudioTrack());

    [McpServerTool(Name = "add_return_track"), Description("Add a return (aux/FX) bus. Returns its track id (0 if all return slots are used).")]
    public Task<int> AddReturnTrack() => Mutate(() => E.AddReturnTrack());

    [McpServerTool(Name = "add_group_track"), Description("Add an empty group (submix) track. Returns its track id.")]
    public Task<int> AddGroupTrack() => Mutate(() => E.AddGroupTrack());

    [McpServerTool(Name = "remove_track"), Description("Remove a track by id.")]
    public Task<bool> RemoveTrack([Description("Track id")] int trackId) => Mutate(() => E.RemoveTrack(trackId));

    [McpServerTool(Name = "rename_track"), Description("Set a track's display name.")]
    public Task RenameTrack(int trackId, string name) => Mutate(() => E.SetTrackName(trackId, name));

    [McpServerTool(Name = "set_track_color"), Description("Set a track's colour palette index (0..7).")]
    public Task SetTrackColor(int trackId, int colorIndex) => Mutate(() => E.SetTrackColorIndex(trackId, colorIndex));

    [McpServerTool(Name = "set_track_volume"), Description("Set a track's fader level (0..1 linear).")]
    public Task SetTrackVolume(int trackId, [Description("Linear gain 0..1")] float volume) => Mutate(() => E.SetTrackVolume(trackId, Math.Clamp(volume, 0f, 1f)));

    [McpServerTool(Name = "set_track_pan"), Description("Set a track's pan (-1 left .. 0 centre .. +1 right).")]
    public Task SetTrackPan(int trackId, [Description("-1..1")] float pan) => Mutate(() => E.SetTrackPan(trackId, Math.Clamp(pan, -1f, 1f)));

    [McpServerTool(Name = "set_track_mute"), Description("Mute or unmute a track.")]
    public Task SetTrackMute(int trackId, bool mute) => Mutate(() => E.SetTrackMute(trackId, mute));

    [McpServerTool(Name = "set_track_solo"), Description("Solo or unsolo a track.")]
    public Task SetTrackSolo(int trackId, bool solo) => Mutate(() => E.SetTrackSolo(trackId, solo));

    [McpServerTool(Name = "set_track_arm"), Description("Arm or disarm a track for recording.")]
    public Task SetTrackArm(int trackId, bool armed) => Mutate(() => E.SetTrackArmed(trackId, armed));

    [McpServerTool(Name = "move_track"), Description("Reorder a (non-return) track to a new index within the regular track list.")]
    public Task MoveTrack(int trackId, int toIndex) => Mutate(() => E.MoveTrack(trackId, toIndex));

    [McpServerTool(Name = "group_tracks"), Description("Group the given tracks under a new group track. Returns the group track id (or -1).")]
    public Task<int> GroupTracks(int[] trackIds) => Mutate(() => E.CreateGroup(trackIds));

    [McpServerTool(Name = "ungroup"), Description("Dissolve a group; its children reparent up one level.")]
    public Task Ungroup(int groupId) => Mutate(() => E.Ungroup(groupId));

    [McpServerTool(Name = "set_track_group"), Description("Move a track into a group (groupId = -1 for top level).")]
    public Task SetTrackGroup(int trackId, int groupId) => Mutate(() => E.SetTrackGroup(trackId, groupId));

    [McpServerTool(Name = "set_send"), Description("Set a track's post-fader send level (0..1) to a return bus (0-based).")]
    public Task SetSend(int trackId, int returnBus, [Description("0..1")] float level) => Mutate(() => E.SetTrackSend(trackId, returnBus, Math.Clamp(level, 0f, 1f)));
}
