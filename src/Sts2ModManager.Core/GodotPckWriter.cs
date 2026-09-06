using System.Security.Cryptography;
using System.Text;

namespace Sts2ModManager.Core;

public static class GodotPckWriter
{
    private const uint Magic = 0x43504447;
    // StS2's bundled Godot build accepts PCK v2/v3 but rejects v4.
    // The shipped community mods in the user's install use v3 with this flag.
    private const uint FormatVersion = 3;
    private const uint RelativeFileBase = 2;
    private const int Alignment = 32;

    public static void Write(
        string outputPath,
        IReadOnlyDictionary<string, byte[]> files,
        int engineMajor = 4,
        int engineMinor = 5,
        int enginePatch = 1)
    {
        if (files.Count == 0)
        {
            throw new ArgumentException("A PCK must contain at least one file.", nameof(files));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var entries = files
            .Select(pair => new PackEntry(NormalizePath(pair.Key), pair.Value ?? throw new ArgumentNullException(nameof(files))))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();

        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write((uint)engineMajor);
        writer.Write((uint)engineMinor);
        writer.Write((uint)enginePatch);
        writer.Write(RelativeFileBase);

        var fileBasePosition = stream.Position;
        writer.Write(0UL);
        var directoryOffsetPosition = stream.Position;
        writer.Write(0UL);
        for (var i = 0; i < 16; i++)
        {
            writer.Write(0U);
        }

        Align(stream, writer);
        var fileBase = checked((ulong)stream.Position);
        stream.Position = fileBasePosition;
        writer.Write(fileBase);
        stream.Position = (long)fileBase;

        foreach (var entry in entries)
        {
            entry.Offset = checked((ulong)stream.Position - fileBase);
            writer.Write(entry.Data);
            Align(stream, writer);
        }

        Align(stream, writer);
        var directoryOffset = checked((ulong)stream.Position);
        stream.Position = directoryOffsetPosition;
        writer.Write(directoryOffset);
        stream.Position = (long)directoryOffset;

        writer.Write((uint)entries.Length);
        foreach (var entry in entries)
        {
            var pathBytes = Encoding.UTF8.GetBytes(entry.Path);
            var paddedLength = checked((pathBytes.Length + 3) / 4 * 4);
            writer.Write((uint)paddedLength);
            writer.Write(pathBytes);
            for (var i = pathBytes.Length; i < paddedLength; i++)
            {
                writer.Write((byte)0);
            }

            writer.Write(entry.Offset);
            writer.Write((ulong)entry.Data.Length);
            writer.Write(MD5.HashData(entry.Data));
            writer.Write(0U);
        }
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("../", StringComparison.Ordinal))
        {
            throw new ArgumentException("PCK paths must be relative and must not escape the pack root.", nameof(path));
        }

        return normalized;
    }

    private static void Align(Stream stream, BinaryWriter writer)
    {
        var remainder = stream.Position % Alignment;
        if (remainder == 0) return;

        for (var i = remainder; i < Alignment; i++)
        {
            writer.Write((byte)0);
        }
    }

    private sealed class PackEntry(string path, byte[] data)
    {
        public string Path { get; } = path;
        public byte[] Data { get; } = data;
        public ulong Offset { get; set; }
    }
}
