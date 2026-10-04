// KrDiff format reference: hdiffpatch-rs, Copyright (c) 2025 TukanDev, MIT.
// See KuroPatch.LICENSE. Output MD5 is verified against Kuro's manifest.
using Tideward.Core.Games;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace Tideward.RPC.GameInstall;

public static class KuroPatch
{
    public static Task ApplyAsync(string source, string patch, string output, KuroPatchGroup group, CancellationToken token)
        => Task.Run(() => Apply(source, patch, output, group, token), token);

    private static void Apply(string source, string patch, string output, KuroPatchGroup group, CancellationToken token)
    {
        using var file = File.OpenRead(patch);
        if (Text(file, '&') != "HDIFF19") throw new InvalidDataException("Unsupported Kuro patch header.");
        string compression = Text(file, '&');
        _ = Text(file, '\0');
        if (Byte(file) != 1 || Byte(file) != 1) throw new InvalidDataException("Expected directory patch.");
        long oldPaths = Number(file); _ = Number(file);
        long newPaths = Number(file); _ = Number(file);
        long oldCount = Number(file), oldSize = Number(file), newCount = Number(file), newSize = Number(file);
        long sameCount = Number(file); _ = Number(file); _ = Number(file); _ = Number(file);
        if (sameCount != 0) throw new InvalidDataException("Unsupported Kuro same-file pairs.");
        long privateSize = Number(file), extraSize = Number(file), headSize = Number(file), headCompressed = Number(file), checksumSize = Number(file);
        Skip(file, checked(checksumSize * 4));
        using var head = Section(file, compression, headSize, headCompressed);
        var oldNames = Names(head, oldPaths); var newNames = Names(head, newPaths);
        var oldOffsets = Numbers(head, oldCount); var newOffsets = Numbers(head, newCount);
        var oldSizes = Numbers(head, oldCount); var newSizes = Numbers(head, newCount);
        for (long i = 0; i < newCount; i++) _ = Unsigned(head);
        var oldFiles = Entries(oldNames, oldOffsets, oldSizes);
        var newFiles = Entries(newNames, newOffsets, newSizes);
        CheckFiles(source, oldFiles, group.SrcFiles);
        CheckFiles(output, newFiles, group.DstFiles);
        if (oldSizes.Sum() != oldSize || newSizes.Sum() != newSize) throw new InvalidDataException("Patch size mismatch.");
        Skip(file, checked(privateSize + extraSize));
        if (Text(file, '&') != "HDIFF13") throw new InvalidDataException("Unsupported Kuro patch data.");
        compression = Text(file, '\0');
        if (Number(file) != newSize || Number(file) != oldSize) throw new InvalidDataException("Patch data size mismatch.");
        long covers = Number(file), coverSize = Number(file), coverCompressed = Number(file);
        long controlSize = Number(file), controlCompressed = Number(file), codeSize = Number(file), codeCompressed = Number(file);
        long dataSize = Number(file), dataCompressed = Number(file);
        using var cover = Section(file, compression, coverSize, coverCompressed);
        Skip(file, checked((controlCompressed > 0 ? controlCompressed : controlSize) + (codeCompressed > 0 ? codeCompressed : codeSize)));
        using var slice = new FileSliceStream(patch, file.Position, dataCompressed > 0 ? dataCompressed : dataSize);
        using Stream data = dataCompressed > 0 ? Decompress(slice, compression) : slice;
        using var old = oldFiles.Count == 0 ? (Stream)new MemoryStream() : new FileCombinedStream(oldFiles.Select(x => GameFilePath.Resolve(source, x.Path)));
        using var target = new OutputFiles(output, newFiles);
        byte[] buffer = new byte[128 * 1024];
        long readPosition = 0, writePosition = 0;
        for (long i = 0; i < covers; i++)
        {
            byte first = Byte(cover);
            long delta = checked((long)Unsigned(cover, first, 1));
            readPosition = checked(readPosition + ((first & 128) != 0 ? -delta : delta));
            if (oldSize > 0) { readPosition %= oldSize; if (readPosition < 0) readPosition += oldSize; }
            long gap = Number(cover), length = Number(cover);
            if (readPosition < 0 || length > oldSize - readPosition || gap > newSize - writePosition || length > newSize - writePosition - gap)
                throw new InvalidDataException("Patch cover exceeds file bounds.");
            Copy(data, target, gap, buffer, token);
            old.Position = readPosition;
            Copy(old, target, length, buffer, token);
            readPosition = checked(readPosition + length); writePosition = checked(writePosition + gap + length);
        }
        Copy(data, target, newSize - writePosition, buffer, token);
        target.Finish();
    }

    private sealed record Entry(string Path, long Size);

