using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayStationSaveManager.Services;

public sealed class Ps2IconService
{
    private readonly MyMcEngine _engine;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, Ps2IconLoadResult> _models = new(StringComparer.OrdinalIgnoreCase);

    public Ps2IconService(MyMcEngine engine, string applicationDirectory)
    {
        _engine = engine;
        _cacheDirectory = Path.Combine(applicationDirectory, "Cache", "SaveIcons");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<Ps2IconModel?> LoadAsync(
        string cardPath, string directoryId, CancellationToken cancellationToken = default) =>
        (await LoadResultAsync(cardPath, directoryId, cancellationToken)).Model;

    public async Task<Ps2IconLoadResult> LoadResultAsync(
        string cardPath, string directoryId, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(cardPath, directoryId);
        if (_models.TryGetValue(key, out var cached))
            return cached;

        var itemDirectory = Path.Combine(_cacheDirectory, key);
        Directory.CreateDirectory(itemDirectory);
        var iconSysPath = Path.Combine(itemDirectory, "icon.sys");

        try
        {
            if (!File.Exists(iconSysPath))
                await _engine.ExtractFileAsync(cardPath, directoryId, "icon.sys", iconSysPath, cancellationToken);
        }
        catch
        {
            return Remember(key, Ps2IconLoadResult.Missing);
        }

        try
        {
            var iconSys = await File.ReadAllBytesAsync(iconSysPath, cancellationToken);
            if (iconSys.Length != 964 || Encoding.ASCII.GetString(iconSys, 0, 4) != "PS2D")
                return Remember(key, Ps2IconLoadResult.Corrupted);

            var normalIconName = ReadAsciiName(iconSys, 260, 64);
            if (string.IsNullOrWhiteSpace(normalIconName))
                return Remember(key, Ps2IconLoadResult.Corrupted);

            var safeName = string.Concat(normalIconName.Select(ch =>
                Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var iconPath = Path.Combine(itemDirectory, safeName);

            try
            {
                if (!File.Exists(iconPath))
                    await _engine.ExtractFileAsync(cardPath, directoryId, normalIconName, iconPath, cancellationToken);
            }
            catch
            {
                return Remember(key, Ps2IconLoadResult.Corrupted);
            }

            try
            {
                var model = Ps2IconModel.Parse(
                    await File.ReadAllBytesAsync(
                        iconPath,
                        cancellationToken));

                model.RenderSettings =
                    ParseRenderSettings(iconSys);

                return Remember(
                    key,
                    Ps2IconLoadResult.Success(model));
            }
            catch
            {
                return Remember(key, Ps2IconLoadResult.Corrupted);
            }
        }
        catch
        {
            return Remember(key, Ps2IconLoadResult.Corrupted);
        }
    }

    private static Ps2IconRenderSettings ParseRenderSettings(
        byte[] iconSys)
    {
        try
        {
            var lights =
                new Ps2IconLight[3];

            for (var index = 0;
                 index < 3;
                 index++)
            {
                var positionOffset =
                    80 + index * 16;
                var colorOffset =
                    128 + index * 16;

                lights[index] =
                    new Ps2IconLight(
                        ReadFloat(
                            iconSys,
                            positionOffset),
                        ReadFloat(
                            iconSys,
                            positionOffset + 4),
                        ReadFloat(
                            iconSys,
                            positionOffset + 8),
                        ReadFloat(
                            iconSys,
                            colorOffset),
                        ReadFloat(
                            iconSys,
                            colorOffset + 4),
                        ReadFloat(
                            iconSys,
                            colorOffset + 8));
            }

            return new Ps2IconRenderSettings
            {
                AmbientR =
                    ReadFloat(iconSys, 176),
                AmbientG =
                    ReadFloat(iconSys, 180),
                AmbientB =
                    ReadFloat(iconSys, 184),
                Lights = lights
            };
        }
        catch
        {
            return Ps2IconRenderSettings.Default;
        }
    }

    private static float ReadFloat(
        byte[] data,
        int offset)
    {
        if (offset < 0 ||
            offset + 4 > data.Length)
        {
            return 0;
        }

        var value =
            BitConverter.ToSingle(
                data,
                offset);

        return float.IsFinite(value)
            ? value
            : 0;
    }

    private Ps2IconLoadResult Remember(string key, Ps2IconLoadResult result)
    {
        _models[key] = result;
        return result;
    }

    private static string ReadAsciiName(byte[] data, int offset, int length)
    {
        var end = offset;
        while (end < offset + length && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data, offset, end - offset).Trim();
    }

    private static string BuildKey(string path, string directoryId)
    {
        var fullPath =
            Path.GetFullPath(path);

        var identity =
            Directory.Exists(fullPath)
                ? $"{fullPath}|folder|{Directory.GetLastWriteTimeUtc(fullPath).Ticks}|{directoryId}"
                : $"{fullPath}|{new FileInfo(fullPath).Length}|{File.GetLastWriteTimeUtc(fullPath).Ticks}|{directoryId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}
