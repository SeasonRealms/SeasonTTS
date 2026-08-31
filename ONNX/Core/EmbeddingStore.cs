using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ElBruno.QwenTTS.Core;

/// <summary>
/// Loads and provides access to all embedding matrices (.npy files).
/// Includes text embeddings, projection layers, codec embeddings, and speaker IDs.
/// </summary>
internal sealed class EmbeddingStore : IDisposable
{
    private readonly float[,] _textEmbedding;           // (vocab_size, text_hidden_size)
    private readonly float[,] _fc1Weight;               // (fc1_out, text_hidden_size)
    private readonly float[] _fc1Bias;                  // (fc1_out,)
    private readonly float[,] _fc2Weight;               // (hidden_size, fc1_out)
    private readonly float[] _fc2Bias;                  // (hidden_size,)
    private readonly float[,] _talkerCodecEmbedding;    // (vocab, hidden_size)
    private readonly float[][,] _cpCodecEmbeddings;     // 15 × (cp_vocab, cp_hidden)
    private readonly Dictionary<string, int> _speakerIds;

    // Optional CP projection weights (only present for 1.7B with re-exported code_predictor)
    private readonly float[,]? _cpProjectionWeight;  // (cp_hidden, talker_hidden) = (1024, 2048) for 1.7B
    private readonly float[]? _cpProjectionBias;      // (cp_hidden,) = (1024,) for 1.7B

    // Pre-computed projected embedding tables (avoids per-step matrix-vector multiplies during inference)
    private readonly float[][,]? _projectedCpCodecEmbeddings;     // 15 × (cp_vocab, cpModelHiddenSize)
    private readonly float[,]? _projectedTalkerCodecEmbedding;    // (talker_vocab, cpModelHiddenSize)

    // Non-null when the embeddings directory is bundled as a single embeddings.bin
    private readonly EmbeddingContainer? _container;

    // Dimensions derived from loaded arrays — no hardcoding
    private readonly int _textHiddenSize;   // text embedding dim (2048 for both 0.6B and 1.7B)
    private readonly int _fc1OutSize;       // intermediate MLP size
    private readonly int _hiddenSize;       // talker hidden_size (1024 for 0.6B, 2048 for 1.7B)
    private readonly int _cpHiddenSize;     // CP embedding dim (1024 for both variants)
    private readonly int _cpModelHiddenSize; // authoritative CP hidden_size from config.json (for ONNX input dim)
    
    public ModelConfig Config { get; }

    /// <summary>Talker hidden_size derived from loaded embedding dimensions.</summary>
    public int HiddenSize => _hiddenSize;

    /// <summary>Text embedding dimension derived from loaded data.</summary>
    public int TextHiddenSize => _textHiddenSize;

    /// <summary>Code Predictor embedding dimension derived from loaded data.</summary>
    public int CpHiddenSize => _cpHiddenSize;

    /// <summary>Whether CP projection weights are loaded (true for 1.7B with re-exported code_predictor).</summary>
    public bool HasCpProjection => _cpProjectionWeight != null;

    /// <summary>Authoritative Code Predictor model hidden_size from config.json (for ONNX input dim).</summary>
    public int CpModelHiddenSize => _cpModelHiddenSize;

