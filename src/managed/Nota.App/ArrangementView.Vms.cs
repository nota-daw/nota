// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // ---- model ------------------------------------------------------------
    internal sealed class TrackVM
    {
        public int Id;
        public bool IsInstrument;
        public bool IsReturn;
        public bool IsGroup;     // a group (submix) track — has children, no clips
        public int GroupId = -1; // parent group track id, or -1 (top-level)
        public int Depth;        // hierarchy indent level (0 = top-level) for the header/lane row
        public int ColorIndex;   // index into the track palette (returns → 8/9)
        public bool Muted;
        public bool Soloed;
        public bool Armed;
        public bool Frozen;      // M7: playing a captured buffer instead of the live chain
        public int LiveRole;     // live-freeze (v1.1): 0 none, 1 sleeping source, 2 linked frozen
        public string Name = "";
        public List<ClipVM> Clips = new();
        // Automation (M9-A3): current target + its live-editable envelope.
        public float Volume = 1f;
        public float Pan;
        public AutomationTarget AutoTarget = AutomationTarget.Volume;
        public int AutoDeviceIndex = -1;
        public int AutoParamIndex = -1;
        public string AutoParamId = "";      // PluginParam target (M9-B3)
        public string AutoLabel = "Vol";
        public List<AutoPt> AutoPoints = new();
        // Group lane summary (M-groups): descendant clips aggregated for the lane preview —
        // a stacked mini-clip per child sub-lane when collapsed, a thin span strip when
        // expanded. Empty for non-group tracks. Computed in ApplyHierarchy.
        public List<GroupMiniClip> GroupMini = new();
        public int GroupSlotCount;   // number of clip-bearing descendant sub-lanes (stack height)
    }

    /// <summary>One descendant clip projected onto a collapsed group's lane: its beat span,
    /// the child track's colour, and which stacked sub-lane it sits in.</summary>
    internal struct GroupMiniClip
    {
        public double Start;
        public double Length;
        public int ColorIndex;
        public int Slot;
    }

    /// <summary>A mutable automation breakpoint used during editing (struct AutomationPoint
    /// can't be held by reference while dragging). Committed back as AutomationPoint[].</summary>
    internal sealed class AutoPt { public double Beat; public float Value; public float Curve; }

    internal sealed class ClipVM
    {
        public int ClipIndex;
        public double StartBeat;
        public double LengthBeats;
        public bool IsMidi;
        public bool Active = true;   // false = deactivated (key 0): stays but silent + greyed
        public float[]? Peaks;
        public int PeakCount;
        /// <summary>Clip gain (linear) — peaks are raw material, the lane scales them by this.</summary>
        public float Gain = 1f;
        public NotaNote[]? Notes;
        public string Name = "";
        /// <summary>True when no clip on the same track ends where this one begins — the head
        /// of a run. Only these carry a name on the lane (design 1a), so a repeated pattern
        /// reads as one block. Computed in <c>Refresh</c>.</summary>
        public bool RunStart = true;
    }
}
