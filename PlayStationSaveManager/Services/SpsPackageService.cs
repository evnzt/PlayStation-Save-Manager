using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayStationSaveManager.Services;

/// <summary>
/// Reads legacy SharkPort/X-Port SPS save packages and writes a lossless
/// temporary EMS/PSU representation for PSM's established preview pipeline.
/// The original SPS package is never modified.
/// </summary>
public static class SpsPackageService
{
    private static readonly byte[] Magic =
    {
        0x0D, 0x00, 0x00, 0x00,
        (byte)'S', (byte)'h', (byte)'a', (byte)'r',
        (byte)'k', (byte)'P', (byte)'o', (byte)'r',
        (byte)'t', (byte)'S', (byte)'a', (byte)'v',
        (byte)'e'
    };

    private const int SpsEntryMinimumLength = 98;
    private const int PsuEntryLength = 512;
    private const int PsuAlignment = 1024;

    public static async Task ConvertToPsuAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);

        var magic = new byte[Magic.Length];
        await ReadExactlyAsync(input, magic, cancellationToken);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException(
                $"{Path.GetFileName(sourcePath)} is not a valid SharkPort SPS package.");

        _ = await ReadUInt32Async(input, cancellationToken); // save type
        _ = await ReadLengthPrefixedBytesAsync(input, cancellationToken); // display name
        _ = await ReadLengthPrefixedBytesAsync(input, cancellationToken); // date stamp
        _ = await ReadLengthPrefixedBytesAsync(input, cancellationToken); // comment
        _ = await ReadUInt32Async(input, cancellationToken); // declared body length

        var root = await ReadSpsEntryAsync(input, cancellationToken);
        if (root.Length < 2)
            throw new InvalidDataException("The SPS package has an invalid root directory.");

        var fileCount = checked((int)root.Length - 2);
        var files = new List<SpsFile>(fileCount);

        for (var index = 0; index < fileCount; index++)
        {
            var entry = await ReadSpsEntryAsync(input, cancellationToken);

            if (entry.Length > int.MaxValue)
                throw new InvalidDataException(
                    $"The SPS entry '{entry.Name}' is too large.");

            var data = new byte[checked((int)entry.Length)];
            await ReadExactlyAsync(input, data, cancellationToken);
            files.Add(new SpsFile(entry, data));
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var rootPsu = CreatePsuEntry(
            root.Mode,
            checked((uint)fileCount + 2),
            root.Created,
            root.Modified,
            root.Name);
        await output.WriteAsync(rootPsu, cancellationToken);

        var dot = CreatePsuEntry(
            root.Mode,
            checked((uint)fileCount + 2),
            root.Created,
            root.Modified,
            ".");
        await output.WriteAsync(dot, cancellationToken);

        var dotDot = CreatePsuEntry(
            root.Mode,
            0,
            root.Created,
            root.Modified,
            "..");
        await output.WriteAsync(dotDot, cancellationToken);

        foreach (var file in files)
        {
            var header = CreatePsuEntry(
                file.Entry.Mode,
                file.Entry.Length,
                file.Entry.Created,
                file.Entry.Modified,
                file.Entry.Name);

            await output.WriteAsync(header, cancellationToken);
            await output.WriteAsync(file.Data, cancellationToken);

            var padding = RoundUp(file.Data.Length, PsuAlignment) - file.Data.Length;
            if (padding > 0)
                await output.WriteAsync(new byte[padding], cancellationToken);
        }

        await output.FlushAsync(cancellationToken);
    }

    private static async Task<SpsEntry> ReadSpsEntryAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var fixedHeader = new byte[SpsEntryMinimumLength];
        await ReadExactlyAsync(input, fixedHeader, cancellationToken);

        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(
            fixedHeader.AsSpan(0, 2));
        if (headerLength < SpsEntryMinimumLength)
            throw new InvalidDataException("An SPS entry header is too short.");

        var nameLength = Array.IndexOf(fixedHeader, (byte)0, 2, 64);
        if (nameLength < 0)
            nameLength = 64;
        else
            nameLength -= 2;

        var name = Encoding.ASCII.GetString(fixedHeader, 2, nameLength).Trim();
        var length = BinaryPrimitives.ReadUInt32LittleEndian(
            fixedHeader.AsSpan(66, 4));

        // SPS stores the two mode bytes in network order.
        var storedMode = BinaryPrimitives.ReadUInt16LittleEndian(
            fixedHeader.AsSpan(78, 2));
        var mode = BinaryPrimitives.ReverseEndianness(storedMode);

        var created = fixedHeader.AsSpan(82, 8).ToArray();
        var modified = fixedHeader.AsSpan(90, 8).ToArray();

        var remaining = headerLength - SpsEntryMinimumLength;
        if (remaining > 0)
            await SkipExactlyAsync(input, remaining, cancellationToken);

        return new SpsEntry(
            mode,
            length,
            created,
            modified,
            string.IsNullOrWhiteSpace(name) ? "Unnamed" : name);
    }

    private static byte[] CreatePsuEntry(
        ushort mode,
        uint length,
        byte[] created,
        byte[] modified,
        string name)
    {
        var data = new byte[PsuEntryLength];

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0, 2),
            mode);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(4, 4),
            length);

        created.AsSpan(0, Math.Min(8, created.Length))
            .CopyTo(data.AsSpan(8, 8));
        modified.AsSpan(0, Math.Min(8, modified.Length))
            .CopyTo(data.AsSpan(24, 8));

        var nameBytes = Encoding.ASCII.GetBytes(name);
        nameBytes.AsSpan(0, Math.Min(31, nameBytes.Length))
            .CopyTo(data.AsSpan(64, 32));

        return data;
    }

    private static bool IsDirectory(ushort mode) =>
        (mode & 0x2000) != 0;

    private static bool IsFile(ushort mode) =>
        (mode & 0x1000) != 0;

    private static int RoundUp(int value, int alignment) =>
        checked(((value + alignment - 1) / alignment) * alignment);

    private static async Task<uint> ReadUInt32Async(
        Stream input,
        CancellationToken cancellationToken)
    {
        var data = new byte[4];
        await ReadExactlyAsync(input, data, cancellationToken);
        return BinaryPrimitives.ReadUInt32LittleEndian(data);
    }

    private static async Task<byte[]> ReadLengthPrefixedBytesAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        var length = await ReadUInt32Async(input, cancellationToken);
        if (length > 16 * 1024 * 1024)
            throw new InvalidDataException("An SPS text field is unreasonably large.");

        var data = new byte[checked((int)length)];
        await ReadExactlyAsync(input, data, cancellationToken);
        return data;
    }

    private static async Task ReadExactlyAsync(
        Stream input,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await input.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("The SPS package ended unexpectedly.");
            offset += read;
        }
    }

    private static async Task SkipExactlyAsync(
        Stream input,
        int count,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(8192, count)];
        var remaining = count;
        while (remaining > 0)
        {
            var take = Math.Min(buffer.Length, remaining);
            var read = await input.ReadAsync(
                buffer.AsMemory(0, take),
                cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("The SPS package ended unexpectedly.");
            remaining -= read;
        }
    }

    private sealed record SpsEntry(
        ushort Mode,
        uint Length,
        byte[] Created,
        byte[] Modified,
        string Name);

    private sealed record SpsFile(
        SpsEntry Entry,
        byte[] Data);
}
