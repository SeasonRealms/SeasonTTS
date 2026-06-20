
// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonTTS

namespace SeasonTTS.GGML;

/// <summary>
/// Managed wrapper around the qwentts.cpp native library.
///
/// Owns a <c>qt_context*</c> handle that aggregates the Talker LM,
/// Code Predictor MTP head, optional speaker encoder, 12 Hz audio
/// tokenizer, BPE tokenizer, and the GGML backend pair.  One init,
/// one free, one synthesize call per utterance.
///
/// <para>Thread safety: the native library uses thread-local error
/// storage and per-context state, so two <see cref="QwenEngine"/>
/// instances may be used concurrently on separate threads, but a
/// single instance is not re-entrant.</para>
/// </summary>
public sealed unsafe class QwenEngine : IDisposable
{
    private IntPtr _ctx;
    private bool   _disposed;

    // ── Construction ───────────────────────────────────────────────

    /// <summary>
    /// Load the talker and codec GGUF files and initialise every module.
    /// </summary>
    /// <param name="talkerGgufPath">Path to qwen-talker-*.gguf</param>
    /// <param name="codecGgufPath">Path to qwen-tokenizer-12hz-*.gguf</param>
    /// <param name="useFA">Enable fused flash attention when a GPU backend is present.</param>
    /// <param name="clampFp16">Clamp hidden states to FP16 range (sub-Ampere CUDA guard).</param>
    /// <param name="backend">
    /// Explicit GGML backend name, e.g. <c>"CPU"</c>, <c>"Vulkan1"</c>,
    /// <c>"CUDA0"</c>, <c>"Metal"</c>.  <c>null</c> or <c>""</c> selects
    /// automatically via <c>ggml_backend_init_best</c>.
    /// </param>
    /// <exception cref="FileNotFoundException">A GGUF was not found.</exception>
    /// <exception cref="InvalidOperationException">Native init failed.</exception>
    public QwenEngine(string talkerGgufPath, string codecGgufPath,
                      bool useFA = true, bool clampFp16 = false,
                      string? backend = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(talkerGgufPath);
        ArgumentException.ThrowIfNullOrEmpty(codecGgufPath);
        if (!File.Exists(talkerGgufPath))
            throw new FileNotFoundException("Talker GGUF not found", talkerGgufPath);
        if (!File.Exists(codecGgufPath))
            throw new FileNotFoundException("Codec GGUF not found", codecGgufPath);

        // Pin UTF-8 strings for the duration of qt_init.
        byte[] talkerBytes = Encoding.UTF8.GetBytes(talkerGgufPath + '\0');
        byte[] codecBytes  = Encoding.UTF8.GetBytes(codecGgufPath + '\0');
        byte[]? backendBytes = backend != null
            ? Encoding.UTF8.GetBytes(backend + '\0')
            : null;

#region debug-point qwen-engine-init
        TraceDebug(
            $"[SeasonTTS2] QwenEngine.ctor " +
            $"talker='{talkerGgufPath}' codec='{codecGgufPath}' " +
            $"useFA={useFA} clampFp16={clampFp16} " +
            $"backend='{backend ?? "(auto)"}'");
#endregion

        fixed (byte* talkerPtr   = talkerBytes)
        fixed (byte* codecPtr    = codecBytes)
        fixed (byte* backendPtr  = backendBytes)
        {
            QtInitParams p;
            QwenNative.qt_init_default_params(&p);
            p.AbiVersion      = QwenNative.QtAbiVersion;
            p.TalkerPath      = talkerPtr;
            p.CodecPath       = codecPtr;
            p.UseFA           = useFA      ? (byte)1 : (byte)0;
            p.ClampFp16       = clampFp16  ? (byte)1 : (byte)0;
            p.Backend         = backendPtr;
            p.GpuDeviceIndex  = -1;

            _ctx = QwenNative.qt_init(&p);
        }

        if (_ctx == IntPtr.Zero)
            throw new InvalidOperationException(GetLastError());

#region debug-point qwen-engine-init
        TraceDebug(
            $"[SeasonTTS2] QwenEngine.ready version='{Version}' speakers={NumSpeakers} codebooks={NumCodebooks}");
#endregion
    }

    // ── Synthesis ──────────────────────────────────────────────────

