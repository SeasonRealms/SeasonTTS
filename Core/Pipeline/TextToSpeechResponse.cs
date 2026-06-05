namespace ElBruno.QwenTTS.Pipeline;

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
