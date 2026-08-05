using System;

namespace PlayStationSaveManager.Services;

public sealed class Ps2IconRenderSettings
{
    public static Ps2IconRenderSettings Default { get; } = new();

    public float AmbientR { get; init; } = 0.55f;
    public float AmbientG { get; init; } = 0.55f;
    public float AmbientB { get; init; } = 0.55f;

    public Ps2IconLight[] Lights { get; init; } =
    {
        new(0.0f, -0.6f, -1.0f, 0.45f, 0.45f, 0.45f),
        new(0.7f, -0.3f, -0.4f, 0.20f, 0.20f, 0.20f),
        new(-0.7f, -0.3f, -0.4f, 0.15f, 0.15f, 0.15f)
    };
}

public readonly record struct Ps2IconLight(
    float X,
    float Y,
    float Z,
    float R,
    float G,
    float B);
