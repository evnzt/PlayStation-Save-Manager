using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace PlayStationSaveManager.Models;

public sealed class Ps1SaveEntry
{
    public string Title { get; set; } = string.Empty;
    public string SaveTitle { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int StartingBlock { get; init; }
    public int BlocksUsed { get; init; }
    public int FileSize { get; init; }
    public string FileName { get; init; } = string.Empty;
    public bool IsDeleted { get; init; }
    public IReadOnlyList<int> BlockChain { get; init; } = [];
    public BitmapSource? IconImage { get; init; }

    public string BlocksDisplay =>
        BlocksUsed == 1 ? "1 block" : $"{BlocksUsed} blocks";

    public string FileSizeDisplay =>
        FileSize >= 1024
            ? $"{FileSize / 1024d:N1} KB"
            : $"{FileSize:N0} bytes";
}
