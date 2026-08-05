namespace PlayStationSaveManager.Models;

public sealed record Ps1GameDatabaseStatus(
    bool GameDbAvailable,
    int GameDbEntries,
    bool LaunchBoxAvailable,
    int LaunchBoxEntries,
    string GameDbPath,
    string LaunchBoxPath);