    /// <summary>
    /// Run the full TTS pipeline and return the synthesised waveform.
    /// </summary>
    /// <param name="text">Input text to synthesise (required).</param>
    /// <param name="lang">Language label: "english", "chinese", "auto", …</param>
    /// <param name="speaker">Named speaker for CustomVoice models.</param>
    /// <param name="instruct">Style instruction for VoiceDesign models.</param>
    /// <param name="refAudio24k">
    /// Optional reference audio for base-mode voice cloning (24 kHz mono float PCM).
    /// </param>
    /// <param name="refText">Transcript matching <paramref name="refAudio24k"/> (enables ICL mode B).</param>
    /// <param name="refSpkEmb">Pre-encoded speaker embedding (Mode A). Mutually exclusive with <paramref name="refAudio24k"/>.</param>
    /// <param name="refCodes">Pre-encoded ICL codes [num_codebooks, T] row-major (Mode B).</param>
    /// <param name="seed">Sampling seed (-1 for random).</param>
    /// <param name="maxNewTokens">Max new audio frames (default 2048).</param>
    /// <param name="temperature">Talker sampling temperature (default 0.9).</param>
    /// <param name="topK">Talker top-k (default 50, 0 disables).</param>
    /// <param name="topP">Talker top-p (default 1.0).</param>
    /// <param name="repetitionPenalty">Repetition penalty (default 1.05).</param>
    /// <param name="cancellationToken">Cooperative cancellation (~83 ms granularity).</param>
    /// <returns>Mono float PCM at 24 kHz.</returns>
    /// <exception cref="ObjectDisposedException">The engine has been freed.</exception>
    /// <exception cref="InvalidOperationException">Synthesis failed.</exception>
    public float[] Synthesize(
        string text,
        string? lang = "auto",
        string? speaker = null,
        string? instruct = null,
        float[]? refAudio24k = null,
        string? refText = null,
        float[]? refSpkEmb = null,
        int[]? refCodes = null,
        long seed = -1,
        int maxNewTokens = 2048,
        bool doSample = true,
        float temperature = 0.9f,
        int topK = 50,
        float topP = 1.0f,
        float repetitionPenalty = 1.05f,
        bool subtalkerDoSample = true,
        float subtalkerTemperature = 0.9f,
        int subtalkerTopK = 50,
        float subtalkerTopP = 1.0f,
        float codecChunkSec = 1.0f,
        float codecLeftContextSec = 2.0f,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var marshal = new SynthesizeMarshal(this, text, lang, speaker, instruct,
            refAudio24k, refText, refSpkEmb, refCodes, cancellationToken);

        try
        {
            return DoSynthesize(ref marshal, seed, maxNewTokens,
                doSample, temperature, topK, topP, repetitionPenalty,
                subtalkerDoSample, subtalkerTemperature, subtalkerTopK, subtalkerTopP,
                codecChunkSec, codecLeftContextSec);
        }
        finally
        {
            marshal.Dispose();
        }
    }

