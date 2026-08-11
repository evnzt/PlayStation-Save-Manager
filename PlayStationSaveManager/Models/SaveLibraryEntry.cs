using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace PlayStationSaveManager.Models;

public sealed class SaveLibraryEntry : INotifyPropertyChanged
{
    private bool _isFavorite;
    private BitmapSource? _iconImage;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string FormatName { get; set; } = string.Empty;
    public string ImportedFrom { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string DirectoryId { get; set; } = string.Empty;
    public string GameTitle { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    [JsonIgnore]
    public BitmapSource? IconImage
    {
        get => _iconImage;
        set
        {
            if (ReferenceEquals(_iconImage, value)) return;
            _iconImage = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    [JsonIgnore]
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(GameTitle) ? OriginalFileName : GameTitle;

    [JsonIgnore]
    public string DisplaySubtitle
    {
        get
        {
            var subtitle =
                string.IsNullOrWhiteSpace(ProfileName)
                    ? FormatName
                    : ProfileName;

            var platform =
                string.IsNullOrWhiteSpace(Platform)
                    ? Extension.Equals(
                        ".ps1save",
                        StringComparison.OrdinalIgnoreCase)
                        ? "PlayStation"
                        : "PlayStation 2"
                    : Platform;

            return $"{platform} • {subtitle}";
        }
    }

    [JsonIgnore]
    public string SizeDisplay =>
        SizeBytes >= 1024 * 1024
            ? $"{SizeBytes / 1024d / 1024d:N2} MB"
            : SizeBytes >= 1024
                ? $"{SizeBytes / 1024d:N1} KB"
                : $"{SizeBytes:N0} bytes";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
