using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayStationSaveManager.Services;

/// <summary>
/// Native writers for legacy PS2 individual-save packages that myMC/myMC++
/// can read but does not export: CBS, SPS/XPS, and signed PS3 PSV.
///
/// A PSU package is used as PSM's normalized lossless representation.
/// </summary>
public static class Ps2PackageWriterService
{
    private const int PsuDirentLength = 512;
    private const int PsuAlignment = 1024;
    private const int SpsEntryLength = 250;

    private static readonly byte[] SpsMagic =
    [
        0x0D, 0x00, 0x00, 0x00,
        (byte)'S', (byte)'h', (byte)'a', (byte)'r',
        (byte)'k', (byte)'P', (byte)'o', (byte)'r',
        (byte)'t', (byte)'S', (byte)'a', (byte)'v',
        (byte)'e'
    ];

    private static readonly byte[] CbsRc4State =
    [
        0x5f,0x1f,0x85,0x6f,0x31,0xaa,0x3b,0x18,0x21,0xb9,0xce,0x1c,0x07,0x4c,0x9c,0xb4,
        0x81,0xb8,0xef,0x98,0x59,0xae,0xf9,0x26,0xe3,0x80,0xa3,0x29,0x2d,0x73,0x51,0x62,
        0x7c,0x64,0x46,0xf4,0x34,0x1a,0xf6,0xe1,0xba,0x3a,0x0d,0x82,0x79,0x0a,0x5c,0x16,
        0x71,0x49,0x8e,0xac,0x8c,0x9f,0x35,0x19,0x45,0x94,0x3f,0x56,0x0c,0x91,0x00,0x0b,
        0xd7,0xb0,0xdd,0x39,0x66,0xa1,0x76,0x52,0x13,0x57,0xf3,0xbb,0x4e,0xe5,0xdc,0xf0,
        0x65,0x84,0xb2,0xd6,0xdf,0x15,0x3c,0x63,0x1d,0x89,0x14,0xbd,0xd2,0x36,0xfe,0xb1,
        0xca,0x8b,0xa4,0xc6,0x9e,0x67,0x47,0x37,0x42,0x6d,0x6a,0x03,0x92,0x70,0x05,0x7d,
        0x96,0x2f,0x40,0x90,0xc4,0xf1,0x3e,0x3d,0x01,0xf7,0x68,0x1e,0xc3,0xfc,0x72,0xb5,
        0x54,0xcf,0xe7,0x41,0xe4,0x4d,0x83,0x55,0x12,0x22,0x09,0x78,0xfa,0xde,0xa7,0x06,
        0x08,0x23,0xbf,0x0f,0xcc,0xc1,0x97,0x61,0xc5,0x4a,0xe6,0xa0,0x11,0xc2,0xea,0x74,
        0x02,0x87,0xd5,0xd1,0x9d,0xb7,0x7e,0x38,0x60,0x53,0x95,0x8d,0x25,0x77,0x10,0x5e,
        0x9b,0x7f,0xd8,0x6e,0xda,0xa2,0x2e,0x20,0x4f,0xcd,0x8f,0xcb,0xbe,0x5a,0xe0,0xed,
        0x2c,0x9a,0xd4,0xe2,0xaf,0xd0,0xa9,0xe8,0xad,0x7a,0xbc,0xa8,0xf2,0xee,0xeb,0xf5,
        0xa6,0x99,0x28,0x24,0x6c,0x2b,0x75,0x5d,0xf8,0xd3,0x86,0x17,0xfb,0xc0,0x7b,0xb3,
        0x58,0xdb,0xc7,0x4b,0xff,0x04,0x50,0xe9,0x88,0x69,0xc9,0x2a,0xab,0xfd,0x5b,0x1b,
        0x8a,0xd9,0xec,0x27,0x44,0x0e,0x33,0xc8,0x6b,0x93,0x32,0x48,0xb6,0x30,0x43,0xa5
    ];

    // PSV signing constants used by the established PS1/PS2 PSV converter.
    private static readonly byte[] PsvPs2Key =
    [
        0xEA,0x02,0xCE,0xEF,0x5B,0xB4,0xD2,0x99,
        0x8F,0x61,0x19,0x10,0xD7,0x7F,0x51,0xC6
    ];