    private float[] DoSynthesize(
        ref SynthesizeMarshal m,
        long seed, int maxNewTokens,
        bool doSample, float temperature, int topK, float topP, float repetitionPenalty,
        bool subtalkerDoSample, float subtalkerTemperature, int subtalkerTopK, float subtalkerTopP,
        float codecChunkSec, float codecLeftContextSec)
    {
        QtTtsParams p;
        QwenNative.qt_tts_default_params(&p);
        p.AbiVersion          = QwenNative.QtAbiVersion;
        p.Text                = m.TextPtr;
        p.Lang                = m.LangPtr;
        p.Speaker             = m.SpeakerPtr;
        p.Instruct            = m.InstructPtr;
        p.RefAudio24k         = m.RefAudioPtr;
        p.RefNSamples         = m.RefNSamples;
        p.RefText             = m.RefTextPtr;
        p.Seed                = seed;
        p.MaxNewTokens        = maxNewTokens;
        p.DoSample            = doSample ? (byte)1 : (byte)0;
        p.Temperature         = temperature;
        p.TopK                = topK;
        p.TopP                = topP;
        p.RepetitionPenalty   = repetitionPenalty;
        p.SubtalkerDoSample   = subtalkerDoSample ? (byte)1 : (byte)0;
        p.SubtalkerTemperature = subtalkerTemperature;
        p.SubtalkerTopK        = subtalkerTopK;
        p.SubtalkerTopP        = subtalkerTopP;
        p.CodecChunkSec       = codecChunkSec;
        p.CodecLeftContextSec = codecLeftContextSec;
        p.RefSpkEmb           = m.RefSpkEmbPtr;
        p.RefSpkDim           = m.RefSpkDim;
        p.RefCodes            = m.RefCodesPtr;
        p.RefT                = m.RefT;

        if (m.CancelHandle != null)
        {
            p.Cancel         = m.CancelHandle.FuncPtr;
            p.CancelUserData = m.CancelHandle.TokenPtr;
        }

#region debug-point qwen-synthesize-buffered
        TraceDebug(
            "[SeasonTTS2] qt_synthesize.request " +
            $"textLen={SafeCStringLength(m.TextPtr)} lang='{SafeUtf8(m.LangPtr)}' " +
            $"speaker='{SafeUtf8(m.SpeakerPtr)}' instructLen={SafeCStringLength(m.InstructPtr)} " +
            $"seed={seed} maxNewTokens={maxNewTokens} doSample={doSample} temperature={temperature} topK={topK} topP={topP} " +
            $"repetitionPenalty={repetitionPenalty} subtalkerDoSample={subtalkerDoSample} " +
            $"subtalkerTemperature={subtalkerTemperature} subtalkerTopK={subtalkerTopK} subtalkerTopP={subtalkerTopP} " +
            $"codecChunkSec={codecChunkSec} codecLeftContextSec={codecLeftContextSec} " +
            $"refNSamples={m.RefNSamples} refSpkDim={m.RefSpkDim} refT={m.RefT} " +
            $"cancel={(m.CancelHandle != null)}");
#endregion

        fixed (QtAudio* pAudio = &m.Audio)
        {
            QtStatus rc = QwenNative.qt_synthesize(_ctx, &p, pAudio);
            if (rc != QtStatus.OK)
            {
#region debug-point qwen-synthesize-buffered
                TraceDebug($"[SeasonTTS2] qt_synthesize.failed status={rc} error='{GetLastError()}'");
#endregion
                throw new InvalidOperationException(GetLastError());
            }

            float[] audio = m.Audio.ToArray();
#region debug-point qwen-synthesize-buffered
            TraceDebug($"[SeasonTTS2] qt_synthesize.audio {FormatAudioStats(in m.Audio, audio)}");
#endregion
            return audio;
        }
    }

    // ── Streaming synthesis ────────────────────────────────────────

    /// <summary>
    /// Run the TTS pipeline in streaming mode, emitting audio chunks via <paramref name="onChunk"/>.
    /// </summary>
    public void SynthesizeStreaming(
        Action<float[], int> onChunk,
        string text,
        string? lang = "auto",
        string? speaker = null,
        string? instruct = null,
        float[]? refAudio24k = null,
        string? refText = null,
        float[]? refSpkEmb = null,
        int[]? refCodes = null,
        long seed = -1,
        int maxNewTokens = 2048,
        bool doSample = true,
        float temperature = 0.9f,
        int topK = 50,
        float topP = 1.0f,
        float repetitionPenalty = 1.05f,
        bool subtalkerDoSample = true,
        float subtalkerTemperature = 0.9f,
        int subtalkerTopK = 50,
        float subtalkerTopP = 1.0f,
        float codecChunkSec = 24.0f,
        float codecLeftContextSec = 2.0f,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var marshal = new SynthesizeMarshal(this, text, lang, speaker, instruct,
            refAudio24k, refText, refSpkEmb, refCodes, cancellationToken);

        // Keep delegate alive for the duration of the call.
        QtAudioChunkCallback chunkDel = (samples, nSamples, _) =>
        {
            var arr = new float[nSamples];
            fixed (float* dst = arr)
                Buffer.MemoryCopy(samples, dst, nSamples * sizeof(float), nSamples * sizeof(float));
            onChunk(arr, nSamples);
            return 1; // continue
        };
        GCHandle chunkHandle = GCHandle.Alloc(chunkDel);

        try
        {
            QtTtsParams p;
            QwenNative.qt_tts_default_params(&p);
            p.AbiVersion          = QwenNative.QtAbiVersion;
            p.Text                = marshal.TextPtr;
            p.Lang                = marshal.LangPtr;
            p.Speaker             = marshal.SpeakerPtr;
            p.Instruct            = marshal.InstructPtr;
            p.RefAudio24k         = marshal.RefAudioPtr;
            p.RefNSamples         = marshal.RefNSamples;
            p.RefText             = marshal.RefTextPtr;
            p.Seed                = seed;
            p.MaxNewTokens        = maxNewTokens;
            p.DoSample            = doSample ? (byte)1 : (byte)0;
            p.Temperature         = temperature;
            p.TopK                = topK;
            p.TopP                = topP;
            p.RepetitionPenalty   = repetitionPenalty;
            p.SubtalkerDoSample   = subtalkerDoSample ? (byte)1 : (byte)0;
            p.SubtalkerTemperature = subtalkerTemperature;
            p.SubtalkerTopK        = subtalkerTopK;
            p.SubtalkerTopP        = subtalkerTopP;
            p.RefSpkEmb           = marshal.RefSpkEmbPtr;
            p.RefSpkDim           = marshal.RefSpkDim;
            p.RefCodes            = marshal.RefCodesPtr;
            p.RefT                = marshal.RefT;
            p.CodecChunkSec       = codecChunkSec;
            p.CodecLeftContextSec = codecLeftContextSec;
            p.OnChunk             = Marshal.GetFunctionPointerForDelegate(chunkDel);
            p.OnChunkUserData     = IntPtr.Zero;

            if (marshal.CancelHandle != null)
            {
                p.Cancel         = marshal.CancelHandle.FuncPtr;
                p.CancelUserData = marshal.CancelHandle.TokenPtr;
            }

            QtStatus rc = QwenNative.qt_synthesize(_ctx, &p, null);
            if (rc != QtStatus.OK && rc != QtStatus.Cancelled)
                throw new InvalidOperationException(GetLastError());
        }
        finally
        {
            chunkHandle.Free();
            marshal.Dispose();
        }
    }

