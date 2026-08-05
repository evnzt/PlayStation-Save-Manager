using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using PlayStationSaveManager.Models;

namespace PlayStationSaveManager.Services;

public sealed class Ps1GameMetadataService
{
    private const string GameDbUrl =
        "https://github.com/niemasd/GameDB-PSX/releases/latest/download/PSX.data.json";

    private readonly string _databaseRoot;
    private readonly string _gameDbPath;
    private readonly string _launchBoxPath;
    private readonly string _overridePath;

    private readonly Dictionary<string, GameMetadataRecord> _gameDbBySerial =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, GameMetadataRecord> _launchBoxByTitle =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, GameMetadataRecord> _overridesBySerial =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(8)
    };

    public Ps1GameMetadataService()
    {
        _databaseRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "PlayStationSaveManager",
            "GameDatabase");

        _gameDbPath = Path.Combine(
            _databaseRoot,
            "PSX.data.json");

        _launchBoxPath = Path.Combine(
            _databaseRoot,
            "LaunchBox.Metadata.xml");

        _overridePath = Path.Combine(
            _databaseRoot,
            "ps1-overrides.json");

        Directory.CreateDirectory(_databaseRoot);
        EnsureOverrideFile();
    }

    public async Task<Ps1GameDatabaseStatus> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        _gameDbBySerial.Clear();
        _launchBoxByTitle.Clear();
        _overridesBySerial.Clear();

        if (File.Exists(_gameDbPath))
        {
            await ParseGameDbAsync(
                _gameDbPath,
                _gameDbBySerial,
                cancellationToken);
        }

        if (File.Exists(_launchBoxPath))
        {
            await ParseLaunchBoxAsync(
                _launchBoxPath,
                _launchBoxByTitle,
                cancellationToken);
        }

        if (File.Exists(_overridePath))
            await LoadOverridesAsync(cancellationToken);

        return GetStatus();
    }

    public async Task<Ps1GameDatabaseStatus> UpdateGameDbAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_databaseRoot);
        var temporary = _gameDbPath + ".download";

        progress?.Report(
            "Downloading the latest GameDB-PSX database...");

        await DownloadFileAsync(
            GameDbUrl,
            temporary,
            cancellationToken);

        progress?.Report("Validating GameDB-PSX...");

        var validation =
            new Dictionary<string, GameMetadataRecord>(
                StringComparer.OrdinalIgnoreCase);

        await ParseGameDbAsync(
            temporary,
            validation,
            cancellationToken);

        if (validation.Count == 0)
        {
            try { File.Delete(temporary); } catch { }

            throw new InvalidDataException(
                "The downloaded GameDB-PSX file contained no recognizable serial entries.");
        }

        File.Move(temporary, _gameDbPath, true);

        progress?.Report(
            $"GameDB-PSX installed: {validation.Count:N0} serial entries.");

        return await LoadAsync(cancellationToken);
    }

    public GameMetadataRecord? Lookup(
        string serial,
        string fallbackTitle,
        string fallbackRegion)
    {
        serial = NormalizeSerial(serial);

        _gameDbBySerial.TryGetValue(
            serial,
            out var gameDb);

        var titleForMatch = FirstNonEmpty(
            gameDb?.Title,
            fallbackTitle);

        _launchBoxByTitle.TryGetValue(
            NormalizeTitle(titleForMatch),
            out var launchBox);

        _overridesBySerial.TryGetValue(
            serial,
            out var correction);

        if (gameDb is null &&
            launchBox is null &&
            correction is null)
        {
            return null;
        }

        var sources = new List<string>();
        if (gameDb is not null)
            sources.Add("GameDB-PSX");
        if (launchBox is not null)
            sources.Add("LaunchBox");
        if (correction is not null)
            sources.Add("PSM PS1 Override");

        return new GameMetadataRecord
        {
            Serial = serial,
            Title = FirstNonEmpty(
                correction?.Title,
                gameDb?.Title,
                launchBox?.Title,
                fallbackTitle),
            Region = NormalizeRegion(FirstNonEmpty(
                correction?.Region,
                gameDb?.Region,
                fallbackRegion)),
            ReleaseDate = FirstNonEmpty(
                correction?.ReleaseDate,
                launchBox?.ReleaseDate,
                gameDb?.ReleaseDate),
            Developer = FirstNonEmpty(
                correction?.Developer,
                launchBox?.Developer,
                gameDb?.Developer),
            Publisher = FirstNonEmpty(
                correction?.Publisher,
                launchBox?.Publisher,
                gameDb?.Publisher),
            Source = string.Join(" + ", sources),
            Verification = FirstNonEmpty(
                correction?.Verification,
                "Imported metadata")
        };
    }

    public Ps1GameDatabaseStatus GetStatus() =>
        new(
            File.Exists(_gameDbPath),
            _gameDbBySerial.Count,
            File.Exists(_launchBoxPath),
            _launchBoxByTitle.Count,
            _gameDbPath,
            _launchBoxPath);

    private async Task ParseGameDbAsync(
        string path,
        Dictionary<string, GameMetadataRecord> destination,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        using var document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

        WalkGameDb(
            document.RootElement,
            destination,
            keyHint: null);
    }

    private static void WalkGameDb(
        JsonElement element,
        Dictionary<string, GameMetadataRecord> destination,
        string? keyHint)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                WalkGameDb(child, destination, keyHint);

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        var objectSerial = FindString(
            element,
            "serial", "Serial",
            "product_code", "productCode",
            "game_id", "gameId", "id");

        var hintedSerial =
            LooksLikeSerial(keyHint ?? string.Empty)
                ? keyHint
                : string.Empty;

        var serial = NormalizeSerial(
            FirstNonEmpty(
                objectSerial,
                hintedSerial));

        if (!string.IsNullOrWhiteSpace(serial))
        {
            var record = new GameMetadataRecord
            {
                Serial = serial,
                Title = FindString(
                    element,
                    "title", "Title",
                    "name", "Name",
                    "game_title", "gameTitle"),
                Region = FindString(
                    element,
                    "region", "Region",
                    "video", "territory"),
                ReleaseDate = FindString(
                    element,
                    "release_date", "releaseDate",
                    "date", "released"),
                Developer = FindString(
                    element,
                    "developer", "Developer",
                    "developers"),
                Publisher = FindString(
                    element,
                    "publisher", "Publisher",
                    "publishers"),
                Source = "GameDB-PSX",
                Verification = "Imported metadata"
            };

            if (record.HasUsefulData)
                destination[serial] = record;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is
                JsonValueKind.Object or
                JsonValueKind.Array)
            {
                WalkGameDb(
                    property.Value,
                    destination,
                    property.Name);
            }
        }
    }

    private static async Task ParseLaunchBoxAsync(
        string path,
        Dictionary<string, GameMetadataRecord> destination,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Ignore
            };

            using var reader = XmlReader.Create(
                path,
                settings);

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element ||
                    !reader.Name.Equals(
                        "Game",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var subtree = reader.ReadSubtree();

                var fields =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                while (subtree.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (subtree.NodeType != XmlNodeType.Element ||
                        subtree.Depth != 1 ||
                        subtree.IsEmptyElement)
                    {
                        continue;
                    }

                    var name = subtree.Name;

                    try
                    {
                        var value =
                            subtree
                                .ReadElementContentAsString()
                                ?.Trim();

                        if (!string.IsNullOrWhiteSpace(value))
                            fields[name] = value;
                    }
                    catch (InvalidOperationException)
                    {
                        subtree.Skip();
                    }
                }

                fields.TryGetValue(
                    "Platform",
                    out var platform);

                if (!IsPs1Platform(platform))
                    continue;

                fields.TryGetValue(
                    "Name",
                    out var title);

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                fields.TryGetValue(
                    "ReleaseDate",
                    out var releaseDate);

                fields.TryGetValue(
                    "Developer",
                    out var developer);

                fields.TryGetValue(
                    "Publisher",
                    out var publisher);

                destination[NormalizeTitle(title)] =
                    new GameMetadataRecord
                    {
                        Title = title,
                        ReleaseDate =
                            NormalizeLaunchBoxDate(
                                releaseDate),
                        Developer =
                            developer ?? string.Empty,
                        Publisher =
                            publisher ?? string.Empty,
                        Source = "LaunchBox",
                        Verification =
                            "Community metadata"
                    };
            }
        }, cancellationToken);
    }

    private async Task LoadOverridesAsync(
        CancellationToken cancellationToken)
    {
        await using var stream =
            File.OpenRead(_overridePath);

        var records =
            await JsonSerializer.DeserializeAsync<
                List<GameMetadataRecord>>(
                stream,
                cancellationToken:
                    cancellationToken)
            ?? [];

        foreach (var record in records)
        {
            var serial =
                NormalizeSerial(record.Serial);

            if (!string.IsNullOrWhiteSpace(serial))
                _overridesBySerial[serial] = record;
        }
    }

    private void EnsureOverrideFile()
    {
        if (File.Exists(_overridePath))
            return;

        File.WriteAllText(
            _overridePath,
            "[]");
    }

    private static async Task DownloadFileAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response =
            await Http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var source =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        await using var output =
            new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                131072,
                useAsync: true);

        await source.CopyToAsync(
            output,
            cancellationToken);

        await output.FlushAsync(
            cancellationToken);
    }

    private static string FindString(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(
                propertyName,
                out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim() ?? string.Empty;

            if (value.ValueKind == JsonValueKind.Array)
            {
                var joined = value
                    .EnumerateArray()
                    .Where(child =>
                        child.ValueKind ==
                        JsonValueKind.String)
                    .Select(child =>
                        child.GetString())
                    .Where(child =>
                        !string.IsNullOrWhiteSpace(child));

                return string.Join(", ", joined!);
            }

            if (value.ValueKind is
                JsonValueKind.Number or
                JsonValueKind.True or
                JsonValueKind.False)
            {
                return value.ToString();
            }
        }

        return string.Empty;
    }

    public static string NormalizeSerial(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value
            .ToUpperInvariant()
            .Replace("_", "-")
            .Replace(" ", string.Empty);

        var match = Regex.Match(
            value,
            @"(?<letters>[A-Z]{4})-?(?<digits>\d{5})");

        return match.Success
            ? $"{match.Groups["letters"].Value}-" +
              $"{match.Groups["digits"].Value}"
            : value;
    }

    private static bool LooksLikeSerial(string value) =>
        Regex.IsMatch(
            NormalizeSerial(value),
            @"^[A-Z]{4}-\d{5}$",
            RegexOptions.CultureInvariant);

    private static string NormalizeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Normalize(NormalizationForm.FormD)
            .Where(character =>
                CharUnicodeInfo.GetUnicodeCategory(
                    character) !=
                UnicodeCategory.NonSpacingMark)
            .ToArray();

        return Regex.Replace(
            new string(normalized)
                .ToLowerInvariant(),
            @"[^a-z0-9]+",
            string.Empty);
    }

    private static bool IsPs1Platform(string? platform)
    {
        var value = NormalizeTitle(
            platform ?? string.Empty);

        return value is
            "sonyplaystation" or
            "playstation" or
            "psx" or
            "ps1";
    }

    private static string NormalizeLaunchBoxDate(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var date))
        {
            return date.ToString(
                "MMMM d, yyyy",
                CultureInfo.InvariantCulture);
        }

        return value.Trim();
    }

    private static string NormalizeRegion(string value) =>
        value.ToUpperInvariant() switch
        {
            "NTSC-U" or "USA" or "US" =>
                "North America",
            "NTSC-J" or "JAPAN" or "JP" =>
                "Japan",
            "PAL" or "EUROPE" or "EU" =>
                "Europe / PAL",
            _ => value
        };

    private static string FirstNonEmpty(
        params string?[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;
}
