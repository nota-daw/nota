// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota ships only ONNX Runtime's managed half (Microsoft.ML.OnnxRuntime.Managed); the native
// library is downloaded with the first AI model (ModelStore) to keep the installer small. The
// managed assembly P/Invokes "onnxruntime", so a resolver points that name at the downloaded
// file. It must be in place before the first InferenceSession, and once the library is loaded
// the process keeps it.

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Nota.Application.Localization;

namespace Nota.Infrastructure;

internal static class OnnxRuntimeLoader
{
    private static readonly object Gate = new();
    private static string? _path;
    private static IntPtr _handle;

    /// <summary>Points the ORT assembly at the native library at <paramref name="libraryPath"/>.
    /// Throws <see cref="StoreException"/> when it can't be loaded.</summary>
    public static void Use(string libraryPath)
    {
        lock (Gate)
        {
            if (_handle != IntPtr.Zero) return;   // already loaded; a process can't swap it
            if (!NativeLibrary.TryLoad(libraryPath, out var h))
                throw new StoreException(Loc.T("ai.onnx_runtime_loader.nota_couldnt_load_the_ai"));
            _handle = h;
            if (_path is null)
                NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly, Resolve);
            _path = libraryPath;
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? search)
        => name == "onnxruntime" ? _handle : IntPtr.Zero;

    /// <summary>A CPU session for the model at <paramref name="modelPath"/>. ORT's own thread
    /// count (one per performance core) beats every core: efficiency cores slow the whole step.</summary>
    public static InferenceSession Session(string modelPath, int intraOpThreads = 0)
    {
        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            // 0 = ORT's choice; a background model (the Assistant's search) asks for fewer so the
            // audio engine keeps its cores.
            IntraOpNumThreads = intraOpThreads,
        };
        try { return new InferenceSession(modelPath, opts); }
        catch (OnnxRuntimeException e)
        {
            throw new StoreException(Loc.T("ai.onnx_runtime_loader.the_ai_model_file_is"), e);
        }
    }
}
