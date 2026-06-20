
namespace ElBruno.QwenTTS.Core;

/// <summary>
/// GPU/CPU execution provider for ONNX Runtime sessions.
/// </summary>
public enum ExecutionProvider
{
    /// <summary>CPU execution (default). No additional NuGet packages required.</summary>
    Cpu,

    /// <summary>
    /// NVIDIA GPU via CUDA. Requires <c>Microsoft.ML.OnnxRuntime.Gpu</c> NuGet package
    /// and CUDA Toolkit + cuDNN installed.
    /// </summary>
    Cuda,

    /// <summary>
    /// DirectML GPU (NVIDIA, AMD, Intel on Windows 10/11).
    /// Requires <c>Microsoft.ML.OnnxRuntime.DirectML</c> NuGet package.
    /// Models must be patched with <c>python/patch_models_for_dml.py</c>.
    /// Uses hybrid mode: GPU for language model, CPU for vocoder.
    /// </summary>
    DirectML
}

/// <summary>
/// Shared catalog of supported TTS language choices.
/// </summary>
public sealed record QwenLanguageOption(string Value, string Label);

public static class QwenLanguageCatalog
{
    public static readonly IReadOnlyList<QwenLanguageOption> Options =
    [
        new("auto", "Auto"),
        new("english", "English"),
        new("spanish", "Spanish"),
        new("chinese", "Chinese"),
        new("japanese", "Japanese"),
        new("korean", "Korean"),
        new("russian", "Russian")
    ];

    public static readonly IReadOnlySet<string> SupportedLanguages =
        Options.Where(option => option.Value != "auto")
            .Select(option => option.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string? language) =>
        !string.IsNullOrWhiteSpace(language) && SupportedLanguages.Contains(language);
}

/// <summary>
/// Response from a text-to-speech synthesis request.
/// Contains audio data and metadata about the generated audio.
/// </summary>
public sealed class TextToSpeechResponse
{
    /// <summary>Raw audio bytes (WAV format).</summary>
    public required byte[] AudioData { get; init; }

    /// <summary>MIME type of the audio data (e.g., "audio/wav").</summary>
    public string MediaType { get; init; } = "audio/wav";

    /// <summary>Sample rate in Hz.</summary>
    public int SampleRate { get; init; } = 24000;

    /// <summary>Model identifier used for synthesis.</summary>
    public string ModelId { get; init; } = "qwen3-tts";
}
