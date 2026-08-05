namespace PlayStationSaveManager.Models;

public sealed record UniversalFormatOption(
    string Extension,
    string DisplayName,
    bool CanRead,
    bool CanWrite)
{
    public string Label => $"{DisplayName} (*{Extension})";
    public override string ToString() => Label;
}
