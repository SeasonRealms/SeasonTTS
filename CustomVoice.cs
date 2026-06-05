
namespace SeasonTTS;

public class CustomVoice : IDisposable
{
    public const string DefaultLanguage = "auto";

    public static readonly QwenVoicePreset DefaultVoice = QwenVoicePreset.Ryan;

    readonly string model;
    readonly Func<SessionOptions>? sessionOptionsFactory;
    readonly Func<SessionOptions>? vocoderSessionOptionsFactory;

    TextTokenizer? _tokenizer;
    LanguageModel? _languageModel;
    Vocoder? _vocoder;
    EmbeddingStore? _embeddings;

    public CustomVoice(
        string model,
        Func<SessionOptions>? sessionOptionsFactory = null,
        Func<SessionOptions>? vocoderSessionOptionsFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        this.model = model;
        this.sessionOptionsFactory = sessionOptionsFactory;
        this.vocoderSessionOptionsFactory = vocoderSessionOptionsFactory;
    }

    public Task Initialize(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        return Task.CompletedTask;
    }

    void EnsureInitialized()
    {
        if (_tokenizer != null)
            return;

        var tokenizerDir = Path.Combine(model, "tokenizer");
        var embeddingsDir = Path.Combine(model, "embeddings");
        var configPath = Path.Combine(embeddingsDir, "config.json");

        _tokenizer = new TextTokenizer(tokenizerDir);
        _embeddings = new EmbeddingStore(embeddingsDir, configPath);
        _languageModel = new LanguageModel(model, _embeddings, sessionOptionsFactory);
        _vocoder = new Vocoder(Path.Combine(model, "vocoder.onnx"), vocoderSessionOptionsFactory ?? sessionOptionsFactory);
    }

    public async Task<byte[]> Generate(        
        string text,
        QwenVoicePreset voice = QwenVoicePreset.Ryan,
        string language = DefaultLanguage,
        string? instruct = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        var tempPath = Path.Combine(Path.GetTempPath(), $"season-qwen-{Guid.NewGuid():N}.wav");

        try
        {
            await SynthesizeAsync(
                text,
                voice.ToSpeakerName(),
                tempPath,
                language: language,
                instruct: instruct);

            return await File.ReadAllBytesAsync(tempPath, cancellationToken);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>Available speaker names from the model.</summary>
    public IReadOnlyCollection<string> Speakers
    {
        get
        {
            EnsureInitialized();
            return _embeddings!.GetAvailableSpeakers();
        }
    }

    /// <summary>
    /// Synthesizes speech from text and saves the output to a WAV file.
    /// </summary>
    /// <param name="text">Input text to synthesize. Must not be null, empty, and cannot exceed 10,000 characters.</param>
    /// <param name="speaker">Speaker name (must exist in model embeddings).</param>
    /// <param name="outputPath">Path where the output WAV file will be saved.</param>
    /// <param name="language">Language code (default: "auto" for auto-detection).</param>
    /// <param name="instruct">Optional instruction prompt for voice style modification.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
    /// <exception cref="ArgumentException">Thrown when text is empty or exceeds 10,000 characters.</exception>
    public async Task SynthesizeAsync(string text, string speaker, string outputPath,
                                     string language = "auto", string? instruct = null,
                                     IProgress<string>? progress = null)
    {
        // Input validation
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            throw new ArgumentException("Text cannot be empty.", nameof(text));
        if (text.Length > 10000)
            throw new ArgumentException("Text exceeds maximum length of 10,000 characters.", nameof(text));

        // Variant-aware instruct handling: 0.6B does not support instruction control
        //if (!string.IsNullOrEmpty(instruct) && !QwenModelVariantConfig.SupportsInstruct(_variant))
        //{
        //    var warning = $"Warning: Instruction text ignored \u2014 {_variant} model does not support instruction control. Use 1.7B for style instructions.";
        //    progress?.Report(warning);
        //    Console.WriteLine(warning);
        //    instruct = null;
        //}

        // Build prompt using tokenizer
        EnsureInitialized();
        var tokenIds = _tokenizer!.BuildCustomVoicePrompt(text, speaker, language, instruct);

        progress?.Report($"Tokenized input ({tokenIds.Length} tokens)");
        Console.WriteLine($"Generating speech ({tokenIds.Length} input tokens)...");

        // Generate audio codes via LM
        progress?.Report("Running language model inference...");
        var codes = _languageModel!.Generate(tokenIds, speaker, language);

        int timesteps = codes.GetLength(2);
        progress?.Report($"Generated {timesteps} audio frames");
        Console.WriteLine($"Generated {timesteps} audio frames");

        // Decode to waveform via vocoder
        progress?.Report("Decoding waveform via vocoder...");
        var waveform = _vocoder!.Decode(codes);

        // Write WAV file
        progress?.Report("Writing WAV file...");
        await Task.Run(() => WavWriter.Write(outputPath, waveform, sampleRate: 24000));

        var duration = waveform.Length / 24000.0;
        progress?.Report($"Saved {Path.GetFileName(outputPath)} ({waveform.Length} samples, {duration:F2}s)");
        Console.WriteLine($"Saved {outputPath} ({waveform.Length} samples, {duration:F2}s)");
    }

    public void Dispose()
    {
        _tokenizer?.Dispose();
        _embeddings?.Dispose();
        _languageModel?.Dispose();
        _vocoder?.Dispose();
    }
}

/// <summary>
/// Pre-defined voice presets for Qwen3-TTS CustomVoice model.
/// Use <see cref="QwenVoicePresetExtensions.ToSpeakerName"/> to convert to the string speaker name.
/// </summary>
public enum QwenVoicePreset
{
    Ryan,
    Serena,
    Vivian,
    Aiden,
    Eric,
    Dylan,
    UncleFu,
    OnoAnna,
    Sohee
}

/// <summary>
/// Extension methods for <see cref="QwenVoicePreset"/>.
/// </summary>
public static class QwenVoicePresetExtensions
{
    /// <summary>
    /// Converts a voice preset to the speaker name string used by the model.
    /// </summary>
    public static string ToSpeakerName(this QwenVoicePreset preset) => preset switch
    {
        QwenVoicePreset.Ryan => "ryan",
        QwenVoicePreset.Serena => "serena",
        QwenVoicePreset.Vivian => "vivian",
        QwenVoicePreset.Aiden => "aiden",
        QwenVoicePreset.Eric => "eric",
        QwenVoicePreset.Dylan => "dylan",
        QwenVoicePreset.UncleFu => "uncle_fu",
        QwenVoicePreset.OnoAnna => "ono_anna",
        QwenVoicePreset.Sohee => "sohee",
        _ => preset.ToString().ToLowerInvariant()
    };
}