    private static readonly byte[] PsvIv =
    [
        0xB3,0x0F,0xFE,0xED,0xB7,0xDC,0x5E,0xB7,
        0x13,0x3D,0xA6,0x0D,0x1B,0x6B,0x2C,0xDC
    ];

    public static async Task WriteFromPsuAsync(
        string psuPath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var save =
            await ReadPsuAsync(
                psuPath,
                cancellationToken);

        var extension =
            Path.GetExtension(destinationPath)
                .ToLowerInvariant();

        switch (extension)
        {
            case ".cbs":
                await WriteCbsAsync(
                    save,
                    destinationPath,
                    cancellationToken);
                break;

            case ".sps":
                await WriteSpsAsync(
                    save,
                    destinationPath,
                    cancellationToken);
                break;

            case ".xps":
                await WriteXpsAsync(
                    save,
                    destinationPath,
                    cancellationToken);
                break;

            case ".psv":
                await WritePsvAsync(
                    save,
                    destinationPath,
                    cancellationToken);
                break;

            default:
                throw new NotSupportedException(
                    $"Native PS2 package output is not implemented for {extension.ToUpperInvariant()}.");
        }
    }

    private static async Task<PsuSave> ReadPsuAsync(
        string psuPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            psuPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);

        var root =
            await ReadPsuDirentAsync(
                stream,
                cancellationToken);

        var dot =
            await ReadPsuDirentAsync(
                stream,
                cancellationToken);

        var dotDot =
            await ReadPsuDirentAsync(
                stream,
                cancellationToken);

        if (!IsDirectory(root.Mode) ||
            !IsDirectory(dot.Mode) ||
            !IsDirectory(dotDot.Mode) ||
            root.Length < 2)
        {
            throw new InvalidDataException(
                "The temporary PSU package is not valid.");
        }

        var fileCount =
            checked((int)root.Length - 2);

        var files =
            new List<PsuFile>(
                fileCount);

        for (var index = 0;
             index < fileCount;
             index++)
        {
            var entry =
                await ReadPsuDirentAsync(
                    stream,
                    cancellationToken);

            if (!IsFile(entry.Mode))
                throw new InvalidDataException(
                    "PS2 package writers do not support subdirectories.");

            if (entry.Length > int.MaxValue)
                throw new InvalidDataException(
                    $"The file '{entry.Name}' is too large.");

            var data =
                new byte[checked((int)entry.Length)];

            await ReadExactlyAsync(
                stream,
                data,
                cancellationToken);

            var padding =
                RoundUp(
                    data.Length,
                    PsuAlignment) -
                data.Length;

            if (padding > 0)
            {
                stream.Seek(
                    padding,
                    SeekOrigin.Current);
            }

            files.Add(
                new PsuFile(
                    entry.Mode,
                    entry.Created,
                    entry.Modified,
                    entry.Name,
                    data));
        }

