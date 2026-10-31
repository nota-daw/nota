// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A device card dragged out of the Devices panel onto a track header (specs/043, research R5).
// Inside the panel a header drag reorders with a drop bar; once the pointer leaves the panel it
// becomes a system drag that the Arrangement and Session headers take. Like the Browser's drag
// (BrowserView.DragItemAsync), it carries one native text item — macOS wants exactly one
// pasteboard item per drag image — and the payload rides this static, in-process.

using System.Threading.Tasks;
using Avalonia.Input;
using Nota.Application;

namespace Nota.App;

internal static class DeviceDrag
{
    private const string Sentinel = "nota-device-drag";

    /// <summary>The device being dragged out of the panel, or null.</summary>
    public static (int TrackId, DeviceChainView.ChainKind Kind, int Index)? Current { get; private set; }

    public static bool IsActive => Current is not null;

    /// <summary>The dragged device can land on <paramref name="trackId"/>: not its own track, and
    /// a track that takes its kind (MIDI effects and the instrument need an instrument track).</summary>
    public static bool CanTake(IAudioEngine engine, int trackId)
        => Current is { } c && DeviceChainView.CanTake(engine, c.Kind, trackId, c.TrackId);

    /// <summary>The move (or ⌥ copy) the drop asks for.</summary>
    public static DeviceMoveRequest? RequestFor(int trackId, KeyModifiers mods)
        => Current is { } c ? new DeviceMoveRequest(c.TrackId, DeviceChainView.FamilyOf(c.Kind), c.Index, trackId, (mods & KeyModifiers.Alt) != 0) : null;

    /// <summary>Runs the system drag from the header press; completes on drop or cancel.</summary>
    public static async Task DragAsync(PointerPressedEventArgs press, int trackId, DeviceChainView.ChainKind kind, int index)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, Sentinel));
        Current = (trackId, kind, index);
        try { await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move | DragDropEffects.Copy); }
        finally { Current = null; }
    }
}
