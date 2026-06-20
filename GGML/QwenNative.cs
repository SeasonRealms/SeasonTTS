
// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonTTS

namespace SeasonTTS.GGML;

/// <summary>
/// Raw P/Invoke declarations for the qwentts.cpp public ABI (qwen.h).
///
/// The library name switches per platform:
///   iOS           -> __Internal (statically linked)
///   Windows       -> qwen.dll
///   everywhere    -> libqwen (libqwen.so / libqwen.dylib)
///
/// On Linux and macOS desktop the <see cref="QwenNativeResolver"/>
/// module initializer intercepts resolution via SetDllImportResolver so
/// the runtime finds the library under runtimes/{rid}/native/ even when
/// consumed as a ProjectReference (no .deps.json entry for the native lib).
/// </summary>
public static unsafe class QwenNative
{
#if IOS
    private const string NativeLib = "__Internal";
#elif WINDOWS
    private const string NativeLib = "qwen";
#else
    private const string NativeLib = "libqwen";
#endif

    /// <summary>ABI version this binding was compiled against.</summary>
    public const int QtAbiVersion = 3;

    // ── Version & error ───────────────────────────────────────────

    /// <returns>NUL-terminated UTF-8 static string: "&lt;git-hash&gt; (&lt;date&gt;)"</returns>
    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* qt_version();

    /// <summary>Thread-local last-error message, NUL-terminated UTF-8.</summary>
    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* qt_last_error();

    // ── Lifecycle ──────────────────────────────────────────────────

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qt_init_default_params(QtInitParams* p);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr qt_init(QtInitParams* p);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qt_free(IntPtr ctx);

    // ── Synthesis ──────────────────────────────────────────────────

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qt_tts_default_params(QtTtsParams* p);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern QtStatus qt_synthesize(IntPtr ctx, QtTtsParams* p, QtAudio* audio);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qt_audio_free(QtAudio* audio);

    // ── Queries ────────────────────────────────────────────────────

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qt_num_codebooks(IntPtr ctx);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qt_n_speakers(IntPtr ctx);

    /// <returns>NUL-terminated UTF-8 speaker name, or NULL if out of range.</returns>
    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* qt_speaker_name(IntPtr ctx, int i);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qt_duration_sec_to_tokens(IntPtr ctx, float durationSec);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qt_backend_count();

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool qt_backend_get_info(int index, QtBackendInfoNative* info);

    // ── Logging ────────────────────────────────────────────────────

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qt_log_set(IntPtr cb, IntPtr userData);
}