    /// <summary>
    /// Creates the store from an embeddings directory. When the directory
    /// contains a bundled embeddings.bin, the container is used and
    /// <paramref name="configPath"/> is ignored; otherwise scattered files are
    /// loaded and <paramref name="configPath"/> must point to config.json.
    /// </summary>
    public EmbeddingStore(string embeddingsDir, string? configPath)
    {
        // Container mode: a single embeddings.bin bundles every .npy/.json file.
        // Scattered-file mode is kept as a fallback for legacy model folders.
        var containerPath = Path.Combine(embeddingsDir, "embeddings.bin");
        _container = File.Exists(containerPath) ? EmbeddingContainer.Open(containerPath) : null;

        // Load config
        var configJson = _container != null
            ? _container.ReadEntryText("config.json")
            : File.ReadAllText(configPath ?? throw new InvalidDataException("config.json path is required when embeddings.bin is absent."));
        Config = JsonSerializer.Deserialize<ModelConfig>(configJson)
            ?? throw new InvalidDataException("Failed to parse config.json");

        // Load text embedding and projection
        _textEmbedding = ReadMatrix(embeddingsDir, "text_embedding.npy");
        _fc1Weight = ReadMatrix(embeddingsDir, "text_projection_fc1_weight.npy");
        _fc1Bias = ReadVector(embeddingsDir, "text_projection_fc1_bias.npy");
        _fc2Weight = ReadMatrix(embeddingsDir, "text_projection_fc2_weight.npy");
        _fc2Bias = ReadVector(embeddingsDir, "text_projection_fc2_bias.npy");

        // Load talker codec embedding
        _talkerCodecEmbedding = ReadMatrix(embeddingsDir, "talker_codec_embedding.npy");

        // Load CP codec embeddings (15 groups)
        _cpCodecEmbeddings = new float[15][,];
        for (int i = 0; i < 15; i++)
            _cpCodecEmbeddings[i] = ReadMatrix(embeddingsDir, $"cp_codec_embedding_{i}.npy");

        // Load speaker IDs
        var speakerJson = _container != null
            ? _container.ReadEntryText("speaker_ids.json")
            : File.ReadAllText(Path.Combine(embeddingsDir, "speaker_ids.json"));
        _speakerIds = JsonSerializer.Deserialize<Dictionary<string, int>>(speakerJson)
            ?? throw new InvalidDataException("Failed to parse speaker_ids.json");

        // Derive dimensions from loaded arrays
        _textHiddenSize = _textEmbedding.GetLength(1);
        _fc1OutSize = _fc1Weight.GetLength(0);
        _hiddenSize = _fc2Weight.GetLength(0);
        _cpHiddenSize = _cpCodecEmbeddings[0].GetLength(1);

        // Authoritative CP hidden_size: prefer config.json, fall back to array-derived value
        _cpModelHiddenSize = Config.code_predictor.hidden_size > 0
            ? Config.code_predictor.hidden_size
            : _cpHiddenSize;

        // Optional: load CP projection weights (only present for 1.7B with re-exported code_predictor)
        if (EntryExists(embeddingsDir, "cp_projection_weight.npy") && EntryExists(embeddingsDir, "cp_projection_bias.npy"))
        {
            _cpProjectionWeight = ReadMatrix(embeddingsDir, "cp_projection_weight.npy");
            _cpProjectionBias = ReadVector(embeddingsDir, "cp_projection_bias.npy");

            // Validate projection weight output dim matches bias length
            if (_cpProjectionWeight.GetLength(0) != _cpProjectionBias.Length)
                throw new InvalidDataException(
                    $"CP projection dimension mismatch: weight rows ({_cpProjectionWeight.GetLength(0)}) != bias length ({_cpProjectionBias.Length})");

            // Validate projection input dim matches talker hidden_size
            if (_cpProjectionWeight.GetLength(1) != _hiddenSize)
                throw new InvalidDataException(
                    $"CP projection input mismatch: weight columns ({_cpProjectionWeight.GetLength(1)}) != hidden_size ({_hiddenSize})");

            // Pre-compute projected embedding tables (SIMD GEMM + 16-way parallel).
            // Replaces the per-token scalar MatMul loop; see ProjectTable below.
            System.Diagnostics.Debug.WriteLine($"[EmbeddingStore] hidden={_hiddenSize} cpHidden={_cpHiddenSize} cpVocab={_cpCodecEmbeddings[0].GetLength(0)}");

            _projectedCpCodecEmbeddings = new float[15][,];
            float[,]? talkerTable = null;
            Parallel.For(0, 16, i =>
            {
                if (i < 15)
                    _projectedCpCodecEmbeddings[i] =
                        ProjectTable(_cpCodecEmbeddings[i], _cpProjectionWeight, _cpProjectionBias);
                else
                    talkerTable = ProjectTable(_talkerCodecEmbedding, _cpProjectionWeight, _cpProjectionBias);
            });
            _projectedTalkerCodecEmbedding = talkerTable!;
        }
    }

