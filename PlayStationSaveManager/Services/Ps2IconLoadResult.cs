namespace PlayStationSaveManager.Services;

public sealed record Ps2IconLoadResult(
    Ps2IconModel? Model,
    bool HasIconFiles,
    bool IsCorrupted)
{
    public static Ps2IconLoadResult Missing { get; } = new(null, false, false);
    public static Ps2IconLoadResult Corrupted { get; } = new(null, true, true);
    public static Ps2IconLoadResult Success(Ps2IconModel model) => new(model, true, false);
}
