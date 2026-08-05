using System;
using System.IO;
using System.Text.Json.Serialization;

namespace PlayStationSaveManager.Models;

public sealed class MemoryCardLibraryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public string StoredName { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public bool IsFolderCard { get; set; }
    public long SizeBytes { get; set; }
    public long? CapacityBytes { get; set; }
    public int SaveCount { get; set; }
    public bool IsFavorite { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore] public string SizeDisplay =>
        IsFolderCard ? "Infinite capacity" :
        CapacityBytes is > 0 ? FormatBytes(CapacityBytes.Value) : FormatBytes(SizeBytes);
    [JsonIgnore] public string StoredSizeDisplay => FormatBytes(SizeBytes);
    [JsonIgnore] public string SaveCountDisplay => $"{SaveCount} {(SaveCount == 1 ? "save" : "saves")}";
    [JsonIgnore] public string DisplaySubtitle => $"{Platform} • {CardType}";

    [JsonIgnore]
    public string LibraryIconPath =>
        Path.Combine(
            AppContext.BaseDirectory,
            (IsFolderCard
                ? "Assets/MemoryCardLibrary/folder-card.png"
                : "Assets/MemoryCardLibrary/memory-card.png")
            .Replace(
                '/',
                Path.DirectorySeparatorChar));


    private static string FormatBytes(long value) =>
        value >= 1024 * 1024 ? $"{value / 1024d / 1024d:N2} MB" :
        value >= 1024 ? $"{value / 1024d:N1} KB" : $"{value:N0} bytes";
}
