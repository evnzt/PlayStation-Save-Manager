using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlayStationSaveManager.Models;

namespace PlayStationSaveManager.Services;

public sealed class Ps1MemoryCardService
{
    public const int CardSize = 128 * 1024;
    public const int BlockSize = 8 * 1024;
    private const int FrameSize = 128;
    private const int DirectoryOffset = FrameSize;
    private const int DirectoryEntries = 15;

    private static readonly HashSet<string> RawExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mcr", ".srm", ".bin", ".mcd", ".mc"
        };

    public static string FileDialogFilter =>
        "PS1 Memory Cards|*.mcr;*.srm;*.bin;*.mcd;*.mc|" +
        "ePSXe / PSEmu Pro (*.mcr)|*.mcr|" +
        "RetroArch / Libretro (*.srm)|*.srm|" +
        "pSX / AdriPSX (*.bin)|*.bin|" +
        "Bleem! (*.mcd)|*.mcd|" +
        "PSXGame Edit (*.mc)|*.mc|" +
        "All files|*.*";

    public async Task CreateEmptyCardAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(destinationPath);
        if (!RawExtensions.Contains(extension))
            throw new NotSupportedException(
                "New PS1 cards can be created as MCR, SRM, BIN, MCD, or MC.");

        if (File.Exists(destinationPath))
            throw new IOException(
                "A file already exists at the selected location.");

        var card = new byte[CardSize];

        card[0] = 0x4D;
        card[1] = 0x43;
        UpdateFrameChecksum(card, 0);

        for (var block = 1; block <= DirectoryEntries; block++)
        {
            var offset =
                DirectoryOffset +
                (block - 1) * FrameSize;

            card[offset] = 0xA0;
            card[offset + 0x08] = 0xFF;
            card[offset + 0x09] = 0xFF;
            UpdateFrameChecksum(card, offset);
        }

        for (var frame = 16; frame < 64; frame++)
        {
            var offset = frame * FrameSize;
            Array.Fill(
                card,
                (byte)0xFF,
                offset,
                FrameSize);
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllBytesAsync(
                destinationPath,
                card,
                cancellationToken);

            var verification =
                await File.ReadAllBytesAsync(
                    destinationPath,
                    cancellationToken);

            ValidateRawCard(
                verification,
                destinationPath);

            var parsed =
                ParseSaves(
                    verification,
                    destinationPath);

            if (parsed.Any(save => !save.IsDeleted))
                throw new InvalidDataException(
                    "The newly created PS1 card was not empty.");
        }
        catch
        {
            try
            {
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
            }
            catch { }

            throw;
        }
    }

    public async Task<Ps1CardReadResult> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "The PS1 memory card was not found.",
                path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        ValidateRawCard(bytes, path);

        var saves = ParseSaves(bytes, path);
        var usedBlocks = saves
            .Where(save => !save.IsDeleted)
            .Sum(save => save.BlocksUsed);

        return new Ps1CardReadResult(
            path,
            saves,
            usedBlocks,
            DirectoryEntries - usedBlocks,
            true,
            FormatName(Path.GetExtension(path)));
    }

    public async Task ExportSavePackageAsync(
        string cardPath,
        Ps1SaveEntry save,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var card = await File.ReadAllBytesAsync(
            cardPath,
            cancellationToken);

        ValidateRawCard(card, cardPath);

        if (save.BlockChain.Count == 0)
            throw new InvalidDataException(
                "The selected PS1 save has no allocation chain.");

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        var temporary = destinationPath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                useAsync: true))
            {
                using var archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Create,
                    leaveOpen: true);

                var manifest = new Ps1SavePackageManifest
                {
                    Title = save.Title,
                    SaveTitle = save.SaveTitle,
                    ProductCode = save.ProductCode,
                    Region = save.Region,
                    Status = save.Status,
                    OriginalFileName = save.FileName,
                    StartingBlock = save.StartingBlock,
                    BlocksUsed = save.BlocksUsed,
                    FileSize = save.FileSize,
                    BlockChain = save.BlockChain.ToList(),
                    CreatedUtc = DateTime.UtcNow
                };

                var manifestEntry = archive.CreateEntry(
                    "manifest.json",
                    CompressionLevel.Optimal);

                await using (var manifestStream =
                    manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(
                        manifestStream,
                        manifest,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        },
                        cancellationToken);
                }

                var directoryEntry = archive.CreateEntry(
                    "directory-frames.bin",
                    CompressionLevel.Optimal);

                await using (var directoryStream =
                    directoryEntry.Open())
                {
                    foreach (var block in save.BlockChain)
                    {
                        var offset =
                            DirectoryOffset +
                            (block - 1) * FrameSize;

                        await directoryStream.WriteAsync(
                            card.AsMemory(offset, FrameSize),
                            cancellationToken);
                    }
                }

                var blocksEntry = archive.CreateEntry(
                    "save-blocks.bin",
                    CompressionLevel.Optimal);

                await using (var blocksStream =
                    blocksEntry.Open())
                {
                    foreach (var block in save.BlockChain)
                    {
                        await blocksStream.WriteAsync(
                            card.AsMemory(
                                block * BlockSize,
                                BlockSize),
                            cancellationToken);
                    }
                }

                if (save.IconImage is not null)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(
                        BitmapFrame.Create(save.IconImage));

                    using var pngBuffer = new MemoryStream();
                    encoder.Save(pngBuffer);
                    pngBuffer.Position = 0;

                    var iconEntry = archive.CreateEntry(
                        "icon.png",
                        CompressionLevel.Optimal);

                    await using var iconStream = iconEntry.Open();
                    await pngBuffer.CopyToAsync(
                        iconStream,
                        cancellationToken);
                }
            }

            var inspected = await InspectSavePackageAsync(
                temporary,
                cancellationToken);

            if (inspected.BlocksUsed != save.BlocksUsed ||
                !inspected.OriginalFileName.Equals(
                    save.FileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The exported PS1 save package failed verification.");
            }

            File.Move(temporary, destinationPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch { }
        }
    }

    public static async Task<Ps1SavePackageManifest>
        InspectSavePackageAsync(
            string packagePath,
            CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException(
                "The PS1 save package was not found.",
                packagePath);

        await using var stream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            leaveOpen: false);

        var manifestEntry =
            archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException(
                "The PS1 save package has no manifest.");

        await using var manifestStream = manifestEntry.Open();
        var manifest =
            await JsonSerializer.DeserializeAsync<
                Ps1SavePackageManifest>(
                manifestStream,
                cancellationToken: cancellationToken)
            ?? throw new InvalidDataException(
                "The PS1 save package manifest is invalid.");

        var directoryEntry =
            archive.GetEntry("directory-frames.bin")
            ?? throw new InvalidDataException(
                "The PS1 save package has no directory frames.");

        var blocksEntry =
            archive.GetEntry("save-blocks.bin")
            ?? throw new InvalidDataException(
                "The PS1 save package has no save blocks.");

        if (manifest.BlocksUsed <= 0 ||
            manifest.BlockChain.Count != manifest.BlocksUsed)
        {
            throw new InvalidDataException(
                "The PS1 save package has an invalid block chain.");
        }

        if (directoryEntry.Length !=
            manifest.BlocksUsed * FrameSize)
        {
            throw new InvalidDataException(
                "The PS1 package directory-frame length is invalid.");
        }

        if (blocksEntry.Length !=
            manifest.BlocksUsed * BlockSize)
        {
            throw new InvalidDataException(
                "The PS1 package save-block length is invalid.");
        }

        return manifest;
    }

    public static BitmapSource? LoadPackageIcon(
        string packagePath)
    {
        try
        {
            using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(
                stream,
                ZipArchiveMode.Read);

            var iconEntry = archive.GetEntry("icon.png");
            if (iconEntry is null)
                return null;

            using var iconStream = iconEntry.Open();
            using var memory = new MemoryStream();
            iconStream.CopyTo(memory);
            memory.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public async Task CopySaveAsync(
        string sourcePath,
        Ps1SaveEntry sourceSave,
        string destinationPath,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        var source = await File.ReadAllBytesAsync(
            sourcePath,
            cancellationToken);
        var destination = await File.ReadAllBytesAsync(
            destinationPath,
            cancellationToken);

        ValidateRawCard(source, sourcePath);
        ValidateRawCard(destination, destinationPath);

        var existingSave = ParseSaves(
                destination,
                destinationPath)
            .FirstOrDefault(save =>
                !save.IsDeleted &&
                save.FileName.Equals(
                    sourceSave.FileName,
                    StringComparison.OrdinalIgnoreCase));

        if (existingSave is not null)
        {
            if (!replaceExisting)
            {
                throw new InvalidOperationException(
                    "The destination card already contains this save.");
            }

            if (existingSave.BlockChain.Count == 0)
            {
                throw new InvalidDataException(
                    "The existing destination save has an invalid allocation chain.");
            }

            foreach (var block in existingSave.BlockChain)
            {
                if (block < 1 || block > DirectoryEntries)
                {
                    throw new InvalidDataException(
                        "The existing destination save contains an invalid block number.");
                }

                var directoryOffset =
                    DirectoryOffset + (block - 1) * FrameSize;

                destination[directoryOffset] = 0xA0;

                WriteUInt16(
                    destination,
                    directoryOffset + 0x08,
                    0xFFFF);

                UpdateFrameChecksum(
                    destination,
                    directoryOffset);
            }

            var remainingDuplicate = ParseSaves(
                    destination,
                    destinationPath)
                .Any(save =>
                    !save.IsDeleted &&
                    save.FileName.Equals(
                        sourceSave.FileName,
                        StringComparison.OrdinalIgnoreCase));

            if (remainingDuplicate)
            {
                throw new InvalidDataException(
                    "The existing destination save could not be removed safely.");
            }
        }

        var freeBlocks = FindFreeBlocks(destination)
            .Take(sourceSave.BlocksUsed)
            .ToArray();

        if (freeBlocks.Length != sourceSave.BlocksUsed)
        {
            throw new InvalidOperationException(
                $"The destination needs {sourceSave.BlocksUsed} free blocks, " +
                $"but only {freeBlocks.Length} are available.");
        }

        var sourceChain = sourceSave.BlockChain.ToArray();
        if (sourceChain.Length != sourceSave.BlocksUsed)
            throw new InvalidDataException(
                "The source save has an invalid allocation chain.");

        for (var index = 0; index < sourceChain.Length; index++)
        {
            var sourceBlock = sourceChain[index];
            var destinationBlock = freeBlocks[index];

            Buffer.BlockCopy(
                source,
                sourceBlock * BlockSize,
                destination,
                destinationBlock * BlockSize,
                BlockSize);

            var sourceDirectoryOffset =
                DirectoryOffset + (sourceBlock - 1) * FrameSize;
            var destinationDirectoryOffset =
                DirectoryOffset + (destinationBlock - 1) * FrameSize;

            Buffer.BlockCopy(
                source,
                sourceDirectoryOffset,
                destination,
                destinationDirectoryOffset,
                FrameSize);

            destination[destinationDirectoryOffset] =
                sourceChain.Length == 1
                    ? (byte)0x51
                    : index == 0
                        ? (byte)0x51
                        : index == sourceChain.Length - 1
                            ? (byte)0x53
                            : (byte)0x52;

            var next = index == sourceChain.Length - 1
                ? 0xFFFF
                : freeBlocks[index + 1] - 1;

            WriteUInt16(
                destination,
                destinationDirectoryOffset + 0x08,
                (ushort)next);

            UpdateFrameChecksum(
                destination,
                destinationDirectoryOffset);
        }

        var verified = ParseSaves(destination, destinationPath);
        var matchingSaves = verified
            .Where(save =>
                !save.IsDeleted &&
                save.FileName.Equals(
                    sourceSave.FileName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var copied = matchingSaves.SingleOrDefault();

        if (copied is null ||
            copied.BlocksUsed != sourceSave.BlocksUsed)
        {
            throw new InvalidDataException(
                "The copied PS1 save failed allocation-chain verification.");
        }

        await CommitWithBackupAsync(
            destinationPath,
            destination,
            cancellationToken);
    }

    public async Task DeleteSaveAsync(
        string cardPath,
        Ps1SaveEntry save,
        CancellationToken cancellationToken = default)
    {
        var card =
            await File.ReadAllBytesAsync(
                cardPath,
                cancellationToken);

        ValidateRawCard(card, cardPath);

        if (save.BlockChain.Count == 0)
            throw new InvalidDataException(
                "The selected PS1 save has no allocation chain.");

        foreach (var block in save.BlockChain)
        {
            var offset =
                DirectoryOffset +
                (block - 1) * FrameSize;

            card[offset] = 0xA1;
            UpdateFrameChecksum(card, offset);
        }

        var verified =
            ParseSaves(card, cardPath);

        if (verified.Any(candidate =>
            !candidate.IsDeleted &&
            candidate.FileName.Equals(
                save.FileName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The PS1 save deletion failed verification.");
        }

        await CommitWithBackupAsync(
            cardPath,
            card,
            cancellationToken);
    }

    public async Task SaveCardAsAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var source = await File.ReadAllBytesAsync(
            sourcePath,
            cancellationToken);

        ValidateRawCard(source, sourcePath);

        var extension = Path.GetExtension(destinationPath);
        if (!RawExtensions.Contains(extension))
            throw new NotSupportedException(
                "The initial PS1 engine writes MCR, SRM, BIN, MCD, and MC cards.");

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        await File.WriteAllBytesAsync(
            destinationPath,
            source,
            cancellationToken);

        var verification = await File.ReadAllBytesAsync(
            destinationPath,
            cancellationToken);
        ValidateRawCard(verification, destinationPath);

        if (!source.AsSpan().SequenceEqual(verification))
            throw new InvalidDataException(
                "The exported PS1 card did not match the source.");
    }

    public async Task BackupAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(
            sourcePath,
            cancellationToken);
        ValidateRawCard(bytes, sourcePath);

        var backup = CreateBackupPath(sourcePath);
        await File.WriteAllBytesAsync(
            backup,
            bytes,
            cancellationToken);
    }

    public static bool LooksLikeSupportedCard(string path)
    {
        if (!RawExtensions.Contains(Path.GetExtension(path)))
            return false;

        try
        {
            var info = new FileInfo(path);
            if (info.Length != CardSize)
                return false;

            using var stream = File.OpenRead(path);
            return stream.ReadByte() == 0x4D &&
                   stream.ReadByte() == 0x43;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<Ps1SaveEntry> ParseSaves(
        byte[] card,
        string sourcePath)
    {
        var entries = new List<Ps1SaveEntry>();
        var visited = new HashSet<int>();

        for (var index = 0; index < DirectoryEntries; index++)
        {
            var block = index + 1;
            if (visited.Contains(block))
                continue;

            var offset = DirectoryOffset + index * FrameSize;
            var status = card[offset];

            if (status is not 0x51 and not 0xA1)
                continue;

            var chain = FollowChain(card, block);
            foreach (var chainedBlock in chain)
                visited.Add(chainedBlock);

            var fileName = ReadAscii(card, offset + 0x0A, 20);
            var size = ReadInt32(card, offset + 0x04);
            var deleted = status == 0xA1;
            var dataOffset = block * BlockSize;

            var nativeSaveTitle =
                ReadNativeSaveTitle(
                    card,
                    dataOffset + 0x04,
                    64);

            var gameTitle =
                BuildFallbackGameTitle(
                    sourcePath,
                    fileName);

            if (string.IsNullOrWhiteSpace(gameTitle))
                gameTitle = fileName;

            entries.Add(new Ps1SaveEntry
            {
                Title = CleanGameTitle(gameTitle),
                SaveTitle = nativeSaveTitle,
                ProductCode = ExtractProductCode(fileName),
                Region = InferRegion(fileName),
                Status = deleted ? "Deleted / recoverable" : "Active",
                StartingBlock = block,
                BlocksUsed = chain.Count,
                FileSize = size,
                FileName = fileName,
                IsDeleted = deleted,
                BlockChain = chain,
                IconImage = RenderIcon(card, dataOffset)
            });
        }

        return entries;
    }

    private static IReadOnlyList<int> FollowChain(
        byte[] card,
        int startingBlock)
    {
        var result = new List<int>();
        var visited = new HashSet<int>();
        var current = startingBlock;

        while (current is >= 1 and <= DirectoryEntries &&
               visited.Add(current))
        {
            result.Add(current);

            var offset =
                DirectoryOffset + (current - 1) * FrameSize;
            var next = ReadUInt16(card, offset + 0x08);

            if (next == 0xFFFF)
                break;

            current = next + 1;
        }

        return result;
    }

    private static BitmapSource? RenderIcon(
        byte[] card,
        int dataOffset)
    {
        if (dataOffset < 0 ||
            dataOffset + 0x100 > card.Length ||
            card[dataOffset] != 0x53 ||
            card[dataOffset + 1] != 0x43)
        {
            return null;
        }

        var frameCount = card[dataOffset + 2] & 0x03;
        if (frameCount == 0)
            frameCount = 1;

        var palette = new uint[16];
        for (var index = 0; index < 16; index++)
        {
            var color = ReadUInt16(
                card,
                dataOffset + 0x60 + index * 2);

            var red = (byte)((color & 0x1F) * 255 / 31);
            var green = (byte)(((color >> 5) & 0x1F) * 255 / 31);
            var blue = (byte)(((color >> 10) & 0x1F) * 255 / 31);
            var alpha = color == 0 ? (byte)0 : (byte)255;

            palette[index] =
                (uint)(alpha << 24 | red << 16 | green << 8 | blue);
        }

        var pixels = new byte[16 * 16 * 4];
        var iconOffset = dataOffset + 0x80;

        if (iconOffset + 128 > card.Length)
            return null;

        for (var pixel = 0; pixel < 256; pixel++)
        {
            var packed = card[iconOffset + pixel / 2];
            var paletteIndex = pixel % 2 == 0
                ? packed & 0x0F
                : packed >> 4;

            var color = palette[paletteIndex];
            var output = pixel * 4;

            pixels[output] = (byte)(color & 0xFF);
            pixels[output + 1] = (byte)((color >> 8) & 0xFF);
            pixels[output + 2] = (byte)((color >> 16) & 0xFF);
            pixels[output + 3] = (byte)((color >> 24) & 0xFF);
        }

        var bitmap = BitmapSource.Create(
            16,
            16,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            16 * 4);

        bitmap.Freeze();
        return bitmap;
    }

    private static IEnumerable<(int Block, byte[] Frame)>
        FindActiveFirstEntries(byte[] card)
    {
        for (var index = 0; index < DirectoryEntries; index++)
        {
            var offset = DirectoryOffset + index * FrameSize;
            if (card[offset] != 0x51)
                continue;

            var frame = new byte[FrameSize];
            Buffer.BlockCopy(card, offset, frame, 0, FrameSize);
            yield return (index + 1, frame);
        }
    }

    private static IEnumerable<int> FindFreeBlocks(byte[] card)
    {
        for (var index = 0; index < DirectoryEntries; index++)
        {
            var status = card[DirectoryOffset + index * FrameSize];
            if (status is 0xA0 or 0x00)
                yield return index + 1;
        }
    }

    private static void ValidateRawCard(
        byte[] bytes,
        string path)
    {
        if (!RawExtensions.Contains(Path.GetExtension(path)))
            throw new NotSupportedException(
                "This PS1 foundation currently supports MCR, SRM, BIN, MCD, and MC.");

        if (bytes.Length != CardSize)
            throw new InvalidDataException(
                $"Expected a 128 KB PS1 memory card, but found {bytes.Length:N0} bytes.");

        if (bytes[0] != 0x4D || bytes[1] != 0x43)
            throw new InvalidDataException(
                "The file does not contain a valid raw PlayStation memory-card header.");
    }

    private static async Task CommitWithBackupAsync(
        string destinationPath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var backup = CreateBackupPath(destinationPath);
        File.Copy(destinationPath, backup, true);

        var temporary = destinationPath + ".psm-temporary";

        try
        {
            await File.WriteAllBytesAsync(
                temporary,
                bytes,
                cancellationToken);

            var verification = await File.ReadAllBytesAsync(
                temporary,
                cancellationToken);

            ValidateRawCard(verification, destinationPath);

            if (!bytes.AsSpan().SequenceEqual(verification))
                throw new InvalidDataException(
                    "Temporary PS1 card verification failed.");

            File.Move(temporary, destinationPath, true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch { }

            throw;
        }
    }

    private static string CreateBackupPath(string sourcePath) =>
        Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}" +
            $".backup-{DateTime.Now:yyyyMMdd-HHmmss}" +
            Path.GetExtension(sourcePath));

    private static void UpdateFrameChecksum(
        byte[] card,
        int frameOffset)
    {
        byte checksum = 0;
        for (var index = 0; index < 127; index++)
            checksum ^= card[frameOffset + index];

        card[frameOffset + 127] = checksum;
    }

    private static bool LooksLikeReadableTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var lettersOrDigits = value.Count(char.IsLetterOrDigit);
        var suspicious = value.Count(character =>
            character is '`' or '@' or '\\' or '^' or '~' or '{' or '}');

        return lettersOrDigits >= 3 &&
               suspicious <= Math.Max(1, value.Length / 8);
    }

    private static string BuildFallbackGameTitle(
        string sourcePath,
        string internalFileName)
    {
        var cardName = Path.GetFileNameWithoutExtension(sourcePath);

        if (!string.IsNullOrWhiteSpace(cardName))
        {
            var cleaned = cardName;

            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s*\([^)]*\)\s*",
                " ");

            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"[_-]\d+$",
                string.Empty);

            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s+",
                " ").Trim();

            if (!string.IsNullOrWhiteSpace(cleaned) &&
                !cleaned.Equals(
                    "card",
                    StringComparison.OrdinalIgnoreCase) &&
                !cleaned.Equals(
                    "memorycard",
                    StringComparison.OrdinalIgnoreCase))
            {
                return cleaned;
            }
        }

        var upper = internalFileName.ToUpperInvariant();
        var serialDigits =
            new string(upper.Where(char.IsDigit).Take(5).ToArray());

        if (serialDigits.Length == 5)
        {
            var suffixIndex =
                upper.IndexOf(serialDigits, StringComparison.Ordinal) + 5;

            if (suffixIndex > 4 &&
                suffixIndex < internalFileName.Length)
            {
                var suffix = internalFileName[suffixIndex..]
                    .Trim('-', '_', ' ', '\0');

                if (!string.IsNullOrWhiteSpace(suffix))
                {
                    return System.Globalization.CultureInfo
                        .InvariantCulture
                        .TextInfo
                        .ToTitleCase(
                            suffix.ToLowerInvariant());
                }
            }
        }

        return string.Empty;
    }

    private static string ReadNativeSaveTitle(
        byte[] bytes,
        int offset,
        int length)
    {
        var available = Math.Min(
            length,
            bytes.Length - offset);

        if (available <= 0)
            return string.Empty;

        var titleBytes = bytes
            .AsSpan(offset, available)
            .ToArray();

        var terminator =
            Array.IndexOf(titleBytes, (byte)0);

        if (terminator >= 0)
            titleBytes = titleBytes[..terminator];

        if (titleBytes.Length == 0)
            return string.Empty;

        string decoded;

        try
        {
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            decoded = Encoding
                .GetEncoding(
                    932,
                    EncoderFallback.ReplacementFallback,
                    DecoderFallback.ReplacementFallback)
                .GetString(titleBytes);
        }
        catch
        {
            decoded = Encoding.ASCII.GetString(titleBytes);
        }

        decoded = decoded
            .Replace('\u3000', ' ')
            .Replace('\uFFFD', ' ');

        decoded = System.Text.RegularExpressions.Regex.Replace(
            decoded,
            @"[\x00-\x08\x0B\x0C\x0E-\x1F]",
            string.Empty);

        decoded = System.Text.RegularExpressions.Regex.Replace(
            decoded,
            @"\s+",
            " ").Trim();

        return LooksLikeReadableTitle(decoded)
            ? decoded
            : string.Empty;
    }

    private static string CleanGameTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                value,
                @"\s*\([^)]*\)",
                string.Empty);

        cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\s+",
                " ").Trim();

        return cleaned;
    }

    private static string ReadAscii(
        byte[] bytes,
        int offset,
        int length)
    {
        var available = Math.Min(length, bytes.Length - offset);
        if (available <= 0)
            return string.Empty;

        return Encoding.ASCII
            .GetString(bytes, offset, available)
            .TrimEnd('\0', ' ', '\xFF');
    }

    private static string ExtractProductCode(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Unknown";

        var value = fileName.ToUpperInvariant();
        var prefixes = new[]
        {
            "BASLUS", "BASCUS", "BESLES", "BESCES",
            "BISLPS", "BISCPS", "BISLPM", "BASLPS"
        };

        var prefix = prefixes.FirstOrDefault(value.StartsWith);
        if (prefix is null)
            return fileName.Length > 12
                ? fileName[..12]
                : fileName;

        var digits = new string(
            value.Skip(prefix.Length)
                .Where(char.IsDigit)
                .Take(5)
                .ToArray());

        var serialPrefix = prefix[2..];
        return digits.Length == 5
            ? $"{serialPrefix}-{digits}"
            : prefix;
    }

    private static string InferRegion(string fileName)
    {
        var value = fileName.ToUpperInvariant();

        if (value.StartsWith("BASLUS") ||
            value.StartsWith("BASCUS"))
            return "North America";

        if (value.StartsWith("BESLES") ||
            value.StartsWith("BESCES"))
            return "Europe / PAL";

        if (value.StartsWith("BISLPS") ||
            value.StartsWith("BISCPS") ||
            value.StartsWith("BISLPM") ||
            value.StartsWith("BASLPS"))
            return "Japan";

        return "Unknown";
    }

    private static string FormatName(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".mcr" => "ePSXe / PSEmu Pro",
            ".srm" => "RetroArch / Libretro",
            ".bin" => "pSX / AdriPSX",
            ".mcd" => "Bleem!",
            ".mc" => "PSXGame Edit",
            _ => "Raw PS1 memory card"
        };

    private static int ReadInt32(byte[] bytes, int offset) =>
        bytes[offset] |
        bytes[offset + 1] << 8 |
        bytes[offset + 2] << 16 |
        bytes[offset + 3] << 24;

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        (ushort)(bytes[offset] | bytes[offset + 1] << 8);

    private static void WriteUInt16(
        byte[] bytes,
        int offset,
        ushort value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)(value >> 8);
    }
}
