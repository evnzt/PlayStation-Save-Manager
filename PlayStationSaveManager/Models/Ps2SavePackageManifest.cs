using System;

namespace PlayStationSaveManager.Models;

public sealed class Ps2SavePackageManifest
{
    public int PackageVersion { get; set; } = 1;
    public string Platform { get; set; } = "PlayStation 2";
    public string GameTitle { get; set; } = string.Empty;
    public string SaveTitle { get; set; } = string.Empty;
    public string DirectoryId { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string PayloadFormat { get; set; } = "PSU";
    public string OriginalFileName { get; set; } = string.Empty;
    public string OriginalFormat { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UnixEpoch;
}
