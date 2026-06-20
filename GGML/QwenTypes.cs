
// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonTTS

namespace SeasonTTS.GGML;

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

public enum QtBackendDeviceType
{
    CPU   = 0,
    GPU   = 1,
    IGPU  = 2,
    ACCEL = 3,
    META  = 4,
}

public readonly record struct QwenBackendInfo(
    string Name,
    string BackendRegistry,
    string Description,
    string? DeviceId,
    QtBackendDeviceType DeviceType);

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

// ── Init parameters ───────────────────────────────────────────────
// Mirrors qwen.h qt_init_params (ABI v3). Strings are UTF-8 byte*.

[StructLayout(LayoutKind.Sequential)]
public unsafe struct QtInitParams
{
    public int   AbiVersion;      // QT_ABI_VERSION = 3
    public byte* TalkerPath;      // talker GGUF path, UTF-8
    public byte* CodecPath;       // codec  GGUF path, UTF-8
    public byte  UseFA;           // flash attention (bool)
    public byte  ClampFp16;       // clamp hidden to fp16 range (bool)
    // ABI v3 additions:
    public byte* Backend;         // explicit backend name (UTF-8), NULL/"" = auto
    public int   GpuDeviceIndex;  // -1 = auto, >=0 = specific device index
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct QtBackendInfoNative
{
    public byte* Name;
    public byte* BackendReg;
    public byte* Description;
    public byte* DeviceId;
    public int   DeviceType;
}

// ── Synthesis parameters ──────────────────────────────────────────
// Mirrors qwen.h qt_tts_params (ABI v2).

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
    public byte   DoSample;
    public float  Temperature;
    public int    TopK;
    public float  TopP;
    public float  RepetitionPenalty;
    public byte   SubtalkerDoSample;
    public float  SubtalkerTemperature;
    public int    SubtalkerTopK;
    public float  SubtalkerTopP;
    public byte*  DumpDir;
    public IntPtr Cancel;
    public IntPtr CancelUserData;
    public IntPtr OnChunk;
    public IntPtr OnChunkUserData;
    public float  CodecChunkSec;
    public float  CodecLeftContextSec;
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