    /// <summary>
    /// Looks up text embedding for a token ID and writes to output.
    /// </summary>
    public void TextEmbedding(int tokenId, Span<float> output)
    {
        if (output.Length != _textHiddenSize)
            throw new ArgumentException($"Output must be length {_textHiddenSize}");
        
        for (int i = 0; i < _textHiddenSize; i++)
            output[i] = _textEmbedding[tokenId, i];
    }

    /// <summary>
    /// Applies text projection MLP: output = fc2(silu(fc1(input)))
    /// Maps from text_hidden_size → talker hidden_size.
    /// </summary>
    public void TextProjection(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length != _textHiddenSize)
            throw new ArgumentException($"Input must be length {_textHiddenSize}");
        if (output.Length != _hiddenSize)
            throw new ArgumentException($"Output must be length {_hiddenSize}");

        // fc1: (fc1_out, text_hidden_size) @ input + bias → hidden
        var hidden = new float[_fc1OutSize];
        MatMul(_fc1Weight, input, hidden);
        for (int i = 0; i < _fc1OutSize; i++)
            hidden[i] = SiLU(hidden[i] + _fc1Bias[i]);

        // fc2: (hidden_size, fc1_out) @ hidden + bias → output
        MatMul(_fc2Weight, hidden, output);
        for (int i = 0; i < _hiddenSize; i++)
            output[i] += _fc2Bias[i];
    }

    /// <summary>
    /// Looks up talker codec embedding for a token ID.
    /// </summary>
    public void TalkerCodecEmbedding(int tokenId, Span<float> output)
    {
        if (output.Length != _hiddenSize)
            throw new ArgumentException($"Output must be length {_hiddenSize}");
        
        for (int i = 0; i < _hiddenSize; i++)
            output[i] = _talkerCodecEmbedding[tokenId, i];
    }

    /// <summary>
    /// Looks up CP codec embedding for a group and token ID.
    /// groupIndex is 0-14 (maps to cp_codec_embedding_0..14).
    /// </summary>
    public void CpCodecEmbedding(int groupIndex, int tokenId, Span<float> output)
    {
        if (groupIndex < 0 || groupIndex >= 15)
            throw new ArgumentException($"groupIndex must be 0-14, got {groupIndex}");
        if (output.Length != _cpHiddenSize)
            throw new ArgumentException($"Output must be length {_cpHiddenSize}");
        
        var table = _cpCodecEmbeddings[groupIndex];
        for (int i = 0; i < _cpHiddenSize; i++)
            output[i] = table[tokenId, i];
    }

    /// <summary>
    /// Applies the CP projection: output = weight @ input + bias.
    /// Maps from talker hidden_size (2048 for 1.7B) → cp_hidden_size (1024).
    /// Only available when <see cref="HasCpProjection"/> is true.
    /// </summary>
    public void CpProjection(ReadOnlySpan<float> input, Span<float> output)
    {
        if (_cpProjectionWeight == null || _cpProjectionBias == null)
            throw new InvalidOperationException("CP projection weights not loaded");

        if (input.Length < _cpProjectionWeight.GetLength(1))
            throw new ArgumentException(
                $"CP projection input too short: got {input.Length}, need {_cpProjectionWeight.GetLength(1)}");
        if (output.Length < _cpProjectionWeight.GetLength(0))
            throw new ArgumentException(
                $"CP projection output too short: got {output.Length}, need {_cpProjectionWeight.GetLength(0)}");

        MatMul(_cpProjectionWeight, input, output);
        int outDim = _cpProjectionWeight.GetLength(0);
        for (int i = 0; i < outDim; i++)
            output[i] += _cpProjectionBias[i];
    }

    /// <summary>
    /// Looks up pre-projected CP codec embedding (projected from talker space to CP input space at init).
    /// Avoids per-step matrix-vector multiplies during inference.
    /// Only available when <see cref="HasCpProjection"/> is true.
    /// </summary>
    public void ProjectedCpCodecEmbedding(int groupIndex, int tokenId, Span<float> output)
    {
        if (_projectedCpCodecEmbeddings == null)
            throw new InvalidOperationException("Projected CP codec embeddings not available");
        if (groupIndex < 0 || groupIndex >= 15)
            throw new ArgumentException($"groupIndex must be 0-14, got {groupIndex}");

        var table = _projectedCpCodecEmbeddings[groupIndex];
        int dim = table.GetLength(1);
        for (int i = 0; i < dim; i++)
            output[i] = table[tokenId, i];
    }

    /// <summary>
    /// Looks up pre-projected talker codec embedding (projected from talker space to CP input space at init).
    /// Avoids per-step matrix-vector multiplies during inference.
    /// Only available when <see cref="HasCpProjection"/> is true.
    /// </summary>
    public void ProjectedTalkerCodecEmbedding(int tokenId, Span<float> output)
    {
        if (_projectedTalkerCodecEmbedding == null)
            throw new InvalidOperationException("Projected talker codec embedding not available");

        int dim = _projectedTalkerCodecEmbedding.GetLength(1);
        for (int i = 0; i < dim; i++)
            output[i] = _projectedTalkerCodecEmbedding[tokenId, i];
    }

    /// <summary>
    /// Gets the speaker token ID for a speaker name.
    /// </summary>
    public int GetSpeakerId(string speaker)
    {
        if (!_speakerIds.TryGetValue(speaker, out var id))
            throw new ArgumentException($"Unknown speaker: {speaker}");
        return id;
    }

    /// <summary>
    /// Gets the list of available speaker names.
    /// </summary>
    public IReadOnlyCollection<string> GetAvailableSpeakers() => _speakerIds.Keys;

    /// <summary>
    /// Gets the talker codec embedding for a speaker as a float array.
    /// Used for similarity search and speaker matching.
    /// </summary>
    /// <param name="speakerId">Speaker token ID (codec embedding token).</param>
    /// <returns>Embedding vector (<see cref="HiddenSize"/> dimensions).</returns>
    public float[] GetSpeakerEmbedding(int speakerId)
    {
        var embedding = new float[_hiddenSize];
        for (int i = 0; i < _hiddenSize; i++)
            embedding[i] = _talkerCodecEmbedding[speakerId, i];
        return embedding;
    }

    /// <summary>
    /// Gets all speaker embeddings as a collection for similarity search.
    /// </summary>
    /// <returns>Tuples of (speakerName, embedding) for all available speakers.</returns>
    public IEnumerable<(string name, float[] embedding)> GetAllSpeakerEmbeddings()
    {
        foreach (var (name, id) in _speakerIds)
        {
            yield return (name, GetSpeakerEmbedding(id));
        }
    }

    public void Dispose()
    {
        _container?.Dispose();
    }

    // Container-aware load helpers: single-file bundle when present, scattered files otherwise.
    float[,] ReadMatrix(string embeddingsDir, string name)
        => _container != null
            ? NpyReader.ReadFloat2D(_container, name)
            : NpyReader.ReadFloat2D(Path.Combine(embeddingsDir, name));

    float[] ReadVector(string embeddingsDir, string name)
        => _container != null
            ? NpyReader.ReadFloat1D(_container, name)
            : NpyReader.ReadFloat1D(Path.Combine(embeddingsDir, name));

    bool EntryExists(string embeddingsDir, string name)
        => _container != null
            ? _container.TryGetEntry(name, out _)
            : File.Exists(Path.Combine(embeddingsDir, name));

    private static float SiLU(float x) => x / (1.0f + MathF.Exp(-x));

    /// <summary>
    /// Batched projection: dst = src @ weight^T + bias.
    /// SIMD dot products (Vector&lt;float&gt;, AVX2/NEON width) per output row; callers
    /// parallelize across the 15 CP groups + talker table (Parallel.For in the constructor).
    /// </summary>
    static float[,] ProjectTable(float[,] src, float[,] weight, float[] bias)
    {
        int rows = src.GetLength(0);
        int inDim = src.GetLength(1);
        int outDim = weight.GetLength(0);

        // Guard against the source table being narrower than the projection input
        // (e.g. cp_codec_embedding stored at cp_hidden instead of talker hidden_size).
        if (inDim != weight.GetLength(1))
            throw new InvalidDataException(
                $"Projection input mismatch: source dim ({inDim}) != weight columns ({weight.GetLength(1)})");

        var dst = new float[rows, outDim];
        int vecWidth = Vector<float>.Count;

        for (int t = 0; t < rows; t++)
        {
            var row = MemoryMarshal.CreateSpan(ref src[t, 0], inDim);
            var drow = MemoryMarshal.CreateSpan(ref dst[t, 0], outDim);

            for (int j = 0; j < outDim; j++)
            {
                var wrow = MemoryMarshal.CreateSpan(ref weight[j, 0], inDim);
                var acc = Vector<float>.Zero;
                int k = 0;
                for (; k <= inDim - vecWidth; k += vecWidth)
                    acc += new Vector<float>(row.Slice(k)) * new Vector<float>(wrow.Slice(k));

                float sum = Vector.Sum(acc) + bias[j];
                for (; k < inDim; k++)
                    sum += wrow[k] * row[k];
                drow[j] = sum;
            }
        }
        return dst;
    }

    /// <summary>
    /// Matrix-vector multiply: output = weight @ input
    /// weight is (M, N), input is (N,), output is (M,)
    /// </summary>
    private static void MatMul(float[,] weight, ReadOnlySpan<float> input, Span<float> output)
    {
        int M = weight.GetLength(0);
        int N = weight.GetLength(1);
        
        for (int i = 0; i < M; i++)
        {
            float sum = 0;
            for (int j = 0; j < N; j++)
                sum += weight[i, j] * input[j];
            output[i] = sum;
        }
    }
}

