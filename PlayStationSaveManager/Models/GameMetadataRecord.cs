using System;

namespace PlayStationSaveManager.Models;

public sealed class GameMetadataRecord
{
    public string Serial { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string ReleaseDate { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Verification { get; set; } = "Imported";

    public bool HasUsefulData =>
        !string.IsNullOrWhiteSpace(Title) ||
        !string.IsNullOrWhiteSpace(Region) ||
        !string.IsNullOrWhiteSpace(ReleaseDate) ||
        !string.IsNullOrWhiteSpace(Developer) ||
        !string.IsNullOrWhiteSpace(Publisher);
}

public sealed record GameDatabaseStatus(
    bool GameDbAvailable,
    int GameDbEntries,
    bool LaunchBoxAvailable,
    int LaunchBoxEntries,
    string GameDbPath,
    string LaunchBoxPath);
