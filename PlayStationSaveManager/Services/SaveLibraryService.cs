using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PlayStationSaveManager.Models;

namespace PlayStationSaveManager.Services;

public sealed record SaveLibraryImportResult(
    SaveLibraryEntry Entry,
    SaveLibraryEntry? Duplicate);

public sealed class SaveLibraryService
{
    private readonly MyMcEngine _engine;
    private readonly string _libraryRoot;
    private readonly string _filesRoot;
    private readonly string _indexPath;
    private readonly string _legacyIndexPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public SaveLibraryService(MyMcEngine engine)
    {
        _engine = engine;
        _libraryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlayStationSaveManager",
            "SaveLibrary");
        _filesRoot = Path.Combine(_libraryRoot, "Files");
        _indexPath = Path.Combine(_libraryRoot, "game-saves.json");
        _legacyIndexPath = Path.Combine(_libraryRoot, "library.json");

        Directory.CreateDirectory(_filesRoot);

        if (!File.Exists(_indexPath) &&
            File.Exists(_legacyIndexPath))
        {
            try
            {
                File.Move(_legacyIndexPath, _indexPath);
            }
            catch
            {
                File.Copy(_legacyIndexPath, _indexPath, overwrite: false);
            }
        }

        // No persistent icon cache is used. Thumbnails are rebuilt in
        // memory from their stored save packages.
        try
        {
            var legacyIcons = Path.Combine(_libraryRoot, "Icons");
            if (Directory.Exists(legacyIcons))
                Directory.Delete(legacyIcons, recursive: true);
        }
        catch { }
    }

    public string LibraryRoot => _libraryRoot;

    public async Task<SaveLibraryIndex> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_indexPath))
            return new SaveLibraryIndex();

        try
        {
            SaveLibraryIndex index;
            await using (var stream = File.OpenRead(_indexPath))
            {
                index = await JsonSerializer.DeserializeAsync<SaveLibraryIndex>(
                    stream,
                    _jsonOptions,
                    cancellationToken) ?? new SaveLibraryIndex();
            }

            if (MigrateStoredFileNames(index))
                await SaveAsync(index, cancellationToken);

            return index;
        }
        catch
        {
            var recoveryPath = _indexPath + ".recovery-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(_indexPath, recoveryPath, true); } catch { }
            return new SaveLibraryIndex();
        }
    }

    public async Task SaveAsync(
        SaveLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_libraryRoot);
        var temporary = _indexPath + ".tmp";

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                index,
                _jsonOptions,
                cancellationToken);
        }

        File.Move(temporary, _indexPath, true);
    }

    public string GetStoredPath(SaveLibraryEntry entry) =>
        Path.Combine(_filesRoot, entry.StoredFileName);

    public async Task<SaveLibraryImportResult> ImportAsync(
        string sourcePath,
        SaveLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The save package does not exist.", sourcePath);

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not ".psu" and not ".max" and not ".cbs" and
            not ".xps" and not ".sps" and not ".psv" and
            not ".ps1save")
        {
            throw new NotSupportedException(
                "The Save Library imports PS1SAVE, PSU, MAX, CBS, XPS, SPS, and PSV packages.");
        }

        var hash = await ComputeSha256Async(sourcePath, cancellationToken);
        var duplicate = index.Entries.FirstOrDefault(entry =>
            entry.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null)
            return new SaveLibraryImportResult(duplicate, duplicate);

        SaveEntry? metadata = null;
        Ps1SavePackageManifest? ps1Metadata = null;

        if (extension == ".ps1save")
        {
            ps1Metadata =
                await Ps1MemoryCardService.InspectSavePackageAsync(
                    sourcePath,
                    cancellationToken);
        }
        else
        {
            metadata = await InspectPackageAsync(
                sourcePath,
                cancellationToken);
        }

        var id = Guid.NewGuid().ToString("N");
        var description = extension == ".ps1save"
            ? (!string.IsNullOrWhiteSpace(ps1Metadata!.SaveTitle)
                ? ps1Metadata.SaveTitle
                : ps1Metadata.OriginalFileName)
            : metadata!.ProfileName;

        var directoryId = extension == ".ps1save"
            ? ps1Metadata!.ProductCode
            : metadata!.DirectoryId;

        var storedFileName = CreateAvailableStoredFileName(
            BuildFriendlySaveFileName(directoryId, description, extension));
        var destination = Path.Combine(_filesRoot, storedFileName);
        File.Copy(sourcePath, destination, true);

        var file = new FileInfo(sourcePath);
        var entry = new SaveLibraryEntry
        {
            Id = id,
            StoredFileName = storedFileName,
            OriginalFileName = file.Name,
            OriginalPath = sourcePath,
            Extension = extension,
            FormatName = FormatName(extension),
            Platform = extension == ".ps1save"
                ? "PlayStation"
                : "PlayStation 2",
            DirectoryId = extension == ".ps1save"
                ? ps1Metadata!.ProductCode
                : metadata!.DirectoryId,
            GameTitle = extension == ".ps1save"
                ? ps1Metadata!.Title
                : metadata!.GameTitle,
            ProfileName = extension == ".ps1save"
                ? (!string.IsNullOrWhiteSpace(ps1Metadata!.SaveTitle)
                    ? ps1Metadata.SaveTitle
                    : ps1Metadata.OriginalFileName)
                : metadata!.ProfileName,
            SizeBytes = file.Length,
            Sha256 = hash,
            AddedUtc = DateTime.UtcNow,
            ModifiedUtc = file.LastWriteTimeUtc
        };

        index.Entries.Add(entry);
        await SaveAsync(index, cancellationToken);

        return new SaveLibraryImportResult(entry, null);
    }

    private bool MigrateStoredFileNames(SaveLibraryIndex index)
    {
        var changed = false;

        foreach (var entry in index.Entries)
        {
            var oldPath = Path.Combine(_filesRoot, entry.StoredFileName);
            if (!File.Exists(oldPath))
                continue;

            var desired = BuildFriendlySaveFileName(
                entry.DirectoryId,
                entry.ProfileName,
                entry.Extension);

            if (entry.StoredFileName.Equals(
                desired,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var newName = CreateAvailableStoredFileName(
                desired,
                oldPath);
            var newPath = Path.Combine(_filesRoot, newName);

            if (!oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                File.Move(oldPath, newPath);

            entry.StoredFileName = newName;
            changed = true;
        }

        return changed;
    }

    private string CreateAvailableStoredFileName(
        string desired,
        string? existingPath = null)
    {
        var stem = Path.GetFileNameWithoutExtension(desired);
        var extension = Path.GetExtension(desired);
        var candidate = desired;
        var number = 2;

        while (File.Exists(Path.Combine(_filesRoot, candidate)) &&
               !Path.Combine(_filesRoot, candidate).Equals(
                   existingPath,
                   StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"{stem} ({number++}){extension}";
        }

        return candidate;
    }

    private static string BuildFriendlySaveFileName(
        string directoryId,
        string description,
        string extension)
    {
        var safeDirectory = SanitizeFileName(
            string.IsNullOrWhiteSpace(directoryId)
                ? "Unknown Save"
                : directoryId.Trim());

        var safeDescription = SanitizeFileName(
            string.IsNullOrWhiteSpace(description)
                ? "Save Data"
                : description.Trim());

        return $"{safeDirectory} - {safeDescription}{extension}";
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        value = value.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(value)
            ? "Untitled"
            : value;
    }

    public async Task ExportAsync(
        SaveLibraryEntry entry,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var storedPath = GetStoredPath(entry);
        if (!File.Exists(storedPath))
            throw new FileNotFoundException(
                "The library package file is missing.", storedPath);

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        // Close both streams before verification.  The previous implementation
        // calculated the destination hash while the destination stream still held
        // FileShare.None, which made a successful export report a false lock error.
        await using (var source = new FileStream(
            storedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, useAsync: true))
        await using (var destination = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, useAsync: true))
        {
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }

        var sourceHash = await ComputeSha256Async(
            storedPath, cancellationToken);
        var destinationHash = await ComputeSha256Async(
            destinationPath, cancellationToken);

        if (!sourceHash.Equals(destinationHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Export verification failed.");
    }

    public async Task RemoveAsync(
        SaveLibraryEntry entry,
        SaveLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        index.Entries.Remove(entry);
        await SaveAsync(index, cancellationToken);

        try
        {
            var storedPath = GetStoredPath(entry);
            if (File.Exists(storedPath))
                File.Delete(storedPath);
        }
        catch
        {
            // Metadata removal is still valid; orphan cleanup can be retried later.
        }
    }

    public async Task ToggleFavoriteAsync(
        SaveLibraryEntry entry,
        SaveLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        entry.IsFavorite = !entry.IsFavorite;
        await SaveAsync(index, cancellationToken);
    }

    private async Task<SaveEntry> InspectPackageAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-LIBRARY-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var cardPath = Path.Combine(temporaryRoot, "inspect.ps2");
            await _engine.CreateCardAsync(cardPath, false, cancellationToken);

            var inspectionPackagePath = packagePath;
            if (Path.GetExtension(packagePath).Equals(
                ".sps",
                StringComparison.OrdinalIgnoreCase))
            {
                inspectionPackagePath = Path.Combine(
                    temporaryRoot,
                    "normalized-inspection.psu");

                await SpsPackageService.ConvertToPsuAsync(
                    packagePath,
                    inspectionPackagePath,
                    cancellationToken);
            }

            await _engine.ImportAsync(
                cardPath,
                inspectionPackagePath,
                cancellationToken);
            await _engine.CheckAsync(cardPath, cancellationToken);

            var saves = await _engine.ReadDirectoryAsync(cardPath, cancellationToken);
            if (saves.Count != 1)
                throw new InvalidDataException(
                    $"The package contains {saves.Count} saves; exactly one was expected.");

            return saves[0];
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); } catch { }
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string FormatName(string extension) =>
        extension switch
        {
            ".psu" => "EMS / Memory Linker PSU",
            ".max" => "ARMAX V3",
            ".cbs" => "CodeBreaker Save",
            ".xps" => "X-Port / Xploder Save",
            ".sps" => "SharkPort Save",
            ".psv" => "PlayStation 3 Virtual Save",
            ".ps1save" => "PSM PlayStation Save Package",
            _ => extension.TrimStart('.').ToUpperInvariant()
        };
}
