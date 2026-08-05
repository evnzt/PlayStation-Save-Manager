using System;
using System.Collections.Generic;

namespace PlayStationSaveManager.Models;

public sealed class Ps1SavePackageManifest
{
    public int PackageVersion { get; set; } = 1;
    public string Platform { get; set; } = "PlayStation";
    public string Title { get; set; } = string.Empty;
    public string SaveTitle { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public int StartingBlock { get; set; }
    public int BlocksUsed { get; set; }
    public int FileSize { get; set; }
    public List<int> BlockChain { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
