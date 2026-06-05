# SeasonTTS

SeasonTTS is a .NET text-to-speech library for Qwen3-TTS ONNX bundles.

The initial public release focuses on a simple application-facing API:

- instance-based preset voice generation through `new CustomVoice(model)` and `Generate(...)`
- instance-based voice cloning through `new CloneVoice(model)` and `Clone(...)`
- explicit initialization through `Initialize()` so model loading stays outside the hot inference path
- standard WAV byte output suitable for saving directly as `.wav`
- local model directory loading without requiring a Python runtime

The current implementation targets the pre-exported Qwen3-TTS ONNX bundles published by `elbruno` on Hugging Face. SeasonTTS is an independent project and is not affiliated with Qwen, Alibaba, or ElBruno.QwenTTS.

## Naming

- Package name: `SeasonTTS`
- Main runtime APIs: `SeasonTTS.CustomVoice`, `SeasonTTS.CloneVoice`
- Repository: [SeasonRealms/SeasonTTS](https://github.com/SeasonRealms/SeasonTTS)

## Origin

- SeasonTTS is built from a simplified and performance-tuned codebase derived in part from [ElBruno.QwenTTS](https://github.com/elbruno/ElBruno.QwenTTS).
- Source files copied or adapted from `ElBruno.QwenTTS` retain their original attribution headers where applicable.
- SeasonTTS restructures the public API around two direct entry points: `CustomVoice` and `CloneVoice`.
- The current release also separates initialization from inference so repeated synthesis avoids repeated model loading.

## Features

- Qwen3-TTS CustomVoice inference from .NET
- Qwen3-TTS Base voice cloning inference from .NET
- Explicit `Initialize()` step for model/session setup
- Returns standard RIFF/WAVE bytes instead of binding to a specific playback stack
- Supports local ONNX model directories
- Supports preset speaker selection via `QwenVoicePreset`
- Supports `instruct` style control on compatible 1.7B CustomVoice models
- Supports reference-audio cloning, with optional `referenceText` for ICL mode when the Base bundle includes `tokenizer12hz_encode.onnx`

## Current API

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

## Install

NuGet packaging is planned, but the first public release is source-first.

For now, either:

- reference the `SeasonTTS` project directly
- copy the published source into your solution
- wait for the upcoming NuGet package

## Supported Models

SeasonTTS currently targets these three ONNX model repositories:

| Model family | Hugging Face repo | Typical use |
|---|---|---|
| CustomVoice 0.6B | `elbruno/Qwen3-TTS-12Hz-0.6B-CustomVoice-ONNX` | preset voices, smaller footprint |
| CustomVoice 1.7B | `elbruno/Qwen3-TTS-12Hz-1.7B-CustomVoice-ONNX` | preset voices plus `instruct` style control |
| Base 0.6B | `elbruno/Qwen3-TTS-12Hz-0.6B-Base-ONNX` | voice cloning from reference audio |

Direct model pages:

- [elbruno/Qwen3-TTS-12Hz-0.6B-CustomVoice-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-0.6B-CustomVoice-ONNX)
- [elbruno/Qwen3-TTS-12Hz-1.7B-CustomVoice-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-1.7B-CustomVoice-ONNX)
- [elbruno/Qwen3-TTS-12Hz-0.6B-Base-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-0.6B-Base-ONNX)

These repositories are public. In normal direct-download usage, `HF_TOKEN` is usually not required.

## Model Download

You have two practical options:

### 1. Download via GitHub Actions

The repository includes:

- `.github/workflows/download-qwen-tts-onnx.yml`

This workflow supports three choices:

- `qwen3-tts-12hz-0.6b-customvoice`
- `qwen3-tts-12hz-1.7b-customvoice`
- `qwen3-tts-12hz-0.6b-base`

It downloads the full selected Hugging Face repository snapshot and uploads it as a workflow artifact.

Notes:

- direct downloads from these public repositories usually work without `HF_TOKEN`
- if GitHub-hosted runners hit Hugging Face rate limits, setting `HF_TOKEN` as an optional repository secret can improve reliability

### 2. Download directly from Hugging Face

You can manually download the same ONNX bundles from:

- [elbruno/Qwen3-TTS-12Hz-0.6B-CustomVoice-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-0.6B-CustomVoice-ONNX)
- [elbruno/Qwen3-TTS-12Hz-1.7B-CustomVoice-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-1.7B-CustomVoice-ONNX)
- [elbruno/Qwen3-TTS-12Hz-0.6B-Base-ONNX](https://huggingface.co/elbruno/Qwen3-TTS-12Hz-0.6B-Base-ONNX)

Typical local layout:

```text
Models/
  qwen3-tts-12hz-0.6b-customvoice-onnx/
  qwen3-tts-12hz-1.7b-customvoice-onnx/
  qwen3-tts-12hz-0.6b-base-onnx/
```

## Quick Start

### CustomVoice

This example matches the current sample app usage:

```csharp
using SeasonTTS;

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
using SeasonTTS;

var model = @"../../../../../../Models/qwen3-tts-12hz-1.7b-customvoice-onnx";
var customVoice = new CustomVoice(model);

await customVoice.Initialize();

byte[] wavBytes = await customVoice.Generate(
    "Welcome to SeasonTTS.",
    QwenVoicePreset.Ryan,
    instruct: "Speak warmly and slowly with a clear presentation style.");

File.WriteAllBytes("customvoice-17b.wav", wavBytes);
```

### CloneVoice

This example also follows the current `samples/Sample/App.cs` pattern:

```csharp
using SeasonTTS;

var model = @"../../../../../../Models/qwen3-tts-12hz-0.6b-base-onnx";
var cloneVoice = new CloneVoice(model);

await cloneVoice.Initialize();

using var referenceStream = File.OpenRead("reference.wav");

byte[] wavBytes = await cloneVoice.Clone(
    "This sentence uses the cloned voice.",
    referenceStream);

File.WriteAllBytes("clonevoice.wav", wavBytes);
```

Optional ICL mode:

```csharp
using SeasonTTS;

var model = @"../../../../../../Models/qwen3-tts-12hz-0.6b-base-onnx";
var cloneVoice = new CloneVoice(model);

await cloneVoice.Initialize();

using var referenceStream = File.OpenRead("reference.wav");

byte[] wavBytes = await cloneVoice.Clone(
    "This sentence uses the cloned voice.",
    referenceStream,
    referenceText: "The transcript of the reference audio.");

File.WriteAllBytes("clonevoice-icl.wav", wavBytes);
```

## Why This API

This initial API is intentionally simple:

- the caller creates either `CustomVoice` or `CloneVoice`
- `Initialize()` performs the expensive model and embedding setup ahead of inference
- `Generate(...)` and `Clone(...)` return ready-to-save WAV bytes
- the host application remains free to choose its own recording, playback, storage, or transport flow

This shape matches the current sample integration style and keeps app-side code small.

## Initialization Notes

The current public API expects the caller to initialize the instance explicitly before synthesis:

```csharp
var customVoice = new CustomVoice(modelDir);
await customVoice.Initialize();

var cloneVoice = new CloneVoice(modelDir);
await cloneVoice.Initialize();
```

This avoids paying model-loading cost inside every inference call.

## Result Format

`Generate(...)` and `Clone(...)` both return a `byte[]` containing a standard `.wav` file:

- sample format: 16-bit PCM WAV
- sample rate: `24000`
- output is ready to save with `File.WriteAllBytes(...)`

## Copyright And Attribution

SeasonTTS contains code derived in part from `ElBruno.QwenTTS`.

- SeasonTTS repository code is released under the MIT License
- portions derived from `ElBruno.QwenTTS` remain subject to the upstream MIT License and attribution requirements
- the upstream project and license text are listed in `THIRD-PARTY-NOTICES.md`
- copied or adapted files should retain their attribution headers

Project relationship:

- Upstream inspiration and source base: [elbruno/ElBruno.QwenTTS](https://github.com/elbruno/ElBruno.QwenTTS)
- This project simplifies the public API, restructures initialization, and continues development independently

## License Split

- This repository code is released under the MIT License
- Pre-exported ONNX model bundles are not covered by this repository MIT license
- Model weights, tokenizer assets, and converted ONNX bundles remain governed by their own upstream terms and repository metadata

Recommended references:

- [ElBruno.QwenTTS](https://github.com/elbruno/ElBruno.QwenTTS)
- [Qwen3-TTS official project](https://github.com/QwenLM/Qwen3-TTS)
- [Qwen official model collection](https://huggingface.co/collections/Qwen/qwen3-tts)

## Disclaimer

SeasonTTS is an independent open source project. It is not affiliated with, endorsed by, or distributed by Qwen, Alibaba, or the ElBruno.QwenTTS project.
