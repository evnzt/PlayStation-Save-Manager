using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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

public sealed class GameMetadataService
{
    private const string GameDbUrl =
        "https://github.com/niemasd/GameDB-PS2/releases/latest/download/PS2.data.json";

    private const string LaunchBoxMetadataUrl =
        "https://gamesdb.launchbox-app.com/Metadata.zip";

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

    public GameMetadataService()
    {
        _databaseRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlayStationSaveManager",
            "GameDatabase");

        _gameDbPath = Path.Combine(_databaseRoot, "PS2.data.json");
        _launchBoxPath = Path.Combine(_databaseRoot, "LaunchBox.Metadata.xml");
        _overridePath = Path.Combine(_databaseRoot, "psm-overrides.json");

        Directory.CreateDirectory(_databaseRoot);
        EnsureOverrideFile();
    }

    public string DatabaseRoot => _databaseRoot;
    public string GameDbPath => _gameDbPath;
    public string LaunchBoxPath => _launchBoxPath;
    public string OverridePath => _overridePath;

    public async Task<GameDatabaseStatus> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        _gameDbBySerial.Clear();
        _launchBoxByTitle.Clear();
        _overridesBySerial.Clear();

        if (File.Exists(_gameDbPath))
            await LoadGameDbAsync(_gameDbPath, cancellationToken);

        var launchBoxSource = FindLaunchBoxMetadata();
        if (!string.IsNullOrWhiteSpace(launchBoxSource))
            await LoadLaunchBoxAsync(launchBoxSource, cancellationToken);

        if (File.Exists(_overridePath))
            await LoadOverridesAsync(_overridePath, cancellationToken);

        return GetStatus(launchBoxSource);
    }

    public async Task<GameDatabaseStatus> UpdateGameDbAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_databaseRoot);
        var temporaryPath = _gameDbPath + ".download";

        progress?.Report("Downloading the latest GameDB-PS2 database...");
        await DownloadFileAsync(GameDbUrl, temporaryPath, cancellationToken);

        progress?.Report("Validating GameDB-PS2...");
        var validation = new Dictionary<string, GameMetadataRecord>(
            StringComparer.OrdinalIgnoreCase);
        await ParseGameDbAsync(temporaryPath, validation, cancellationToken);

        if (validation.Count == 0)
        {
            File.Delete(temporaryPath);
            throw new InvalidDataException(
                "The downloaded GameDB-PS2 file contained no recognizable serial entries.");
        }

        File.Move(temporaryPath, _gameDbPath, true);
        progress?.Report($"GameDB-PS2 installed: {validation.Count:N0} serial entries.");

        return await LoadAsync(cancellationToken);
    }

    public async Task<GameDatabaseStatus> DownloadLaunchBoxAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_databaseRoot);
        var zipPath = Path.Combine(_databaseRoot, "LaunchBox.Metadata.zip");
        var extractRoot = Path.Combine(
            _databaseRoot,
            "LaunchBox-extract-" + Guid.NewGuid().ToString("N"));

        try
        {
            progress?.Report("Downloading LaunchBox Metadata.zip...");
            await DownloadFileAsync(
                LaunchBoxMetadataUrl,
                zipPath,
                cancellationToken);

            Directory.CreateDirectory(extractRoot);
            progress?.Report("Extracting LaunchBox metadata...");
            ZipFile.ExtractToDirectory(zipPath, extractRoot, true);

            var metadata = Directory
                .EnumerateFiles(extractRoot, "Metadata.xml", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (metadata is null)
                throw new InvalidDataException(
                    "LaunchBox Metadata.zip did not contain Metadata.xml.");

            progress?.Report("Validating LaunchBox PlayStation 2 records...");
            var validation = new Dictionary<string, GameMetadataRecord>(
                StringComparer.OrdinalIgnoreCase);
            await ParseLaunchBoxAsync(
                metadata,
                validation,
                cancellationToken);

            if (validation.Count == 0)
                throw new InvalidDataException(
                    "No Sony PlayStation 2 records were found in LaunchBox Metadata.xml.");

            File.Copy(metadata, _launchBoxPath, true);
            progress?.Report(
                $"LaunchBox metadata installed: {validation.Count:N0} PlayStation 2 titles.");

            return await LoadAsync(cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
                if (Directory.Exists(extractRoot))
                    Directory.Delete(extractRoot, true);
            }
            catch { }
        }
    }

    public async Task<GameDatabaseStatus> ImportLaunchBoxAsync(
        string metadataPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException(
                "LaunchBox Metadata.xml was not found.",
                metadataPath);

        var validation = new Dictionary<string, GameMetadataRecord>(
            StringComparer.OrdinalIgnoreCase);

        await ParseLaunchBoxAsync(
            metadataPath,
            validation,
            cancellationToken);

        if (validation.Count == 0)
            throw new InvalidDataException(
                "The selected file contains no Sony PlayStation 2 metadata.");

        File.Copy(metadataPath, _launchBoxPath, true);
        return await LoadAsync(cancellationToken);
    }

    public GameMetadataRecord? Lookup(
        string serial,
        string fallbackTitle,
        string fallbackRegion)
    {
        serial = NormalizeSerial(serial);

        _gameDbBySerial.TryGetValue(serial, out var gameDb);
        var titleForMatch = FirstNonEmpty(gameDb?.Title, fallbackTitle);
        _launchBoxByTitle.TryGetValue(
            NormalizeTitle(titleForMatch),
            out var launchBox);
        _overridesBySerial.TryGetValue(serial, out var correction);

        if (gameDb is null && launchBox is null && correction is null)
            return null;

        var sources = new List<string>();
        if (gameDb is not null) sources.Add("GameDB-PS2");
        if (launchBox is not null) sources.Add("LaunchBox");
        if (correction is not null) sources.Add("PSM Override");

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

    public GameDatabaseStatus GetStatus() =>
        GetStatus(FindLaunchBoxMetadata());

    private GameDatabaseStatus GetStatus(string? launchBoxSource) =>
        new(
            File.Exists(_gameDbPath),
            _gameDbBySerial.Count,
            !string.IsNullOrWhiteSpace(launchBoxSource),
            _launchBoxByTitle.Count,
            _gameDbPath,
            launchBoxSource ?? string.Empty);

    private async Task LoadGameDbAsync(
        string path,
        CancellationToken cancellationToken) =>
        await ParseGameDbAsync(path, _gameDbBySerial, cancellationToken);

    private async Task ParseGameDbAsync(
        string path,
        Dictionary<string, GameMetadataRecord> destination,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(
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
            "serial", "Serial", "product_code", "productCode",
            "game_id", "gameId", "id");

        var hintedSerial = LooksLikeSerial(keyHint ?? string.Empty)
            ? keyHint
            : string.Empty;

        var serial = NormalizeSerial(
            FirstNonEmpty(objectSerial, hintedSerial));

        if (!string.IsNullOrWhiteSpace(serial))
        {
            var title = FindString(
                element,
                "title", "Title", "name", "Name",
                "game_title", "gameTitle");

            var record = new GameMetadataRecord
            {
                Serial = serial,
                Title = title,
                Region = FindString(
                    element,
                    "region", "Region", "video", "territory"),
                ReleaseDate = FindString(
                    element,
                    "release_date", "releaseDate", "date", "released"),
                Developer = FindString(
                    element,
                    "developer", "Developer", "developers"),
                Publisher = FindString(
                    element,
                    "publisher", "Publisher", "publishers"),
                Source = "GameDB-PS2",
                Verification = "Imported metadata"
            };

            if (record.HasUsefulData)
                destination[serial] = record;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                WalkGameDb(property.Value, destination, property.Name);
        }
    }

    private async Task LoadLaunchBoxAsync(
        string path,
        CancellationToken cancellationToken) =>
        await ParseLaunchBoxAsync(path, _launchBoxByTitle, cancellationToken);

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

            using var reader = XmlReader.Create(path, settings);

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element ||
                    !reader.Name.Equals("Game", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var subtree = reader.ReadSubtree();
                var fields = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

                // Depth 0 is the outer <Game> container. It has child
                // elements, so ReadElementContentAsString cannot be used on it.
                // LaunchBox's textual metadata fields are direct children at
                // depth 1; read only those leaf fields.
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
                            subtree.ReadElementContentAsString()?.Trim();

                        if (!string.IsNullOrWhiteSpace(value))
                            fields[name] = value;
                    }
                    catch (InvalidOperationException)
                    {
                        // Ignore unexpected nested/container fields while
                        // preserving the rest of the Game record.
                        subtree.Skip();
                    }
                }

                fields.TryGetValue("Platform", out var platform);
                if (!IsPs2Platform(platform))
                    continue;

                fields.TryGetValue("Name", out var title);
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                fields.TryGetValue("ReleaseDate", out var releaseDate);
                fields.TryGetValue("Developer", out var developer);
                fields.TryGetValue("Publisher", out var publisher);

                destination[NormalizeTitle(title)] = new GameMetadataRecord
                {
                    Title = title,
                    ReleaseDate = NormalizeLaunchBoxDate(releaseDate),
                    Developer = developer ?? string.Empty,
                    Publisher = publisher ?? string.Empty,
                    Source = "LaunchBox",
                    Verification = "Community metadata"
                };
            }
        }, cancellationToken);
    }

    private async Task LoadOverridesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var records = await JsonSerializer.DeserializeAsync<List<GameMetadataRecord>>(
            stream,
            cancellationToken: cancellationToken) ?? [];

        foreach (var record in records)
        {
            var serial = NormalizeSerial(record.Serial);
            if (!string.IsNullOrWhiteSpace(serial))
                _overridesBySerial[serial] = record;
        }
    }

    private string? FindLaunchBoxMetadata()
    {
        if (File.Exists(_launchBoxPath))
            return _launchBoxPath;

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "LaunchBox", "Metadata", "Metadata.xml"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LaunchBox", "Metadata", "Metadata.xml"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "LaunchBox", "Metadata", "Metadata.xml"),
            @"C:\LaunchBox\Metadata\Metadata.xml",
            @"D:\LaunchBox\Metadata\Metadata.xml",
            @"E:\LaunchBox\Metadata\Metadata.xml"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void EnsureOverrideFile()
    {
        if (File.Exists(_overridePath))
            return;

        File.WriteAllText(
            _overridePath,
            """
            [
              {
                "Serial": "SLUS-20014",
                "Title": "Armored Core 2",
                "Region": "North America",
                "ReleaseDate": "",
                "Developer": "",
                "Publisher": "",
                "Source": "PSM Override",
                "Verification": "Starter mapping"
              }
            ]
            """);
    }

    private static async Task DownloadFileAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            131072,
            useAsync: true);

        await source.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static string FindString(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim() ?? string.Empty;

            if (value.ValueKind == JsonValueKind.Array)
            {
                var joined = value
                    .EnumerateArray()
                    .Where(child => child.ValueKind == JsonValueKind.String)
                    .Select(child => child.GetString())
                    .Where(child => !string.IsNullOrWhiteSpace(child));

                return string.Join(", ", joined!);
            }

            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }

        return string.Empty;
    }

    private static bool LooksLikeSerial(string value) =>
        Regex.IsMatch(
            NormalizeSerial(value),
            @"^[A-Z]{4}-\d{5}$",
            RegexOptions.CultureInvariant);

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
            ? $"{match.Groups["letters"].Value}-{match.Groups["digits"].Value}"
            : value;
    }

    private static string NormalizeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Normalize(NormalizationForm.FormD)
            .Where(character =>
                CharUnicodeInfo.GetUnicodeCategory(character) !=
                UnicodeCategory.NonSpacingMark)
            .ToArray();

        return Regex.Replace(
            new string(normalized).ToLowerInvariant(),
            @"[^a-z0-9]+",
            string.Empty);
    }

    private static bool IsPs2Platform(string? platform)
    {
        var value = NormalizeTitle(platform ?? string.Empty);
        return value is "sonyplaystation2" or "playstation2" or "ps2";
    }

    private static string NormalizeLaunchBoxDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var date))
        {
            return date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        }

        return value.Trim();
    }

    private static string NormalizeRegion(string value) =>
        value.ToUpperInvariant() switch
        {
            "NTSC-U" or "USA" or "US" => "North America",
            "NTSC-J" or "JAPAN" or "JP" => "Japan",
            "PAL" or "EUROPE" or "EU" => "Europe / PAL",
            _ => value
        };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;
}
