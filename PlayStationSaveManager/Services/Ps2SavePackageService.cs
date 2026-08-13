using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PlayStationSaveManager.Models;

namespace PlayStationSaveManager.Services;

public sealed class Ps2SavePackageService
{
    private static readonly DateTimeOffset StableZipTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly MyMcEngine _engine;

    public Ps2SavePackageService(MyMcEngine engine)
    {
        _engine = engine;
    }

    public async Task ExportFromCardAsync(
        string cardPath,
        SaveEntry save,
        string destinationPath,
        string? originalFileName = null,
        string? originalFormat = null,
        DateTime? createdUtc = null,
        CancellationToken cancellationToken = default)
    {
        var destinationDirectory =
            Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-PS2SAVE-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryRoot);

        var payloadPath =
            Path.Combine(temporaryRoot, "save.psu");

        var temporaryPackage =
            destinationPath + ".tmp-" +
            Guid.NewGuid().ToString("N");

        try
        {
            await _engine.ExportPsuAsync(
                cardPath,
                save.DirectoryId,
                payloadPath,
                cancellationToken);

            var manifest =
                new Ps2SavePackageManifest
                {
                    GameTitle = save.GameTitle,
                    SaveTitle = save.ProfileName,
                    DirectoryId = save.DirectoryId,
                    SizeBytes = save.SizeBytes,
                    PayloadFormat = "PSU",
                    OriginalFileName =
                        string.IsNullOrWhiteSpace(originalFileName)
                            ? save.DirectoryId + ".ps2save"
                            : originalFileName,
                    OriginalFormat =
                        string.IsNullOrWhiteSpace(originalFormat)
                            ? "Native PS2 Memory Card Save"
                            : originalFormat,
                    CreatedUtc =
                        createdUtc ??
                        DateTime.UnixEpoch
                };

            await using (var output =
                new FileStream(
                    temporaryPackage,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81920,
                    useAsync: true))
            {
                using var archive =
                    new ZipArchive(
                        output,
                        ZipArchiveMode.Create,
                        leaveOpen: true);

                var manifestEntry =
                    archive.CreateEntry(
                        "manifest.json",
                        CompressionLevel.Optimal);
                manifestEntry.LastWriteTime =
                    StableZipTimestamp;

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

                var payloadEntry =
                    archive.CreateEntry(
                        "save.psu",
                        CompressionLevel.Optimal);
                payloadEntry.LastWriteTime =
                    StableZipTimestamp;

                await using var payloadOutput =
                    payloadEntry.Open();
                await using var payloadInput =
                    new FileStream(
                        payloadPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81920,
                        useAsync: true);

                await payloadInput.CopyToAsync(
                    payloadOutput,
                    cancellationToken);
            }

            var inspected =
                await InspectAsync(
                    temporaryPackage,
                    cancellationToken);

            if (!inspected.DirectoryId.Equals(
                    save.DirectoryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The exported PS2 save package failed verification.");
            }

            File.Move(
                temporaryPackage,
                destinationPath,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPackage))
                    File.Delete(temporaryPackage);
            }
            catch { }

            try
            {
                Directory.Delete(
                    temporaryRoot,
                    recursive: true);
            }
            catch { }
        }
    }

    public async Task CreateFromLegacyPackageAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-PS2SAVE-CONVERT-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var cardPath =
                Path.Combine(
                    temporaryRoot,
                    "source.ps2");

            await _engine.CreateCardAsync(
                cardPath,
                false,
                cancellationToken);

            var importPath = sourcePath;

            if (Path.GetExtension(sourcePath).Equals(
                    ".sps",
                    StringComparison.OrdinalIgnoreCase))
            {
                importPath =
                    Path.Combine(
                        temporaryRoot,
                        "source-normalized.psu");

                await SpsPackageService.ConvertToPsuAsync(
                    sourcePath,
                    importPath,
                    cancellationToken);
            }

            await _engine.ImportAsync(
                cardPath,
                importPath,
                cancellationToken);

            await _engine.CheckAsync(
                cardPath,
                cancellationToken);

            var saves =
                await _engine.ReadDirectoryAsync(
                    cardPath,
                    cancellationToken);

            if (saves.Count != 1)
            {
                throw new InvalidDataException(
                    $"Expected one PS2 save, but the source contains {saves.Count}.");
            }

            await ExportFromCardAsync(
                cardPath,
                saves[0],
                destinationPath,
                Path.GetFileName(sourcePath),
                Path.GetExtension(sourcePath)
                    .TrimStart('.')
                    .ToUpperInvariant(),
                File.GetLastWriteTimeUtc(sourcePath),
                cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryRoot,
                    recursive: true);
            }
            catch { }
        }
    }

    public async Task<Ps2SavePackageManifest> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException(
                "The PS2 save package was not found.",
                packagePath);
        }

        Ps2SavePackageManifest manifest;

        await using (var input =
            new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true))
        using (var archive =
            new ZipArchive(
                input,
                ZipArchiveMode.Read,
                leaveOpen: false))
        {
            var manifestEntry =
                archive.GetEntry("manifest.json")
                ?? throw new InvalidDataException(
                    "The PS2 save package has no manifest.");

            if (archive.GetEntry("save.psu") is null)
            {
                throw new InvalidDataException(
                    "The PS2 save package has no PSU payload.");
            }

            await using var manifestStream =
                manifestEntry.Open();

            manifest =
                await JsonSerializer.DeserializeAsync<
                    Ps2SavePackageManifest>(
                    manifestStream,
                    cancellationToken: cancellationToken)
                ?? throw new InvalidDataException(
                    "The PS2 save package manifest is invalid.");
        }

        if (!manifest.Platform.Equals(
                "PlayStation 2",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The package is not a PlayStation 2 save package.");
        }

        if (string.IsNullOrWhiteSpace(
                manifest.DirectoryId))
        {
            throw new InvalidDataException(
                "The PS2 save package manifest has no directory ID.");
        }

        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-PS2SAVE-VERIFY-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var payload =
                Path.Combine(
                    temporaryRoot,
                    "save.psu");

            await ExtractPsuAsync(
                packagePath,
                payload,
                cancellationToken);

            var card =
                Path.Combine(
                    temporaryRoot,
                    "verify.ps2");

            await _engine.CreateCardAsync(
                card,
                false,
                cancellationToken);

            await _engine.ImportAsync(
                card,
                payload,
                cancellationToken);

            await _engine.CheckAsync(
                card,
                cancellationToken);

            var saves =
                await _engine.ReadDirectoryAsync(
                    card,
                    cancellationToken);

            if (saves.Count != 1 ||
                !saves[0].DirectoryId.Equals(
                    manifest.DirectoryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The PS2 save package payload failed verification.");
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
            catch { }
        }

        return manifest;
    }

    public static async Task ExtractPsuAsync(
        string packagePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var destinationDirectory =
            Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        await using var input =
            new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

        using var archive =
            new ZipArchive(
                input,
                ZipArchiveMode.Read,
                leaveOpen: false);

        var payload =
            archive.GetEntry("save.psu")
            ?? throw new InvalidDataException(
                "The PS2 save package has no PSU payload.");

        await using var payloadInput =
            payload.Open();

        await using var output =
            new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

        await payloadInput.CopyToAsync(
            output,
            cancellationToken);
    }
}
