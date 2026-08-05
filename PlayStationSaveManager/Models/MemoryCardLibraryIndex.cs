using System.Collections.Generic;
namespace PlayStationSaveManager.Models;
public sealed class MemoryCardLibraryIndex
{
    public List<MemoryCardLibraryEntry> Entries { get; set; } = [];
}
