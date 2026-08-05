namespace PlayStationSaveManager.Services;

public sealed record EngineResult(int ExitCode, string Output, string Error)
{
    public string Combined => string.Join(Environment.NewLine,
        new[] { Output, Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
