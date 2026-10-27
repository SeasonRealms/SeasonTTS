// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonTTS

namespace Season.TTS.GGML;

// ── Status codes ──────────────────────────────────────────────────

public enum QtStatus
{
    OK              =  0,
    InvalidParams   = -1,
    ModeInvalid     = -2,
    GenerateFailed  = -3,
    OOM             = -4,
    Cancelled       = -5,
}

// ── Log level ─────────────────────────────────────────────────────

public enum QtLogLevel
{
    Debug = 0,
    Info  = 1,
    Warn  = 2,
    Error = 3,
}

// ── Output audio buffer ───────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
public unsafe struct QtAudio
{
    public float* Samples;
    public int    NSamples;
    public int    SampleRate;  // 24000
    public int    Channels;    // 1

    public readonly float[] ToArray()
    {
        if (Samples == null || NSamples <= 0)
            return [];
        var arr = new float[NSamples];
        fixed (float* dst = arr)
            Buffer.MemoryCopy(Samples, dst, NSamples * sizeof(float), NSamples * sizeof(float));
        return arr;
    }

    public readonly ReadOnlySpan<float> AsSpan() =>
        new(Samples, NSamples);
}

// ── Devices ───────────────────────────────────────────────────────
// Mirrors qwen.h qt_device_info / ggml_backend_dev_type. Every pointer
// is owned by the native backend and stays valid for the process
// lifetime: read it with Marshal.PtrToStringUTF8, never free it.

public enum QtDeviceType
{
    Cpu   = 0,   // system memory
    Gpu   = 1,   // dedicated memory
    IGPU  = 2,   // integrated GPU on host memory
    Accel = 3,   // BLAS / AMX style accelerator used alongside the CPU
    Meta  = 4,   // meta device wrapping several others
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct QtDeviceInfo
{
    public byte* Name;         // "CUDA0", "Vulkan1", "CPU", ... -> qt_init_params.device
    public byte* Backend;      // family that registered it: "CUDA", "Vulkan", "CPU", ...
    public byte* Description;  // human readable device string
    public byte* DeviceId;     // PCI "domain:bus:device.function", NULL or "" when unknown
    public int   Type;         // QtDeviceType
}

/// <summary>
/// Managed copy of <see cref="QtDeviceInfo"/>: the native pointers are
/// converted while they are still guaranteed to be alive.
/// </summary>
public sealed record QtDevice(
    string       Name,
    string       Backend,
    string       Description,
    string       DeviceId,
    QtDeviceType Type)
{
    public override string ToString() =>
        $"{Name} ({Backend}, {Type}) — {Description}";
}

// ── Init parameters ───────────────────────────────────────────────
// Mirrors qwen.h qt_init_params (ABI 6). Strings are UTF-8 byte*.
// Field order is the native layout, padding included: do not reorder.

[StructLayout(LayoutKind.Sequential)]
public unsafe struct QtInitParams
{
    public int   AbiVersion;      // QT_ABI_VERSION = 6
    public byte* TalkerPath;      // talker GGUF path, UTF-8
    public byte* CodecPath;       // codec  GGUF path, UTF-8
    public byte  UseFA;           // flash attention (bool)
    public byte  ClampFp16;       // clamp hidden to fp16 range (bool)
    // ABI 5 additions:
    public int   MaxBatch;        // concurrent requests batched on the GPU, 0/1 = single
    public float CodecChunkSec;   // codec decode chunk in seconds, 0 selects the 24.0 default
    // ABI 6 additions:
    public byte* Device;          // GGML device name, NULL selects the best available
}

// ── Synthesis parameters ──────────────────────────────────────────
// Mirrors qwen.h qt_tts_params (ABI 6).
//
// ABI 6 dropped the do_sample flags: a stack samples when its
// temperature is above zero and takes the argmax when it is exactly
// zero, so the managed layer folds doSample into Temperature and
// SubtalkerDoSample into SubtalkerTemperature. The codec chunk moved
// into qt_init_params and is a per-handle setting now.

[StructLayout(LayoutKind.Sequential)]
public unsafe struct QtTtsParams
{
    public int    AbiVersion;
    public byte*  Text;
    public byte*  Lang;
    public byte*  Instruct;
    public byte*  Speaker;
    public float* RefAudio24k;
    public int    RefNSamples;
    public byte*  RefText;
    public long   Seed;
    public int    MaxNewTokens;
    public float  Temperature;
    public int    TopK;
    public float  TopP;
    public float  RepetitionPenalty;
    public float  SubtalkerTemperature;
    public int    SubtalkerTopK;
    public float  SubtalkerTopP;
    public byte*  DumpDir;
    public IntPtr Cancel;
    public IntPtr CancelUserData;
    public IntPtr OnChunk;
    public IntPtr OnChunkUserData;
    public float* RefSpkEmb;
    public int    RefSpkDim;
    public int*   RefCodes;
    public int    RefT;
}

// ── Callback delegates ────────────────────────────────────────────

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int QtCancelCallback(IntPtr userData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int QtAudioChunkCallback(float* samples, int nSamples, IntPtr userData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate void QtLogCallback(int level, byte* msg, IntPtr userData);