    private static void CheckFiles(string root, List<Entry> entries, List<KuroFile> expected)
    {
        if (entries.Count != expected.Count || entries.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidDataException("Patch file list mismatch.");
        foreach (var entry in entries)
        {
            _ = GameFilePath.Resolve(root, entry.Path);
            if (!expected.Any(x => x.Dest.Equals(entry.Path, StringComparison.OrdinalIgnoreCase) && x.Size == entry.Size))
                throw new InvalidDataException("Patch contains an unlisted file.");
        }
    }

    private static List<Entry> Entries(List<string> names, List<long> offsets, List<long> sizes)
    {
        var entries = new List<Entry>(); long index = -1;
        for (int i = 0; i < offsets.Count; i++)
        {
            index = checked(index + offsets[i] + 1);
            if (index >= names.Count) throw new InvalidDataException("Invalid patch path index.");
            entries.Add(new(names[(int)index].Replace('\\', '/'), sizes[i]));
        }
        return entries;
    }

    private static List<string> Names(Stream stream, long count)
    { if (count > 100000) throw new InvalidDataException("Excessive patch paths."); var result = new List<string>(); for (long i = 0; i < count; i++) result.Add(Text(stream, '\0', 4096)); return result; }
    private static List<long> Numbers(Stream stream, long count)
    { if (count > 100000) throw new InvalidDataException("Excessive patch files."); var result = new List<long>(); for (long i = 0; i < count; i++) result.Add(Number(stream)); return result; }
    private static byte Byte(Stream stream) { int value = stream.ReadByte(); return value < 0 ? throw new EndOfStreamException() : (byte)value; }
    private static long Number(Stream stream) => checked((long)Unsigned(stream));
    private static ulong Unsigned(Stream stream, int first = -1, int tag = 0)
    {
        int b = first < 0 ? Byte(stream) : first; ulong result = (ulong)(b & ((1 << (7 - tag)) - 1));
        bool more = (b & (1 << (7 - tag))) != 0;
        while (more) { b = Byte(stream); if (result > ulong.MaxValue >> 7) throw new InvalidDataException("Patch integer overflow."); result = (result << 7) | (uint)(b & 127); more = (b & 128) != 0; }
        return result;
    }
    private static string Text(Stream stream, char delimiter, int limit = 32)
    { var bytes = new List<byte>(); for (int i = 0; i < limit; i++) { byte b = Byte(stream); if (b == delimiter) return Encoding.UTF8.GetString(bytes.ToArray()); bytes.Add(b); } throw new InvalidDataException("Invalid patch string."); }
    private static void Skip(Stream stream, long length)
    { if (length < 0 || length > stream.Length - stream.Position) throw new EndOfStreamException(); stream.Position += length; }
    private static Stream Decompress(Stream stream, string compression)
        => compression == "zstd" ? new DecompressionStream(stream) : throw new InvalidDataException("Unsupported patch compression.");
    private static MemoryStream Section(Stream file, string compression, long size, long compressed)
    {
        if (size > 64 * 1024 * 1024 || compressed > 64 * 1024 * 1024) throw new InvalidDataException("Excessive patch metadata.");
        byte[] bytes = new byte[checked((int)(compressed > 0 ? compressed : size))]; file.ReadExactly(bytes);
        if (compressed == 0) return new MemoryStream(bytes);
        using var input = new MemoryStream(bytes); using var decoder = Decompress(input, compression);
        byte[] decoded = new byte[checked((int)size)]; decoder.ReadExactly(decoded);
        if (decoder.ReadByte() != -1) throw new InvalidDataException("Patch metadata size mismatch.");
        return new MemoryStream(decoded);
    }
    private static void Copy(Stream source, Stream dest, long count, byte[] buffer, CancellationToken token)
    { while (count > 0) { token.ThrowIfCancellationRequested(); int n = (int)Math.Min(count, buffer.Length); source.ReadExactly(buffer.AsSpan(0, n)); dest.Write(buffer, 0, n); count -= n; } }

    private sealed class OutputFiles(string root, List<Entry> files) : Stream
    {
        private int index; private FileStream? current; private long remaining;
        public override void Write(byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                if (current is null) Open();
                if (remaining == 0) { current!.Dispose(); current = null; continue; }
                int n = (int)Math.Min(count, remaining); current!.Write(buffer, offset, n); offset += n; count -= n; remaining -= n;
            }
        }
        private void Open()
        {
            if (index >= files.Count) throw new InvalidDataException("Patch output exceeds manifest.");
            var entry = files[index++]; string path = GameFilePath.Resolve(root, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); current = File.Create(path); remaining = entry.Size;
        }
        public void Finish()
        {
            if (remaining != 0) throw new InvalidDataException("Incomplete patched output.");
            current?.Dispose(); current = null;
            while (index < files.Count) { Open(); if (remaining != 0) throw new InvalidDataException("Incomplete patched output."); current!.Dispose(); current = null; }
        }
        protected override void Dispose(bool disposing) { current?.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => current?.Flush(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
}
