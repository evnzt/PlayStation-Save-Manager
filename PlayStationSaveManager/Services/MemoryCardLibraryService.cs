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
    private readonly string _indexPath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public MemoryCardLibraryService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlayStationSaveManager", "SaveLibrary");
        _cardsRoot = Path.Combine(_root, "MemoryCards");
        _indexPath = Path.Combine(_root, "memory-cards.json");
        Directory.CreateDirectory(_cardsRoot);
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
        var displayName = Path.GetFileName(sourcePath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var extension = folder ? string.Empty : Path.GetExtension(sourcePath).ToLowerInvariant();
        var storedName = CreateAvailableStoredName(displayName, folder);
        var destination = Path.Combine(_cardsRoot, storedName);

        if (folder) CopyDirectory(sourcePath, destination);
        else File.Copy(sourcePath, destination, true);

        var size = folder
            ? Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length)
            : new FileInfo(sourcePath).Length;

        var entry = new MemoryCardLibraryEntry
        {
            Id=id, DisplayName=displayName, StoredName=storedName,
            OriginalPath=sourcePath, Platform=platform, CardType=cardType,
            Extension=extension, IsFolderCard=folder, SizeBytes=size,
            CapacityBytes=capacityBytes, SaveCount=saveCount, Sha256=hash,
            AddedUtc=DateTime.UtcNow,
            ModifiedUtc=folder ? Directory.GetLastWriteTimeUtc(sourcePath) : File.GetLastWriteTimeUtc(sourcePath)
        };
        index.Entries.Add(entry);
        await SaveAsync(index, cancellationToken);
        return new MemoryCardStoreResult(entry, null);
    }

    private bool MigrateStoredNames(MemoryCardLibraryIndex index)
    {
        var changed = false;

        foreach (var entry in index.Entries)
        {
            var oldPath = Path.Combine(_cardsRoot, entry.StoredName);
            var exists = entry.IsFolderCard
                ? Directory.Exists(oldPath)
                : File.Exists(oldPath);

            if (!exists)
                continue;

            var desired = SanitizeFileName(entry.DisplayName);
            if (entry.StoredName.Equals(desired, StringComparison.OrdinalIgnoreCase))
                continue;

            var newName = CreateAvailableStoredName(
                desired,
                entry.IsFolderCard,
                oldPath);
            var newPath = Path.Combine(_cardsRoot, newName);

            if (!oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
            {
                if (entry.IsFolderCard)
                    Directory.Move(oldPath, newPath);
                else
                    File.Move(oldPath, newPath);
            }

            entry.StoredName = newName;
            changed = true;
        }

        return changed;
    }

    private string CreateAvailableStoredName(
        string requestedName,
        bool folder,
        string? existingPath = null)
    {
        var safe = SanitizeFileName(requestedName);
        var extension = folder ? string.Empty : Path.GetExtension(safe);
        var stem = folder ? safe : Path.GetFileNameWithoutExtension(safe);
        var candidate = safe;
        var number = 2;

        while (StoredPathExists(candidate, folder) &&
               !Path.Combine(_cardsRoot, candidate).Equals(
                   existingPath,
                   StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"{stem} ({number++}){extension}";
        }

        return candidate;
    }

    private bool StoredPathExists(string name, bool folder)
    {
        var path = Path.Combine(_cardsRoot, name);
        return folder ? Directory.Exists(path) : File.Exists(path);
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
            Path.Combine(
                _cardsRoot,
                entry.StoredName);

        if (entry.IsFolderCard)
        {
            if (Directory.Exists(storedPath))
                Directory.Delete(storedPath, recursive: true);
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