/// <summary>
/// Model configuration loaded from config.json
/// </summary>
internal sealed class ModelConfig
{
    public TalkerConfig talker { get; set; } = new();
    public CodePredictorConfig code_predictor { get; set; } = new();
    public TtsConfig tts { get; set; } = new();
    public Dictionary<string, int> language_ids { get; set; } = new();
    public Dictionary<string, object> speaker_dialect { get; set; } = new();
}

internal sealed class TalkerConfig
{
    public int codec_eos_token_id { get; set; }
    public int codec_pad_id { get; set; }
    public int codec_bos_id { get; set; }
    public int codec_think_id { get; set; }
    public int codec_nothink_id { get; set; }
    public int codec_think_bos_id { get; set; }
    public int codec_think_eos_id { get; set; }
    public int num_code_groups { get; set; }
    public int hidden_size { get; set; }
    public int text_hidden_size { get; set; }
    public int num_hidden_layers { get; set; }
    public int num_key_value_heads { get; set; }
    public int head_dim { get; set; }
    public int vocab_size { get; set; }
}

internal sealed class CodePredictorConfig
{
    public int num_hidden_layers { get; set; }
    public int num_key_value_heads { get; set; }
    public int head_dim { get; set; }
    public int vocab_size { get; set; }
    public int hidden_size { get; set; }
}

internal sealed class TtsConfig
{
    public int tts_bos_token_id { get; set; }
    public int tts_eos_token_id { get; set; }
    public int tts_pad_token_id { get; set; }
}
