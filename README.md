# SeasonTTS

SeasonTTS is a cross-platform .NET text-to-speech library for Qwen3-TTS, offering two runtime backends:

- **ONNX mode** — direct ONNX Runtime inference against pre-exported ONNX model bundles, with a simple `CustomVoice` / `CloneVoice` API
- **GGML mode** — P/Invoke wrapper around [qwentts.cpp](https://github.com/ServeurpersoCom/qwentts.cpp) (Qwen3-TTS GGML), supporting base synthesis, CustomVoice, VoiceDesign, and voice cloning

Supports Windows, Linux, macOS, Android, iOS, and MacCatalyst. SeasonTTS is an independent project and is not affiliated with Qwen, Alibaba, or the upstream projects whose code it derives from.

## Naming

- Package name: `SeasonTTS`
- ONNX namespace: `SeasonTTS.ONNX` (classes: `CustomVoice`, `CloneVoice`)
- GGML namespace: `SeasonTTS.GGML` (class: `QwenEngine`)
- Repository: [SeasonRealms/SeasonTTS](https://github.com/SeasonRealms/SeasonTTS)

## Origin

SeasonTTS combines and restructures code derived from two upstream projects:

| Backend | Upstream | License | What was adapted |
|---|---|---|---|
| ONNX | [ElBruno.QwenTTS](https://github.com/elbruno/ElBruno.QwenTTS) | MIT | ONNX inference pipeline, tokenizer, vocoder, embedding store — restructured into `CustomVoice` / `CloneVoice` with explicit `Initialize()` separation |
| GGML | [ServeurpersoCom/qwentts.cpp](https://github.com/ServeurpersoCom/qwentts.cpp) | MIT | Native C library wrapped via P/Invoke; managed bindings under `SeasonTTS.GGML` with `QwenEngine` high-level API, backend auto-selection, streaming, and cancellation |

Source files copied or adapted from upstream retain their original attribution headers where applicable.

## Modes at a Glance

| Capability | ONNX (`SeasonTTS.ONNX`) | GGML (`SeasonTTS.GGML`) |
|---|---|---|
| Base (plain) synthesis | — | `engine.Synthesize(text)` |
| CustomVoice (named speakers) | `new CustomVoice(model).Generate(text, voice)` | `engine.Synthesize(text, speaker: "vivian")` |
| VoiceDesign (attribute instruction) | — | `engine.Synthesize(text, instruct: "...")` |
| Voice cloning (reference audio) | `new CloneVoice(model).Clone(text, stream)` | `engine.Synthesize(text, refAudio24k: wav)` |
| GPU acceleration | via ONNX Runtime EP selection | CUDA / Vulkan / Metal auto-select |
| Cross-platform | .NET 10 targets | Windows, Linux, macOS, Android, iOS, MacCatalyst |
| Streaming output | — | `engine.SynthesizeStreaming(callback, ...)` |
| Cancellation | `CancellationToken` | `CancellationToken` |

## Features

- Qwen3-TTS CustomVoice inference with 9 preset speakers
- Qwen3-TTS Base voice cloning from reference audio
- VoiceDesign attribute instruction control (GGML, 1.7B)
- Explicit `Initialize()` step for ONNX model/session setup
- Standard RIFF/WAVE bytes (ONNX) or float PCM samples (GGML)
- Local model directory loading (ONNX) or GGUF files (GGML)
- GPU auto-detection: CUDA / Vulkan / Metal with CPU fallback (GGML)
- Streaming audio chunks with rolling overlap (GGML)
- Cancellation support with step-level granularity (GGML)

## ONNX Mode

The ONNX backend targets pre-exported Qwen3-TTS ONNX bundles published by `elbruno` on Hugging Face. It uses [Microsoft.ML.OnnxRuntime](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.Managed) for inference.

### ONNX API

```csharp
public class CustomVoice : IDisposable
{
    public CustomVoice(
        string model,
        Func<SessionOptions>? sessionOptionsFactory = null,
        Func<SessionOptions>? vocoderSessionOptionsFactory = null);

    public Task Initialize(CancellationToken cancellationToken = default);

    public Task<byte[]> Generate(
        string text,
        QwenVoicePreset voice = QwenVoicePreset.Ryan,
        string language = "auto",
        string? instruct = null,
        CancellationToken cancellationToken = default);
}

public class CloneVoice : IDisposable
{
    public CloneVoice(
        string model,
        Func<SessionOptions>? sessionOptionsFactory = null);

    public Task Initialize(CancellationToken cancellationToken = default);

    public Task<byte[]> Clone(
        string text,
        Stream referenceAudioStream,
        string? referenceText = null,
        string language = "auto",
        CancellationToken cancellationToken = default);
}
```

### Supported ONNX Models

| Model family | Hugging Face repo | Typical use |
|---|---|---|
| CustomVoice 0.6B | `elbruno/Qwen3-TTS-12Hz-0.6B-CustomVoice-ONNX` | preset voices, smaller footprint |
| CustomVoice 1.7B | `elbruno/Qwen3-TTS-12Hz-1.7B-CustomVoice-ONNX` | preset voices plus `instruct` style control |
| Base 0.6B | `elbruno/Qwen3-TTS-12Hz-0.6B-Base-ONNX` | voice cloning from reference audio |

Direct model pages:

- [elbruno/Qwen3-TTS-12Hz-0.6B-CustomVoice-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-0.6B-CustomVoice-ONNX)
- [elbruno/Qwen3-TTS-12Hz-1.7B-CustomVoice-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-1.7B-CustomVoice-ONNX)
- [elbruno/Qwen3-TTS-12Hz-0.6B-Base-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-0.6B-Base-ONNX)

### ONNX Quick Start

**CustomVoice:**

```csharp
using SeasonTTS.ONNX;

var model = @"../../../../../../Models/qwen3-tts-12hz-0.6b-customvoice-onnx";
var customVoice = new CustomVoice(model);

await customVoice.Initialize();

byte[] wavBytes = await customVoice.Generate(
    "Hello from SeasonTTS.",
    QwenVoicePreset.Vivian);

File.WriteAllBytes("customvoice.wav", wavBytes);
```

Example with `1.7B` instruct control:

```csharp
using SeasonTTS.ONNX;

var model = @"../../../../../../Models/qwen3-tts-12hz-1.7b-customvoice-onnx";
var customVoice = new CustomVoice(model);

await customVoice.Initialize();

byte[] wavBytes = await customVoice.Generate(
    "Welcome to SeasonTTS.",
    QwenVoicePreset.Ryan,
    instruct: "Speak warmly and slowly with a clear presentation style.");

File.WriteAllBytes("customvoice-17b.wav", wavBytes);
```

**CloneVoice:**

```csharp
using SeasonTTS.ONNX;

var model = @"../../../../../../Models/qwen3-tts-12hz-0.6b-base-onnx";
var cloneVoice = new CloneVoice(model);

await cloneVoice.Initialize();

using var referenceStream = File.OpenRead("reference.wav");

byte[] wavBytes = await cloneVoice.Clone(
    "This sentence uses the cloned voice.",
    referenceStream);

File.WriteAllBytes("clonevoice.wav", wavBytes);
```

Optional ICL mode with reference transcript:

```csharp
byte[] wavBytes = await cloneVoice.Clone(
    "This sentence uses the cloned voice.",
    referenceStream,
    referenceText: "The transcript of the reference audio.");
```

## GGML Mode

The GGML backend wraps [qwentts.cpp](https://github.com/ServeurpersoCom/qwentts.cpp) via P/Invoke. It loads GGUF model files and uses GGML's hardware-accelerated backends for inference. The managed API lives in `SeasonTTS.GGML`.

### GGML API

```csharp
public sealed class QwenEngine : IDisposable
{
    public QwenEngine(
        string talkerGgufPath,
        string codecGgufPath,
        bool useFA = true,
        bool clampFp16 = false,
        string? backend = null);

    // Base / CustomVoice / VoiceDesign / Clone — mode auto-detected from GGUF
    public float[] Synthesize(
        string text,
        string lang = "English",
        string? speaker = null,
        string? instruct = null,
        float[]? refAudio24k = null,
        string? refText = null,
        long? seed = null,
        CancellationToken cancellationToken = default);

    // Streaming variant
    public void SynthesizeStreaming(
        AudioChunkCallback onChunk,
        string text,
        string lang = "English",
        ...);

    // Queries
    public string[] GetAvailableSpeakers();
    public QtBackendInfo[] EnumerateBackends();
}
```

The engine auto-detects the synthesis mode from the talker GGUF:

| Talker GGUF | Mode | Speaker | Instruct |
|---|---|---|---|
| `*-base-*.gguf` | Base + Clone | — | — |
| `*-customvoice-*.gguf` | CustomVoice | `speaker: "vivian"` | — |
| `*-voicedesign-*.gguf` | VoiceDesign | — | `instruct: "male, young..."` |

### Platform Support & GPU Backends

| Platform | RID | GPU backend | CPU fallback |
|---|---|---|---|
| Windows x64 | win-x64 | CUDA / Vulkan | Yes |
| Linux x64 | linux-x64 | CUDA / Vulkan | Yes |
| macOS ARM64 | osx-arm64 | Metal | Yes |
| MacCatalyst ARM64 | maccatalyst-arm64 | Metal | Yes |
| iOS ARM64 | ios-arm64 | Metal | Yes |
| Android ARM64 | android-arm64-v8a | Vulkan | Yes |

At runtime the GGML backend auto-selects the best available GPU. Set `GGML_BACKEND=CUDA0|Vulkan0|CPU` to force a specific device.

### Supported GGUF Models

Pre-converted GGUFs are available on Hugging Face:

- [Serveurperso/Qwen3-TTS-GGUF](https://huggingface.co/Serveurperso/Qwen3-TTS-GGUF)

Two GGUFs are required per pipeline instance:

- **Talker**: `qwen-talker-{size}-{mode}-{quant}.gguf`
  - sizes: `0.6b` (all modes), `1.7b` (all modes; VoiceDesign is 1.7B only)
  - modes: `base`, `customvoice`, `voicedesign`
  - quants: `F32`, `BF16`, `Q8_0`, `Q4_K_M`
- **Codec**: `qwen-tokenizer-12hz-{quant}.gguf` (shared across all modes)

### GGML Quick Start

```csharp
using SeasonTTS.GGML;

// 1. Create engine (loads models once)
using var engine = new QwenEngine(
    "Models/qwen-talker-0.6b-base-Q8_0.gguf",
    "Models/qwen-tokenizer-12hz-Q8_0.gguf");

// 2. Base synthesis
float[] audio = engine.Synthesize("Hello world.", lang: "English");
// audio is mono float PCM at 24 kHz

// 3. CustomVoice — named speaker
float[] customAudio = engine.Synthesize(
    "Hello from Vivian.", lang: "English", speaker: "vivian");
// Requires a customvoice talker GGUF; engine auto-validates mode.

// 4. VoiceDesign — attribute instruction (1.7B only)
float[] designAudio = engine.Synthesize(
    "A designed voice.", lang: "English",
    instruct: "male, young adult, moderate pitch");

// 5. Clone — reference audio (base model)
float[] refWav = LoadWav24k("reference.wav"); // your own loader
float[] cloneAudio = engine.Synthesize(
    "Cloned voice speaking.", lang: "English",
    refAudio24k: refWav, refText: "The transcript of the reference.");
```

### Streaming

```csharp
engine.SynthesizeStreaming(
    (chunk, n) => { /* process n samples of float PCM */ },
    "A longer text for streaming synthesis.",
    lang: "English");
```

Chunks are emitted every `codecChunkSec` (default 24 s) of decoded audio with a `codecLeftContextSec` (default 2 s) rolling overlap to avoid edge artifacts.

### Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
float[] audio = engine.Synthesize("...", cancellationToken: cts.Token);
```

The native loop polls the token at the top of every autoregressive step (~83 ms granularity).

### Runtime Backend Selection

Set the environment variable before creating the engine:

```bash
GGML_BACKEND=CUDA0     # force CUDA GPU 0
GGML_BACKEND=Vulkan0   # force Vulkan GPU 0
GGML_BACKEND=CPU       # force CPU only
```

## Build Native Libraries (GGML)

Native libraries for the GGML backend are built via the GitHub Actions workflow:

```
.github/workflows/build-qwentts.yml
```

Trigger it manually (`workflow_dispatch`) with the desired qwentts.cpp ref and CUDA version. Artifacts are produced for all six RIDs.

For local builds, clone qwentts.cpp with submodules and use one of:

```bash
./buildcuda.sh      # NVIDIA GPU
./buildvulkan.sh    # AMD / Intel GPU (Vulkan)
./buildcpu.sh       # CPU only
./buildall.sh       # all backends, runtime DL
```

The shared library target (`-DQWEN_SHARED=ON`) exports only the `qt_*` symbols described in the public ABI header (`qwen.h`).

## ONNX Model Download

You have two practical options:

### 1. Download via GitHub Actions

The repository includes `.github/workflows/download-qwen-tts-onnx.yml`. This workflow supports three choices:

- `qwen3-tts-12hz-0.6b-customvoice`
- `qwen3-tts-12hz-1.7b-customvoice`
- `qwen3-tts-12hz-0.6b-base`

It downloads the full selected Hugging Face repository snapshot and uploads it as a workflow artifact.

Notes:

- direct downloads from these public repositories usually work without `HF_TOKEN`
- if GitHub-hosted runners hit Hugging Face rate limits, setting `HF_TOKEN` as an optional repository secret can improve reliability

### 2. Download directly from Hugging Face

You can manually download the same ONNX bundles from the Hugging Face model pages listed above.

Typical local layout:

```text
Models/
  qwen3-tts-12hz-0.6b-customvoice-onnx/
  qwen3-tts-12hz-1.7b-customvoice-onnx/
  qwen3-tts-12hz-0.6b-base-onnx/
```

## Install

NuGet packaging is planned, but the first public release is source-first.

For now, either:

- reference the `SeasonTTS` project directly
- copy the published source into your solution
- wait for the upcoming NuGet package

## Result Format

**ONNX mode** — `Generate(...)` and `Clone(...)` return a `byte[]` containing a standard `.wav` file:

- sample format: 16-bit PCM WAV
- sample rate: `24000`
- output is ready to save with `File.WriteAllBytes(...)`

**GGML mode** — `Synthesize(...)` returns `float[]` mono PCM:

- sample format: 32-bit float PCM
- sample rate: `24000`
- convert to WAV or play directly with your preferred audio stack

## Copyright And Attribution

SeasonTTS contains code derived in part from two upstream projects:

### ElBruno.QwenTTS (ONNX backend)

- Repository: [elbruno/ElBruno.QwenTTS](https://github.com/elbruno/ElBruno.QwenTTS)
- License: MIT
- Copyright: Bruno Capuano
- Portions adapted for the ONNX inference pipeline, restructured into `CustomVoice` and `CloneVoice` with explicit initialization separation

### qwentts.cpp (GGML backend)

- Repository: [ServeurpersoCom/qwentts.cpp](https://github.com/ServeurpersoCom/qwentts.cpp)
- License: MIT
- Native C library wrapped via P/Invoke; managed bindings under `SeasonTTS.GGML`

Copied or adapted files retain their attribution headers where applicable. Full upstream license texts are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

- This repository code is released under the MIT License (see [LICENSE](LICENSE))
- Pre-exported ONNX model bundles and GGUF files are not covered by this repository's MIT license
- Model weights, tokenizer assets, and converted bundles remain governed by their own upstream terms:
  - Qwen3-TTS models by Alibaba / Qwen team — Apache 2.0
  - ONNX bundles by elbruno — see individual Hugging Face repos
  - GGUF bundles by Serveurperso — see [Serveurperso/Qwen3-TTS-GGUF](https://huggingface.co/Serveurperso/Qwen3-TTS-GGUF)

Recommended references:

- [ElBruno.QwenTTS](https://github.com/elbruno/ElBruno.QwenTTS)
- [qwentts.cpp](https://github.com/ServeurpersoCom/qwentts.cpp)
- [Qwen3-TTS official project](https://github.com/QwenLM/Qwen3-TTS)
- [Qwen official model collection](https://huggingface.co/collections/Qwen/qwen3-tts)

## Disclaimer

SeasonTTS is an independent open source project. It is not affiliated with, endorsed by, or distributed by Qwen, Alibaba, the ElBruno.QwenTTS project, or the qwentts.cpp project.
