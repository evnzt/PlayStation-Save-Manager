using System.Collections.Generic;

namespace PlayStationSaveManager.Models;

public sealed class SaveLibraryIndex
{
    public List<SaveLibraryEntry> Entries { get; set; } = [];
}
