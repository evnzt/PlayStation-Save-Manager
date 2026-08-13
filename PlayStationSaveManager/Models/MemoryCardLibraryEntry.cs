using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PlayStationSaveManager.Models;

public sealed class MemoryCardLibraryEntry : INotifyPropertyChanged
{
    private bool _isFavorite;
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

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
                return;

            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    [JsonIgnore]
    public string FavoriteGlyph =>
        IsFavorite ? "★" : "☆";

    public string Sha256 { get; set; } = string.Empty;
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public bool IsUserRenamed { get; set; }
    public string OriginalDisplayName { get; set; } = string.Empty;

    [JsonIgnore] public string SizeDisplay =>
        IsFolderCard ? "Infinite capacity" :
        CapacityBytes is > 0 ? FormatBytes(CapacityBytes.Value) : FormatBytes(SizeBytes);
    [JsonIgnore] public string StoredSizeDisplay => FormatBytes(SizeBytes);
    [JsonIgnore] public string SaveCountDisplay => $"{SaveCount} {(SaveCount == 1 ? "save" : "saves")}";

    [JsonIgnore]
    public string CardTypeDisplay
    {
        get
        {
            if (IsFolderCard ||
                Extension.Equals(
                    ".foldercard",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "PCSX2 Folder Card (*.foldercard)";
            }

            var isPs1 =
                Platform.Contains(
                    "PlayStation",
                    StringComparison.OrdinalIgnoreCase) &&
                !Platform.Contains(
                    "2",
                    StringComparison.OrdinalIgnoreCase);

            if (isPs1)
            {
                return Extension.ToLowerInvariant() switch
                {
                    ".bin" => "pSX / AdriPSX Memory Card (*.bin)",
                    ".ddf" => "DataDeck Memory Card (*.ddf)",
                    ".gme" => "DexDrive Memory Card (*.gme)",
                    ".mc" => "PSXGame Edit Memory Card (*.mc)",
                    ".mcd" => "Bleem! Memory Card (*.mcd)",
                    ".mci" => "MCExplorer Memory Card (*.mci)",
                    ".mcr" => "ePSXe / PSEmu Pro Memory Card (*.mcr)",
                    ".mem" => "VGS / Connectix Memory Card (*.mem)",
                    ".ps" => "WinPSM Memory Card (*.ps)",
                    ".psm" => "Smart Link Memory Card (*.psm)",
                    ".sav" => "SAV Memory Card (*.sav)",
                    ".srm" => "RetroArch / Libretro Memory Card (*.srm)",
                    ".vgs" => "VGS / Connectix Memory Card (*.vgs)",
                    ".vm1" => "PS3 Virtual Memory Card (*.vm1)",
                    ".vmc" => "Virtual Memory Card (*.vmc)",
                    ".vmp" => "PSP Virtual Memory Card (*.vmp)",
                    _ => string.IsNullOrWhiteSpace(CardType)
                        ? Extension.TrimStart('.').ToUpperInvariant()
                        : CardType
                };
            }

            return Extension.ToLowerInvariant() switch
            {
                ".bin" => "PS2 BIN Memory Card (*.bin)",
                ".mc2" => "MemCard PRO2 Memory Card (*.mc2)",
                ".mcd" => "PS2 MCD Memory Card (*.mcd)",
                ".ps2" => "PCSX2 Memory Card (*.ps2)",
                ".vm2" => "PS2 Virtual Memory Card (*.vm2)",
                ".vmc" => "PS2 VMC Memory Card (*.vmc)",
                _ => string.IsNullOrWhiteSpace(CardType)
                    ? Extension.TrimStart('.').ToUpperInvariant()
                    : CardType
            };
        }
    }

    [JsonIgnore] public string DisplaySubtitle => $"{Platform} • {CardTypeDisplay}";

    [JsonIgnore]
    public string LibraryIconPath =>
        Path.Combine(
            AppContext.BaseDirectory,
            (IsFolderCard
                ? "Assets/MemoryCardLibrary/folder-card.png"
                : Platform.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) &&
                  !Platform.Contains("2", StringComparison.OrdinalIgnoreCase)
                    ? "Assets/MemoryCardLibrary/memory-card-ps1.png"
                    : "Assets/MemoryCardLibrary/memory-card.png")
            .Replace(
                '/',
                Path.DirectorySeparatorChar));


    private static string FormatBytes(long value) =>
        value >= 1024 * 1024 ? $"{value / 1024d / 1024d:N2} MB" :
        value >= 1024 ? $"{value / 1024d:N1} KB" : $"{value:N0} bytes";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
