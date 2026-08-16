using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayStationSaveManager.Services;

public sealed record Ps1ExternalSaveData(
    string FileName,
    string FormatName,
    byte[] Data,
    string Description)
{
    public int BlocksUsed => Data.Length / Ps1MemoryCardService.BlockSize;
}

public static class Ps1ExternalSaveService
{
    private const int FrameSize = 128;
    private const int DirectoryOffset = 128;
    private const int DirectoryEntries = 15;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".gme", ".mcb", ".mcs", ".mcx", ".pda", ".ps1", ".psv", ".psx", ".raw"
        };

    public static string FileDialogFilter =>
        FormatCatalog.Ps1IndividualSaveFilter;

    public static bool IsSupportedExtension(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public static bool LooksLikePs1SingleSave(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            _ = Read(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPs1Psv(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return bytes.Length >= 0x84 &&
                   bytes[0] == 0x00 &&
                   bytes[1] == 0x56 &&
                   bytes[2] == 0x53 &&
                   bytes[3] == 0x50 &&
                   BitConverter.ToInt32(bytes, 0x3C) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static Ps1ExternalSaveData Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < Ps1MemoryCardService.BlockSize)
            throw new InvalidDataException("The file is too small to contain a PS1 save.");

        if (HasPsvMagic(bytes))
        {
            if (IsPsv(bytes))
                return ReadPsv(path, bytes);

            throw new InvalidDataException(
                "The PSV file is not a PlayStation 1 save.");
        }

        if (Path.GetExtension(path).Equals(
                ".gme",
                StringComparison.OrdinalIgnoreCase) &&
            LooksLikeDexDriveSingleSave(bytes))
        {
            return ReadDexDriveSingleSave(path, bytes);
        }

        if (LooksLikeMcs(bytes))
            return ReadMcs(path, bytes);

        if (LooksLikeActionReplay(bytes))
            return ReadActionReplay(bytes);

        if (LooksLikeRaw(bytes))
        {
            var fileName = InferRawFileName(path);
            return new Ps1ExternalSaveData(
                fileName,
                "Raw PS1 Save",
                bytes.ToArray(),
                ReadNativeTitle(bytes));
        }

        throw new InvalidDataException(
            "The file is not a recognized PS1 individual-save format.");
    }

    public static async Task ConvertAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var save = Read(sourcePath);
        var output = Encode(save, destinationPath);
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(destinationPath, output, cancellationToken);

        var verified = Read(destinationPath);
        if (!save.Data.AsSpan().SequenceEqual(verified.Data) ||
            !save.FileName.Equals(verified.FileName, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(destinationPath); } catch { }
            throw new InvalidDataException("PS1 individual-save conversion failed verification.");
        }
    }

    public static byte[] Encode(
        Ps1ExternalSaveData save,
        string destinationPath)
    {
        ValidateSave(save);
        var extension = Path.GetExtension(destinationPath).ToLowerInvariant();

        return extension switch
        {
            ".gme" => EncodeDexDriveSingleSave(save),
            ".mcs" or ".ps1" => EncodeMcs(save),
            ".mcb" or ".mcx" or ".pda" or ".psx" => EncodeActionReplay(save),
            ".raw" => save.Data.ToArray(),
            ".psv" => EncodePsv(save),
            _ => throw new NotSupportedException(
                "PS1 individual saves can be written as GME, MCS, PS1, MCB, MCX, PDA, PSX, RAW, or PSV.")
        };
    }

    public static byte[] CreateSingleSaveRawCard(Ps1ExternalSaveData save)
    {
        ValidateSave(save);
        if (save.BlocksUsed > DirectoryEntries)
            throw new InvalidDataException("The PS1 save is larger than a standard 15-block memory card.");

        var card = CreateBlankRawCard();
        var fileSize = save.Data.Length;

        for (var index = 0; index < save.BlocksUsed; index++)
        {
            var block = index + 1;
            var directoryOffset = DirectoryOffset + index * FrameSize;

            card[directoryOffset] = save.BlocksUsed == 1 || index == 0
                ? (byte)0x51
                : index == save.BlocksUsed - 1
                    ? (byte)0x53
                    : (byte)0x52;

            WriteInt32(card, directoryOffset + 0x04, fileSize);
            WriteUInt16(
                card,
                directoryOffset + 0x08,
                index == save.BlocksUsed - 1
                    ? (ushort)0xFFFF
                    : (ushort)(index + 1));

            if (index == 0)
                WriteAscii(card, directoryOffset + 0x0A, 20, save.FileName);

            UpdateFrameChecksum(card, directoryOffset);
            Buffer.BlockCopy(
                save.Data,
                index * Ps1MemoryCardService.BlockSize,
                card,
                block * Ps1MemoryCardService.BlockSize,
                Ps1MemoryCardService.BlockSize);
        }

        return card;
    }

    private static byte[] EncodeDexDriveSingleSave(
        Ps1ExternalSaveData save)
    {
        ValidateSave(save);

        const int dexDriveHeaderSize = 0xF40;
        var rawCard = CreateSingleSaveRawCard(save);
        var payloadLength =
            (save.BlocksUsed + 1) *
            Ps1MemoryCardService.BlockSize;

        var header = new byte[dexDriveHeaderSize];
        Encoding.ASCII.GetBytes("123-456-STD").CopyTo(header, 0);
        header[18] = 0x01;
        header[20] = 0x01;
        header[21] = 0x4D;

        for (var slot = 0; slot < DirectoryEntries; slot++)
        {
            var directoryOffset =
                DirectoryOffset +
                slot * FrameSize;

            header[22 + slot] =
                rawCard[directoryOffset];

            header[38 + slot] =
                rawCard[directoryOffset + 8];
        }

        var encoded =
            new byte[dexDriveHeaderSize + payloadLength];

        Buffer.BlockCopy(
            header,
            0,
            encoded,
            0,
            header.Length);

        Buffer.BlockCopy(
            rawCard,
            0,
            encoded,
            dexDriveHeaderSize,
            payloadLength);

        return encoded;
    }

    private static bool LooksLikeDexDriveSingleSave(byte[] bytes)
    {
        const int dexDriveHeaderSize = 0xF40;
        var payloadLength = bytes.Length - dexDriveHeaderSize;

        // Full DexDrive cards are handled by Ps1MemoryCardService. Some
        // DexDrive-era save archives use the same 0xF40 GME wrapper but store
        // only block 0 plus the blocks occupied by one save. Recognize only
        // that truncated, single-save form here so whole-card GME detection
        // remains unchanged.
        if (payloadLength < Ps1MemoryCardService.BlockSize * 2 ||
            payloadLength >= Ps1MemoryCardService.CardSize ||
            payloadLength % Ps1MemoryCardService.BlockSize != 0)
        {
            return false;
        }

        if (bytes[dexDriveHeaderSize] != 0x4D ||
            bytes[dexDriveHeaderSize + 1] != 0x43)
        {
            return false;
        }

        var activeStarts = 0;
        for (var index = 0; index < 15; index++)
        {
            var offset =
                dexDriveHeaderSize +
                128 +
                index * 128;

            if (offset >= bytes.Length)
                return false;

            if (bytes[offset] == 0x51)
                activeStarts++;
        }

        return activeStarts == 1;
    }

    private static Ps1ExternalSaveData ReadDexDriveSingleSave(
        string path,
        byte[] bytes)
    {
        const int dexDriveHeaderSize = 0xF40;
        const int directoryOffset = 128;
        const int directoryEntries = 15;

        if (!LooksLikeDexDriveSingleSave(bytes))
            throw new InvalidDataException(
                "The GME file is not a recognized DexDrive individual save.");

        var payloadLength = bytes.Length - dexDriveHeaderSize;
        var payloadBlocks = payloadLength / Ps1MemoryCardService.BlockSize;
        var card = new byte[Ps1MemoryCardService.CardSize];
        Buffer.BlockCopy(
            bytes,
            dexDriveHeaderSize,
            card,
            0,
            payloadLength);

        var startingBlock = 0;
        for (var index = 0; index < directoryEntries; index++)
        {
            var offset = directoryOffset + index * 128;
            if (card[offset] == 0x51)
            {
                startingBlock = index + 1;
                break;
            }
        }

        if (startingBlock == 0)
            throw new InvalidDataException(
                "The DexDrive GME does not contain an active PS1 save.");

        var chain = new List<int>();
        var visited = new HashSet<int>();
        var current = startingBlock;

        while (current is >= 1 and <= directoryEntries &&
               visited.Add(current))
        {
            if (current >= payloadBlocks)
                throw new InvalidDataException(
                    "The DexDrive GME save references a block that is not present in the file.");

            chain.Add(current);
            var offset = directoryOffset + (current - 1) * 128;
            var next = BitConverter.ToUInt16(card, offset + 0x08);
            if (next == 0xFFFF)
                break;

            current = next + 1;
        }

        if (chain.Count == 0)
            throw new InvalidDataException(
                "The DexDrive GME save has an invalid allocation chain.");

        var firstDirectory =
            directoryOffset +
            (startingBlock - 1) * 128;
        var fileName = ReadAscii(card, firstDirectory + 0x0A, 20);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = InferRawFileName(path);

        var data = new byte[chain.Count * Ps1MemoryCardService.BlockSize];
        for (var index = 0; index < chain.Count; index++)
        {
            Buffer.BlockCopy(
                card,
                chain[index] * Ps1MemoryCardService.BlockSize,
                data,
                index * Ps1MemoryCardService.BlockSize,
                Ps1MemoryCardService.BlockSize);
        }

        return new Ps1ExternalSaveData(
            fileName,
            "DexDrive GME Individual Save",
            data,
            ReadNativeTitle(data));
    }

    private static Ps1ExternalSaveData ReadMcs(string path, byte[] bytes)
    {
        var declaredSize = BitConverter.ToInt32(bytes, 4);
        var dataLength = bytes.Length - FrameSize;
        if (declaredSize > 0 && declaredSize <= dataLength)
            dataLength = declaredSize;

        dataLength = NormalizeDataLength(dataLength);
        var data = bytes.AsSpan(FrameSize, dataLength).ToArray();
        var fileName = ReadAscii(bytes, 0x0A, 20);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = InferRawFileName(path);

        return new Ps1ExternalSaveData(
            fileName,
            "MCS / PS1 Individual Save",
            data,
            ReadNativeTitle(data));
    }

    private static Ps1ExternalSaveData ReadActionReplay(byte[] bytes)
    {
        var dataLength = NormalizeDataLength(bytes.Length - 54);
        var fileName = ReadAscii(bytes, 0, 20);
        var description = ReadAscii(bytes, 21, 33);
        var data = bytes.AsSpan(54, dataLength).ToArray();

        return new Ps1ExternalSaveData(
            fileName,
            "Action Replay / Smart Link Individual Save",
            data,
            string.IsNullOrWhiteSpace(description)
                ? ReadNativeTitle(data)
                : description);
    }

    private static Ps1ExternalSaveData ReadPsv(string path, byte[] bytes)
    {
        var dataOffset = BitConverter.ToInt32(bytes, 0x44);
        var saveSize = BitConverter.ToInt32(bytes, 0x40);
        if (dataOffset < 0x84 || saveSize <= 0 || dataOffset + saveSize > bytes.Length)
            throw new InvalidDataException("The PS1 PSV header contains an invalid payload range.");

        var dataLength = NormalizeDataLength(saveSize);
        var fileName = ReadAscii(bytes, 0x64, 32);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = InferRawFileName(path);
        var data = bytes.AsSpan(dataOffset, dataLength).ToArray();

        return new Ps1ExternalSaveData(
            fileName,
            "PS3 PS1 Virtual Save (PSV)",
            data,
            ReadNativeTitle(data));
    }

    private static byte[] EncodeMcs(Ps1ExternalSaveData save)
    {
        var result = new byte[FrameSize + save.Data.Length];
        result[0] = 0x51;
        WriteInt32(result, 4, save.Data.Length);
        WriteUInt16(result, 8, 0xFFFF);
        WriteAscii(result, 0x0A, 20, save.FileName);
        UpdateFrameChecksum(result, 0);
        Buffer.BlockCopy(save.Data, 0, result, FrameSize, save.Data.Length);
        return result;
    }

    private static byte[] EncodeActionReplay(Ps1ExternalSaveData save)
    {
        var result = new byte[54 + save.Data.Length];
        WriteAscii(result, 0, 20, save.FileName);
        WriteAscii(result, 21, 33,
            string.IsNullOrWhiteSpace(save.Description)
                ? "PSM Export"
                : save.Description);
        Buffer.BlockCopy(save.Data, 0, result, 54, save.Data.Length);
        return result;
    }

    private static byte[] EncodePsv(Ps1ExternalSaveData save)
    {
        var result = new byte[0x84 + save.Data.Length];
        result[1] = 0x56;
        result[2] = 0x53;
        result[3] = 0x50;
        WriteInt32(result, 0x38, 0x14);
        WriteInt32(result, 0x3C, 1);
        WriteInt32(result, 0x40, save.Data.Length);
        WriteInt32(result, 0x44, 0x84);
        result[0x49] = 2;
        WriteInt32(result, 0x5C, save.Data.Length);
        result[0x60] = 3;
        result[0x61] = 0x90;
        WriteAscii(result, 0x64, 32, save.FileName);
        Buffer.BlockCopy(save.Data, 0, result, 0x84, save.Data.Length);
        Ps1FormatCrypto.SignPsv(result);
        return result;
    }

    private static bool HasPsvMagic(byte[] bytes) =>
        bytes.Length >= 4 &&
        bytes[0] == 0x00 && bytes[1] == 0x56 &&
        bytes[2] == 0x53 && bytes[3] == 0x50;

    private static bool IsPsv(byte[] bytes) =>
        bytes.Length >= 0x84 &&
        HasPsvMagic(bytes) &&
        BitConverter.ToInt32(bytes, 0x3C) == 1;

    private static bool LooksLikeMcs(byte[] bytes) =>
        bytes.Length >= FrameSize + Ps1MemoryCardService.BlockSize &&
        bytes[0] is 0x51 or 0x41 &&
        bytes[FrameSize] is 0x53 or 0x73 &&
        bytes[FrameSize + 1] is 0x43 or 0x63;

    private static bool LooksLikeActionReplay(byte[] bytes) =>
        bytes.Length >= 54 + Ps1MemoryCardService.BlockSize &&
        bytes[54] is 0x53 or 0x73 &&
        bytes[55] is 0x43 or 0x63;

    private static bool LooksLikeRaw(byte[] bytes) =>
        bytes.Length >= Ps1MemoryCardService.BlockSize &&
        bytes.Length % Ps1MemoryCardService.BlockSize == 0 &&
        bytes[0] is 0x53 or 0x73 &&
        bytes[1] is 0x43 or 0x63;

    private static void ValidateSave(Ps1ExternalSaveData save)
    {
        if (save.Data.Length == 0 ||
            save.Data.Length % Ps1MemoryCardService.BlockSize != 0)
        {
            throw new InvalidDataException(
                "PS1 individual-save data must contain complete 8 KB blocks.");
        }

        if (save.BlocksUsed > DirectoryEntries)
            throw new InvalidDataException("The PS1 save uses more than 15 blocks.");

        if (save.Data[0] is not 0x53 and not 0x73 ||
            save.Data[1] is not 0x43 and not 0x63)
        {
            throw new InvalidDataException("The PS1 save data has no SC header.");
        }
    }

    private static int NormalizeDataLength(int length)
    {
        if (length <= 0 || length % Ps1MemoryCardService.BlockSize != 0)
            throw new InvalidDataException(
                "The PS1 individual-save payload is not a whole number of 8 KB blocks.");
        return length;
    }

    private static byte[] CreateBlankRawCard()
    {
        var card = new byte[Ps1MemoryCardService.CardSize];
        card[0] = 0x4D;
        card[1] = 0x43;
        UpdateFrameChecksum(card, 0);

        for (var block = 1; block <= DirectoryEntries; block++)
        {
            var offset = DirectoryOffset + (block - 1) * FrameSize;
            card[offset] = 0xA0;
            card[offset + 8] = 0xFF;
            card[offset + 9] = 0xFF;
            UpdateFrameChecksum(card, offset);
        }

        for (var frame = 16; frame < 64; frame++)
            Array.Fill(card, (byte)0xFF, frame * FrameSize, FrameSize);

        return card;
    }

    private static string InferRawFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
            name = Path.GetFileName(path);
        if (name.EndsWith("raw", StringComparison.OrdinalIgnoreCase) && name.Length > 3)
            name = name[..^3];
        if (name.Length > 20)
            name = name[..20];
        return string.IsNullOrWhiteSpace(name) ? "PSM-RAW-SAVE" : name;
    }

    private static string ReadNativeTitle(byte[] data)
    {
        if (data.Length < 0x44 ||
            data[0] is not 0x53 and not 0x73 ||
            data[1] is not 0x43 and not 0x63)
        {
            return string.Empty;
        }

        var titleBytes = data
            .AsSpan(0x04, Math.Min(64, data.Length - 0x04))
            .ToArray();

        var terminator =
            Array.IndexOf(titleBytes, (byte)0);

        if (terminator >= 0)
            titleBytes = titleBytes[..terminator];

        if (titleBytes.Length == 0)
            return string.Empty;

        try
        {
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            return Encoding
                .GetEncoding(
                    932,
                    EncoderFallback.ReplacementFallback,
                    DecoderFallback.ReplacementFallback)
                .GetString(titleBytes)
                .Replace('\u3000', ' ')
                .Replace('\uFFFD', ' ')
                .Trim();
        }
        catch
        {
            return Encoding.ASCII
                .GetString(titleBytes)
                .Trim();
        }
    }

    private static string ReadAscii(byte[] bytes, int offset, int count)
    {
        if (offset < 0 || offset >= bytes.Length)
            return string.Empty;
        count = Math.Min(count, bytes.Length - offset);
        return Encoding.ASCII.GetString(bytes, offset, count).TrimEnd('\0', ' ', '\u001A');
    }

    private static void WriteAscii(byte[] bytes, int offset, int count, string value)
    {
        Array.Clear(bytes, offset, Math.Min(count, bytes.Length - offset));
        var encoded = Encoding.ASCII.GetBytes(value ?? string.Empty);
        Buffer.BlockCopy(encoded, 0, bytes, offset, Math.Min(count, encoded.Length));
    }

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 2);

    private static void UpdateFrameChecksum(byte[] bytes, int offset)
    {
        byte checksum = 0;
        for (var index = 0; index < 127; index++)
            checksum ^= bytes[offset + index];
        bytes[offset + 127] = checksum;
    }
}
