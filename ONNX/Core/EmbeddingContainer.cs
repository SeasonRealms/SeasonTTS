using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ElBruno.QwenTTS.Core;

/// <summary>
/// Single-file container that bundles the whole embeddings directory (all .npy and
/// .json files) into one "embeddings.bin" with a small TOC header, enabling
/// single-file deployment. Entry payloads are stored verbatim (original NPY bytes
/// including their headers), so parsing semantics are identical to reading the
/// individual files.
///
/// Layout (little-endian):
///   magic  : 8 bytes ASCII "SEEMB001"
///   version: int32 (=1)
///   count  : int32
///   per entry: name_len int32, name UTF8 bytes, offset int64, length int64
///   payloads follow the TOC in the order listed.
/// </summary>
internal sealed class EmbeddingContainer : IDisposable
{
    const string Magic = "SEEMB001";
    const int Version = 1;

    readonly FileStream _fs;
    readonly Dictionary<string, (long Offset, long Length)> _entries;
    readonly object _sync = new();

    EmbeddingContainer(FileStream fs, Dictionary<string, (long Offset, long Length)> entries)
    {
        _fs = fs;
        _entries = entries;
    }

    /// <summary>Opens an embeddings.bin container for reading.</summary>
    public static EmbeddingContainer Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

            var magicBytes = reader.ReadBytes(8);
            if (!Encoding.ASCII.GetBytes(Magic).SequenceEqual(magicBytes))
                throw new InvalidDataException("Not a Season embeddings container (bad magic)");

            int version = reader.ReadInt32();
            if (version != Version)
                throw new NotSupportedException($"Unsupported embeddings container version {version}");

            int count = reader.ReadInt32();
            var entries = new Dictionary<string, (long, long)>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                int nameLen = reader.ReadInt32();
                string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLen));
                long offset = reader.ReadInt64();
                long length = reader.ReadInt64();
                entries[name] = (offset, length);
            }

            return new EmbeddingContainer(fs, entries);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>Returns whether the container holds an entry with the given name.</summary>
    public bool TryGetEntry(string name, out (long Offset, long Length) entry)
        => _entries.TryGetValue(name, out entry);

    /// <summary>Opens a read-only stream positioned at the entry payload.</summary>
    public Stream OpenEntryStream(string name)
    {
        if (!_entries.TryGetValue(name, out var entry))
            throw new FileNotFoundException($"Entry not found in embeddings container: {name}");
        return new RangeReadStream(_fs, entry.Offset, entry.Length, _sync);
    }

    /// <summary>Reads a full entry as raw bytes.</summary>
    public byte[] ReadEntryBytes(string name)
    {
        if (!_entries.TryGetValue(name, out var entry))
            throw new FileNotFoundException($"Entry not found in embeddings container: {name}");
        lock (_sync)
        {
            var buffer = new byte[entry.Length];
            _fs.Seek(entry.Offset, SeekOrigin.Begin);
            _fs.ReadExactly(buffer);
            return buffer;
        }
    }

    /// <summary>Reads a full entry as UTF8 text (config.json / speaker_ids.json).</summary>
    public string ReadEntryText(string name)
    {
        if (!_entries.TryGetValue(name, out var entry))
            throw new FileNotFoundException($"Entry not found in embeddings container: {name}");
        lock (_sync)
        {
            var buffer = new byte[entry.Length];
            _fs.Seek(entry.Offset, SeekOrigin.Begin);
            _fs.ReadExactly(buffer);
            return Encoding.UTF8.GetString(buffer);
        }
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>
/// Read-only stream over a fixed byte range of an underlying stream. Shares the
/// parent FileStream with sibling entries, so seeks are re-issued before every
/// read and guarded by the container lock to stay safe under concurrent access.
/// </summary>
internal sealed class RangeReadStream : Stream
{
    readonly Stream _inner;
    readonly long _start;
    readonly long _length;
    readonly object _sync;
    long _position;

    public RangeReadStream(Stream inner, long start, long length, object sync)
    {
        _inner = inner;
        _start = start;
        _length = length;
        _sync = sync;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length)
            return 0;
        int toRead = (int)Math.Min(count, _length - _position);
        lock (_sync)
        {
            _inner.Seek(_start + _position, SeekOrigin.Begin);
            int read = _inner.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }
    }

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length)
            return 0;
        if (buffer.Length > _length - _position)
            buffer = buffer[..(int)(_length - _position)];
        lock (_sync)
        {
            _inner.Seek(_start + _position, SeekOrigin.Begin);
            int read = _inner.Read(buffer);
            _position += read;
            return read;
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
}
