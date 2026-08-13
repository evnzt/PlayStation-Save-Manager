using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PlayStationSaveManager.Models;

namespace PlayStationSaveManager.Services;

public sealed record MemoryCardStoreResult(
    MemoryCardLibraryEntry Entry,
    MemoryCardLibraryEntry? Duplicate);

public sealed class MemoryCardLibraryService
{
    private readonly string _root;
    private readonly string _cardsRoot;
    private readonly string _ps1CardsRoot;
    private readonly string _ps2CardsRoot;
    private const string Ps1CardsFolderName = "PS1 Memory Cards";
    private const string Ps2CardsFolderName = "PS2 Memory Cards";
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public MemoryCardLibraryService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlayStationSaveManager", "SaveLibrary");
        _cardsRoot = Path.Combine(_root, "MemoryCards");
        _ps1CardsRoot = Path.Combine(_root, Ps1CardsFolderName);
        _ps2CardsRoot = Path.Combine(_root, Ps2CardsFolderName);
        _indexPath = Path.Combine(_root, "memory-cards.json");
        Directory.CreateDirectory(_ps1CardsRoot);
        Directory.CreateDirectory(_ps2CardsRoot);
    }

    public async Task<MemoryCardLibraryIndex> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_indexPath)) return new MemoryCardLibraryIndex();
        try
        {
            MemoryCardLibraryIndex index;
            await using (var stream = File.OpenRead(_indexPath))
            {
                index = await JsonSerializer.DeserializeAsync<MemoryCardLibraryIndex>(
                    stream, _options, cancellationToken) ?? new MemoryCardLibraryIndex();
            }

            if (MigrateStoredNames(index))
                await SaveAsync(index, cancellationToken);

            return index;
        }
        catch
        {
            try { File.Copy(_indexPath, _indexPath + ".recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); }
            catch { }
            return new MemoryCardLibraryIndex();
        }
    }

    public async Task<MemoryCardStoreResult> StoreAsync(
        string sourcePath, string platform, string cardType,
        int saveCount, long? capacityBytes,
        string? displayNameOverride = null,
        string? originalPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        var folder = Directory.Exists(sourcePath);
        if (!folder && !File.Exists(sourcePath))
            throw new FileNotFoundException("The memory card was not found.", sourcePath);

        var index = await LoadAsync(cancellationToken);
        var hash = folder
            ? await ComputeFolderHashAsync(sourcePath, cancellationToken)
            : await ComputeFileHashAsync(sourcePath, cancellationToken);

        var duplicate = index.Entries.FirstOrDefault(
            entry => entry.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            return new MemoryCardStoreResult(duplicate, duplicate);

        var id = Guid.NewGuid().ToString("N");
        var sourceName = Path.GetFileName(sourcePath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var extension = folder ? string.Empty : Path.GetExtension(sourcePath).ToLowerInvariant();
        var displayName = string.IsNullOrWhiteSpace(displayNameOverride)
            ? sourceName
            : SanitizeFileName(displayNameOverride.Trim());
        if (!folder && displayName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            displayName = Path.GetFileNameWithoutExtension(displayName);
        var isPs1 =
            platform.Contains(
                "PlayStation",
                StringComparison.OrdinalIgnoreCase) &&
            !platform.Contains(
                "2",
                StringComparison.OrdinalIgnoreCase);

        var categoryName =
            isPs1
                ? Ps1CardsFolderName
                : Ps2CardsFolderName;
        var categoryRoot =
            isPs1
                ? _ps1CardsRoot
                : _ps2CardsRoot;

        Directory.CreateDirectory(categoryRoot);

        var storedBaseName = folder
            ? displayName
            : displayName + extension;
        var storedLeafName =
            CreateAvailableStoredName(
                storedBaseName,
                folder,
                categoryRoot);
        var storedName =
            Path.Combine(
                categoryName,
                storedLeafName);
        var destination =
            Path.Combine(
                categoryRoot,
                storedLeafName);

        if (folder) CopyDirectory(sourcePath, destination);
        else File.Copy(sourcePath, destination, true);

        var size = folder
            ? Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length)
            : new FileInfo(sourcePath).Length;

        var entry = new MemoryCardLibraryEntry
        {
            Id=id, DisplayName=displayName, StoredName=storedName,
            OriginalPath=string.IsNullOrWhiteSpace(originalPathOverride)
                ? sourcePath
                : originalPathOverride,
            OriginalDisplayName=displayName,
            Platform=platform, CardType=cardType,
            Extension=extension, IsFolderCard=folder, SizeBytes=size,
            CapacityBytes=capacityBytes, SaveCount=saveCount, Sha256=hash,
            AddedUtc=DateTime.UtcNow,
            ModifiedUtc=
                !string.IsNullOrWhiteSpace(originalPathOverride) &&
                File.Exists(originalPathOverride)
                    ? File.GetLastWriteTimeUtc(originalPathOverride)
                    : folder
                        ? Directory.GetLastWriteTimeUtc(sourcePath)
                        : File.GetLastWriteTimeUtc(sourcePath)
        };
        index.Entries.Add(entry);
        await SaveAsync(index, cancellationToken);
        return new MemoryCardStoreResult(entry, null);
    }

    private bool MigrateStoredNames(MemoryCardLibraryIndex index)
    {
        var changed = false;

        Directory.CreateDirectory(_ps1CardsRoot);
        Directory.CreateDirectory(_ps2CardsRoot);

        foreach (var entry in index.Entries)
        {
            var currentPath = GetStoredPath(entry);
            var exists = entry.IsFolderCard
                ? Directory.Exists(currentPath)
                : File.Exists(currentPath);

            if (!exists)
                continue;

            var isPs1 =
                IsPs1Entry(entry);
            var categoryName =
                isPs1
                    ? Ps1CardsFolderName
                    : Ps2CardsFolderName;
            var categoryRoot =
                isPs1
                    ? _ps1CardsRoot
                    : _ps2CardsRoot;

            var desiredDisplay =
                SanitizeFileName(
                    GetExtensionFreeDisplayName(entry));
            var desired =
                entry.IsFolderCard
                    ? desiredDisplay
                    : desiredDisplay + entry.Extension;

            var newLeafName =
                CreateAvailableStoredName(
                    desired,
                    entry.IsFolderCard,
                    categoryRoot,
                    currentPath);
            var newPath =
                Path.Combine(
                    categoryRoot,
                    newLeafName);

            if (!currentPath.Equals(
                    newPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(categoryRoot);

                if (entry.IsFolderCard)
                    Directory.Move(currentPath, newPath);
                else
                    File.Move(currentPath, newPath);
            }

            var newStoredName =
                Path.Combine(
                    categoryName,
                    newLeafName);

            if (!entry.StoredName.Equals(
                    newStoredName,
                    StringComparison.OrdinalIgnoreCase))
            {
                entry.StoredName = newStoredName;
                changed = true;
            }

            if (!entry.DisplayName.Equals(
                    desiredDisplay,
                    StringComparison.Ordinal))
            {
                entry.DisplayName = desiredDisplay;
                changed = true;
            }
        }

        TryDeleteEmptyDirectory(_cardsRoot);
        return changed;
    }

    private static bool IsPs1Entry(
        MemoryCardLibraryEntry entry) =>
        entry.Platform.Contains(
            "PlayStation",
            StringComparison.OrdinalIgnoreCase) &&
        !entry.Platform.Contains(
            "2",
            StringComparison.OrdinalIgnoreCase);

    private static string GetExtensionFreeDisplayName(
        MemoryCardLibraryEntry entry)
    {
        if (!entry.IsFolderCard &&
            !string.IsNullOrWhiteSpace(entry.Extension) &&
            entry.DisplayName.EndsWith(
                entry.Extension,
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(
                entry.DisplayName);
        }

        return entry.DisplayName;
    }

    private static string CreateAvailableStoredName(
        string requestedName,
        bool folder,
        string destinationRoot,
        string? existingPath = null)
    {
        var safe = SanitizeFileName(requestedName);
        var extension =
            folder
                ? string.Empty
                : Path.GetExtension(safe);
        var stem =
            folder
                ? safe
                : Path.GetFileNameWithoutExtension(safe);
        var candidate = safe;
        var number = 2;

        while (StoredPathExists(
                   destinationRoot,
                   candidate,
                   folder) &&
               !Path.Combine(
                    destinationRoot,
                    candidate).Equals(
                        existingPath,
                        StringComparison.OrdinalIgnoreCase))
        {
            candidate =
                $"{stem} ({number++}){extension}";
        }

        return candidate;
    }

    private static bool StoredPathExists(
        string root,
        string name,
        bool folder)
    {
        var path =
            Path.Combine(
                root,
                name);

        return folder
            ? Directory.Exists(path)
            : File.Exists(path);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        value = value.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(value)
            ? "Memory Card"
            : value;
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

    public string GetStoredPath(
        MemoryCardLibraryEntry entry)
    {
        var organizedPath =
            Path.Combine(
                _root,
                entry.StoredName);

        if ((entry.IsFolderCard &&
             Directory.Exists(organizedPath)) ||
            (!entry.IsFolderCard &&
             File.Exists(organizedPath)) ||
            entry.StoredName.Contains(
                Path.DirectorySeparatorChar) ||
            entry.StoredName.Contains(
                Path.AltDirectorySeparatorChar))
        {
            return organizedPath;
        }

        // Compatibility with libraries created before platform folders.
        var legacyPath =
            Path.Combine(
                _cardsRoot,
                entry.StoredName);

        return legacyPath;
    }

    public async Task RenameAsync(
        MemoryCardLibraryEntry entry,
        MemoryCardLibraryIndex index,
        string requestedDisplayName,
        CancellationToken cancellationToken = default)
    {
        var displayName =
            SanitizeFileName(
                requestedDisplayName.Trim());

        if (!entry.IsFolderCard &&
            !string.IsNullOrWhiteSpace(entry.Extension) &&
            displayName.EndsWith(
                entry.Extension,
                StringComparison.OrdinalIgnoreCase))
        {
            displayName =
                Path.GetFileNameWithoutExtension(
                    displayName);
        }

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "Memory Card";

        var oldPath =
            GetStoredPath(entry);

        var isPs1 =
            IsPs1Entry(entry);
        var categoryName =
            isPs1
                ? Ps1CardsFolderName
                : Ps2CardsFolderName;
        var categoryRoot =
            isPs1
                ? _ps1CardsRoot
                : _ps2CardsRoot;

        Directory.CreateDirectory(categoryRoot);

        var requestedStoredName =
            entry.IsFolderCard
                ? displayName
                : displayName + entry.Extension;

        var newLeafName =
            CreateAvailableStoredName(
                requestedStoredName,
                entry.IsFolderCard,
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
            if (entry.IsFolderCard)
                Directory.Move(oldPath, newPath);
            else
                File.Move(oldPath, newPath);
        }

        if (string.IsNullOrWhiteSpace(entry.OriginalDisplayName))
            entry.OriginalDisplayName = GetOriginalCardDisplayName(entry);

        entry.DisplayName = displayName;
        entry.StoredName =
            Path.Combine(
                categoryName,
                newLeafName);
        entry.IsUserRenamed = true;
        entry.ModifiedUtc = DateTime.UtcNow;

        await SaveAsync(index, cancellationToken);
    }

    private static string GetOriginalCardDisplayName(
        MemoryCardLibraryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.OriginalDisplayName))
            return entry.OriginalDisplayName;

        if (!string.IsNullOrWhiteSpace(entry.OriginalPath))
        {
            var trimmed = entry.OriginalPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var leaf = Path.GetFileName(trimmed);

            if (!string.IsNullOrWhiteSpace(leaf))
                return entry.IsFolderCard
                    ? leaf
                    : Path.GetFileNameWithoutExtension(leaf);
        }

        return GetExtensionFreeDisplayName(entry);
    }

    public async Task ResetNameAsync(
        MemoryCardLibraryEntry entry,
        MemoryCardLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        var oldPath = GetStoredPath(entry);
        var exists = entry.IsFolderCard
            ? Directory.Exists(oldPath)
            : File.Exists(oldPath);

        if (!exists)
            throw new FileNotFoundException("The stored memory card is missing.", oldPath);

        var displayName =
            SanitizeFileName(
                GetOriginalCardDisplayName(entry));

        var isPs1 = IsPs1Entry(entry);
        var categoryName = isPs1 ? Ps1CardsFolderName : Ps2CardsFolderName;
        var categoryRoot = isPs1 ? _ps1CardsRoot : _ps2CardsRoot;
        Directory.CreateDirectory(categoryRoot);

        var requestedStoredName =
            entry.IsFolderCard
                ? displayName
                : displayName + entry.Extension;

        var newLeafName =
            CreateAvailableStoredName(
                requestedStoredName,
                entry.IsFolderCard,
                categoryRoot,
                oldPath);
        var newPath = Path.Combine(categoryRoot, newLeafName);

        if (!oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
        {
            if (entry.IsFolderCard)
                Directory.Move(oldPath, newPath);
            else
                File.Move(oldPath, newPath);
        }

        entry.DisplayName = displayName;
        entry.StoredName = Path.Combine(categoryName, newLeafName);
        entry.IsUserRenamed = false;
        entry.OriginalDisplayName = displayName;
        entry.ModifiedUtc = DateTime.UtcNow;

        await SaveAsync(index, cancellationToken);
    }

    public async Task ToggleFavoriteAsync(
        MemoryCardLibraryEntry entry,
        MemoryCardLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        entry.IsFavorite = !entry.IsFavorite;
        await SaveAsync(index, cancellationToken);
    }

    public async Task RemoveAsync(
        MemoryCardLibraryEntry entry,
        MemoryCardLibraryIndex index,
        CancellationToken cancellationToken = default)
    {
        var storedPath =
            GetStoredPath(entry);

        if (entry.IsFolderCard)
        {
            if (Directory.Exists(storedPath))
                Directory.Delete(
                    storedPath,
                    recursive: true);
        }
        else if (File.Exists(storedPath))
        {
            File.Delete(storedPath);
        }

        index.Entries.Remove(entry);
        await SaveAsync(index, cancellationToken);
    }

    private async Task SaveAsync(MemoryCardLibraryIndex index, CancellationToken cancellationToken)
    {
        var temp=_indexPath+".tmp";
        await using (var stream=File.Create(temp))
            await JsonSerializer.SerializeAsync(stream,index,_options,cancellationToken);
        File.Move(temp,_indexPath,true);
    }

    private static async Task<string> ComputeFileHashAsync(string path,CancellationToken token)
    {
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
    }

    private static async Task<string> ComputeFolderHashAsync(string path,CancellationToken token)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var file in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories)
            .OrderBy(v=>v,StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(path,file).Replace('\\','/')));
            await using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
            var buffer=new byte[81920];
            int read;
            while((read=await stream.ReadAsync(buffer,token))>0) hash.AppendData(buffer,0,read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void CopyDirectory(string source,string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(var file in Directory.EnumerateFiles(source))
            File.Copy(file,Path.Combine(destination,Path.GetFileName(file)),true);
        foreach(var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir,Path.Combine(destination,Path.GetFileName(dir)));
    }
}
