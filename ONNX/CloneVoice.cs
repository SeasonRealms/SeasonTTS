
// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonTTS

using ElBruno.QwenTTS.Core;
using ElBruno.QwenTTS.VoiceCloning;

namespace SeasonTTS.ONNX;

public class CloneVoice : IDisposable
{
    public const string DefaultLanguage = "auto";

    readonly string model;
    readonly Func<SessionOptions>? sessionOptionsFactory;
    TextTokenizer? tokenizer;
    EmbeddingStore? embeddings;
    LanguageModel? languageModel;
    Vocoder? vocoder;
    SpeakerEncoder? speakerEncoder;
    SpeechTokenizer? speechTokenizer;

    public CloneVoice(string model, Func<SessionOptions>? sessionOptionsFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        this.model = model;
        this.sessionOptionsFactory = sessionOptionsFactory;
    }

    public Task Initialize(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        return Task.CompletedTask;
    }

    public async Task<byte[]> Clone(
        string text,
        Stream referenceAudioStream,
        string? referenceText = null,
        string language = DefaultLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (referenceAudioStream is null)
        {
            throw new ArgumentNullException(nameof(referenceAudioStream));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        var referenceAudio = await ReadAllBytesAsync(referenceAudioStream, cancellationToken);
        var tempPath = Path.Combine(Path.GetTempPath(), $"season-qwen-{Guid.NewGuid():N}.wav");

        try
        {
            if (string.IsNullOrWhiteSpace(referenceText))
            {
                using var speakerStream = new MemoryStream(referenceAudio, writable: false);
                await SynthesizeAsync(text, speakerStream, tempPath, language: language);
            }
            else
            {
                using var speakerStream = new MemoryStream(referenceAudio, writable: false);
                using var refAudioStream = new MemoryStream(referenceAudio, writable: false);
                await SynthesizeAsync(text, speakerStream, tempPath, referenceText, refAudioStream, language: language);
            }

            return await File.ReadAllBytesAsync(tempPath, cancellationToken);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    void EnsureInitialized()
    {
        if (tokenizer != null)
            return;

        var tokenizerDir = Path.Combine(model, "tokenizer");
        var embeddingsDir = Path.Combine(model, "embeddings");
        var configPath = Path.Combine(embeddingsDir, "config.json");
        var speakerEncoderPath = Path.Combine(model, "speaker_encoder.onnx");

        if (!File.Exists(speakerEncoderPath))
        {
            throw new FileNotFoundException(
                "Speaker encoder model not found. Use the Base model (not CustomVoice) for voice cloning.",
                speakerEncoderPath);
        }

        tokenizer = new TextTokenizer(tokenizerDir);
        embeddings = new EmbeddingStore(embeddingsDir, configPath);
        languageModel = new LanguageModel(model, embeddings, sessionOptionsFactory);
        vocoder = new Vocoder(Path.Combine(model, "vocoder.onnx"), sessionOptionsFactory);
        speakerEncoder = new SpeakerEncoder(speakerEncoderPath, sessionOptionsFactory);
    }

    async Task SynthesizeAsync(
        string text,
        Stream speakerAudioStream,
        string outputPath,
        string language,
        IProgress<string>? progress = null)
    {
        progress?.Report("Extracting speaker embedding from reference audio...");
        var speakerEmbedding = speakerEncoder!.EncodeFromWav(speakerAudioStream);
        progress?.Report($"Speaker embedding extracted ({speakerEmbedding.Length} dimensions)");

        await SynthesizeWithEmbeddingAsync(
            text,
            speakerEmbedding,
            outputPath,
            refText: null,
            refAudioCodes: null,
            language: language,
            progress: progress);
    }

    async Task SynthesizeAsync(
        string text,
        Stream speakerAudioStream,
        string outputPath,
        string refText,
        Stream refAudioStream,
        string language,
        IProgress<string>? progress = null)
    {
        progress?.Report("Extracting speaker embedding from reference audio...");
        var speakerEmbedding = speakerEncoder!.EncodeFromWav(speakerAudioStream);
        progress?.Report($"Speaker embedding extracted ({speakerEmbedding.Length} dimensions)");

        progress?.Report("Encoding reference audio for ICL mode...");
        var refAudioCodes = GetSpeechTokenizer().EncodeFromWav(refAudioStream);
        int tFrames = refAudioCodes.GetLength(1);
        progress?.Report($"Reference audio encoded ({tFrames} frames)");

        await SynthesizeWithEmbeddingAsync(
            text,
            speakerEmbedding,
            outputPath,
            refText: refText,
            refAudioCodes: refAudioCodes,
            language: language,
            progress: progress);
    }

    async Task SynthesizeWithEmbeddingAsync(
        string text,
        float[] speakerEmbedding,
        string outputPath,
        string? refText,
        long[,,]? refAudioCodes,
        string language,
        IProgress<string>? progress = null)
    {
        var tokenIds = tokenizer!.BuildCustomVoicePrompt(text, "none", language, instruct: null);
        progress?.Report($"Tokenized input ({tokenIds.Length} tokens)");

        int[]? refTokenIds = null;
        if (!string.IsNullOrWhiteSpace(refText))
        {
            refTokenIds = tokenizer.Encode(refText);
            progress?.Report($"ICL mode: {refTokenIds.Length} ref text tokens, {refAudioCodes?.GetLength(1) ?? 0} ref audio frames");
        }

        progress?.Report("Running language model inference...");
        long[,,] codes = refTokenIds != null && refAudioCodes != null
            ? languageModel!.GenerateWithSpeakerEmbeddingAndRefText(
                tokenIds,
                speakerEmbedding,
                language,
                refTokenIds: refTokenIds,
                refAudioCodes: refAudioCodes)
            : languageModel!.GenerateWithSpeakerEmbedding(tokenIds, speakerEmbedding, language);

        int timesteps = codes.GetLength(2);
        progress?.Report($"Generated {timesteps} audio frames");

        progress?.Report("Decoding waveform via vocoder...");
        var waveform = vocoder!.Decode(codes);

        progress?.Report("Writing WAV file...");
        await Task.Run(() => WavWriter.Write(outputPath, waveform, sampleRate: 24000));
    }

    SpeechTokenizer GetSpeechTokenizer()
    {
        if (speechTokenizer != null)
            return speechTokenizer;

        var modelPath = Path.Combine(model, "tokenizer12hz_encode.onnx");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                "Speech tokenizer model not found. Required for ICL (ref_text) mode.",
                modelPath);
        }

        speechTokenizer = new SpeechTokenizer(modelPath, sessionOptionsFactory);
        return speechTokenizer;
    }

    static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var buffer))
            return buffer.AsSpan(0, (int)memoryStream.Length).ToArray();

        if (stream.CanSeek)
            stream.Position = 0;

        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }

    public void Dispose()
    {
        tokenizer?.Dispose();
        embeddings?.Dispose();
        languageModel?.Dispose();
        vocoder?.Dispose();
        speakerEncoder?.Dispose();
        speechTokenizer?.Dispose();
    }
}
