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
    private readonly Ps2SavePackageService _ps2PackageService;
    private readonly string _libraryRoot;
    private readonly string _filesRoot;
    private readonly string _ps1SavesRoot;
    private readonly string _ps2SavesRoot;
    private const string Ps1SavesFolderName = "PS1 Saves";
    private const string Ps2SavesFolderName = "PS2 Saves";
    private readonly string _indexPath;
    private readonly string _legacyIndexPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public SaveLibraryService(MyMcEngine engine)
    {
        _engine = engine;
        _ps2PackageService =
            new Ps2SavePackageService(engine);
        _libraryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlayStationSaveManager",
            "SaveLibrary");
        _filesRoot = Path.Combine(_libraryRoot, "Files");
        _ps1SavesRoot = Path.Combine(_libraryRoot, Ps1SavesFolderName);
        _ps2SavesRoot = Path.Combine(_libraryRoot, Ps2SavesFolderName);
        _indexPath = Path.Combine(_libraryRoot, "game-saves.json");
        _legacyIndexPath = Path.Combine(_libraryRoot, "library.json");

        Directory.CreateDirectory(_ps1SavesRoot);
        Directory.CreateDirectory(_ps2SavesRoot);

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

    public string GetStoredPath(SaveLibraryEntry entry)
    {
        var organizedPath =
            Path.Combine(
                _libraryRoot,
                entry.StoredFileName);

        if (File.Exists(organizedPath) ||
            entry.StoredFileName.Contains(
                Path.DirectorySeparatorChar) ||
            entry.StoredFileName.Contains(
                Path.AltDirectorySeparatorChar))
        {
            return organizedPath;
        }

        // Compatibility with libraries created before PS1/PS2 folders.
        var legacyPath =
            Path.Combine(
                _filesRoot,
                entry.StoredFileName);

        return File.Exists(legacyPath)
            ? legacyPath
            : organizedPath;
    }

    public async Task<SaveLibraryImportResult> ImportAsync(
        string sourcePath,
        SaveLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "The save package does not exist.",
                sourcePath);
        }

        var sourceExtension =
            Path.GetExtension(sourcePath)
                .ToLowerInvariant();

        if (sourceExtension is not ".psu" and
            not ".max" and
            not ".cbs" and
            not ".xps" and
            not ".sps" and
            not ".psv" and
            not ".ps1save" and
            not ".ps2save")
        {
            throw new NotSupportedException(
                "The Save Library imports PS1SAVE, PS2SAVE, PSU, MAX, CBS, XPS, SPS, and PSV packages.");
        }

        string? temporaryPs2Package = null;

        try
        {
            var storedSourcePath =
                sourcePath;
            var storedExtension =
                sourceExtension;

            Ps1SavePackageManifest? ps1Metadata =
                null;
            Ps2SavePackageManifest? ps2Metadata =
                null;

            if (sourceExtension == ".ps1save")
            {
                ps1Metadata =
                    await Ps1MemoryCardService
                        .InspectSavePackageAsync(
                            sourcePath,
                            cancellationToken);
            }
            else
            {
                if (sourceExtension == ".ps2save")
                {
                    ps2Metadata =
                        await _ps2PackageService
                            .InspectAsync(
                                sourcePath,
                                cancellationToken);
                }
                else
                {
                    temporaryPs2Package =
                        Path.Combine(
                            Path.GetTempPath(),
                            "PSM-LIBRARY-PS2-" +
                            Guid.NewGuid().ToString("N") +
                            ".ps2save");

                    await _ps2PackageService
                        .CreateFromLegacyPackageAsync(
                            sourcePath,
                            temporaryPs2Package,
                            cancellationToken);

                    storedSourcePath =
                        temporaryPs2Package;
                    storedExtension =
                        ".ps2save";

                    ps2Metadata =
                        await _ps2PackageService
                            .InspectAsync(
                                temporaryPs2Package,
                                cancellationToken);
                }
            }

            var hash =
                await ComputeSha256Async(
                    storedSourcePath,
                    cancellationToken);

            var duplicate =
                index.Entries.FirstOrDefault(
                    entry =>
                        entry.Sha256.Equals(
                            hash,
                            StringComparison.OrdinalIgnoreCase));

            if (duplicate is not null)
            {
                return new SaveLibraryImportResult(
                    duplicate,
                    duplicate);
            }

            var description =
                storedExtension == ".ps1save"
                    ? (!string.IsNullOrWhiteSpace(
                            ps1Metadata!.SaveTitle)
                        ? ps1Metadata.SaveTitle
                        : ps1Metadata.OriginalFileName)
                    : ps2Metadata!.SaveTitle;

            var directoryId =
                storedExtension == ".ps1save"
                    ? ps1Metadata!.ProductCode
                    : ps2Metadata!.DirectoryId;

            var categoryName =
                storedExtension == ".ps1save"
                    ? Ps1SavesFolderName
                    : Ps2SavesFolderName;

            var categoryRoot =
                storedExtension == ".ps1save"
                    ? _ps1SavesRoot
                    : _ps2SavesRoot;

            Directory.CreateDirectory(
                categoryRoot);

            var storedBaseName =
                CreateAvailableStoredFileName(
                    BuildFriendlySaveFileName(
                        directoryId,
                        description,
                        storedExtension),
                    categoryRoot);

            var storedFileName =
                Path.Combine(
                    categoryName,
                    storedBaseName);

            var destination =
                Path.Combine(
                    categoryRoot,
                    storedBaseName);

            File.Copy(
                storedSourcePath,
                destination,
                true);

            var originalFile =
                new FileInfo(sourcePath);
            var storedFile =
                new FileInfo(storedSourcePath);

            var entry =
                new SaveLibraryEntry
                {
                    Id =
                        Guid.NewGuid().ToString("N"),
                    StoredFileName =
                        storedFileName,
                    OriginalFileName =
                        originalFile.Name,
                    OriginalPath =
                        sourcePath,
                    Extension =
                        storedExtension,
                    FormatName =
                        FormatName(storedExtension),
                    ImportedFrom =
                        originalFile.Name,
                    Platform =
                        storedExtension == ".ps1save"
                            ? "PlayStation"
                            : "PlayStation 2",
                    DirectoryId =
                        directoryId,
                    GameTitle =
                        storedExtension == ".ps1save"
                            ? ps1Metadata!.Title
                            : ps2Metadata!.GameTitle,
                    ProfileName =
                        description,
                    SizeBytes =
                        storedFile.Length,
                    Sha256 =
                        hash,
                    AddedUtc =
                        DateTime.UtcNow,
                    ModifiedUtc =
                        originalFile.LastWriteTimeUtc
                };

            index.Entries.Add(
                entry);

            await SaveAsync(
                index,
                cancellationToken);

            return new SaveLibraryImportResult(
                entry,
                null);
        }
        finally
        {
            if (temporaryPs2Package is not null)
            {
                try
                {
                    File.Delete(
                        temporaryPs2Package);
                }
                catch { }
            }
        }
    }

    private bool MigrateStoredFileNames(SaveLibraryIndex index)
    {
        var changed = false;

        Directory.CreateDirectory(_ps1SavesRoot);
        Directory.CreateDirectory(_ps2SavesRoot);

        foreach (var entry in index.Entries)
        {
            var categoryName =
                entry.Extension.Equals(
                    ".ps1save",
                    StringComparison.OrdinalIgnoreCase)
                    ? Ps1SavesFolderName
                    : Ps2SavesFolderName;
            var categoryRoot =
                entry.Extension.Equals(
                    ".ps1save",
                    StringComparison.OrdinalIgnoreCase)
                    ? _ps1SavesRoot
                    : _ps2SavesRoot;

            var currentPath = GetStoredPath(entry);
            if (!File.Exists(currentPath))
                continue;

            if (entry.IsUserRenamed ||
                LooksLikeUserRenamedSave(
                    entry,
                    currentPath))
            {
                if (!entry.IsUserRenamed)
                {
                    entry.IsUserRenamed = true;
                    changed = true;
                }

                continue;
            }

            var desiredFileName =
                BuildFriendlySaveFileName(
                    entry.DirectoryId,
                    entry.ProfileName,
                    entry.Extension);

            var desiredName =
                CreateAvailableStoredFileName(
                    desiredFileName,
                    categoryRoot,
                    currentPath);

            var newPath =
                Path.Combine(
                    categoryRoot,
                    desiredName);

            if (!currentPath.Equals(
                    newPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(categoryRoot);
                File.Move(currentPath, newPath);
            }

            var newStoredFileName =
                Path.Combine(
                    categoryName,
                    desiredName);

            if (!entry.StoredFileName.Equals(
                    newStoredFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                entry.StoredFileName = newStoredFileName;
                changed = true;
            }
        }

        TryDeleteEmptyDirectory(_filesRoot);
        return changed;
    }

    private static bool LooksLikeUserRenamedSave(
        SaveLibraryEntry entry,
        string currentPath)
    {
        var canonicalBaseName =
            Path.GetFileNameWithoutExtension(
                BuildFriendlySaveFileName(
                    entry.DirectoryId,
                    entry.ProfileName,
                    entry.Extension));

        var currentBaseName =
            Path.GetFileNameWithoutExtension(
                currentPath);

        if (string.IsNullOrWhiteSpace(canonicalBaseName) ||
            string.IsNullOrWhiteSpace(currentBaseName))
        {
            return false;
        }

        // User-renamed saves use:
        // Canonical Original Name (Custom Library Name).ext
        return currentBaseName.StartsWith(
                   canonicalBaseName + " (",
                   StringComparison.CurrentCultureIgnoreCase) &&
               currentBaseName.EndsWith(
                   ")",
                   StringComparison.Ordinal);
    }

    private static string CreateAvailableStoredFileName(
        string desired,
        string destinationRoot,
        string? existingPath = null)
    {
        var stem = Path.GetFileNameWithoutExtension(desired);
        var extension = Path.GetExtension(desired);
        var candidate = desired;
        var number = 2;

        while (File.Exists(Path.Combine(destinationRoot, candidate)) &&
               !Path.Combine(destinationRoot, candidate).Equals(
                   existingPath,
                   StringComparison.OrdinalIgnoreCase))
        {
            candidate =
                $"{stem} ({number++}){extension}";
        }

        return candidate;
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Cosmetic cleanup only.
        }
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

    public async Task RenameAsync(
        SaveLibraryEntry entry,
        SaveLibraryIndex index,
        string requestedDisplayName,
        CancellationToken cancellationToken = default)
    {
        var displayName =
            SanitizeFileName(
                requestedDisplayName.Trim());

        if (!string.IsNullOrWhiteSpace(entry.Extension) &&
            displayName.EndsWith(
                entry.Extension,
                StringComparison.OrdinalIgnoreCase))
        {
            displayName =
                Path.GetFileNameWithoutExtension(displayName);
        }

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "Game Save";

        var oldPath = GetStoredPath(entry);
        if (!File.Exists(oldPath))
        {
            throw new FileNotFoundException(
                "The stored library save is missing.",
                oldPath);
        }

        var isPs1 =
            entry.Extension.Equals(
                ".ps1save",
                StringComparison.OrdinalIgnoreCase) ||
            entry.Platform.Equals(
                "PlayStation",
                StringComparison.OrdinalIgnoreCase);

        var categoryName =
            isPs1
                ? Ps1SavesFolderName
                : Ps2SavesFolderName;
        var categoryRoot =
            isPs1
                ? _ps1SavesRoot
                : _ps2SavesRoot;

        Directory.CreateDirectory(categoryRoot);

        // Rebuild the canonical archive filename from the save identity.
        // Direct-from-card imports can have a temporary OriginalFileName that
        // contains PSM's generated GUID; that internal name must never leak
        // into a user-facing Library rename.
        var canonicalBaseName =
            Path.GetFileNameWithoutExtension(
                BuildFriendlySaveFileName(
                    entry.DirectoryId,
                    entry.ProfileName,
                    entry.Extension));

        if (string.IsNullOrWhiteSpace(canonicalBaseName))
            canonicalBaseName = "Game Save";

        var desiredName =
            $"{canonicalBaseName} ({displayName}){entry.Extension}";

        if (displayName.Equals(
                canonicalBaseName,
                StringComparison.CurrentCultureIgnoreCase))
        {
            desiredName =
                canonicalBaseName + entry.Extension;
        }

        var newLeafName =
            CreateAvailableStoredFileName(
                desiredName,
                categoryRoot,
                oldPath);
        var newPath =
            Path.Combine(
                categoryRoot,
                newLeafName);

        if (!oldPath.Equals(
                newPath,
                StringComparison.OrdinalIgnoreCase))
        {
            File.Move(oldPath, newPath);
        }

        if (string.IsNullOrWhiteSpace(entry.OriginalDisplayTitle))
            entry.OriginalDisplayTitle = entry.GameTitle;

        entry.GameTitle = displayName;
        entry.StoredFileName =
            Path.Combine(
                categoryName,
                newLeafName);
        entry.IsUserRenamed = true;
        entry.ModifiedUtc = DateTime.UtcNow;

        await SaveAsync(index, cancellationToken);
    }

    public async Task ResetNameAsync(
        SaveLibraryEntry entry,
        SaveLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        var oldPath = GetStoredPath(entry);
        if (!File.Exists(oldPath))
            throw new FileNotFoundException("The stored library save is missing.", oldPath);

        var isPs1 =
            entry.Extension.Equals(".ps1save", StringComparison.OrdinalIgnoreCase) ||
            entry.Platform.Equals("PlayStation", StringComparison.OrdinalIgnoreCase);

        var categoryName = isPs1 ? Ps1SavesFolderName : Ps2SavesFolderName;
        var categoryRoot = isPs1 ? _ps1SavesRoot : _ps2SavesRoot;
        Directory.CreateDirectory(categoryRoot);

        var desiredFileName =
            BuildFriendlySaveFileName(
                entry.DirectoryId,
                entry.ProfileName,
                entry.Extension);

        var newLeafName =
            CreateAvailableStoredFileName(
                desiredFileName,
                categoryRoot,
                oldPath);
        var newPath = Path.Combine(categoryRoot, newLeafName);

        if (!oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
            File.Move(oldPath, newPath);

        var originalTitle = entry.OriginalDisplayTitle;
        if (string.IsNullOrWhiteSpace(originalTitle))
        {
            try
            {
                if (entry.Extension.Equals(".ps1save", StringComparison.OrdinalIgnoreCase))
                {
                    var manifest =
                        await Ps1MemoryCardService.InspectSavePackageAsync(
                            newPath,
                            cancellationToken);
                    originalTitle = manifest.Title;
                }
                else
                {
                    var metadata =
                        await InspectPackageAsync(
                            newPath,
                            cancellationToken);
                    originalTitle = metadata.GameTitle;
                }
            }
            catch
            {
                // Legacy entry: filename can still be reset even if the old
                // display title cannot be reconstructed.
            }
        }

        if (!string.IsNullOrWhiteSpace(originalTitle))
            entry.GameTitle = originalTitle;

        entry.StoredFileName = Path.Combine(categoryName, newLeafName);
        entry.IsUserRenamed = false;
        entry.OriginalDisplayTitle = string.Empty;
        entry.ModifiedUtc = DateTime.UtcNow;

        await SaveAsync(index, cancellationToken);
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
            ".ps2save" => "PSM PlayStation Save Package",
            _ => extension.TrimStart('.').ToUpperInvariant()
        };
}