    // ── Queries ────────────────────────────────────────────────────

    public int NumCodebooks
    {
        get
        {
            ThrowIfDisposed();
            return QwenNative.qt_num_codebooks(_ctx);
        }
    }

    public int NumSpeakers
    {
        get
        {
            ThrowIfDisposed();
            return QwenNative.qt_n_speakers(_ctx);
        }
    }

    public string? GetSpeakerName(int index)
    {
        ThrowIfDisposed();
        byte* p = QwenNative.qt_speaker_name(_ctx, index);
        return p != null ? Marshal.PtrToStringUTF8((IntPtr)p) : null;
    }

    public string[] SpeakerNames
    {
        get
        {
            ThrowIfDisposed();
            int n = QwenNative.qt_n_speakers(_ctx);
            var names = new string[n];
            for (int i = 0; i < n; i++)
                names[i] = GetSpeakerName(i) ?? "";
            return names;
        }
    }

    /// <summary>
    /// Convert a target audio duration to the corresponding number of
    /// autoregressive audio frames used by the loaded tokenizer.
    /// </summary>
    public int DurationSecToTokens(float durationSec)
    {
        ThrowIfDisposed();
        return QwenNative.qt_duration_sec_to_tokens(_ctx, durationSec);
    }

    public string Version
    {
        get
        {
            byte* p = QwenNative.qt_version();
            return Marshal.PtrToStringUTF8((IntPtr)p) ?? "unknown";
        }
    }

    public static QwenBackendInfo[] GetAvailableBackends()
    {
        int count = QwenNative.qt_backend_count();
        if (count <= 0)
            return [];

        var backends = new QwenBackendInfo[count];
        for (int i = 0; i < count; i++)
        {
            QtBackendInfoNative nativeInfo;
            if (!QwenNative.qt_backend_get_info(i, &nativeInfo))
                throw new InvalidOperationException(GetLastError());

            backends[i] = new QwenBackendInfo(
                Name: PtrToStringUtf8(nativeInfo.Name),
                BackendRegistry: PtrToStringUtf8(nativeInfo.BackendReg),
                Description: PtrToStringUtf8(nativeInfo.Description),
                DeviceId: PtrToStringUtf8OrNull(nativeInfo.DeviceId),
                DeviceType: (QtBackendDeviceType)nativeInfo.DeviceType);
        }
        return backends;
    }

    // ── Logging ────────────────────────────────────────────────────

    private static QtLogCallback? _logCallbackDelegate;
    private static event Action<QtLogLevel, string>? _logCallback;

