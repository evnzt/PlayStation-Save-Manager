using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlayStationSaveManager.Models;

namespace PlayStationSaveManager.Services;

public sealed class MyMcEngine
{
    private readonly string _pythonPath;
    private readonly string _runnerPath;

    public MyMcEngine(string applicationDirectory)
    {
        _pythonPath = Path.Combine(applicationDirectory, "Tools", "python", "python.exe");
        _runnerPath = Path.Combine(applicationDirectory, "Tools", "mymcplusplus_runner.py");
    }

    public bool IsInstalled => File.Exists(_pythonPath) && File.Exists(_runnerPath);

    public async Task<EngineResult> RunAsync(
        string cardPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            throw new FileNotFoundException("The private myMC++ engine is not installed.");

        var info = new ProcessStartInfo
        {
            FileName = _pythonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_runnerPath) ?? AppContext.BaseDirectory
        };

        info.ArgumentList.Add(_runnerPath);
        info.ArgumentList.Add(cardPath);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        if (!process.Start())
            throw new InvalidOperationException("Could not start the myMC++ engine.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"The memory-card engine timed out after {timeout.TotalSeconds:N0} seconds.");
        }

        return new EngineResult(process.ExitCode, await outputTask, await errorTask);
    }

    public async Task<CardReadResult> ReadCardAsync(
        string cardPath,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(cardPath))
            return await ReadFolderCardAsync(
                cardPath,
                cancellationToken);

        var result = await RunAsync(cardPath, ["dir"], TimeSpan.FromSeconds(30), cancellationToken);
        EnsureSuccess(result, "Could not read the memory card");

        var saves = ParseDirectory(result.Output);
        var freeBytes = ParseFreeBytes(result.Output);
        var totalBytes = GetLogicalCardSize(cardPath);

        if (freeBytes.HasValue &&
            totalBytes.HasValue &&
            freeBytes.Value > totalBytes.Value)
        {
            freeBytes = null;
        }

        var containerInfo =
            GetBankedVm2ContainerInfo(
                cardPath,
                totalBytes);

        return new CardReadResult(
            saves,
            totalBytes,
            freeBytes,
            containerInfo.ContainerTotalBytes,
            containerInfo.BankCount);
    }

    public async Task<IReadOnlyList<SaveEntry>> ReadDirectoryAsync(
        string cardPath,
        CancellationToken cancellationToken = default) =>
        (await ReadCardAsync(cardPath, cancellationToken)).Saves;

    public async Task ExportPsuAsync(
        string cardPath,
        string saveId,
        string destination,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(cardPath))
        {
            await BuildPsuFromFolderSaveAsync(
                cardPath,
                saveId,
                destination,
                cancellationToken);
            return;
        }

        var result = await RunAsync(
            cardPath,
            ["export", "-f", "-o", destination, saveId],
            TimeSpan.FromSeconds(60), cancellationToken);
        EnsureSuccess(result, "Could not export the save");
    }

    public async Task ExportPackageAsync(
        string cardPath,
        string saveId,
        string destination,
        CancellationToken cancellationToken = default)
    {
        var extension =
            Path.GetExtension(destination)
                .ToLowerInvariant();

        if (extension is ".psu" or ".max")
        {
            var result = await RunAsync(
                cardPath,
                ["export", "-f", "-o", destination, saveId],
                TimeSpan.FromSeconds(60),
                cancellationToken);

            EnsureSuccess(
                result,
                $"Could not export the {extension.ToUpperInvariant()} package");

            return;
        }

        if (extension is not ".cbs" and
            not ".sps" and
            not ".xps" and
            not ".psv")
        {
            throw new NotSupportedException(
                $"PSM cannot write {extension.ToUpperInvariant()} PS2 save packages.");
        }

        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-NATIVE-PS2-PACKAGE-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            temporaryRoot);

        var temporaryPsu =
            Path.Combine(
                temporaryRoot,
                saveId + ".psu");

        try
        {
            await ExportPsuAsync(
                cardPath,
                saveId,
                temporaryPsu,
                cancellationToken);

            await Ps2PackageWriterService.WriteFromPsuAsync(
                temporaryPsu,
                destination,
                cancellationToken);

            await VerifyNativePackageRoundTripAsync(
                destination,
                saveId,
                cancellationToken);
        }
        catch
        {
            try
            {
                if (File.Exists(destination))
                    File.Delete(destination);
            }
            catch
            {
                // Do not hide the actual export/verification failure.
            }

            throw;
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryRoot,
                    recursive: true);
            }
            catch
            {
                // Temporary cleanup must not mask a successful export.
            }
        }
    }

    private async Task VerifyNativePackageRoundTripAsync(
        string packagePath,
        string expectedSaveId,
        CancellationToken cancellationToken)
    {
        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-NATIVE-PS2-VERIFY-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            temporaryRoot);

        var verificationCard =
            Path.Combine(
                temporaryRoot,
                "verify.ps2");

        try
        {
            await CreateCardAsync(
                verificationCard,
                noEcc: false,
                cancellationToken: cancellationToken);

            await ImportAsync(
                verificationCard,
                packagePath,
                cancellationToken);

            await CheckAsync(
                verificationCard,
                cancellationToken);

            var saves =
                await ReadDirectoryAsync(
                    verificationCard,
                    cancellationToken);

            if (!saves.Any(
                    save =>
                        save.DirectoryId.Equals(
                            expectedSaveId,
                            StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"The generated {Path.GetExtension(packagePath).ToUpperInvariant()} package could not be verified after round-trip import.");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryRoot,
                    recursive: true);
            }
            catch
            {
                // Verification cleanup is best-effort.
            }
        }
    }


    public async Task ExtractFileAsync(
        string cardPath,
        string saveId,
        string fileName,
        string destination,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (Directory.Exists(cardPath))
        {
            var source =
                Path.Combine(
                    cardPath,
                    SanitizeHostName(saveId),
                    SanitizeHostName(fileName));

            if (!File.Exists(source))
                throw new FileNotFoundException(
                    $"The folder card does not contain {fileName}.",
                    source);

            await using var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                useAsync: true);

            await using var output = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                8192,
                useAsync: true);

            await input.CopyToAsync(
                output,
                cancellationToken);
            return;
        }

        var cardDirectory = "/" + saveId.TrimStart('/');
        var result = await RunAsync(
            cardPath,
            ["extract", "-d", cardDirectory, "-o", destination, fileName],
            TimeSpan.FromSeconds(45), cancellationToken);
        EnsureSuccess(result, $"Could not extract {fileName}");
    }

    public async Task ConvertToPcsx2FolderCardAsync(
        string sourceCardPath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceCardPath))
            throw new FileNotFoundException(
                "The source PS2 memory card was not found.",
                sourceCardPath);

        var destination =
            Path.GetFullPath(destinationDirectory);

        if (Directory.Exists(destination) &&
            Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new IOException(
                "The destination folder already exists and is not empty.");
        }

        var parent =
            Path.GetDirectoryName(destination);

        if (string.IsNullOrWhiteSpace(parent))
            throw new InvalidOperationException(
                "The destination must have a parent folder.");

        Directory.CreateDirectory(parent);

        var staging =
            destination +
            ".psm-converting-" +
            Guid.NewGuid().ToString("N");

        var packages =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-PSU-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(packages);

        try
        {
            await WriteFolderCardSuperblockAsync(
                sourceCardPath,
                Path.Combine(staging, "_pcsx2_superblock"),
                cancellationToken);

            var saves =
                await ReadDirectoryAsync(
                    sourceCardPath,
                    cancellationToken);

            foreach (var save in saves)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var packagePath =
                    Path.Combine(
                        packages,
                        SanitizeHostName(save.DirectoryId) + ".psu");

                await ExportPsuAsync(
                    sourceCardPath,
                    save.DirectoryId,
                    packagePath,
                    cancellationToken);

                await ExtractPsuToPcsx2FolderAsync(
                    packagePath,
                    staging,
                    cancellationToken);
            }

            VerifyPcsx2FolderCard(
                staging,
                saves);

            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);

            Directory.Move(staging, destination);
        }
        catch
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch { }

            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(packages))
                    Directory.Delete(packages, recursive: true);
            }
            catch { }
        }
    }

    private static async Task ExtractPsuToPcsx2FolderAsync(
        string psuPath,
        string cardFolder,
        CancellationToken cancellationToken)
    {
        const int dataAlignment = 1024;

        await using var source = new FileStream(
            psuPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            8192,
            useAsync: true);

        var directoryEntry =
            await ReadDirentAsync(source, cancellationToken);

        var dotEntry =
            await ReadDirentAsync(source, cancellationToken);

        var dotDotEntry =
            await ReadDirentAsync(source, cancellationToken);

        if (!IsDirectory(directoryEntry.Mode) ||
            !IsDirectory(dotEntry.Mode) ||
            !IsDirectory(dotDotEntry.Mode))
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(psuPath)} is not a valid PSU save.");
        }

        var directoryName =
            SanitizeHostName(directoryEntry.Name);

        if (directoryEntry.Length < 2)
            throw new InvalidDataException(
                $"The PSU directory {directoryName} has an invalid entry count.");

        var saveFolder =
            Path.Combine(
                cardFolder,
                directoryName);

        Directory.CreateDirectory(saveFolder);

        var rootCreated =
            Ps2TimeToUnix(directoryEntry.Created);
        var rootModified =
            Ps2TimeToUnix(directoryEntry.Modified);

        var indexEntries =
            new List<Pcsx2IndexEntry>();

        var metadata =
            new List<(string Name, byte[] Raw)>();

        var fileCount =
            checked((int)directoryEntry.Length - 2);

        for (var order = 1;
             order <= fileCount;
             order++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry =
                await ReadDirentAsync(
                    source,
                    cancellationToken);

            if (!IsFile(entry.Mode))
                throw new InvalidDataException(
                    $"The PSU save {directoryName} contains a subdirectory.");

            var fileName =
                SanitizeHostName(entry.Name);

            var fileLength =
                checked((int)entry.Length);

            var data =
                new byte[fileLength];

            await ReadExactlyAsync(
                source,
                data,
                cancellationToken);

            var outputPath =
                Path.Combine(
                    saveFolder,
                    fileName);

            await File.WriteAllBytesAsync(
                outputPath,
                data,
                cancellationToken);

            SetHostTimestamps(
                outputPath,
                entry.Created,
                entry.Modified);

            var padded =
                RoundUp(
                    fileLength,
                    dataAlignment);

            var padding =
                padded - fileLength;

            if (padding > 0)
            {
                source.Seek(
                    padding,
                    SeekOrigin.Current);
            }

            indexEntries.Add(
                new Pcsx2IndexEntry(
                    fileName,
                    order,
                    Ps2TimeToUnix(entry.Created),
                    Ps2TimeToUnix(entry.Modified)));

            if (!IsDefaultFileMetadata(entry))
            {
                metadata.Add(
                    (fileName, entry.Raw));
            }
        }

        WritePcsx2Index(
            Path.Combine(
                saveFolder,
                "_pcsx2_index"),
            rootCreated,
            rootModified,
            indexEntries);

        SetHostTimestamps(
            saveFolder,
            directoryEntry.Created,
            directoryEntry.Modified);

        if (!IsDefaultDirectoryMetadata(directoryEntry))
        {
            await File.WriteAllBytesAsync(
                Path.Combine(
                    saveFolder,
                    "_pcsx2_meta_directory"),
                directoryEntry.Raw,
                cancellationToken);
        }

        if (metadata.Count > 0)
        {
            var metaFolder =
                Path.Combine(
                    saveFolder,
                    "_pcsx2_meta");

            Directory.CreateDirectory(metaFolder);

            foreach (var item in metadata)
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(
                        metaFolder,
                        item.Name),
                    item.Raw,
                    cancellationToken);
            }
        }
    }

    private static async Task<PsuDirent> ReadDirentAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var raw = new byte[512];

        await ReadExactlyAsync(
            source,
            raw,
            cancellationToken);

        var mode =
            BinaryPrimitives.ReadUInt16LittleEndian(
                raw.AsSpan(0, 2));

        var length =
            BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(4, 4));

        var created =
            raw.AsSpan(8, 8).ToArray();

        var modified =
            raw.AsSpan(24, 8).ToArray();

        var attr =
            BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(32, 4));

        var nameLength =
            Array.IndexOf(
                raw,
                (byte)0,
                64,
                448);

        if (nameLength < 0)
            nameLength = 512;

        var name =
            Encoding.ASCII.GetString(
                raw,
                64,
                nameLength - 64);

        return new PsuDirent(
            raw,
            mode,
            length,
            created,
            modified,
            attr,
            name);
    }

    private static bool IsDirectory(ushort mode) =>
        (mode & 0x8030) == 0x8020;

    private static bool IsFile(ushort mode) =>
        (mode & 0x8030) == 0x8010;

    private static bool IsDefaultDirectoryMetadata(
        PsuDirent entry) =>
        entry.Mode == 0x8427 &&
        entry.Attr == 0;

    private static bool IsDefaultFileMetadata(
        PsuDirent entry) =>
        entry.Mode == 0x8497 &&
        entry.Attr == 0;

    private static string SanitizeHostName(
        string name)
    {
        var value = name.Trim();

        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException(
                $"The memory card contains an unsafe filename: {name}");
        }

        return value;
    }

    private static int RoundUp(
        int value,
        int alignment)
    {
        if (value == 0)
            return 0;

        return checked(
            ((value + alignment - 1) /
             alignment) *
            alignment);
    }

    private static long Ps2TimeToUnix(
        byte[] value)
    {
        if (value.Length < 8)
            return 0;

        var second = value[1];
        var minute = value[2];
        var hour = value[3];
        var day = value[4];
        var month =
            Math.Max(
                1,
                (int)value[5]);
        var year =
            BinaryPrimitives.ReadUInt16LittleEndian(
                value.AsSpan(6, 2));

        try
        {
            var utc = new DateTimeOffset(
                year,
                month,
                day,
                hour,
                minute,
                second,
                TimeSpan.Zero);

            return utc.ToUnixTimeSeconds();
        }
        catch
        {
            return 0;
        }
    }

    private static void SetHostTimestamps(
        string path,
        byte[] created,
        byte[] modified)
    {
        try
        {
            var createdUnix =
                Ps2TimeToUnix(created);

            var modifiedUnix =
                Ps2TimeToUnix(modified);

            if (createdUnix > 0)
            {
                File.SetCreationTimeUtc(
                    path,
                    DateTimeOffset
                        .FromUnixTimeSeconds(createdUnix)
                        .UtcDateTime);
            }

            if (modifiedUnix > 0)
            {
                File.SetLastWriteTimeUtc(
                    path,
                    DateTimeOffset
                        .FromUnixTimeSeconds(modifiedUnix)
                        .UtcDateTime);
            }
        }
        catch
        {
            // PCSX2 primarily uses _pcsx2_index metadata. Host timestamp
            // failures do not invalidate the converted folder card.
        }
    }

    private static void WritePcsx2Index(
        string path,
        long rootCreated,
        long rootModified,
        IReadOnlyList<Pcsx2IndexEntry> entries)
    {
        var builder = new StringBuilder();

        builder.AppendLine("$ROOT:");
        builder.Append("  timeCreated: ")
            .AppendLine(rootCreated.ToString());
        builder.Append("  timeModified: ")
            .AppendLine(rootModified.ToString());

        foreach (var entry in entries)
        {
            builder.Append(YamlKey(entry.Name))
                .AppendLine(":");
            builder.Append("  order: ")
                .AppendLine(entry.Order.ToString());
            builder.Append("  timeCreated: ")
                .AppendLine(entry.Created.ToString());
            builder.Append("  timeModified: ")
                .AppendLine(entry.Modified.ToString());
        }

        File.WriteAllText(
            path,
            builder.ToString(),
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));
    }

    private static string YamlKey(
        string value) =>
        "'" +
        value.Replace("'", "''") +
        "'";

    private static async Task WriteFolderCardSuperblockAsync(
        string sourceCardPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int logicalPage = 512;
        const int physicalPage = 528;
        const int bytesRequired = 8192;
        const int pageCount =
            bytesRequired / logicalPage;

        await using var source = new FileStream(
            sourceCardPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            physicalPage * 4,
            useAsync: true);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bytesRequired,
            useAsync: true);

        var ecc =
            source.Length % physicalPage == 0 &&
            source.Length % logicalPage != 0;

        if (!ecc)
        {
            var data = new byte[bytesRequired];

            await ReadExactlyAsync(
                source,
                data,
                cancellationToken);

            await destination.WriteAsync(
                data,
                cancellationToken);
        }
        else
        {
            var page = new byte[physicalPage];

            for (var index = 0;
                 index < pageCount;
                 index++)
            {
                await ReadExactlyAsync(
                    source,
                    page,
                    cancellationToken);

                await destination.WriteAsync(
                    page.AsMemory(0, logicalPage),
                    cancellationToken);
            }
        }

        await destination.FlushAsync(
            cancellationToken);
    }

    private static async Task ReadExactlyAsync(
        Stream source,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var readTotal = 0;

        while (readTotal < buffer.Length)
        {
            var read =
                await source.ReadAsync(
                    buffer.AsMemory(readTotal),
                    cancellationToken);

            if (read == 0)
                throw new EndOfStreamException(
                    "The save package ended unexpectedly.");

            readTotal += read;
        }
    }

    private static void VerifyPcsx2FolderCard(
        string folder,
        IReadOnlyList<SaveEntry> saves)
    {
        var superblock =
            Path.Combine(
                folder,
                "_pcsx2_superblock");

        if (!File.Exists(superblock) ||
            new FileInfo(superblock).Length != 8192)
        {
            throw new InvalidDataException(
                "The PCSX2 folder card superblock is missing or invalid.");
        }

        foreach (var save in saves)
        {
            var directory =
                Path.Combine(
                    folder,
                    save.DirectoryId);

            if (!Directory.Exists(directory))
                throw new InvalidDataException(
                    $"The folder card is missing {save.DirectoryId}.");

            var index =
                Path.Combine(
                    directory,
                    "_pcsx2_index");

            if (!File.Exists(index))
                throw new InvalidDataException(
                    $"The folder card is missing the index for {save.DirectoryId}.");

            var realFiles =
                Directory.EnumerateFiles(directory)
                    .Where(path =>
                        !Path.GetFileName(path)
                            .StartsWith(
                                "_pcsx2_",
                                StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (realFiles.Length == 0)
                throw new InvalidDataException(
                    $"The folder card contains no save files for {save.DirectoryId}.");
        }
    }

    private sealed record PsuDirent(
        byte[] Raw,
        ushort Mode,
        uint Length,
        byte[] Created,
        byte[] Modified,
        uint Attr,
        string Name);

    private sealed record Pcsx2IndexEntry(
        string Name,
        int Order,
        long Created,
        long Modified);

    public async Task ImportAsync(
        string cardPath,
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(cardPath))
        {
            await ExtractPsuToPcsx2FolderAsync(
                packagePath,
                cardPath,
                cancellationToken);
            return;
        }

        var result = await RunAsync(cardPath, ["import", packagePath], TimeSpan.FromSeconds(60), cancellationToken);
        EnsureSuccess(result, "Could not import the save");
    }

    public async Task DeleteAsync(
        string cardPath,
        string saveId,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(cardPath))
        {
            var saveDirectory =
                Path.Combine(
                    cardPath,
                    SanitizeHostName(saveId));

            if (Directory.Exists(saveDirectory))
                Directory.Delete(
                    saveDirectory,
                    recursive: true);

            return;
        }

        var result = await RunAsync(cardPath, ["delete", saveId], TimeSpan.FromSeconds(30), cancellationToken);
        if (result.ExitCode != 0 && !result.Combined.Contains("not found", StringComparison.OrdinalIgnoreCase))
            EnsureSuccess(result, "Could not delete the existing destination save");
    }


    public async Task CreateCardAsync(
        string destinationPath,
        bool noEcc,
        CancellationToken cancellationToken = default)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        if (!noEcc)
        {
            var result = await RunRawAsync(
                ["-i", destinationPath, "format"],
                TimeSpan.FromSeconds(60),
                cancellationToken);

            EnsureSuccess(result, "Could not create the destination memory card");
            return;
        }

        // Some bundled myMC++ 3.2.0 runners reject the documented
        // -e / --no-ecc switch. Create a normal formatted ECC image first,
        // then remove each page's 16-byte spare/ECC region internally.
        var temporaryEccPath = destinationPath + ".temporary-ecc.ps2";

        try
        {
            var result = await RunRawAsync(
                ["-i", temporaryEccPath, "format"],
                TimeSpan.FromSeconds(60),
                cancellationToken);

            EnsureSuccess(
                result,
                "Could not create the temporary formatted memory card");

            await ConvertEccCardToNoEccAsync(
                temporaryEccPath,
                destinationPath,
                cancellationToken);

            if (!File.Exists(destinationPath) ||
                new FileInfo(destinationPath).Length == 0)
            {
                throw new InvalidOperationException(
                    "The MemCard PRO2 destination image was not created correctly.");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryEccPath))
                    File.Delete(temporaryEccPath);
            }
            catch
            {
                // Do not mask a successful conversion because temporary cleanup failed.
            }
        }
    }

    private static async Task ConvertEccCardToNoEccAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int logicalPageBytes = 512;
        const int physicalPageBytes = 528;

        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: physicalPageBytes * 64,
            useAsync: true);

        if (source.Length % physicalPageBytes != 0)
        {
            throw new InvalidDataException(
                $"The temporary card size ({source.Length:N0} bytes) is not " +
                "a valid 528-byte-page PS2 image.");
        }

        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: logicalPageBytes * 64,
            useAsync: true);

        var physicalPage = new byte[physicalPageBytes];

        while (source.Position < source.Length)
        {
            var totalRead = 0;

            while (totalRead < physicalPage.Length)
            {
                var read = await source.ReadAsync(
                    physicalPage.AsMemory(
                        totalRead,
                        physicalPage.Length - totalRead),
                    cancellationToken);

                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "The temporary ECC memory card ended in the middle of a page.");
                }

                totalRead += read;
            }

            await destination.WriteAsync(
                physicalPage.AsMemory(0, logicalPageBytes),
                cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);

        var expectedLength =
            source.Length / physicalPageBytes * logicalPageBytes;

        if (destination.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"No-ECC conversion produced {destination.Length:N0} bytes; " +
                $"expected {expectedLength:N0}.");
        }
    }

    private async Task<EngineResult> RunRawAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            throw new FileNotFoundException("The private myMC++ engine is not installed.");

        var info = new ProcessStartInfo
        {
            FileName = _pythonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_runnerPath) ?? AppContext.BaseDirectory
        };
        info.ArgumentList.Add(_runnerPath);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Could not start the myMC++ engine.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutSource.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"The memory-card engine timed out after {timeout.TotalSeconds:N0} seconds.");
        }
        return new EngineResult(process.ExitCode, await outputTask, await errorTask);
    }

    public Task CreateCardAsync(
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        CreateCardAsync(destinationPath, 8, cancellationToken);

    public async Task CreateCardAsync(
        string destinationPath,
        int sizeMegabytes,
        CancellationToken cancellationToken = default)
    {
        if (sizeMegabytes is not (8 or 16 or 32 or 64))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeMegabytes),
                "PS2 memory card size must be 8, 16, 32, or 64 MB.");
        }

        if (File.Exists(destinationPath))
            throw new IOException("A file already exists at the selected location.");

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        // myMC++ formats cards by cluster count. PS2 clusters are 1 KiB,
        // so 8/16/32/64 MB map directly to 8192/16384/32768/65536 clusters.
        var clusters = checked(sizeMegabytes * 1024);

        try
        {
            var result = await RunAsync(
                destinationPath,
                ["format", "-c", clusters.ToString()],
                TimeSpan.FromSeconds(180),
                cancellationToken);

            EnsureSuccess(
                result,
                "The PS2 memory card could not be created");

            await CheckAsync(destinationPath, cancellationToken);
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

    public async Task CreateCardAsync(
        string destinationPath,
        int sizeMegabytes,
        bool noEcc,
        CancellationToken cancellationToken = default)
    {
        if (!noEcc)
        {
            await CreateCardAsync(
                destinationPath,
                sizeMegabytes,
                cancellationToken);
            return;
        }

        var temporaryEccPath =
            destinationPath + ".temporary-ecc.ps2";

        try
        {
            await CreateCardAsync(
                temporaryEccPath,
                sizeMegabytes,
                cancellationToken);

            await ConvertEccCardToNoEccAsync(
                temporaryEccPath,
                destinationPath,
                cancellationToken);

            await CheckAsync(
                destinationPath,
                cancellationToken);
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
        finally
        {
            try
            {
                if (File.Exists(temporaryEccPath))
                    File.Delete(temporaryEccPath);
            }
            catch { }
        }
    }

    public async Task CreateFolderCardAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(destinationDirectory) &&
            Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
        {
            throw new IOException(
                "The selected folder already exists and is not empty.");
        }

        Directory.CreateDirectory(destinationDirectory);

        var temporaryCard = Path.Combine(
            Path.GetTempPath(),
            "PSM-FolderCard-" + Guid.NewGuid().ToString("N") + ".ps2");

        try
        {
            await CreateCardAsync(temporaryCard, cancellationToken);

            await using var source = new FileStream(
                temporaryCard,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                useAsync: true);

            var superblock = new byte[8192];
            var totalRead = 0;

            while (totalRead < superblock.Length)
            {
                var read = await source.ReadAsync(
                    superblock.AsMemory(totalRead),
                    cancellationToken);

                if (read == 0)
                    throw new EndOfStreamException(
                        "The formatted PS2 card did not contain a complete superblock.");

                totalRead += read;
            }

            var marker = Path.Combine(
                destinationDirectory,
                "_pcsx2_superblock");

            await File.WriteAllBytesAsync(
                marker,
                superblock,
                cancellationToken);

            if (new FileInfo(marker).Length != 8192)
                throw new InvalidDataException(
                    "The PCSX2 folder-card superblock failed verification.");
        }
        catch
        {
            try
            {
                if (Directory.Exists(destinationDirectory) &&
                    !Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
                {
                    Directory.Delete(destinationDirectory);
                }
            }
            catch { }

            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryCard))
                    File.Delete(temporaryCard);
            }
            catch { }
        }
    }

    public async Task CheckAsync(string cardPath, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(cardPath))
        {
            var saves =
                await ReadFolderCardAsync(
                    cardPath,
                    cancellationToken);

            VerifyPcsx2FolderCard(
                cardPath,
                saves.Saves);
            return;
        }

        var result = await RunAsync(cardPath, ["check"], TimeSpan.FromSeconds(60), cancellationToken);
        EnsureSuccess(result, "The destination card did not pass verification");
    }

    public async Task ConvertFolderCardToImageAsync(
        string folderCardPath,
        string destinationPath,
        bool noEcc,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderCardPath))
            throw new DirectoryNotFoundException(
                "The PCSX2 folder card was not found.");

        var temporary =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-Folder-To-Card-" +
                Guid.NewGuid().ToString("N") +
                ".ps2");

        try
        {
            await CreateCardAsync(
                temporary,
                noEcc: false,
                cancellationToken);

            var card =
                await ReadFolderCardAsync(
                    folderCardPath,
                    cancellationToken);

            foreach (var save in card.Saves)
            {
                var psu =
                    Path.Combine(
                        Path.GetTempPath(),
                        "PSM-" +
                        Guid.NewGuid().ToString("N") +
                        ".psu");

                try
                {
                    await BuildPsuFromFolderSaveAsync(
                        folderCardPath,
                        save.DirectoryId,
                        psu,
                        cancellationToken);

                    await ImportAsync(
                        temporary,
                        psu,
                        cancellationToken);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(psu))
                            File.Delete(psu);
                    }
                    catch { }
                }
            }

            await CheckAsync(
                temporary,
                cancellationToken);

            if (noEcc)
            {
                await ConvertEccCardToNoEccAsync(
                    temporary,
                    destinationPath,
                    cancellationToken);
            }
            else
            {
                File.Copy(
                    temporary,
                    destinationPath,
                    overwrite: true);
            }
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

    private Task<CardReadResult> ReadFolderCardAsync(
        string cardPath,
        CancellationToken cancellationToken)
    {
        var superblock =
            Path.Combine(
                cardPath,
                "_pcsx2_superblock");

        if (!File.Exists(superblock))
            throw new InvalidDataException(
                "The selected folder is not a PCSX2 folder memory card.");

        var saves =
            new List<SaveEntry>();

        foreach (var directory in Directory
            .EnumerateDirectories(cardPath)
            .OrderBy(
                path => path,
                StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directoryId =
                Path.GetFileName(directory);

            if (directoryId.StartsWith(
                "_pcsx2_",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var files =
                Directory.EnumerateFiles(
                    directory)
                    .Where(path =>
                        !Path.GetFileName(path)
                            .StartsWith(
                                "_pcsx2_",
                                StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (files.Length == 0)
                continue;

            var size =
                files.Sum(path =>
                    new FileInfo(path).Length);

            var (title, subtitle) =
                ReadFolderIconTitle(directory);

            var identity =
                NormalizePs2SaveIdentity(
                    directoryId,
                    title,
                    subtitle);

            saves.Add(
                new SaveEntry
                {
                    Title =
                        string.IsNullOrWhiteSpace(identity.Subtitle)
                            ? identity.GameTitle
                            : $"{identity.GameTitle} - {identity.Subtitle}",
                    GameTitle = identity.GameTitle,
                    DirectoryId = directoryId,
                    SizeBytes = size,
                    Subtitle = identity.Subtitle
                });
        }

        return Task.FromResult(
            new CardReadResult(
                saves,
                TotalBytes: null,
                FreeBytes: null));
    }

    private static (string Title, string Subtitle)
        ReadFolderIconTitle(
            string saveDirectory)
    {
        var iconSys =
            Path.Combine(
                saveDirectory,
                "icon.sys");

        if (!File.Exists(iconSys))
            return (
                Path.GetFileName(saveDirectory),
                "Save data");

        try
        {
            var data =
                File.ReadAllBytes(iconSys);

            if (data.Length != 964 ||
                Encoding.ASCII.GetString(
                    data,
                    0,
                    4) != "PS2D")
            {
                return (
                    Path.GetFileName(saveDirectory),
                    "Save data");
            }

            const int titleOffset = 192;
            const int titleLength = 68;

            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            var lineBreak =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    data.AsSpan(6, 2));

            if (lineBreak <= 0 ||
                lineBreak >= titleLength)
            {
                lineBreak = titleLength;
            }

            var encoding =
                Encoding.GetEncoding(932);

            var title =
                DecodeNativePs2TitleLine(
                    encoding,
                    data,
                    titleOffset,
                    lineBreak);

            var subtitle =
                lineBreak < titleLength
                    ? DecodeNativePs2TitleLine(
                        encoding,
                        data,
                        titleOffset + lineBreak,
                        titleLength - lineBreak)
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(title))
            {
                title =
                    DecodeNativePs2TitleLine(
                        encoding,
                        data,
                        titleOffset,
                        titleLength);
            }

            return (
                string.IsNullOrWhiteSpace(title)
                    ? Path.GetFileName(saveDirectory)
                    : title,
                string.IsNullOrWhiteSpace(subtitle)
                    ? "Save data"
                    : subtitle);
        }
        catch
        {
            return (
                Path.GetFileName(saveDirectory),
                "Save data");
        }
    }

    private static string DecodeNativePs2TitleLine(
        Encoding encoding,
        byte[] data,
        int offset,
        int count)
    {
        if (count <= 0 ||
            offset < 0 ||
            offset + count > data.Length)
        {
            return string.Empty;
        }

        var value =
            encoding.GetString(
                    data,
                    offset,
                    count)
                .Replace("\0", string.Empty)
                .Normalize(
                    NormalizationForm.FormKC);

        value =
            Regex.Replace(
                value,
                @"[\x00-\x1F]+",
                " ");

        value =
            Regex.Replace(
                value,
                @"\s+",
                " ");

        return value.Trim()
            .TrimEnd('@')
            .Trim();
    }

    private static async Task BuildPsuFromFolderSaveAsync(
        string cardFolder,
        string saveId,
        string destination,
        CancellationToken cancellationToken)
    {
        const int alignment = 1024;

        var saveDirectory =
            Path.Combine(
                cardFolder,
                SanitizeHostName(saveId));

        if (!Directory.Exists(saveDirectory))
            throw new DirectoryNotFoundException(
                $"The folder card does not contain {saveId}.");

        var ordered =
            ReadFolderIndexOrder(saveDirectory);

        var files =
            Directory.EnumerateFiles(saveDirectory)
                .Where(path =>
                    !Path.GetFileName(path)
                        .StartsWith(
                            "_pcsx2_",
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(path =>
                {
                    var name =
                        Path.GetFileName(path);

                    return ordered.TryGetValue(
                        name,
                        out var order)
                            ? order
                            : int.MaxValue;
                })
                .ThenBy(
                    path => Path.GetFileName(path),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (files.Length == 0)
            throw new InvalidDataException(
                $"The folder save {saveId} contains no files.");

        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);

        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            8192,
            useAsync: true);

        var directoryMetadata =
            Path.Combine(
                saveDirectory,
                "_pcsx2_meta_directory");

        var root =
            File.Exists(directoryMetadata)
                ? await File.ReadAllBytesAsync(
                    directoryMetadata,
                    cancellationToken)
                : CreatePsuEntry(
                    mode: 0x8427,
                    length: checked((uint)files.Length + 2),
                    name: saveId,
                    sourcePath: saveDirectory);

        NormalizePsuEntry(
            root,
            0x8427,
            checked((uint)files.Length + 2),
            saveId);

        await output.WriteAsync(root, cancellationToken);

        var dot =
            CreatePsuEntry(
                0x8427,
                checked((uint)files.Length + 2),
                ".",
                saveDirectory);

        var dotDot =
            CreatePsuEntry(
                0x8427,
                0,
                "..",
                saveDirectory);

        await output.WriteAsync(dot, cancellationToken);
        await output.WriteAsync(dotDot, cancellationToken);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name =
                Path.GetFileName(file);

            var metadata =
                Path.Combine(
                    saveDirectory,
                    "_pcsx2_meta",
                    name);

            var entry =
                File.Exists(metadata)
                    ? await File.ReadAllBytesAsync(
                        metadata,
                        cancellationToken)
                    : CreatePsuEntry(
                        0x8497,
                        checked((uint)new FileInfo(file).Length),
                        name,
                        file);

            NormalizePsuEntry(
                entry,
                0x8497,
                checked((uint)new FileInfo(file).Length),
                name);

            await output.WriteAsync(
                entry,
                cancellationToken);

            await using var input = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                useAsync: true);

            await input.CopyToAsync(
                output,
                cancellationToken);

            var padding =
                RoundUp(
                    checked((int)input.Length),
                    alignment) -
                checked((int)input.Length);

            if (padding > 0)
            {
                await output.WriteAsync(
                    new byte[padding],
                    cancellationToken);
            }
        }
    }

    private static Dictionary<string, int>
        ReadFolderIndexOrder(
            string saveDirectory)
    {
        var result =
            new Dictionary<string, int>(
                StringComparer.Ordinal);

        var path =
            Path.Combine(
                saveDirectory,
                "_pcsx2_index");

        if (!File.Exists(path))
            return result;

        string? current = null;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.TrimEnd();

            if (!line.StartsWith(" ") &&
                line.EndsWith(":") &&
                !line.StartsWith("$ROOT", StringComparison.Ordinal))
            {
                current =
                    line[..^1]
                        .Trim()
                        .Trim('\'')
                        .Replace("''", "'");
                continue;
            }

            if (current is not null &&
                line.TrimStart()
                    .StartsWith(
                        "order:",
                        StringComparison.OrdinalIgnoreCase))
            {
                var value =
                    line[(line.IndexOf(':') + 1)..]
                        .Trim();

                if (int.TryParse(value, out var order))
                    result[current] = order;
            }
        }

        return result;
    }

    private static byte[] CreatePsuEntry(
        ushort mode,
        uint length,
        string name,
        string sourcePath)
    {
        var data = new byte[512];

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0, 2),
            mode);

        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(4, 4),
            length);

        WritePs2Time(
            data.AsSpan(8, 8),
            Directory.Exists(sourcePath)
                ? Directory.GetCreationTimeUtc(sourcePath)
                : File.GetCreationTimeUtc(sourcePath));

        WritePs2Time(
            data.AsSpan(24, 8),
            Directory.Exists(sourcePath)
                ? Directory.GetLastWriteTimeUtc(sourcePath)
                : File.GetLastWriteTimeUtc(sourcePath));

        WriteAsciiName(
            data,
            name);

        return data;
    }

    private static void NormalizePsuEntry(
        byte[] data,
        ushort mode,
        uint length,
        string name)
    {
        if (data.Length != 512)
            throw new InvalidDataException(
                "A PCSX2 metadata entry was not 512 bytes.");

        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0, 2),
            mode);

        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(4, 4),
            length);

        Array.Clear(data, 64, 448);
        WriteAsciiName(data, name);
    }

    private static void WriteAsciiName(
        byte[] data,
        string name)
    {
        var bytes =
            Encoding.ASCII.GetBytes(name);

        if (bytes.Length >= 448)
            throw new InvalidDataException(
                $"The filename is too long: {name}");

        bytes.CopyTo(
            data,
            64);
    }

    private static void WritePs2Time(
        Span<byte> destination,
        DateTime utc)
    {
        var value =
            utc.Kind == DateTimeKind.Utc
                ? utc
                : utc.ToUniversalTime();

        destination.Clear();
        destination[1] = (byte)value.Second;
        destination[2] = (byte)value.Minute;
        destination[3] = (byte)value.Hour;
        destination[4] = (byte)value.Day;
        destination[5] = (byte)value.Month;

        BinaryPrimitives.WriteUInt16LittleEndian(
            destination.Slice(6, 2),
            checked((ushort)value.Year));
    }

    private static long? ParseFreeBytes(string output)
    {
        var patterns = new[]
        {
            @"(?im)^\s*(?<free>[\d,]+)\s*KB\s+Free\s*$",
            @"(?im)^\s*(?<free>[\d,]+)\s*K(?:B)?\s+of\s+[\d,]+\s*K(?:B)?\s+free\s*$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(output, pattern);
            if (!match.Success) continue;

            var digits = match.Groups["free"].Value.Replace(",", string.Empty);
            if (long.TryParse(digits, out var freeKb))
                return freeKb * 1024L;
        }

        return null;
    }

    private static (
        long? ContainerTotalBytes,
        int? BankCount)
        GetBankedVm2ContainerInfo(
            string cardPath,
            long? activeBankBytes)
    {
        try
        {
            if (!Path.GetExtension(cardPath).Equals(
                    ".vm2",
                    StringComparison.OrdinalIgnoreCase) ||
                !activeBankBytes.HasValue ||
                activeBankBytes.Value <= 0)
            {
                return (null, null);
            }

            var physicalBytes =
                new FileInfo(cardPath).Length;

            if (physicalBytes <= 0 ||
                physicalBytes % 528 != 0)
            {
                return (null, null);
            }

            var containerLogicalBytes =
                physicalBytes / 528 * 512;

            if (containerLogicalBytes <=
                activeBankBytes.Value)
            {
                return (null, null);
            }

            if (containerLogicalBytes %
                activeBankBytes.Value != 0)
            {
                return (null, null);
            }

            var bankCount =
                checked((int)(
                    containerLogicalBytes /
                    activeBankBytes.Value));

            if (bankCount <= 1)
                return (null, null);

            // Confirm that each expected bank begins with a PS2
            // memory-card superblock. This prevents ordinary padded
            // images from being misidentified as banked VM2 files.
            var bankPhysicalBytes =
                activeBankBytes.Value / 512 * 528;

            using var stream =
                File.OpenRead(cardPath);

            var magic =
                Encoding.ASCII.GetBytes(
                    "Sony PS2 Memory Card Format ");

            var buffer =
                new byte[magic.Length];

            for (var bank = 0;
                 bank < bankCount;
                 bank++)
            {
                stream.Position =
                    bank * bankPhysicalBytes;

                var read =
                    stream.Read(
                        buffer,
                        0,
                        buffer.Length);

                if (read != buffer.Length ||
                    !buffer.AsSpan()
                        .SequenceEqual(magic))
                {
                    return (null, null);
                }
            }

            return (
                containerLogicalBytes,
                bankCount);
        }
        catch
        {
            return (null, null);
        }
    }

    private static long? GetLogicalCardSize(string cardPath)
    {
        try
        {
            var physicalBytes = new FileInfo(cardPath).Length;
            if (physicalBytes <= 0) return null;

            // Prefer the filesystem geometry stored in the PS2 superblock.
            // This avoids treating multi-image / padded ECC containers such
            // as some VM2 files as one giant active filesystem.
            using (var stream = File.OpenRead(cardPath))
            {
                if (stream.Length >= 0x34)
                {
                    var superblock = new byte[0x34];
                    var read = stream.Read(
                        superblock,
                        0,
                        superblock.Length);

                    if (read == superblock.Length &&
                        Encoding.ASCII.GetString(
                            superblock,
                            0,
                            28)
                            .Equals(
                                "Sony PS2 Memory Card Format ",
                                StringComparison.Ordinal))
                    {
                        var pageLength =
                            BinaryPrimitives.ReadUInt16LittleEndian(
                                superblock.AsSpan(0x28, 2));

                        var pagesPerCluster =
                            BinaryPrimitives.ReadUInt16LittleEndian(
                                superblock.AsSpan(0x2A, 2));

                        var clustersPerCard =
                            BinaryPrimitives.ReadUInt32LittleEndian(
                                superblock.AsSpan(0x30, 4));

                        var logicalBytes =
                            (long)pageLength *
                            pagesPerCluster *
                            clustersPerCard;

                        if (logicalBytes > 0)
                            return logicalBytes;
                    }
                }
            }

            if (physicalBytes % 528 == 0)
            {
                var logicalBytes = physicalBytes / 528 * 512;
                if (logicalBytes > 0)
                    return logicalBytes;
            }

            return physicalBytes;
        }
        catch
        {
            return null;
        }
    }

    private static (string GameTitle, string Subtitle)
        NormalizePs2SaveIdentity(
            string directoryId,
            string gameTitle,
            string subtitle)
    {
        if (directoryId.Equals(
                "BEDATA-SYSTEM",
                StringComparison.OrdinalIgnoreCase) ||
            directoryId.EndsWith(
                "-SYSTEM",
                StringComparison.OrdinalIgnoreCase))
        {
            return (
                "Your System",
                "Configuration");
        }

        return (
            string.IsNullOrWhiteSpace(gameTitle)
                ? directoryId
                : gameTitle.Trim(),
            string.IsNullOrWhiteSpace(subtitle)
                ? string.Empty
                : subtitle.Trim());
    }

    private static IReadOnlyList<SaveEntry> ParseDirectory(string output)
    {
        var lines = output.Replace("\r", string.Empty).Split('\n');
        var saves = new Dictionary<string, SaveEntry>(StringComparer.OrdinalIgnoreCase);
        var header = new Regex(@"^\s*(?<id>\S+)\s{2,}(?<title>.+?)\s*$", RegexOptions.Compiled);
        var detail = new Regex(@"^\s*(?<kb>\d+)KB\s+(?<rest>.*)$", RegexOptions.Compiled);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || Regex.IsMatch(line, @"^\s*[\d,]+\s+KB Free\s*$"))
                continue;

            var match = header.Match(line);
            if (!match.Success)
                continue;

            var id = match.Groups["id"].Value.Trim();
            var title = match.Groups["title"].Value.Trim();
            long sizeBytes = 0;
            var subtitle = string.Empty;

            if (index + 1 < lines.Length)
            {
                var detailMatch = detail.Match(lines[index + 1]);
                if (detailMatch.Success)
                {
                    sizeBytes = long.Parse(detailMatch.Groups["kb"].Value) * 1024;
                    var remainder = detailMatch.Groups["rest"].Value.Trim();
                    if (remainder.Length > 25)
                        subtitle = remainder[25..].Trim();
                    index++;
                }
            }

            if (!saves.ContainsKey(id))
            {
                var identity =
                    NormalizePs2SaveIdentity(
                        id,
                        title,
                        subtitle);

                saves[id] = new SaveEntry
                {
                    DirectoryId = id,
                    GameTitle = identity.GameTitle,
                    Title =
                        string.IsNullOrWhiteSpace(identity.Subtitle)
                            ? identity.GameTitle
                            : $"{identity.GameTitle} - {identity.Subtitle}",
                    Subtitle = identity.Subtitle,
                    SizeBytes = sizeBytes
                };
            }
        }

        return saves.Values.OrderBy(save => save.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static void EnsureSuccess(EngineResult result, string message)
    {
        if (result.ExitCode == 0)
            return;
        var details = string.IsNullOrWhiteSpace(result.Combined) ? "The engine returned no details." : result.Combined.Trim();
        throw new InvalidOperationException($"{message}.{Environment.NewLine}{Environment.NewLine}{details}");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort only.
        }
    }
}