        return new PsuSave(
            root.Mode,
            root.Created,
            root.Modified,
            root.Name,
            files);
    }

    private static async Task WriteCbsAsync(
        PsuSave save,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        byte[] body;

        using (var bodyStream = new MemoryStream())
        {
            foreach (var file in save.Files)
            {
                var fileHeader =
                    new byte[64];

                file.Created.AsSpan(0, 8)
                    .CopyTo(fileHeader.AsSpan(0, 8));

                file.Modified.AsSpan(0, 8)
                    .CopyTo(fileHeader.AsSpan(8, 8));

                BinaryPrimitives.WriteUInt32LittleEndian(
                    fileHeader.AsSpan(16, 4),
                    checked((uint)file.Data.Length));

                BinaryPrimitives.WriteUInt16LittleEndian(
                    fileHeader.AsSpan(20, 2),
                    file.Mode);

                WriteAscii(
                    fileHeader,
                    32,
                    32,
                    file.Name);

                bodyStream.Write(fileHeader);
                bodyStream.Write(file.Data);
            }

            body =
                bodyStream.ToArray();
        }

        byte[] compressed;

        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(
                compressedStream,
                CompressionLevel.Optimal,
                leaveOpen: true))
            {
                await zlib.WriteAsync(
                    body,
                    cancellationToken);
            }

            compressed =
                compressedStream.ToArray();
        }

        CbsCrypt(compressed);

        const int headerLength = 296;
        var header =
            new byte[headerLength];

        Encoding.ASCII.GetBytes("CFU\0")
            .CopyTo(
                header,
                0);

        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(8, 4),
            headerLength);

        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(12, 4),
            checked((uint)body.Length));

        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(16, 4),
            checked((uint)compressed.Length));

        WriteAscii(
            header,
            20,
            32,
            save.DirectoryName);

        save.Created.AsSpan(0, 8)
            .CopyTo(header.AsSpan(52, 8));

        save.Modified.AsSpan(0, 8)
            .CopyTo(header.AsSpan(60, 8));

        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(72, 4),
            save.Mode);

        WriteAscii(
            header,
            92,
            72,
            save.DirectoryName);

        WriteAscii(
            header,
            164,
            132,
            "Exported by PlayStation Save Manager");

        var directory =
            Path.GetDirectoryName(
                destinationPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var output =
            new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

        await output.WriteAsync(
            header,
            cancellationToken);

        await output.WriteAsync(
            compressed,
            cancellationToken);

        await output.FlushAsync(
            cancellationToken);
    }

    private static async Task WriteSpsAsync(
        PsuSave save,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var body =
            CreateSpsXpsBody(
                save);

        var displayName =
            Encoding.ASCII.GetBytes(
                save.DirectoryName);

        var dateStamp =
            Encoding.ASCII.GetBytes(
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"));

        var comment =
            Encoding.ASCII.GetBytes(
                "PlayStation Save Manager");

        using var package =
            new MemoryStream();

        package.Write(SpsMagic);

        // SharkPort/SPS save type.
        WriteUInt32(
            package,
            0);

        WriteLengthPrefixed(
            package,
            displayName);

        WriteLengthPrefixed(
            package,
            dateStamp);

        WriteLengthPrefixed(
            package,
            comment);

        WriteUInt32(
            package,
            checked((uint)body.Length));

        package.Write(
            body);

        AppendSpsXpsChecksum(
            package);

        await WritePackageAsync(
            destinationPath,
            package.ToArray(),
            cancellationToken);
    }

    private static async Task WriteXpsAsync(
        PsuSave save,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var body =
            CreateSpsXpsBody(
                save);

        var displayName =
            Encoding.ASCII.GetBytes(
                save.DirectoryName);

        var dateStamp =
            Encoding.ASCII.GetBytes(
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"));

        using var package =
            new MemoryStream();

        // X-Port's fixed 0x15-byte prefix is the same 17-byte
        // SharkPortSave signature followed by a zero save-type field.
        package.Write(SpsMagic);
        WriteUInt32(
            package,
            0);

        // XPS has two variable strings, followed by a reserved zero
        // and then the body length. SPS has an additional comment
        // string instead.
        WriteLengthPrefixed(
            package,
            displayName);

        WriteLengthPrefixed(
            package,
            dateStamp);

        WriteUInt32(
            package,
            0);

        WriteUInt32(
            package,
            checked((uint)body.Length));

        package.Write(
            body);

        AppendSpsXpsChecksum(
            package);

        await WritePackageAsync(
            destinationPath,
            package.ToArray(),
            cancellationToken);
    }

    private static byte[] CreateSpsXpsBody(
        PsuSave save)
    {
        using var body =
            new MemoryStream();

        body.Write(
            CreateSpsEntry(
                save.DirectoryName,
                checked((uint)save.Files.Count + 2),
                save.Mode,
                save.Created,
                save.Modified));

        foreach (var file in save.Files)
        {
            body.Write(
                CreateSpsEntry(
                    file.Name,
                    checked((uint)file.Data.Length),
                    file.Mode,
                    file.Created,
                    file.Modified));

            body.Write(
                file.Data);
        }

        return body.ToArray();
    }

    private static void AppendSpsXpsChecksum(
        MemoryStream package)
    {
        var checksum =
            CalculateSpsChecksum(
                package.GetBuffer()
                    .AsSpan(
                        0,
                        checked((int)package.Length)));

        WriteUInt32(
            package,
            checksum);
    }

    private static async Task WritePackageAsync(
        string destinationPath,
        byte[] package,
        CancellationToken cancellationToken)
    {
        var directory =
            Path.GetDirectoryName(
                destinationPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(
            destinationPath,
            package,
            cancellationToken);
    }

    private static async Task WritePsvAsync(
        PsuSave save,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int psvHeaderLength = 64;
        const int ps2HeaderLength = 40;
        // These match the naturally aligned C structs used by Sony's
        // PSV format: ps2_MainDirInfo_t = 56 bytes and
        // ps2_FileInfo_t = 60 bytes.
        const int mainDirLength = 56;
        const int fileInfoLength = 60;

        var fileCount =
            save.Files.Count;

        var dataPosition =
            checked(
                psvHeaderLength +
                ps2HeaderLength +
                mainDirLength +
                fileInfoLength * fileCount);

        var iconSys =
            save.Files.FirstOrDefault(
                file =>
                    file.Name.Equals(
                        "icon.sys",
                        StringComparison.OrdinalIgnoreCase));

        var iconNames =
            iconSys is null
                ? new IconNames(
                    string.Empty,
                    string.Empty,
                    string.Empty)
                : ReadIconNames(
                    iconSys.Data);

        var header =
            new byte[psvHeaderLength];

        header[0] = 0x00;
        header[1] = (byte)'V';
        header[2] = (byte)'S';
        header[3] = (byte)'P';

        WriteAscii(
            header,
            8,
            20,
            "www.bucanero.com.ar");

        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(56, 4),
            0x2C);

        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(60, 4),
            2);

        var ps2Header =
            new byte[ps2HeaderLength];

        BinaryPrimitives.WriteUInt32LittleEndian(
            ps2Header.AsSpan(0, 4),
            checked((uint)save.Files.Sum(
                file => file.Data.Length)));

        BinaryPrimitives.WriteUInt32LittleEndian(
            ps2Header.AsSpan(36, 4),
            checked((uint)fileCount));

        var mainDir =
            new byte[mainDirLength];

        save.Created.AsSpan(0, 8)
            .CopyTo(mainDir.AsSpan(0, 8));

        save.Modified.AsSpan(0, 8)
            .CopyTo(mainDir.AsSpan(8, 8));

        BinaryPrimitives.WriteUInt32LittleEndian(
            mainDir.AsSpan(16, 4),
            checked((uint)fileCount + 2));

        BinaryPrimitives.WriteUInt32LittleEndian(
            mainDir.AsSpan(20, 4),
            save.Mode);

        WriteAscii(
            mainDir,
            24,
            32,
            save.DirectoryName);

        var fileInfos =
            new byte[fileInfoLength * fileCount];

        var currentDataPosition =
            dataPosition;

        for (var index = 0;
             index < fileCount;
             index++)
        {
            var file =
                save.Files[index];

            var infoOffset =
                index * fileInfoLength;

            Buffer.BlockCopy(
                file.Created,
                0,
                fileInfos,
                infoOffset,
                8);

            Buffer.BlockCopy(
                file.Modified,
                0,
                fileInfos,
                infoOffset + 8,
                8);

            WriteUInt32LittleEndian(
                fileInfos,
                infoOffset + 16,
                checked((uint)file.Data.Length));

            WriteUInt32LittleEndian(
                fileInfos,
                infoOffset + 20,
                file.Mode);

            WriteAscii(
                fileInfos,
                infoOffset + 24,
                32,
                file.Name);

            WriteUInt32LittleEndian(
                fileInfos,
                infoOffset + 56,
                checked((uint)currentDataPosition));

            SetIconPosition(
                ps2Header,
                file,
                currentDataPosition,
                iconNames);

            currentDataPosition =
                checked(
                    currentDataPosition +
                    file.Data.Length);
        }

        using var package =
            new MemoryStream();

        package.Write(
            header,
            0,
            header.Length);

        package.Write(
            ps2Header,
            0,
            ps2Header.Length);

        package.Write(
            mainDir,
            0,
            mainDir.Length);

        package.Write(
            fileInfos,
            0,
            fileInfos.Length);

        foreach (var file in save.Files)
        {
            package.Write(
                file.Data,
                0,
                file.Data.Length);
        }

        var result =
            package.ToArray();

        SignPs2Psv(result);

        var directory =
            Path.GetDirectoryName(
                destinationPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(
            destinationPath,
            result,
            cancellationToken);
    }

    private static void SetIconPosition(
        Span<byte> ps2Header,
        PsuFile file,
        int dataPosition,
        IconNames iconNames)
    {
        var position =
            checked((uint)dataPosition);

        var size =
            checked((uint)file.Data.Length);

        if (file.Name.Equals(
                "icon.sys",
                StringComparison.OrdinalIgnoreCase))
        {
            WritePair(
                ps2Header,
                4,
                position,
                size);
        }

        if (!string.IsNullOrWhiteSpace(iconNames.Normal) &&
            file.Name.Equals(
                iconNames.Normal,
                StringComparison.OrdinalIgnoreCase))
        {
            WritePair(
                ps2Header,
                12,
                position,
                size);
        }

        if (!string.IsNullOrWhiteSpace(iconNames.Copy) &&
            file.Name.Equals(
                iconNames.Copy,
                StringComparison.OrdinalIgnoreCase))
        {
            WritePair(
                ps2Header,
                20,
                position,
                size);
        }

        if (!string.IsNullOrWhiteSpace(iconNames.Delete) &&
            file.Name.Equals(
                iconNames.Delete,
                StringComparison.OrdinalIgnoreCase))
        {
            WritePair(
                ps2Header,
                28,
                position,
                size);
        }
    }

    private static void WritePair(
        Span<byte> buffer,
        int offset,
        uint position,
        uint size)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(
            buffer.Slice(offset, 4),
            position);

        BinaryPrimitives.WriteUInt32LittleEndian(
            buffer.Slice(offset + 4, 4),
            size);
    }

    private static IconNames ReadIconNames(
        byte[] iconSys)
    {
        if (iconSys.Length < 452)
            return new IconNames(
                string.Empty,
                string.Empty,
                string.Empty);

        return new IconNames(
            ReadAscii(
                iconSys,
                260,
                64),
            ReadAscii(
                iconSys,
                324,
                64),
            ReadAscii(
                iconSys,
                388,
                64));
    }

    private static void SignPs2Psv(
        byte[] package)
    {
        if (package.Length < 64)
            throw new InvalidDataException(
                "The generated PSV package is too short.");

        package.AsSpan(
                0x1C,
                20)
            .Clear();

        var salt =
            new byte[64];

        package.AsSpan(
                0x08,
                20)
            .CopyTo(salt);

        using (var aes = Aes.Create())
        {
            aes.Key = PsvPs2Key;
            aes.IV = PsvIv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            using var decryptor =
                aes.CreateDecryptor();

            var decrypted =
                decryptor.TransformFinalBlock(
                    salt,
                    0,
                    salt.Length);

            decrypted.CopyTo(
                salt,
                0);
        }

        salt.AsSpan(20)
            .Clear();

        for (var index = 0;
             index < salt.Length;
             index++)
        {
            salt[index] ^= 0x36;
        }

        byte[] innerHash;

        using (var sha1 = SHA1.Create())
        using (var inner = new MemoryStream())
        {
            inner.Write(salt);
            inner.Write(package);
            innerHash =
                sha1.ComputeHash(
                    inner.ToArray());
        }

        for (var index = 0;
             index < salt.Length;
             index++)
        {
            salt[index] ^= 0x6A;
        }

        byte[] signature;

        using (var sha1 = SHA1.Create())
        using (var outer = new MemoryStream())
        {
            outer.Write(salt);
            outer.Write(innerHash);

            signature =
                sha1.ComputeHash(
                    outer.ToArray());
        }

        signature.AsSpan(0, 20)
            .CopyTo(
                package.AsSpan(
                    0x1C,
                    20));
    }

    private static byte[] CreateSpsEntry(
        string name,
        uint length,
        ushort mode,
        byte[] created,
        byte[] modified)
    {
        var entry =
            new byte[SpsEntryLength];

        BinaryPrimitives.WriteUInt16LittleEndian(
            entry.AsSpan(0, 2),
            SpsEntryLength);

        WriteAscii(
            entry,
            2,
            64,
            name);

        BinaryPrimitives.WriteUInt32LittleEndian(
            entry.AsSpan(66, 4),
            length);

        var storedMode =
            BinaryPrimitives.ReverseEndianness(
                mode);

        BinaryPrimitives.WriteUInt32LittleEndian(
            entry.AsSpan(78, 4),
            storedMode);

        created.AsSpan(0, 8)
            .CopyTo(entry.AsSpan(82, 8));

        modified.AsSpan(0, 8)
            .CopyTo(entry.AsSpan(90, 8));

        WriteAscii(
            entry,
            114,
            64,
            name);

        WriteAscii(
            entry,
            178,
            64,
            name);

        return entry;
    }

    private static uint CalculateSpsChecksum(
        ReadOnlySpan<byte> data)
    {
        uint hash = 0;

        foreach (var value in data)
        {
            hash +=
                (uint)value <<
                (int)(hash % 24);
        }

        return hash;
    }

    private static void CbsCrypt(
        Span<byte> data)
    {
        var state =
            CbsRc4State.ToArray();

        byte j = 0;

        for (var index = 0;
             index < data.Length;
             index++)
        {
            var i =
                (byte)(
                    (index + 1) &
                    0xFF);

            j =
                (byte)(
                    j +
                    state[i]);

            (state[i], state[j]) =
                (state[j], state[i]);

            data[index] ^=
                state[
                    (byte)(
                        state[i] +
                        state[j])];
        }
    }

    private static async Task<PsuDirent> ReadPsuDirentAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var raw =
            new byte[PsuDirentLength];

        await ReadExactlyAsync(
            stream,
            raw,
            cancellationToken);

        return new PsuDirent(
            BinaryPrimitives.ReadUInt16LittleEndian(
                raw.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(4, 4)),
            raw.AsSpan(8, 8).ToArray(),
            raw.AsSpan(24, 8).ToArray(),
            ReadAscii(
                raw,
                64,
                32));
    }

    private static bool IsDirectory(
        ushort mode) =>
        (mode & 0x0020) != 0;

    private static bool IsFile(
        ushort mode) =>
        (mode & 0x0010) != 0;

    private static int RoundUp(
        int value,
        int alignment) =>
        checked(
            ((value + alignment - 1) /
             alignment) *
            alignment);

    private static void WriteLengthPrefixed(
        Stream stream,
        byte[] value)
    {
        WriteUInt32(
            stream,
            checked((uint)value.Length));

        stream.Write(value);
    }

    private static void WriteUInt32LittleEndian(
        byte[] buffer,
        int offset,
        uint value)
    {
        buffer[offset] =
            (byte)value;

        buffer[offset + 1] =
            (byte)(value >> 8);

        buffer[offset + 2] =
            (byte)(value >> 16);

        buffer[offset + 3] =
            (byte)(value >> 24);
    }

    private static void WriteUInt32(
        Stream stream,
        uint value)
    {
        Span<byte> data =
            stackalloc byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(
            data,
            value);

        stream.Write(data);
    }

    private static void WriteAscii(
        byte[] destination,
        int offset,
        int length,
        string value) =>
        WriteAscii(
            destination.AsSpan(),
            offset,
            length,
            value);

    private static void WriteAscii(
        Span<byte> destination,
        int offset,
        int length,
        string value)
    {
        destination.Slice(
                offset,
                length)
            .Clear();

        var encoded =
            Encoding.ASCII.GetBytes(
                value ?? string.Empty);

        encoded.AsSpan(
                0,
                Math.Min(
                    encoded.Length,
                    Math.Max(
                        0,
                        length - 1)))
            .CopyTo(
                destination.Slice(
                    offset,
                    length));
    }

    private static string ReadAscii(
        byte[] data,
        int offset,
        int length)
    {
        var slice =
            data.AsSpan(
                offset,
                Math.Min(
                    length,
                    data.Length - offset));

        var terminator =
            slice.IndexOf((byte)0);

        if (terminator >= 0)
            slice =
                slice.Slice(
                    0,
                    terminator);

        return Encoding.ASCII
            .GetString(slice)
            .Trim();
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read =
                await stream.ReadAsync(
                    buffer.AsMemory(
                        offset,
                        buffer.Length - offset),
                    cancellationToken);

            if (read == 0)
                throw new EndOfStreamException(
                    "The PSU package ended unexpectedly.");

            offset += read;
        }
    }

    private sealed record PsuDirent(
        ushort Mode,
        uint Length,
        byte[] Created,
        byte[] Modified,
        string Name);

    private sealed record PsuFile(
        ushort Mode,
        byte[] Created,
        byte[] Modified,
        string Name,
        byte[] Data);

    private sealed record PsuSave(
        ushort Mode,
        byte[] Created,
        byte[] Modified,
        string DirectoryName,
        IReadOnlyList<PsuFile> Files);

    private sealed record IconNames(
        string Normal,
        string Copy,
        string Delete);
}