    /// <summary>
    /// Install a process-wide log callback (shared across all <see cref="QwenEngine"/> instances).
    /// Pass null to restore default (stderr) behaviour.
    /// </summary>
    public static void SetLogCallback(Action<QtLogLevel, string>? callback)
    {
        _logCallback = callback;
        if (callback != null)
        {
            _logCallbackDelegate = (level, msg, _) =>
                callback((QtLogLevel)level, Marshal.PtrToStringUTF8((IntPtr)msg) ?? "");
            QwenNative.qt_log_set(
                Marshal.GetFunctionPointerForDelegate(_logCallbackDelegate),
                IntPtr.Zero);
        }
        else
        {
            _logCallbackDelegate = null;
            QwenNative.qt_log_set(IntPtr.Zero, IntPtr.Zero);
        }
    }

    // ── Disposal ───────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ctx != IntPtr.Zero)
        {
            QwenNative.qt_free(_ctx);
            _ctx = IntPtr.Zero;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(QwenEngine));
    }

    private static string GetLastError()
    {
        byte* p = QwenNative.qt_last_error();
        string msg = Marshal.PtrToStringUTF8((IntPtr)p) ?? "Unknown native error";
        return string.IsNullOrEmpty(msg) ? "Unknown native error" : msg;
    }

    private static string PtrToStringUtf8(byte* p) =>
        Marshal.PtrToStringUTF8((IntPtr)p) ?? "";

    private static string? PtrToStringUtf8OrNull(byte* p) =>
        p != null ? Marshal.PtrToStringUTF8((IntPtr)p) : null;

    private static void TraceDebug(string message) =>
        System.Diagnostics.Debug.WriteLine(message);

    private static unsafe string SafeUtf8(byte* ptr) =>
        ptr == null ? "" : (Marshal.PtrToStringUTF8((IntPtr)ptr) ?? "");

    private static unsafe int SafeCStringLength(byte* ptr)
    {
        if (ptr == null)
            return 0;

        int len = 0;
        while (ptr[len] != 0)
            len++;
        return len;
    }

    private static string FormatAudioStats(in QtAudio audio, float[] managed)
    {
        if (managed.Length == 0)
            return $"samples=0 sampleRate={audio.SampleRate} channels={audio.Channels}";

        float min = managed[0];
        float max = managed[0];
        double sumSquares = 0.0;
        double sumAbs = 0.0;
        int previewCount = Math.Min(managed.Length, 8);
        string[] preview = new string[previewCount];

        for (int i = 0; i < managed.Length; i++)
        {
            float sample = managed[i];
            if (sample < min) min = sample;
            if (sample > max) max = sample;
            sumSquares += sample * sample;
            sumAbs += Math.Abs(sample);
            if (i < previewCount)
                preview[i] = sample.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        double rms = Math.Sqrt(sumSquares / managed.Length);
        double meanAbs = sumAbs / managed.Length;
        double seconds = audio.SampleRate > 0 ? (double)managed.Length / audio.SampleRate : 0.0;

        return
            $"samples={managed.Length} sampleRate={audio.SampleRate} channels={audio.Channels} " +
            $"durationSec={seconds:0.000} min={min.ToString("0.000000", CultureInfo.InvariantCulture)} " +
            $"max={max.ToString("0.000000", CultureInfo.InvariantCulture)} " +
            $"meanAbs={meanAbs.ToString("0.000000", CultureInfo.InvariantCulture)} " +
            $"rms={rms.ToString("0.000000", CultureInfo.InvariantCulture)} " +
            $"preview=[{string.Join(", ", preview)}]";
    }

    // ── Internal marshal helper ────────────────────────────────────

    private ref struct SynthesizeMarshal
    {
        // Pinned UTF-8 strings
        private byte[]? _textBytes, _langBytes, _speakerBytes, _instructBytes, _refTextBytes;
        private GCHandle _textHandle, _langHandle, _speakerHandle, _instructHandle, _refTextHandle;

        // Pinned float / int arrays
        private GCHandle _refAudioHandle, _refSpkEmbHandle, _refCodesHandle;

        // Cancellation
        public CancelHandle? CancelHandle;

        // Output
        public QtAudio Audio;

        public SynthesizeMarshal(
            QwenEngine _,
            string text, string? lang, string? speaker, string? instruct,
            float[]? refAudio24k, string? refText,
            float[]? refSpkEmb, int[]? refCodes,
            CancellationToken ct)
        {
            PinStr(text,        out _textBytes,     out _textHandle);
            PinStr(lang,        out _langBytes,     out _langHandle);
            PinStr(speaker,     out _speakerBytes,  out _speakerHandle);
            PinStr(instruct,    out _instructBytes, out _instructHandle);
            PinStr(refText,     out _refTextBytes,  out _refTextHandle);

            if (refAudio24k != null)
                _refAudioHandle = GCHandle.Alloc(refAudio24k, GCHandleType.Pinned);
            if (refSpkEmb != null)
                _refSpkEmbHandle = GCHandle.Alloc(refSpkEmb, GCHandleType.Pinned);
            if (refCodes != null)
                _refCodesHandle = GCHandle.Alloc(refCodes, GCHandleType.Pinned);

            if (ct.CanBeCanceled)
                CancelHandle = new CancelHandle(ct);

            Audio = default;
        }

        public readonly byte* TextPtr      => PinPtr(_textHandle);
        public readonly byte* LangPtr      => PinPtr(_langHandle);
        public readonly byte* SpeakerPtr   => PinPtr(_speakerHandle);
        public readonly byte* InstructPtr  => PinPtr(_instructHandle);
        public readonly byte* RefTextPtr   => PinPtr(_refTextHandle);
        public readonly float* RefAudioPtr => (float*)(_refAudioHandle.IsAllocated ? _refAudioHandle.AddrOfPinnedObject() : IntPtr.Zero);
        public readonly int RefNSamples    => _refAudioHandle.IsAllocated ? ((float[])_refAudioHandle.Target!).Length : 0;
        public readonly float* RefSpkEmbPtr => (float*)(_refSpkEmbHandle.IsAllocated ? _refSpkEmbHandle.AddrOfPinnedObject() : IntPtr.Zero);
        public readonly int RefSpkDim      => _refSpkEmbHandle.IsAllocated ? ((float[])_refSpkEmbHandle.Target!).Length : 0;
        public readonly int* RefCodesPtr   => (int*)(_refCodesHandle.IsAllocated ? _refCodesHandle.AddrOfPinnedObject() : IntPtr.Zero);
        public readonly int RefT           => _refCodesHandle.IsAllocated ? ((int[])_refCodesHandle.Target!).Length : 0; // simplified

        public void Dispose()
        {
            FreeHandle(ref _textHandle);
            FreeHandle(ref _langHandle);
            FreeHandle(ref _speakerHandle);
            FreeHandle(ref _instructHandle);
            FreeHandle(ref _refTextHandle);
            FreeHandle(ref _refAudioHandle);
            FreeHandle(ref _refSpkEmbHandle);
            FreeHandle(ref _refCodesHandle);
            CancelHandle?.Dispose();
            if (Audio.Samples != null)
            {
                fixed (QtAudio* p = &Audio)
                    QwenNative.qt_audio_free(p);
            }
        }

        private static void PinStr(string? s, out byte[]? bytes, out GCHandle handle)
        {
            if (s != null)
            {
                bytes  = Encoding.UTF8.GetBytes(s + '\0');
                handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            }
            else
            {
                bytes  = null;
                handle = default;
            }
        }

        private static byte* PinPtr(GCHandle h) =>
            h.IsAllocated ? (byte*)h.AddrOfPinnedObject() : null;

        private static void FreeHandle(ref GCHandle h)
        {
            if (h.IsAllocated) h.Free();
            h = default;
        }
    }

    /// <summary>Pins a CancellationToken and exposes a native-compatible callback.</summary>
    private sealed class CancelHandle : IDisposable
    {
        private readonly CancellationTokenRegistration _reg;
        private readonly QtCancelCallback              _del;
        private          GCHandle                      _selfHandle;

        public IntPtr FuncPtr  { get; }
        public IntPtr TokenPtr { get; }

        public CancelHandle(CancellationToken ct)
        {
            _del = _ => ct.IsCancellationRequested ? 1 : 0;
            _selfHandle = GCHandle.Alloc(this);
            FuncPtr  = Marshal.GetFunctionPointerForDelegate(_del);
            TokenPtr = GCHandle.ToIntPtr(_selfHandle);
            _reg     = ct.Register(static () => { /* no-op; polled by native */ });
        }

        public void Dispose()
        {
            _reg.Dispose();
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
        }
    }
}
