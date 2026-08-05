using System.Collections.Generic;

namespace PlayStationSaveManager.Models;

public sealed record Ps1CardReadResult(
    string Path,
    IReadOnlyList<Ps1SaveEntry> Saves,
    int UsedBlocks,
    int FreeBlocks,
    bool IsValid,
    string FormatName);
