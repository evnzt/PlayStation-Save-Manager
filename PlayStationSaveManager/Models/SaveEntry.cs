using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using PlayStationSaveManager.Services;

namespace PlayStationSaveManager.Models;

public sealed class SaveEntry : INotifyPropertyChanged
{
    private ImageSource? _iconImage;

    public required string Title { get; init; }
    public required string GameTitle { get; init; }
    public required string DirectoryId { get; init; }
    public required long SizeBytes { get; init; }
    public string SizeText => SizeBytes <= 0 ? "Unknown" : $"{SizeBytes / 1024d:N0} KB";
    public string Subtitle { get; init; } = string.Empty;
    public string ProfileName => string.IsNullOrWhiteSpace(Subtitle) ? "Save data" : Subtitle;

    public ImageSource? IconImage
    {
        get => _iconImage;
        set
        {
            if (ReferenceEquals(_iconImage, value)) return;
            _iconImage = value;
            OnPropertyChanged();
        }
    }

    public Ps2IconModel? IconModel { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
