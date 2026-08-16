namespace PlayStationSaveManager.Models;

public sealed record UniversalFormatOption(
    string Extension,
    string DisplayName,
    bool CanRead,
    bool CanWrite,
    bool IsPs1SingleSaveGme = false)
{
    public string Label => $"{DisplayName} (*{Extension})";
    public override string ToString() => Label;
}
