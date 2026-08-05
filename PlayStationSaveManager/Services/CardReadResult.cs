using System.Collections.Generic;
using PlayStationSaveManager.Models;

namespace PlayStationSaveManager.Services;

public sealed record CardReadResult(
    IReadOnlyList<SaveEntry> Saves,
    long? TotalBytes,
    long? FreeBytes)
{
    public long? UsedBytes =>
        TotalBytes.HasValue && FreeBytes.HasValue
            ? System.Math.Max(0, TotalBytes.Value - FreeBytes.Value)
            : null;

    public double? UsedPercent =>
        TotalBytes is > 0 && UsedBytes.HasValue
            ? System.Math.Clamp(UsedBytes.Value * 100d / TotalBytes.Value, 0d, 100d)
            : null;
}
