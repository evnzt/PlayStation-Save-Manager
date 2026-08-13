using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PlayStationSaveManager.Models;
using PlayStationSaveManager.Services;

namespace PlayStationSaveManager;

public partial class MainWindow : Window
{
    private readonly MyMcEngine _engine;
    private readonly SaveLibraryService _saveLibraryService;
    private readonly MemoryCardLibraryService _memoryCardLibraryService;
    private readonly GameMetadataService _gameMetadataService;
    private readonly Ps1GameMetadataService _ps1GameMetadataService;
    private readonly Ps1MemoryCardService _ps1CardService;
    private readonly Ps2SavePackageService _ps2PackageService;
    private GameDatabaseStatus? _gameDatabaseStatus;
    private Ps1GameDatabaseStatus? _ps1GameDatabaseStatus;
    private SaveLibraryIndex _saveLibraryIndex = new();
    private MemoryCardLibraryIndex _memoryCardLibraryIndex = new();
    private readonly List<SaveLibraryEntry> _saveLibraryView = [];
    public ObservableCollection<MemoryCardLibraryEntry>
        MemoryCardLibraryEntries { get; } = [];

    private enum SaveLibraryContentMode
    {
        GameSaves,
        MemoryCards
    }

    private SaveLibraryContentMode _saveLibraryContentMode =
        SaveLibraryContentMode.GameSaves;

    private sealed record SaveRelationshipLink(
        string EntryId,
        string Label,
        string ToolTip,
        BitmapSource? IconImage);

    private readonly Dictionary<string, string>
        _savePayloadFingerprintCache =
            new(StringComparer.OrdinalIgnoreCase);

    private int _saveStatusGeneration;

    private readonly Dictionary<string, BitmapSource>
        _saveLibraryIconMemoryCache =
            new(StringComparer.OrdinalIgnoreCase);
    private readonly Ps2IconService _iconService;
    private readonly DispatcherTimer _iconAnimationTimer;
    private readonly Stopwatch _iconAnimationClock = Stopwatch.StartNew();
    private const double Ps2IconFrontRotation = Math.PI - 0.32;
    private readonly ObservableCollection<SaveEntry> _allA = [];
    private readonly ObservableCollection<SaveEntry> _allB = [];
    private Ps2IconModel? _previewModelA;
    private Ps2IconModel? _previewModelB;
    private ObjIconModel? _previewFallbackA;
    private ObjIconModel? _previewFallbackB;
    private Ps2IconModel? _libraryPreviewModel;
    private ObjIconModel? _libraryPreviewFallback;
    private bool _libraryPreviewRendering;
    private SaveLibraryEntry? _saveInformationEntry;
    private bool _previewRenderA;
    private bool _previewRenderB;
    private double _previewRotationStartA;
    private double _previewRotationStartB;
    private double _libraryPreviewRotationStart;
    private string? _pathA;
    private string? _pathB;
    private string? _ps1PathA;
    private string? _ps1PathB;
    private readonly ObservableCollection<Ps1SaveEntry> _ps1SavesA = [];
    private readonly ObservableCollection<Ps1SaveEntry> _ps1SavesB = [];
    private bool _busy;
    private bool _automaticBackupsEnabled = true;
    private string? _selectedPackagePath;
    private string? _universalSourcePath;
    private string? _wizardSourcePath;
    private bool _wizardSourceIsCard;
    private bool _wizardSourceIsPs1Card;
    private bool _wizardSourceIsPs1SingleSave;
    private bool _wizardSourceIsPs1Package;
    private bool _wizardSourceIsReadablePackage;
    private bool _wizardSourceIsFolderSave;
    private string? _wizardFolderCardPath;
    private string? _wizardFolderSaveId;


    private static readonly UniversalFormatOption[] UniversalFormats =
    [
        new(".bin", "Memory Card - BIN (PS1 / PS2)", true, true),
        new(".cbs", "PS2 Individual Save - CBS • CodeBreaker", true, true),
        new(".ddf", "PS1 Memory Card - DDF • DataDeck", true, true),
        new(".foldercard", "PS2 Memory Card - PCSX2 Folder Card", true, true),
        new(".gme", "PS1 Memory Card - GME • DexDrive", true, true),
        new(".max", "PS2 Individual Save - MAX • Action Replay MAX", true, true),
        new(".mc", "PS1 Memory Card - MC • PSXGame Edit", true, true),
        new(".mc2", "PS2 Memory Card - MC2 • MemCard PRO2", true, true),
        new(".mcb", "PS1 Individual Save - MCB • Smart Link", true, true),
        new(".mcd", "Memory Card - MCD (PS1 / PS2)", true, true),
        new(".mci", "PS1 Memory Card - MCI • MCExplorer", true, true),
        new(".mcr", "PS1 Memory Card - MCR • ePSXe / PSEmu Pro", true, true),
        new(".mcs", "PS1 Individual Save - MCS • PSXGameEdit", true, true),
        new(".mcx", "PS1 Individual Save - MCX • Datel", true, true),
        new(".mem", "PS1 Memory Card - MEM • VGS / Connectix", true, true),
        new(".npo", "PS2 Individual Save - NPO • NPort", false, false),
        new(".p2m", "PS2 Individual Save - P2M • Xploder 4 Pro", false, false),
        new(".pda", "PS1 Individual Save - PDA • Datel", true, true),
        new(".ps", "PS1 Memory Card - PS • WinPSM", true, true),
        new(".ps1", "PS1 Individual Save - PS1 • Memory Juggler", true, true),
        new(".ps1save", "PSM PlayStation Save Package", true, true),
        new(".ps2", "PS2 Memory Card - PS2 • PCSX2", true, true),
        new(".psm", "PS1 Memory Card - PSM • Smart Link", true, true),
        new(".psu", "PS2 Individual Save - PSU • EMS / uLaunchELF", true, true),
        new(".psv", "PlayStation Virtual Save - PSV (PS1 / PS2)", true, true),
        new(".psx", "PS1 Individual Save - PSX • X-Port / AR / GameShark", true, true),
        new(".raw", "PS1 Individual Save - RAW", true, true),
        new(".sav", "PS1 Memory Card - SAV", true, true),
        new(".sps", "PS2 Individual Save - SPS • SharkPort", true, true),
        new(".srm", "PS1 Memory Card - SRM • RetroArch / Libretro", true, true),
        new(".vgs", "PS1 Memory Card - VGS • VGS / Connectix", true, true),
        new(".vm1", "PS1 Memory Card - VM1 • PS3 Virtual Memory Card", true, true),
        new(".vm2", "PS2 Memory Card - VM2 • Virtual Memory Card", true, true),
        new(".vmc", "Memory Card - VMC (PS1 / PS2)", true, true),
        new(".vmp", "PS1 Memory Card - VMP • PSP Virtual Memory Card", true, true),
        new(".xps", "PS2 Individual Save - XPS • X-Port / Xploder", true, true)
    ];

    private enum UniversalSourceKind
    {
        Unsupported,
        Ps1Card,
        Ps1SingleSave,
        Ps1Package,
        Ps2Card,
        Ps2Package
    }

    private static readonly string[] Ps1CardExtensions =
    [
        ".bin", ".ddf", ".gme", ".mc", ".mcd", ".mci", ".mcr", ".mem",
        ".ps", ".psm", ".sav", ".srm", ".vgs", ".vm1", ".vmc", ".vmp"
    ];

    private static readonly string[] Ps1SingleSaveExtensions =
    [
        ".mcb", ".mcs", ".mcx", ".pda", ".ps1", ".psv", ".psx", ".raw"
    ];

    private static readonly string[] Ps2CardExtensions =
    [
        ".bin", ".foldercard", ".mc2", ".mcd", ".ps2", ".vm2", ".vmc"
    ];

    private enum SaveSortField
    {
        GameName,
        DirectoryId,
        Size
    }

    private SaveSortField _sortFieldA = SaveSortField.GameName;
    private SaveSortField _sortFieldB = SaveSortField.GameName;
    private bool _sortDescendingA;
    private bool _sortDescendingB;

    private enum Ps1SortField
    {
        GameName,
        SaveDescription,
        ProductCode,
        BlocksUsed
    }

    private Ps1SortField _ps1SortFieldA = Ps1SortField.GameName;
    private Ps1SortField _ps1SortFieldB = Ps1SortField.GameName;
    private bool _ps1SortDescendingA;
    private bool _ps1SortDescendingB;

    private enum LibraryFilterMode
    {
        All,
        Favorites,
        Duplicates
    }

    private enum LibrarySortField
    {
        GameName,
        DirectoryId,
        Format,
        Size,
        DateAdded,
        DateModified,
        FavoritesFirst
    }

    private enum LibraryPlatformFilter
    {
        All,
        Ps1,
        Ps2
    }

    private LibraryFilterMode _libraryFilterMode =
        LibraryFilterMode.All;

    private LibraryPlatformFilter _libraryPlatformFilter =
        LibraryPlatformFilter.All;

    private LibrarySortField _librarySortField =
        LibrarySortField.FavoritesFirst;

    private bool _librarySortDescending;
    private bool _saveLibraryIconsStarted;
    private bool _saveLibraryLoaded;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new MyMcEngine(AppContext.BaseDirectory);
        _saveLibraryService = new SaveLibraryService(_engine);
        _memoryCardLibraryService = new MemoryCardLibraryService();
        _gameMetadataService = new GameMetadataService();
        _ps1GameMetadataService = new Ps1GameMetadataService();
        _ps1CardService = new Ps1MemoryCardService();
        _ps2PackageService = new Ps2SavePackageService(_engine);
        LoadAutomaticBackupSetting();
        _iconService = new Ps2IconService(_engine, AppContext.BaseDirectory);
        CardAList.ItemsSource = _allA;
        CardBList.ItemsSource = _allB;
        _iconAnimationTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _iconAnimationTimer.Tick += IconAnimationTimer_Tick;
        _iconAnimationTimer.Start();
        Loaded += MainWindow_Loaded;
        _ = LoadSaveLibraryAsync();
        _ = LoadMemoryCardLibraryAsync();
        _ = LoadGameMetadataDatabaseAsync();
        _ = LoadPs1GameMetadataDatabaseAsync();
        Ps1CardAList.ItemsSource = _ps1SavesA;
        Ps1CardBList.ItemsSource = _ps1SavesB;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_engine.IsInstalled)
        {
            StartSaveLibraryIconLoading();
            return;
        }

        var setup = Path.Combine(AppContext.BaseDirectory, "Setup-Engine.ps1");
        if (!File.Exists(setup))
        {
            MessageBox.Show("The private memory-card engine is missing. Re-extract the complete package.", "Engine Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            "PlayStation Save Manager needs to set up its private memory-card engine inside this folder. Nothing will be installed system-wide. Continue?",
            "Set Up Engine", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(true, "Setting up the private memory-card engine...");
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{setup}\"",
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
            if (process is null)
                throw new InvalidOperationException("Could not start engine setup.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !_engine.IsInstalled)
                throw new InvalidOperationException("Engine setup did not complete. Check engine-setup-error.log.");
            Log("Private myMC++ engine setup completed.");
            StatusText.Text = "Engine ready.";
            StartSaveLibraryIconLoading();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Engine Setup Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task LoadCardAsync(string path, char side, string? highlightId = null, bool allowWhileBusy = false)
    {
        if ((!allowWhileBusy && _busy) ||
            (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }
        try
        {
            SetBusy(true, $"Reading {Path.GetFileName(path)}...");
            Log($"Reading card: {path}");
            var cardResult = await _engine.ReadCardAsync(path);
            var saves = cardResult.Saves;
            var target = side == 'A' ? _allA : _allB;
            target.Clear();
            foreach (var save in saves) target.Add(save);

            // Normal card browsing can load thumbnails in the background.
            // Import/transfer calls pass highlightId; in that path wait for
            // the complete icon refresh before selecting the new save. This
            // prevents the UI from being one icon refresh behind.
            if (!string.IsNullOrWhiteSpace(highlightId))
                await LoadThumbnailsAsync(path, saves, side);
            else
                _ = LoadThumbnailsAsync(path, saves, side);

            if (side == 'A')
            {
                _pathA = path;
                CardAInfo.Text =
                    BuildPs2CardHeader(
                        path,
                        cardResult,
                        saves.Count);
                if (Directory.Exists(path))
                    UpdateFolderCapacityDisplay('A');
                else
                    UpdateCapacityDisplay('A', cardResult);
            }
            else
            {
                _pathB = path;
                CardBInfo.Text =
                    BuildPs2CardHeader(
                        path,
                        cardResult,
                        saves.Count);
                if (Directory.Exists(path))
                    UpdateFolderCapacityDisplay('B');
                else
                    UpdateCapacityDisplay('B', cardResult);
            }

            ApplyFilter(side);
            if (!string.IsNullOrWhiteSpace(highlightId))
                HighlightSave(side, highlightId);
            Log($"Loaded {Path.GetFileName(path)}: {saves.Count} saves.");
            StatusText.Text = $"Loaded {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Log($"Load failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
            RefreshButtons();
        }
    }

    private async Task TransferAsync(string source, string destination, SaveEntry save, char destinationSide)
    {
        var confirm = MessageBox.Show(
            $"Copy {save.Title}\n{save.DirectoryId}\n\nto {Path.GetFileName(destination)}?",
            "Confirm Transfer", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var destinationSaves =
            await _engine.ReadDirectoryAsync(
                destination);

        var existingSave =
            destinationSaves.FirstOrDefault(
                candidate =>
                    candidate.DirectoryId.Equals(
                        save.DirectoryId,
                        StringComparison.OrdinalIgnoreCase));

        if (existingSave is not null &&
            ReplaceExisting.IsChecked != true)
        {
            MessageBox.Show(
                $"{save.Title}\n{save.DirectoryId}\n\nalready exists on {Path.GetFileName(destination)}.\n\n" +
                "Enable \"Replace save if it already exists\" to overwrite it.",
                "Save Already Exists",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "PSAM-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            SetBusy(true, $"Transferring {save.DirectoryId}...");
            VerifiedBanner.Visibility = Visibility.Hidden;
            var psu = Path.Combine(temporaryDirectory, save.DirectoryId + ".psu");
            var destinationIsFolder =
                Directory.Exists(destination);

            var temporaryCard =
                destinationIsFolder
                    ? Path.Combine(temporaryDirectory, "FolderCard")
                    : Path.Combine(
                        temporaryDirectory,
                        Path.GetFileName(destination));

            if (destinationIsFolder)
                CopyDirectory(destination, temporaryCard);
            else
                File.Copy(destination, temporaryCard, true);

            Log($"Exporting {save.DirectoryId} from source card.");
            await _engine.ExportPsuAsync(source, save.DirectoryId, psu);
            if (ReplaceExisting.IsChecked == true)
            {
                Log("Removing an existing destination save with the same directory ID, when present.");
                await _engine.DeleteAsync(temporaryCard, save.DirectoryId);
            }

            Log("Importing save into temporary destination card.");
            await _engine.ImportAsync(temporaryCard, psu);
            Log("Verifying temporary destination card.");
            await _engine.CheckAsync(temporaryCard);

            var backup =
                destinationIsFolder
                    ? CreateAutomaticFolderBackup(destination)
                    : CreateAutomaticBackup(destination);

            if (destinationIsFolder)
            {
                Directory.Delete(destination, recursive: true);
                CopyDirectory(
                    temporaryCard,
                    destination);
                Directory.Delete(
                    temporaryCard,
                    recursive: true);
            }
            else
            {
                File.Copy(temporaryCard, destination, true);
            }

            LogAutomaticBackup("Transfer committed.", backup);

            await LoadCardAsync(destination, destinationSide, save.DirectoryId, allowWhileBusy: true);
            VerifiedText.Text = $"TRANSFER VERIFIED - {save.Title} copied successfully";
            VerifiedBanner.Visibility = Visibility.Visible;
            StatusText.Text = "Transfer verified.";
            MessageBox.Show($"Save copied and verified successfully.\n\n{AutomaticBackupDetails(backup)}", "Transfer Verified", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Transfer failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Transfer Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, true); } catch { }
            SetBusy(false, "Ready.");
            RefreshButtons();
        }
    }

    private static string CreateBackup(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}.backup-{stamp}{Path.GetExtension(path)}");
        File.Copy(path, destination, true);
        return destination;
    }

    private static string CreateFolderBackup(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination =
            path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            $".backup-{stamp}";

        CopyDirectory(
            path,
            destination);

        return destination;
    }

    private static void CopyDirectory(
        string source,
        string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(
                    destination,
                    Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(
                    destination,
                    Path.GetFileName(directory)));
        }
    }

    private void ApplyFilter(char side)
    {
        var query = (side == 'A' ? SearchA.Text : SearchB.Text)?.Trim() ?? string.Empty;
        var source = side == 'A' ? _allA : _allB;
        IEnumerable<SaveEntry> filtered = string.IsNullOrEmpty(query)
            ? source
            : source.Where(save =>
                save.GameTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                save.ProfileName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                save.DirectoryId.Contains(query, StringComparison.OrdinalIgnoreCase));

        var field = side == 'A' ? _sortFieldA : _sortFieldB;
        var descending = side == 'A' ? _sortDescendingA : _sortDescendingB;

        filtered = field switch
        {
            SaveSortField.DirectoryId => descending
                ? filtered
                    .OrderByDescending(
                        save => save.DirectoryId,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(
                        save => save.GameTitle,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        save => save.ProfileName,
                        StringComparer.CurrentCultureIgnoreCase)
                : filtered
                    .OrderBy(
                        save => save.DirectoryId,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        save => save.GameTitle,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(
                        save => save.ProfileName,
                        StringComparer.CurrentCultureIgnoreCase),

            SaveSortField.Size => descending
                ? filtered
                    .OrderByDescending(
                        save => save.SizeBytes)
                    .ThenByDescending(
                        save => save.GameTitle,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        save => save.ProfileName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        save => save.DirectoryId,
                        StringComparer.OrdinalIgnoreCase)
                : filtered
                    .OrderBy(
                        save => save.SizeBytes)
                    .ThenBy(
                        save => save.GameTitle,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(
                        save => save.ProfileName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(
                        save => save.DirectoryId,
                        StringComparer.OrdinalIgnoreCase),

            _ => descending
                ? filtered
                    .OrderByDescending(
                        save => save.GameTitle,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        save => save.ProfileName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        save => save.DirectoryId,
                        StringComparer.OrdinalIgnoreCase)
                : filtered
                    .OrderBy(
                        save => save.GameTitle,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(
                        save => save.ProfileName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(
                        save => save.DirectoryId,
                        StringComparer.OrdinalIgnoreCase)
        };

        var results = filtered.ToArray();
        if (side == 'A') CardAList.ItemsSource = results;
        else CardBList.ItemsSource = results;
    }

    private void HighlightSave(char side, string directoryId)
    {
        var list = side == 'A' ? CardAList : CardBList;
        var item = list.Items.Cast<SaveEntry>().FirstOrDefault(save => save.DirectoryId.Equals(directoryId, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        list.SelectedItem = item;
        list.ScrollIntoView(item);
        list.Focus();
    }

    private void SetBusy(bool busy, string message)
    {
        _busy = busy;
        Progress.IsIndeterminate = busy;
        StatusText.Text = message;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        CloseAButton.IsEnabled = !_busy && _pathA is not null;
        CloseBButton.IsEnabled = !_busy && _pathB is not null;
        BackupAButton.IsEnabled = !_busy && _pathA is not null;
        BackupBButton.IsEnabled = !_busy && _pathB is not null;
        SaveAsAButton.IsEnabled = !_busy && _pathA is not null;
        SaveAsBButton.IsEnabled = !_busy && _pathB is not null;
        ExportAButton.IsEnabled = !_busy && CardAList.SelectedItem is SaveEntry;
        ExportBButton.IsEnabled = !_busy && CardBList.SelectedItem is SaveEntry;
        DeleteAButton.IsEnabled = !_busy && _pathA is not null && CardAList.SelectedItem is SaveEntry;
        DeleteBButton.IsEnabled = !_busy && _pathB is not null && CardBList.SelectedItem is SaveEntry;
        AddLibraryAButton.IsEnabled = !_busy && _pathA is not null;
        AddLibraryBButton.IsEnabled = !_busy && _pathB is not null;
        CopyAToBButton.IsEnabled = !_busy && _pathA is not null && _pathB is not null && CardAList.SelectedItem is SaveEntry;
        CopyBToAButton.IsEnabled = !_busy && _pathA is not null && _pathB is not null && CardBList.SelectedItem is SaveEntry;
        ClosePs1AButton.IsEnabled = !_busy && _ps1PathA is not null;
        ClosePs1BButton.IsEnabled = !_busy && _ps1PathB is not null;
        BackupPs1AButton.IsEnabled = !_busy && _ps1PathA is not null;
        BackupPs1BButton.IsEnabled = !_busy && _ps1PathB is not null;
        SaveAsPs1AButton.IsEnabled = !_busy && _ps1PathA is not null;
        SaveAsPs1BButton.IsEnabled = !_busy && _ps1PathB is not null;
        ExportPs1AButton.IsEnabled =
            !_busy &&
            _ps1PathA is not null &&
            Ps1CardAList.SelectedItem is Ps1SaveEntry;
        ExportPs1BButton.IsEnabled =
            !_busy &&
            _ps1PathB is not null &&
            Ps1CardBList.SelectedItem is Ps1SaveEntry;
        DeletePs1AButton.IsEnabled =
            !_busy &&
            _ps1PathA is not null &&
            Ps1CardAList.SelectedItem is Ps1SaveEntry { IsDeleted: false };
        DeletePs1BButton.IsEnabled =
            !_busy &&
            _ps1PathB is not null &&
            Ps1CardBList.SelectedItem is Ps1SaveEntry { IsDeleted: false };
        AddLibraryPs1AButton.IsEnabled = !_busy && _ps1PathA is not null;
        AddLibraryPs1BButton.IsEnabled = !_busy && _ps1PathB is not null;
        CopyPs1AToBButton.IsEnabled =
            !_busy &&
            _ps1PathA is not null &&
            _ps1PathB is not null &&
            Ps1CardAList.SelectedItem is Ps1SaveEntry { IsDeleted: false };
        CopyPs1BToAButton.IsEnabled =
            !_busy &&
            _ps1PathA is not null &&
            _ps1PathB is not null &&
            Ps1CardBList.SelectedItem is Ps1SaveEntry { IsDeleted: false };
        if (_wizardSourcePath is not null && !_wizardSourceIsCard)
        {
            if (_wizardSourceIsPs1SingleSave || _wizardSourceIsPs1Package)
            {
                WizardCardAButton.IsEnabled = !_busy && _ps1PathA is not null;
                WizardCardBButton.IsEnabled = !_busy && _ps1PathB is not null;
            }
            else if (_wizardSourceIsReadablePackage)
            {
                WizardCardAButton.IsEnabled = !_busy && _pathA is not null;
                WizardCardBButton.IsEnabled = !_busy && _pathB is not null;
            }
        }

        RefreshLibrarySlotButtons();
    }

    private void RefreshLibrarySlotButtons()
    {
        if (_saveLibraryContentMode != SaveLibraryContentMode.GameSaves)
            return;

        var selected = SaveLibraryList?.SelectedItems
            .Cast<SaveLibraryEntry>()
            .ToArray() ?? Array.Empty<SaveLibraryEntry>();

        if (selected.Length == 0)
        {
            LibrarySlotAButton.IsEnabled = false;
            LibrarySlotBButton.IsEnabled = false;
            return;
        }

        var allPs1 = selected.All(IsPs1LibraryEntry);
        var allPs2 = selected.All(entry => !IsPs1LibraryEntry(entry));

        LibrarySlotAButton.IsEnabled =
            !_busy &&
            ((allPs1 && _ps1PathA is not null) ||
             (allPs2 && _pathA is not null));

        LibrarySlotBButton.IsEnabled =
            !_busy &&
            ((allPs1 && _ps1PathB is not null) ||
             (allPs2 && _pathB is not null));
    }

    private static string PreferencesPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "PlayStationSaveManager",
            "settings.ini");

    private void LoadAutomaticBackupSetting()
    {
        var enabled = true;

        try
        {
            if (File.Exists(PreferencesPath))
            {
                var value = File.ReadAllText(
                    PreferencesPath).Trim();

                if (value.Equals(
                    "AutomaticBackups=false",
                    StringComparison.OrdinalIgnoreCase))
                {
                    enabled = false;
                }
            }
        }
        catch
        {
            enabled = true;
        }

        _automaticBackupsEnabled = enabled;
        AutomaticBackupsToggle.IsChecked = enabled;
        _ps1CardService.AutomaticBackupsEnabled = enabled;
    }

    private void AutomaticBackupsToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        _automaticBackupsEnabled =
            AutomaticBackupsToggle.IsChecked == true;

        _ps1CardService.AutomaticBackupsEnabled =
            _automaticBackupsEnabled;

        try
        {
            var directory =
                Path.GetDirectoryName(
                    PreferencesPath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                PreferencesPath,
                _automaticBackupsEnabled
                    ? "AutomaticBackups=true"
                    : "AutomaticBackups=false");
        }
        catch (Exception ex)
        {
            Log(
                "Could not save Automatic Backups preference: " +
                ex.Message);
        }

        Log(
            _automaticBackupsEnabled
                ? "Automatic backups enabled."
                : "Automatic backups disabled. Verification remains enabled.");

        StatusText.Text =
            _automaticBackupsEnabled
                ? "Automatic backups enabled."
                : "Automatic backups disabled.";
    }

    private string? CreateAutomaticBackup(string path) =>
        _automaticBackupsEnabled
            ? CreateBackup(path)
            : null;

    private string? CreateAutomaticFolderBackup(string path) =>
        _automaticBackupsEnabled
            ? CreateFolderBackup(path)
            : null;

    private string AutomaticBackupDetails(string? backup) =>
        backup is not null
            ? $"Backup:\n{backup}"
            : "Automatic backups are disabled.";

    private void LogAutomaticBackup(
        string operation,
        string? backup)
    {
        Log(
            backup is not null
                ? $"{operation} Backup: {backup}"
                : $"{operation} Automatic backup disabled.");
    }

    private void Log(string message)
    {
        ActivityLog.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        ActivityLog.ScrollToEnd();

        AppLog.WriteActivity(message);
    }

    private void OpenLogsFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(
                AppLog.LogsDirectory);

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        AppLog.LogsDirectory,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could Not Open Logs Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string BuildPs2CardHeader(
        string path,
        CardReadResult result,
        int saveCount)
    {
        var header =
            $"{Path.GetFileName(path)} • " +
            $"{FormatCatalog.GetPs2CardTypeName(path)} • " +
            $"{FormatSaveCount(saveCount)}";

        if (result.ContainerTotalBytes.HasValue &&
            result.BankCount is > 1 &&
            result.TotalBytes.HasValue)
        {
            header +=
                $" • {FormatBytes(result.ContainerTotalBytes.Value)} container " +
                $"({result.BankCount.Value} × " +
                $"{FormatBytes(result.TotalBytes.Value)} banks)";
        }

        return header;
    }

    private void UpdateCapacityDisplay(char side, CardReadResult result)
    {
        var container = side == 'A' ? CapacityAContainer : CapacityBContainer;
        var placeholder = side == 'A' ? CapacityAPlaceholder : CapacityBPlaceholder;
        var details = side == 'A' ? CapacityADetails : CapacityBDetails;
        var text = side == 'A' ? CapacityAText : CapacityBText;
        var progress = side == 'A' ? CapacityAProgress : CapacityBProgress;

        if (!result.TotalBytes.HasValue ||
            !result.FreeBytes.HasValue ||
            !result.UsedBytes.HasValue ||
            !result.UsedPercent.HasValue)
        {
            container.Visibility = Visibility.Collapsed;
            return;
        }

        container.Visibility = Visibility.Visible;
        placeholder.Visibility = Visibility.Collapsed;
        details.Visibility = Visibility.Visible;
        progress.Visibility = Visibility.Visible;
        progress.Value = result.UsedPercent.Value;
        text.Text =
            result.ContainerTotalBytes.HasValue &&
            result.BankCount is > 1
                ? $"{FormatBytes(result.UsedBytes.Value)} used  •  " +
                  $"{FormatBytes(result.FreeBytes.Value)} free  •  " +
                  $"{FormatBytes(result.TotalBytes.Value)} active bank  •  " +
                  $"{FormatBytes(result.ContainerTotalBytes.Value)} VM2 container"
                : $"{FormatBytes(result.UsedBytes.Value)} used  •  " +
                  $"{FormatBytes(result.FreeBytes.Value)} free  •  " +
                  $"{FormatBytes(result.TotalBytes.Value)} total";
    }

    private void UpdateFolderCapacityDisplay(
        char side)
    {
        var container =
            side == 'A'
                ? CapacityAContainer
                : CapacityBContainer;

        var placeholder =
            side == 'A'
                ? CapacityAPlaceholder
                : CapacityBPlaceholder;

        var details =
            side == 'A'
                ? CapacityADetails
                : CapacityBDetails;

        var text =
            side == 'A'
                ? CapacityAText
                : CapacityBText;

        var progress =
            side == 'A'
                ? CapacityAProgress
                : CapacityBProgress;

        container.Visibility = Visibility.Visible;
        placeholder.Visibility = Visibility.Collapsed;
        details.Visibility = Visibility.Visible;
        text.Text =
            "PCSX2 Folder Memory Card  •  Infinite capacity";

        // Hidden reserves the same layout height as a standard card's
        // progress bar, keeping Card A and Card B lists aligned.
        progress.Visibility = Visibility.Hidden;
        progress.Value = 0;
    }

    private void ResetCapacityDisplay(char side)
    {
        var container = side == 'A' ? CapacityAContainer : CapacityBContainer;
        var placeholder = side == 'A' ? CapacityAPlaceholder : CapacityBPlaceholder;
        var details = side == 'A' ? CapacityADetails : CapacityBDetails;
        var progress = side == 'A' ? CapacityAProgress : CapacityBProgress;

        container.Visibility = Visibility.Visible;
        placeholder.Visibility = Visibility.Visible;
        details.Visibility = Visibility.Collapsed;
        progress.Visibility = Visibility.Visible;
        progress.Value = 0;
    }

    private string? PickCard(string? currentPath = null)
    {
        var sourceType =
            ShowPs2CardTypeDialog();

        if (sourceType == 0)
            return null;

        if (sourceType == 2)
        {
            var folderDialog =
                new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Open PCSX2 Folder Memory Card",
                    Multiselect = false,
                    InitialDirectory =
                        !string.IsNullOrWhiteSpace(currentPath) &&
                        Directory.Exists(currentPath)
                            ? currentPath
                            : null
                };

            if (folderDialog.ShowDialog() != true)
                return null;

            var superblock =
                Path.Combine(
                    folderDialog.FolderName,
                    "_pcsx2_superblock");

            if (!File.Exists(superblock))
            {
                MessageBox.Show(
                    "That folder does not contain _pcsx2_superblock.",
                    "Not a PCSX2 Folder Card",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            return folderDialog.FolderName;
        }

        var dialog =
            new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open Standard PS2 Memory Card",
                Filter = FormatCatalog.Ps2MemoryCardFilter,
                InitialDirectory =
                    !string.IsNullOrWhiteSpace(currentPath) &&
                    File.Exists(currentPath)
                        ? Path.GetDirectoryName(currentPath)
                        : null,
                FileName =
                    !string.IsNullOrWhiteSpace(currentPath) &&
                    File.Exists(currentPath)
                        ? Path.GetFileName(currentPath)
                        : string.Empty
            };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    private int ShowPs2CardTypeDialog() =>
        ShowNewCardTypeDialog(
            "OPEN PS2 MEMORY CARD",
            "Choose the type of memory card you want to open.",
            new[]
            {
                new CardChoice(
                    FindResource("IconSourceFile") as ImageSource,
                    "Standard PS2 Memory Card",
                    "Open .ps2, .mc2, .vm2, .vmc, .bin, .mcd, or another verified PS2 image.",
                    1),
                new CardChoice(
                    FindResource("IconSourceFolder") as ImageSource,
                    "PCSX2 Folder Memory Card",
                    "Open a PCSX2 folder card containing _pcsx2_superblock.",
                    2)
            },
            "Open PS2 Memory Card");

    private sealed record CardChoice(
        ImageSource? Icon,
        string Title,
        string Description,
        int Value);

    private int ShowNewCardTypeDialog(
        string headingText,
        string subtitleText,
        IReadOnlyList<CardChoice> choices,
        string windowTitle = "New Memory Card")
    {
        var result = 0;

        var dialog =
            new Window
            {
                Title = windowTitle,
                Width = choices.Count >= 4 ? 620 : (choices.Count > 1 ? 640 : 570),
                Height = choices.Count >= 4 ? 355 : (choices.Count > 1 ? 270 : 225),
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Owner = this,
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(11, 18, 27)),
                Foreground = Brushes.White,
                ShowInTaskbar = false
            };

        var root =
            new Grid
            {
                Margin = new Thickness(22)
            };

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        var heading = new StackPanel();
        heading.Children.Add(
            new TextBlock
            {
                Text = headingText,
                FontSize = 20,
                FontWeight = FontWeights.Bold
            });
        heading.Children.Add(
            new TextBlock
            {
                Text = subtitleText,
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(159, 176, 197)),
                Margin = new Thickness(0, 6, 0, 0)
            });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var choicesPanel =
            new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Width = choices.Count >= 4 ? 520 : double.NaN
            };

        foreach (var choice in choices)
        {
            var button =
                new Button
                {
                    Width = choices.Count >= 4 ? 245 : (choices.Count > 1 ? 265 : 300),
                    Height = choices.Count > 1 ? 76 : 70,
                    Margin = new Thickness(6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Foreground = Brushes.White,
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(17, 29, 43)),
                    BorderBrush =
                        new SolidColorBrush(
                            Color.FromRgb(47, 72, 101))
                };

            var content =
                new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

            content.Children.Add(
                new Image
                {
                    Source = choice.Icon,
                    Width = 26,
                    Height = 26,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment =
                        VerticalAlignment.Center
                });

            var text = new StackPanel();
            text.Children.Add(
                new TextBlock
                {
                    Text = choice.Title,
                    FontWeight = FontWeights.SemiBold
                });
            text.Children.Add(
                new TextBlock
                {
                    Text = choice.Description,
                    FontSize = 10,
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(159, 176, 197)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = choices.Count >= 4 ? 190 : (choices.Count > 1 ? 210 : 240),
                    Margin = new Thickness(0, 3, 0, 0)
                });

            content.Children.Add(text);
            button.Content = content;
            button.Click += (_, _) =>
            {
                result = choice.Value;
                dialog.DialogResult = true;
                dialog.Close();
            };

            choicesPanel.Children.Add(button);
        }

        Grid.SetRow(choicesPanel, 1);
        root.Children.Add(choicesPanel);

        var cancel =
            new Button
            {
                Content = "Cancel",
                Width = 110,
                Height = 34,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Foreground = Brushes.White,
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(17, 29, 43)),
                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(47, 72, 101))
            };
        cancel.Click += (_, _) =>
        {
            result = 0;
            dialog.DialogResult = false;
            dialog.Close();
        };
        Grid.SetRow(cancel, 2);
        root.Children.Add(cancel);

        dialog.Content = root;
        dialog.ShowDialog();
        return result;
    }

    private int ShowFileOrFolderSourceDialog(
        string heading,
        string subtitle,
        string windowTitle) =>
        ShowNewCardTypeDialog(
            heading,
            subtitle,
            new[]
            {
                new CardChoice(
                    FindResource("IconSourceFile") as ImageSource,
                    "Save Package or MC File",
                    "Choose a supported save package or memory-card image.",
                    1),
                new CardChoice(
                    FindResource("IconSourceFolder") as ImageSource,
                    "PCSX2 Folder Memory Card",
                    "Choose a PCSX2 folder card containing _pcsx2_superblock.",
                    2)
            },
            windowTitle);

    private async void NewPs2Card_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag?.ToString() is not string sideText ||
            sideText.Length != 1)
        {
            return;
        }

        var choice =
            ShowNewCardTypeDialog(
                "CREATE PS2 MEMORY CARD",
                "Choose the type of PS2 memory card to create.",
                new[]
                {
                    new CardChoice(
                        FindResource("IconStandardPs2Card") as ImageSource,
                        "Standard PS2 Memory Card",
                        "Create a card in 8, 16, 32, or 64 MB.",
                        1),
                    new CardChoice(
                        FindResource("IconPcsx2FolderCard") as ImageSource,
                        "PCSX2 Folder Memory Card",
                        "Creates an infinite-capacity PCSX2 folder card.",
                        2)
                });

        if (choice == 1)
        {
            var sizeMb =
                ShowNewCardTypeDialog(
                    "SELECT PS2 CARD SIZE",
                    "Choose the capacity for the new memory card. 8 MB offers the widest game compatibility.",
                    new[]
                    {
                        new CardChoice(
                            FindResource("IconStandardPs2Card") as ImageSource,
                            "8 MB",
                            "Standard PS2 capacity. Recommended for maximum compatibility.",
                            8),
                        new CardChoice(
                            FindResource("IconStandardPs2Card") as ImageSource,
                            "16 MB",
                            "Extended-capacity PS2 memory card.",
                            16),
                        new CardChoice(
                            FindResource("IconStandardPs2Card") as ImageSource,
                            "32 MB",
                            "Extended-capacity PS2 memory card.",
                            32),
                        new CardChoice(
                            FindResource("IconStandardPs2Card") as ImageSource,
                            "64 MB",
                            "Extended-capacity PS2 memory card.",
                            64)
                    },
                    "PS2 Memory Card Size");

            if (sizeMb != 0)
                await CreateNewPs2FileCardAsync(sideText[0], sizeMb);
        }
        else if (choice == 2)
        {
            await CreateNewPs2FolderCardAsync(sideText[0]);
        }
    }

    private async Task CreateNewPs2FileCardAsync(
        char side,
        int sizeMb)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Create {sizeMb} MB PS2 Memory Card",
            Filter = FormatCatalog.Ps2MemoryCardFilter,
            FilterIndex = 4, // PCSX2 .ps2
            DefaultExt = ".ps2",
            AddExtension = true,
            FileName = side == 'A'
                ? $"PS2 Card A - {sizeMb}MB.ps2"
                : $"PS2 Card B - {sizeMb}MB.ps2",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
            return;

        var extension =
            Path.GetExtension(dialog.FileName).ToLowerInvariant();

        var supportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".bin", ".mc2", ".mcd", ".ps2", ".vm2", ".vmc"
            };

        if (!supportedExtensions.Contains(extension))
        {
            MessageBox.Show(
                "Choose one of PSM's supported PS2 memory-card formats: BIN, MC2, MCD, PS2, VM2, or VMC.",
                "Unsupported PS2 Card Format",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var formatName =
            extension switch
            {
                ".bin" => "BIN Raw Dump",
                ".mc2" => "MemCard PRO2",
                ".mcd" => "MCD",
                ".ps2" => "PCSX2",
                ".vm2" => "PS2 Virtual Memory Card",
                ".vmc" => "VMC",
                _ => "PS2"
            };

        // MC2 is PSM's no-ECC MemCard PRO2 representation.
        // The other supported image-card extensions use the standard ECC image.
        var noEcc = extension.Equals(
            ".mc2",
            StringComparison.OrdinalIgnoreCase);

        try
        {
            SetBusy(
                true,
                $"Creating {sizeMb} MB {formatName} memory card...");

            await _engine.CreateCardAsync(
                dialog.FileName,
                sizeMb,
                noEcc);

            Log(
                $"Created {sizeMb} MB {formatName} memory card: {dialog.FileName}");

            await LoadCardAsync(
                dialog.FileName,
                side,
                allowWhileBusy: true);

            MessageBox.Show(
                $"The {sizeMb} MB {formatName} memory card was created, verified, and opened.",
                "PS2 Card Created",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"PS2 card creation failed: {ex.Message}");
            MessageBox.Show(
                ex.Message,
                "Could Not Create PS2 Card",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task CreateNewPs2FolderCardAsync(char side)
    {
        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a Parent Folder for the PCSX2 Folder Card",
            Multiselect = false
        };

        if (folderDialog.ShowDialog() != true)
            return;

        var nameDialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Name the PCSX2 Folder Memory Card",
            InitialDirectory = folderDialog.FolderName,
            FileName = side == 'A'
                ? "PS2 Folder Card A"
                : "PS2 Folder Card B",
            Filter = "Folder name (*.foldercard)|*.foldercard",
            DefaultExt = ".foldercard",
            AddExtension = true,
            OverwritePrompt = false
        };

        if (nameDialog.ShowDialog() != true)
            return;

        var destination = Path.Combine(
            Path.GetDirectoryName(nameDialog.FileName)!,
            Path.GetFileNameWithoutExtension(nameDialog.FileName));

        try
        {
            SetBusy(true, "Creating PCSX2 folder memory card...");
            await _engine.CreateFolderCardAsync(destination);
            Log($"Created PCSX2 folder memory card: {destination}");

            MessageBox.Show(
                "The PCSX2 folder memory card was created successfully.\n\n" +
                "It can already be selected in PCSX2. Direct folder-card " +
                "browsing in PSM will be added in a later stage.",
                "Folder Card Created",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Folder-card creation failed: {ex.Message}");
            MessageBox.Show(
                ex.Message,
                "Folder Card Creation Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
            RefreshButtons();
        }
    }

    private async void NewPs1Card_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag?.ToString() is not string sideText ||
            sideText.Length != 1)
        {
            return;
        }

        var side = sideText[0];
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Create PS1 Memory Card",
            Filter = Ps1MemoryCardService.FileDialogFilter,
            DefaultExt = ".mcr",
            AddExtension = true,
            FileName = side == 'A'
                ? "PS1 Card A.mcr"
                : "PS1 Card B.mcr",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(true, "Creating standard 128 KB PS1 memory card...");
            await _ps1CardService.CreateEmptyCardAsync(dialog.FileName);
            await LoadPs1CardAsync(dialog.FileName, side);
            Log($"Created PS1 memory card: {dialog.FileName}");

            MessageBox.Show(
                "The standard 128 KB PlayStation memory card was created, verified, and opened.",
                "PS1 Card Created",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"PS1 card creation failed: {ex.Message}");
            MessageBox.Show(
                ex.Message,
                "PS1 Card Creation Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
            RefreshButtons();
        }
    }

    private async void OpenA_Click(object sender, RoutedEventArgs e) { var path = PickCard(_pathA); if (path is not null) await LoadCardAsync(path, 'A'); }
    private async void OpenB_Click(object sender, RoutedEventArgs e) { var path = PickCard(_pathB); if (path is not null) await LoadCardAsync(path, 'B'); }

    private void CloseA_Click(object sender, RoutedEventArgs e) => ClearCard('A');
    private void CloseB_Click(object sender, RoutedEventArgs e) => ClearCard('B');

    private void ClearCard(char side)
    {
        if (side == 'A')
        {
            _pathA = null;
            _allA.Clear();
            CardAList.ItemsSource = _allA;
            CardAInfo.Text = "Open or drop an .mc2/.ps2 memory card";
            PreviewTitleA.Text = "PS2 Save Preview";
            PreviewA.Text = "Select a save to view its details.";
            ApplyPs2PreviewLayout(
                'A',
                false);
            PreviewImageA.Source = null;
            PreviewPlaceholderA.Visibility = Visibility.Visible;
            _previewModelA = null;
            _previewFallbackA = null;
            ResetCapacityDisplay('A');
        }
        else
        {
            _pathB = null;
            _allB.Clear();
            CardBList.ItemsSource = _allB;
            CardBInfo.Text = "Open or drop a second PS2 memory card";
            PreviewTitleB.Text = "PS2 Save Preview";
            PreviewB.Text = "Select a save to view its details.";
            ApplyPs2PreviewLayout(
                'B',
                false);
            PreviewImageB.Source = null;
            PreviewPlaceholderB.Visibility = Visibility.Visible;
            _previewModelB = null;
            _previewFallbackB = null;
            ResetCapacityDisplay('B');
        }
        VerifiedBanner.Visibility = Visibility.Hidden;
        StatusText.Text = $"Card {side} closed. Browse or drop another card.";
        RefreshButtons();
    }

    private async void PreviewA_Click(object sender, RoutedEventArgs e) =>
        await SelectPreviewAsync('A', CardAList.SelectedItem as SaveEntry);
    private async void PreviewB_Click(object sender, RoutedEventArgs e) =>
        await SelectPreviewAsync('B', CardBList.SelectedItem as SaveEntry);

    private async void CopyAToB_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pathA is not null &&
            _pathB is not null)
        {
            await TransferSelectedPs2SavesAsync(
                _pathA,
                _pathB,
                GetSelectedPs2CardSaves(CardAList),
                'B');
        }
    }

    private async void CopyBToA_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pathA is not null &&
            _pathB is not null)
        {
            await TransferSelectedPs2SavesAsync(
                _pathB,
                _pathA,
                GetSelectedPs2CardSaves(CardBList),
                'A');
        }
    }

    private async Task TransferSelectedPs2SavesAsync(
        string source,
        string destination,
        IReadOnlyList<SaveEntry> saves,
        char destinationSide)
    {
        if (saves.Count == 0)
            return;

        if (saves.Count == 1)
        {
            await TransferAsync(
                source,
                destination,
                saves[0],
                destinationSide);
            return;
        }

        var confirmation =
            MessageBox.Show(
                $"Copy {saves.Count} selected PS2 saves to Card {destinationSide}?\n\n" +
                (_automaticBackupsEnabled
                    ? "PSM will create one backup of the destination card before committing the transfer."
                    : "Automatic Backups is off. PSM will still verify the complete transfer before committing it."),
                "Confirm PS2 Save Transfer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
            return;

        var temporaryDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "PSAM-BATCH-" +
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            SetBusy(
                true,
                $"Transferring {saves.Count} PS2 saves...");

            VerifiedBanner.Visibility = Visibility.Hidden;

            var destinationIsFolder =
                Directory.Exists(destination);

            var temporaryCard =
                destinationIsFolder
                    ? Path.Combine(
                        temporaryDirectory,
                        "FolderCard")
                    : Path.Combine(
                        temporaryDirectory,
                        Path.GetFileName(destination));

            if (destinationIsFolder)
                CopyDirectory(destination, temporaryCard);
            else
                File.Copy(destination, temporaryCard, true);

            var existingDestinationSaves =
                await _engine.ReadDirectoryAsync(
                    temporaryCard);

            var conflicts =
                saves.Where(
                    selected =>
                        existingDestinationSaves.Any(
                            candidate =>
                                candidate.DirectoryId.Equals(
                                    selected.DirectoryId,
                                    StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

            if (conflicts.Length > 0 &&
                ReplaceExisting.IsChecked != true)
            {
                MessageBox.Show(
                    conflicts.Length == 1
                        ? $"{conflicts[0].Title}\n{conflicts[0].DirectoryId}\n\nalready exists on {Path.GetFileName(destination)}.\n\n" +
                          "Enable \"Replace save if it already exists\" to overwrite it."
                        : $"{conflicts.Length} selected saves already exist on {Path.GetFileName(destination)}.\n\n" +
                          "Enable \"Replace save if it already exists\" to overwrite them.",
                    "Save Already Exists",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            foreach (var save in saves)
            {
                var psu =
                    Path.Combine(
                        temporaryDirectory,
                        SanitizeUniversalFileName(
                            save.DirectoryId) +
                        "-" +
                        Guid.NewGuid().ToString("N") +
                        ".psu");

                Log(
                    $"Exporting {save.DirectoryId} from source card.");

                await _engine.ExportPsuAsync(
                    source,
                    save.DirectoryId,
                    psu);

                if (ReplaceExisting.IsChecked == true)
                {
                    await _engine.DeleteAsync(
                        temporaryCard,
                        save.DirectoryId);
                }

                await _engine.ImportAsync(
                    temporaryCard,
                    psu);
            }

            Log(
                "Verifying temporary destination card.");

            await _engine.CheckAsync(
                temporaryCard);

            var verifiedSaves =
                await _engine.ReadDirectoryAsync(
                    temporaryCard);

            var missing =
                saves.Where(
                    selected =>
                        !verifiedSaves.Any(
                            candidate =>
                                candidate.DirectoryId.Equals(
                                    selected.DirectoryId,
                                    StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

            if (missing.Length > 0)
            {
                throw new InvalidDataException(
                    $"{missing.Length} selected save(s) were not present after transfer verification.");
            }

            var backup =
                destinationIsFolder
                    ? CreateAutomaticFolderBackup(destination)
                    : CreateAutomaticBackup(destination);

            if (destinationIsFolder)
            {
                Directory.Delete(
                    destination,
                    recursive: true);
                CopyDirectory(
                    temporaryCard,
                    destination);
            }
            else
            {
                File.Copy(
                    temporaryCard,
                    destination,
                    true);
            }

            await LoadCardAsync(
                destination,
                destinationSide,
                saves[^1].DirectoryId,
                allowWhileBusy: true);

            VerifiedText.Text =
                $"TRANSFER VERIFIED - {saves.Count} saves copied successfully";
            VerifiedBanner.Visibility =
                Visibility.Visible;
            StatusText.Text =
                "Transfer verified.";

            LogAutomaticBackup(
                $"Batch transfer committed. {saves.Count} saves.",
                backup);

            MessageBox.Show(
                $"{saves.Count} PS2 saves were copied and verified successfully.\n\n" +
                AutomaticBackupDetails(backup),
                "Transfer Verified",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                $"Batch PS2 transfer failed: {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "Transfer Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryDirectory,
                    true);
            }
            catch { }

            SetBusy(false, "Ready.");
            RefreshButtons();
        }
    }

    private async void ExportA_Click(object sender, RoutedEventArgs e) => await ExportSelectedAsync(_pathA, CardAList.SelectedItem as SaveEntry);
    private async void ExportB_Click(object sender, RoutedEventArgs e) => await ExportSelectedAsync(_pathB, CardBList.SelectedItem as SaveEntry);

    private async Task ExportSelectedAsync(string? card, SaveEntry? save)
    {
        if (card is null || save is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export PS2 Save",
            Filter = FormatCatalog.Ps2SaveExportFilter,
            DefaultExt = ".psu",
            FileName = save.DirectoryId + ".psu",
            AddExtension = true,
            OverwritePrompt = true,
            FilterIndex = 3
        };

        if (dialog.ShowDialog() != true) return;

        var output = Path.GetFullPath(dialog.FileName);
        var extension = Path.GetExtension(output).ToLowerInvariant();

        if (extension == ".foldercard")
        {
            output =
                Path.Combine(
                    Path.GetDirectoryName(output)!,
                    Path.GetFileNameWithoutExtension(output));
        }

        if (extension == ".mc2")
        {
            output = PromptForMemCardPro2ReadyOutput(output, save.DirectoryId);
            if (string.IsNullOrWhiteSpace(output)) return;
        }

        try
        {
            SetBusy(true, "Exporting save...");

            if (extension == ".ps2save")
            {
                await _ps2PackageService.ExportFromCardAsync(
                    card,
                    save,
                    output);

                Log(
                    $"Exported {save.DirectoryId} to PSM PS2 save package {output}.");

                MessageBox.Show(
                    "PSM PlayStation Save Package exported and verified successfully.",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (extension == ".foldercard")
            {
                await CreateSingleSaveFolderCardAsync(
                    card,
                    save.DirectoryId,
                    output);

                Log(
                    $"Exported {save.DirectoryId} to single-save PCSX2 folder card {output}.");

                MessageBox.Show(
                    $"A fresh PCSX2 folder memory card containing only the selected save was created and verified.\n\n{output}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (extension is ".ps2" or ".mc2" or ".vm2" or ".vmc" or ".bin" or ".mcd")
            {
                await CreateSingleSaveCardAsync(card, save.DirectoryId, output, extension == ".mc2");
                Log($"Exported {save.DirectoryId} to single-save card {output}.");
                MessageBox.Show(
                    $"A fresh memory card containing only the selected save was created and verified.\n\n{output}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                await _engine.ExportPackageAsync(card, save.DirectoryId, output);
                Log($"Exported {save.DirectoryId} to {output}.");
                MessageBox.Show(
                    "Save exported and verified successfully.",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(output))
                    File.Delete(output);
                else if (Directory.Exists(output))
                    Directory.Delete(output, recursive: true);
            }
            catch { }

            MessageBox.Show(
                ex.Message,
                "Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally { SetBusy(false, "Ready."); }
    }

    private async Task CreateSingleSaveCardAsync(
        string sourceCard,
        string directoryId,
        string output,
        bool noEcc)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-SINGLE-SAVE-CARD-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var package = Path.Combine(temporaryRoot, directoryId + ".psu");
            await _engine.ExportPackageAsync(sourceCard, directoryId, package);

            if (File.Exists(output)) File.Delete(output);
            await _engine.CreateCardAsync(output, noEcc);
            await _engine.ImportAsync(output, package);
            await _engine.CheckAsync(output);

            var saves = await _engine.ReadDirectoryAsync(output);
            if (saves.Count != 1 ||
                !saves[0].DirectoryId.Equals(directoryId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The single-save memory card could not be verified exactly.");
            }
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); } catch { }
        }
    }

    private async Task CreateSingleSaveFolderCardAsync(
        string sourceCard,
        string directoryId,
        string outputDirectory)
    {
        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-SINGLE-SAVE-FOLDER-CARD-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var temporaryCard =
                Path.Combine(
                    temporaryRoot,
                    "single-save.ps2");

            await CreateSingleSaveCardAsync(
                sourceCard,
                directoryId,
                temporaryCard,
                noEcc: false);

            var temporaryFolder =
                Path.Combine(
                    temporaryRoot,
                    "FolderCard");

            await _engine.ConvertToPcsx2FolderCardAsync(
                temporaryCard,
                temporaryFolder);

            await _engine.CheckAsync(temporaryFolder);

            var saves =
                await _engine.ReadDirectoryAsync(
                    temporaryFolder);

            if (saves.Count != 1 ||
                !saves[0].DirectoryId.Equals(
                    directoryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The single-save PCSX2 folder memory card could not be verified exactly.");
            }

            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);

            CopyDirectory(
                temporaryFolder,
                outputDirectory);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
            catch { }
        }
    }

    private void BackupA_Click(object sender, RoutedEventArgs e) { if (_pathA is not null) ShowBackup(_pathA); }
    private void BackupB_Click(object sender, RoutedEventArgs e) { if (_pathB is not null) ShowBackup(_pathB); }
    private void ShowBackup(string path)
    {
        var backup =
            Directory.Exists(path)
                ? CreateFolderBackup(path)
                : CreateBackup(path);

        Log($"Backup created: {backup}");
        MessageBox.Show(
            $"Backup created:\n{backup}",
            "Backup",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void SaveAsA_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pathA is not null)
            await SavePs2CardAsAsync(_pathA, CardAList.SelectedItem as SaveEntry);
    }

    private async void SaveAsB_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pathB is not null)
            await SavePs2CardAsAsync(_pathB, CardBList.SelectedItem as SaveEntry);
    }

    private async Task SavePs2CardAsAsync(
        string sourcePath,
        SaveEntry? selectedSave)
    {
        var sourceExtension =
            Path.GetExtension(sourcePath)
                .ToLowerInvariant();

        var supportedImageExtensions =
            new[]
            {
                ".bin",
                ".mc2",
                ".mcd",
                ".ps2",
                ".vm2",
                ".vmc"
            };

        var defaultExtension =
            supportedImageExtensions.Contains(sourceExtension)
                ? sourceExtension
                : ".ps2";

        var sourceBaseName =
            Directory.Exists(sourcePath)
                ? Path.GetFileName(sourcePath)
                : Path.GetFileNameWithoutExtension(sourcePath);

        var filterEntries =
            new[]
            {
                ("PS2 Memory Card - BIN • Raw Dump", ".bin"),
                ("PS2 Memory Card - MC2 • MemCard PRO2", ".mc2"),
                ("PS2 Memory Card - MCD • Memory Card Image", ".mcd"),
                ("PS2 Memory Card - PS2 • PCSX2 Virtual Memory Card", ".ps2"),
                ("PS2 Memory Card - VM2 • Virtual Memory Card", ".vm2"),
                ("PS2 Memory Card - VMC • Virtual Memory Card", ".vmc")
            };

        var filter =
            string.Join(
                "|",
                filterEntries.Select(
                    entry =>
                        $"{entry.Item1} (*{entry.Item2})|*{entry.Item2}")) +
            "|PS2 Memory Card - Folder • PCSX2 Folder Card|*.*";

        var defaultFilterIndex =
            Array.FindIndex(
                filterEntries,
                entry =>
                    entry.Item2.Equals(
                        defaultExtension,
                        StringComparison.OrdinalIgnoreCase)) +
            1;

        if (defaultFilterIndex <= 0)
            defaultFilterIndex = 4;

        var dialog =
            new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save PS2 Memory Card As",
                Filter = filter,
                DefaultExt = defaultExtension,
                FileName =
                    sourceBaseName +
                    "_converted" +
                    defaultExtension,
                AddExtension = true,
                OverwritePrompt = true,
                FilterIndex = defaultFilterIndex
            };

        if (dialog.ShowDialog() != true)
            return;

        var folderCard =
            dialog.FilterIndex ==
            filterEntries.Length + 1;

        var targetExtension =
            folderCard
                ? ".foldercard"
                : filterEntries[
                    dialog.FilterIndex - 1]
                    .Item2;

        var destinationPath =
            Path.GetFullPath(
                dialog.FileName);

        if (folderCard)
        {
            destinationPath =
                Path.Combine(
                    Path.GetDirectoryName(
                        destinationPath)!,
                    Path.GetFileNameWithoutExtension(
                        destinationPath));
        }
        else
        {
            destinationPath =
                Path.ChangeExtension(
                    destinationPath,
                    targetExtension);
        }

        if (!folderCard &&
            targetExtension.Equals(
                ".mc2",
                StringComparison.OrdinalIgnoreCase))
        {
            var preferredDirectoryId =
                selectedSave?.DirectoryId;

            if (string.IsNullOrWhiteSpace(
                    preferredDirectoryId))
            {
                try
                {
                    var saves =
                        await _engine.ReadDirectoryAsync(
                            sourcePath);

                    preferredDirectoryId =
                        saves
                            .Select(
                                save =>
                                    save.DirectoryId)
                            .FirstOrDefault(
                                id =>
                                    !string.IsNullOrWhiteSpace(
                                        ExtractGameSerial(id)));
                }
                catch
                {
                }
            }

            destinationPath =
                PromptForMemCardPro2ReadyOutput(
                    destinationPath,
                    preferredDirectoryId);

            if (string.IsNullOrWhiteSpace(
                    destinationPath))
            {
                return;
            }
        }

        if (!folderCard &&
            !Directory.Exists(sourcePath) &&
            Path.GetFullPath(sourcePath).Equals(
                destinationPath,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Choose a different filename or location.",
                "Save Card As",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-SAVE-CARD-AS-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            temporaryRoot);

        try
        {
            SetBusy(
                true,
                folderCard
                    ? "Creating a verified PCSX2 folder memory card..."
                    : $"Converting PS2 card to {targetExtension.ToUpperInvariant()}...");

            if (folderCard)
            {
                var temporaryFolder =
                    Path.Combine(
                        temporaryRoot,
                        "FolderCard");

                if (Directory.Exists(sourcePath))
                {
                    CopyDirectory(
                        sourcePath,
                        temporaryFolder);
                }
                else
                {
                    await _engine.ConvertToPcsx2FolderCardAsync(
                        sourcePath,
                        temporaryFolder);
                }

                await _engine.CheckAsync(
                    temporaryFolder);

                if (Directory.Exists(
                        destinationPath))
                {
                    Directory.Delete(
                        destinationPath,
                        recursive: true);
                }

                CopyDirectory(
                    temporaryFolder,
                    destinationPath);

                Log(
                    $"PCSX2 folder card created and verified: {destinationPath}");

                MessageBox.Show(
                    "The PCSX2 folder memory card was created and verified.\n\n" +
                    destinationPath,
                    "Folder Card Created",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var temporaryCard =
                Path.Combine(
                    temporaryRoot,
                    "converted" +
                    targetExtension);

            if (Directory.Exists(sourcePath))
            {
                await _engine.ConvertFolderCardToImageAsync(
                    sourcePath,
                    temporaryCard,
                    noEcc:
                        targetExtension ==
                        ".mc2");
            }
            else
            {
                var sourceRead =
                    await _engine.ReadCardAsync(
                        sourcePath);

                var sourceSaves =
                    sourceRead.Saves;

                var sourceMegabytes =
                    sourceRead.TotalBytes.HasValue
                        ? (int)Math.Round(
                            sourceRead.TotalBytes.Value /
                            1024d /
                            1024d,
                            MidpointRounding.AwayFromZero)
                        : 8;

                var targetMegabytes =
                    sourceMegabytes switch
                    {
                        <= 8 => 8,
                        <= 16 => 16,
                        <= 32 => 32,
                        _ => 64
                    };

                await _engine.CreateCardAsync(
                    temporaryCard,
                    targetMegabytes,
                    noEcc:
                        targetExtension ==
                        ".mc2");

                for (
                    var index = 0;
                    index < sourceSaves.Count;
                    index++)
                {
                    var save =
                        sourceSaves[index];

                    StatusText.Text =
                        $"Save Card As: {index + 1} of {sourceSaves.Count} - " +
                        save.DirectoryId;

                    var psu =
                        Path.Combine(
                            temporaryRoot,
                            $"{index:D4}-" +
                            SanitizeUniversalFileName(
                                save.DirectoryId) +
                            ".psu");

                    await _engine.ExportPsuAsync(
                        sourcePath,
                        save.DirectoryId,
                        psu);

                    await _engine.ImportAsync(
                        temporaryCard,
                        psu);
                }

                var rebuilt =
                    await _engine.ReadDirectoryAsync(
                        temporaryCard);

                if (rebuilt.Count !=
                    sourceSaves.Count)
                {
                    throw new InvalidOperationException(
                        $"Verification mismatch: source has {sourceSaves.Count} saves; " +
                        $"output has {rebuilt.Count}.");
                }
            }

            await _engine.CheckAsync(
                temporaryCard);

            var destinationDirectory =
                Path.GetDirectoryName(
                    destinationPath);

            if (!string.IsNullOrWhiteSpace(
                    destinationDirectory))
            {
                Directory.CreateDirectory(
                    destinationDirectory);
            }

            if (File.Exists(
                    destinationPath))
            {
                var backup =
                    CreateAutomaticBackup(
                        destinationPath);

                LogAutomaticBackup(
                    "Existing Save Card As destination replaced.",
                    backup);
            }

            File.Copy(
                temporaryCard,
                destinationPath,
                overwrite: true);

            await _engine.CheckAsync(
                destinationPath);

            Log(
                $"PS2 memory card converted and verified: {destinationPath}");

            MessageBox.Show(
                $"PS2 memory card converted and verified.\n\n" +
                destinationPath,
                "PS2 Card Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            try
            {
                if (folderCard &&
                    Directory.Exists(
                        destinationPath))
                {
                    Directory.Delete(
                        destinationPath,
                        recursive: true);
                }
                else if (File.Exists(
                             destinationPath))
                {
                    File.Delete(
                        destinationPath);
                }
            }
            catch
            {
            }

            Log(
                "PS2 Save Card As failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "PS2 Card Save Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                if (Directory.Exists(
                        temporaryRoot))
                {
                    Directory.Delete(
                        temporaryRoot,
                        recursive: true);
                }
            }
            catch
            {
            }

            SetBusy(
                false,
                "Ready.");
        }
    }

    private async void DeleteA_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pathA is null)
            return;

        await DeletePs2SavesAsync(
            _pathA,
            GetSelectedPs2CardSaves(CardAList),
            'A');
    }

    private async void DeleteB_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pathB is null)
            return;

        await DeletePs2SavesAsync(
            _pathB,
            GetSelectedPs2CardSaves(CardBList),
            'B');
    }

    private async Task DeletePs2SavesAsync(
        string cardPath,
        IReadOnlyList<SaveEntry> saves,
        char side)
    {
        if (saves.Count == 0)
            return;

        var description =
            saves.Count == 1
                ? $"{saves[0].Title}\n\n{saves[0].DirectoryId}"
                : $"{saves.Count} selected PS2 saves";

        var confirmation =
            MessageBox.Show(
                $"Delete {description}?\n\n" +
                (_automaticBackupsEnabled
                    ? "PSM will create one timestamped backup of the card before committing the deletion."
                    : "Automatic backups are disabled. PSM will still verify every deletion before committing it."),
                saves.Count == 1
                    ? "Delete Save"
                    : "Delete Selected Saves",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-Delete-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryRoot);

        try
        {
            SetBusy(
                true,
                saves.Count == 1
                    ? $"Deleting {saves[0].DirectoryId}..."
                    : $"Deleting {saves.Count} selected PS2 saves...");

            var folderCard =
                Directory.Exists(cardPath);

            var temporaryCard =
                folderCard
                    ? Path.Combine(temporaryRoot, "FolderCard")
                    : Path.Combine(temporaryRoot, Path.GetFileName(cardPath));

            if (folderCard)
                CopyDirectory(cardPath, temporaryCard);
            else
                File.Copy(cardPath, temporaryCard, true);

            foreach (var save in saves)
            {
                await _engine.DeleteAsync(
                    temporaryCard,
                    save.DirectoryId);
            }

            await _engine.CheckAsync(temporaryCard);

            var verified =
                await _engine.ReadDirectoryAsync(temporaryCard);

            var stillPresent =
                saves.Where(selected =>
                    verified.Any(candidate =>
                        candidate.DirectoryId.Equals(
                            selected.DirectoryId,
                            StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

            if (stillPresent.Length > 0)
            {
                throw new InvalidDataException(
                    $"{stillPresent.Length} selected save(s) were still present after deletion verification.");
            }

            var backup =
                folderCard
                    ? CreateAutomaticFolderBackup(cardPath)
                    : CreateAutomaticBackup(cardPath);

            if (folderCard)
            {
                Directory.Delete(cardPath, recursive: true);
                CopyDirectory(temporaryCard, cardPath);
            }
            else
            {
                File.Copy(
                    temporaryCard,
                    cardPath,
                    overwrite: true);
            }

            await LoadCardAsync(
                cardPath,
                side,
                allowWhileBusy: true);

            LogAutomaticBackup(
                saves.Count == 1
                    ? $"Deleted {saves[0].DirectoryId}."
                    : $"Deleted {saves.Count} selected PS2 saves.",
                backup);

            MessageBox.Show(
                $"{saves.Count} PS2 save{(saves.Count == 1 ? "" : "s")} deleted and verified.\n\n" +
                AutomaticBackupDetails(backup),
                "Delete Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Delete failed: {ex.Message}");
            MessageBox.Show(
                ex.Message,
                "Delete Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
            catch { }

            SetBusy(false, "Ready.");
            RefreshButtons();
        }
    }

    private async void CardAList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await SelectPreviewAsync('A', CardAList.SelectedItem as SaveEntry);
        RefreshButtons();
    }

    private async void CardBList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await SelectPreviewAsync('B', CardBList.SelectedItem as SaveEntry);
        RefreshButtons();
    }

    private async Task LoadThumbnailsAsync(string cardPath, IReadOnlyList<SaveEntry> saves, char side)
    {
        using var throttle = new SemaphoreSlim(2);
        var tasks = saves.Select(async save =>
        {
            await throttle.WaitAsync();
            try
            {
                if (BuiltInSaveIcons.IsSystemConfiguration(save.DirectoryId, save.GameTitle))
                {
                    var systemIcon = BuiltInSaveIcons.RenderSystemConfiguration(48, 48);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        save.IconImage = systemIcon;
                        if ((side == 'A' ? CardAList.SelectedItem : CardBList.SelectedItem) == save)
                            _ = SelectPreviewAsync(side, save);
                    });
                    return;
                }

                var iconResult = await _iconService.LoadResultAsync(cardPath, save.DirectoryId);
                var model = iconResult.Model;
                var thumbnail = model is not null
                    ? await Task.Run(() => model.Render(48, 48, 0, Ps2IconFrontRotation))
                    : iconResult.IsCorrupted
                        ? await Task.Run(() => BuiltInSaveIcons.RenderCorruptedSave(48, 48))
                        : null;
                if (thumbnail is null) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    save.IconModel = model;
                    save.IconImage = thumbnail;
                    if ((side == 'A' ? CardAList.SelectedItem : CardBList.SelectedItem) == save)
                        _ = SelectPreviewAsync(side, save);
                });
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    private void ApplyPs2PreviewLayout(
        char side,
        bool hasSave)
    {
        var content =
            side == 'A'
                ? PreviewContentA
                : PreviewContentB;

        var title =
            side == 'A'
                ? PreviewTitleA
                : PreviewTitleB;

        var details =
            side == 'A'
                ? PreviewA
                : PreviewB;

        if (!hasSave)
        {
            content.VerticalAlignment =
                VerticalAlignment.Center;

            content.Margin =
                new Thickness(12, 0, 0, 0);

            title.FontSize = 17;
            title.LineHeight = double.NaN;
            title.MaxHeight = double.PositiveInfinity;

            details.FontSize = 12;
            details.LineHeight = double.NaN;
            details.Margin =
                new Thickness(0, 5, 0, 0);

            return;
        }

        content.VerticalAlignment =
            VerticalAlignment.Top;

        content.Margin =
            new Thickness(12, 1, 0, 0);

        title.FontSize = 16;
        title.LineHeight = 18;
        title.MaxHeight = 36;

        details.FontSize = 11;
        details.LineHeight = 14;
        details.Margin =
            new Thickness(0, 2, 0, 0);
    }

    private async Task SelectPreviewAsync(char side, SaveEntry? save)
    {
        var targetText = side == 'A' ? PreviewA : PreviewB;
        var targetTitle = side == 'A' ? PreviewTitleA : PreviewTitleB;
        var targetImage = side == 'A' ? PreviewImageA : PreviewImageB;
        var targetPlaceholder =
            side == 'A'
                ? PreviewPlaceholderA
                : PreviewPlaceholderB;
        var cardPath = side == 'A' ? _pathA : _pathB;

        ApplyPs2PreviewLayout(
            side,
            save is not null);

        if (save is null)
        {
            targetTitle.Text = "PS2 Save Preview";
            targetText.Text = "Select a save to view its details.";
            targetPlaceholder.Visibility = Visibility.Visible;
        }
        else
        {
            var gameSerial = ExtractGameSerial(save.DirectoryId);
            var gameMetadata = _gameMetadataService.Lookup(
                gameSerial,
                save.GameTitle,
                InferRegion(gameSerial, save.DirectoryId));

            targetTitle.Text =
                !string.IsNullOrWhiteSpace(gameMetadata?.Title)
                    ? gameMetadata.Title
                    : save.GameTitle;

            targetText.Text =
                $"{save.ProfileName}\n" +
                $"Directory ID: {save.DirectoryId}\n" +
                $"Game Serial: " +
                $"{(string.IsNullOrWhiteSpace(gameSerial) ? "Unknown" : gameSerial)}\n" +
                $"Format: PS2 Memory Card Save Directory\n" +
                $"Size: {save.SizeText}";

            targetPlaceholder.Visibility = Visibility.Collapsed;
        }

        if (save is null || cardPath is null)
        {
            targetImage.Source = null;
            if (side == 'A')
            {
                _previewModelA = null;
                _previewFallbackA = null;
            }
            else
            {
                _previewModelB = null;
                _previewFallbackB = null;
            }
            return;
        }

        if (BuiltInSaveIcons.IsSystemConfiguration(save.DirectoryId, save.GameTitle))
        {
            var systemIcon = BuiltInSaveIcons.RenderSystemConfiguration(180, 165);
            targetImage.Source = systemIcon;
            targetPlaceholder.Visibility = Visibility.Collapsed;
            save.IconImage ??= BuiltInSaveIcons.RenderSystemConfiguration(48, 48);
            if (side == 'A')
            {
                _previewModelA = null;
                _previewFallbackA = BuiltInSaveIcons.GetSystemModel();
            }
            else
            {
                _previewModelB = null;
                _previewFallbackB = BuiltInSaveIcons.GetSystemModel();
            }
            return;
        }

        var iconResult = save.IconModel is not null
            ? Ps2IconLoadResult.Success(save.IconModel)
            : await _iconService.LoadResultAsync(cardPath, save.DirectoryId);
        var model = iconResult.Model;
        save.IconModel = model;

        if (side == 'A')
        {
            _previewModelA = model;
            _previewRotationStartA = _iconAnimationClock.Elapsed.TotalSeconds;
            _previewFallbackA = iconResult.IsCorrupted ? BuiltInSaveIcons.GetCorruptedModel() : null;
        }
        else
        {
            _previewModelB = model;
            _previewRotationStartB = _iconAnimationClock.Elapsed.TotalSeconds;
            _previewFallbackB = iconResult.IsCorrupted ? BuiltInSaveIcons.GetCorruptedModel() : null;
        }

        if (model is null)
        {
            if (iconResult.IsCorrupted)
            {
                targetImage.Source = await Task.Run(() => BuiltInSaveIcons.RenderCorruptedSave(150, 138));
                targetPlaceholder.Visibility = Visibility.Collapsed;
                save.IconImage ??= BuiltInSaveIcons.RenderCorruptedSave(48, 48);
            }
            else
            {
                targetImage.Source = save.IconImage;
            }

            targetPlaceholder.Visibility =
                targetImage.Source is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            return;
        }

        var rendered = await Task.Run(() => model.Render(150, 138, _iconAnimationClock.Elapsed.TotalSeconds, Ps2IconFrontRotation));
        targetImage.Source = rendered;
        targetPlaceholder.Visibility = Visibility.Collapsed;
        save.IconImage ??= await Task.Run(() => model.Render(48, 48, 0, Ps2IconFrontRotation));
    }

    private async void IconAnimationTimer_Tick(object? sender, EventArgs e)
    {
        var time = _iconAnimationClock.Elapsed.TotalSeconds;

        if (_previewModelA is not null && !_previewRenderA)
        {
            _previewRenderA = true;
            try
            {
                var model = _previewModelA;
                var rotationTime = Math.Max(0, time - _previewRotationStartA);
                var frame = await Task.Run(() => model.Render(150, 138, time, Ps2IconFrontRotation + rotationTime * 0.42));
                if (ReferenceEquals(model, _previewModelA)) PreviewImageA.Source = frame;
            }
            finally { _previewRenderA = false; }
        }

        if (_previewModelB is not null && !_previewRenderB)
        {
            _previewRenderB = true;
            try
            {
                var model = _previewModelB;
                var rotationTime = Math.Max(0, time - _previewRotationStartB);
                var frame = await Task.Run(() => model.Render(150, 138, time, Ps2IconFrontRotation + rotationTime * 0.42));
                if (ReferenceEquals(model, _previewModelB)) PreviewImageB.Source = frame;
            }
            finally { _previewRenderB = false; }
        }

        if (_previewModelA is null && _previewFallbackA is not null && !_previewRenderA)
        {
            _previewRenderA = true;
            try
            {
                var model = _previewFallbackA;
                var frame = await Task.Run(() => model.Render(150, 138, time * 0.42));
                if (ReferenceEquals(model, _previewFallbackA)) PreviewImageA.Source = frame;
            }
            finally { _previewRenderA = false; }
        }

        if (_previewModelB is null && _previewFallbackB is not null && !_previewRenderB)
        {
            _previewRenderB = true;
            try
            {
                var model = _previewFallbackB;
                var frame = await Task.Run(() => model.Render(150, 138, time * 0.42));
                if (ReferenceEquals(model, _previewFallbackB)) PreviewImageB.Source = frame;
            }
            finally { _previewRenderB = false; }
        }

        if (!_libraryPreviewRendering &&
            (_saveLibraryContentMode ==
                SaveLibraryContentMode.GameSaves ||
             SaveInformationTab.IsSelected) &&
            (SaveLibraryTab.IsSelected ||
             SaveInformationTab.IsSelected) &&
            (_libraryPreviewModel is not null ||
             _libraryPreviewFallback is not null))
        {
            _libraryPreviewRendering = true;
            try
            {
                if (_libraryPreviewModel is not null)
                {
                    var model = _libraryPreviewModel;
                    var rotationTime = Math.Max(0, time - _libraryPreviewRotationStart);
                    var frame = await Task.Run(() =>
                        model.Render(160, 160, time, Ps2IconFrontRotation + rotationTime * 0.42));

                    if (ReferenceEquals(model, _libraryPreviewModel))
                    {
                        if (SaveLibraryTab.IsSelected)
                            LibraryPreviewImage.Source = frame;
                        if (SaveInformationTab.IsSelected)
                        {
                            SaveInfoPreviewImage.Source = frame;
                            SaveInfoPreviewPlaceholder.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                else if (_libraryPreviewFallback is not null)
                {
                    var model = _libraryPreviewFallback;
                    var frame = await Task.Run(() =>
                        model.Render(160, 160, time * 0.42));

                    if (ReferenceEquals(model, _libraryPreviewFallback))
                    {
                        if (SaveLibraryTab.IsSelected)
                            LibraryPreviewImage.Source = frame;
                        if (SaveInformationTab.IsSelected)
                        {
                            SaveInfoPreviewImage.Source = frame;
                            SaveInfoPreviewPlaceholder.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }
            finally
            {
                _libraryPreviewRendering = false;
            }
        }
    }

    private void FilterA_Click(object sender, RoutedEventArgs e) =>
        ShowSortMenu(FilterAButton, 'A');

    private void FilterB_Click(object sender, RoutedEventArgs e) =>
        ShowSortMenu(FilterBButton, 'B');

    private void ShowSortMenu(Button anchor, char side)
    {
        var field = side == 'A' ? _sortFieldA : _sortFieldB;
        var descending = side == 'A' ? _sortDescendingA : _sortDescendingB;

        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 18, 27)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 60, 82)),
            BorderThickness = new Thickness(1),
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        menu.Items.Add(CreateSortMenuItem("Game Name", field == SaveSortField.GameName,
            () => SetSort(side, SaveSortField.GameName, descending)));
        menu.Items.Add(CreateSortMenuItem("Directory ID", field == SaveSortField.DirectoryId,
            () => SetSort(side, SaveSortField.DirectoryId, descending)));
        menu.Items.Add(CreateSortMenuItem("Size", field == SaveSortField.Size,
            () => SetSort(side, SaveSortField.Size, descending)));
        menu.Items.Add(new Separator());

        var ascendingLabel = field == SaveSortField.Size ? "Small to Large" : "A to Z";
        var descendingLabel = field == SaveSortField.Size ? "Large to Small" : "Z to A";

        menu.Items.Add(CreateSortMenuItem(ascendingLabel, !descending,
            () => SetSort(side, field, false)));
        menu.Items.Add(CreateSortMenuItem(descendingLabel, descending,
            () => SetSort(side, field, true)));

        menu.IsOpen = true;
    }

    private static MenuItem CreateSortMenuItem(string header, bool isChecked, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = isChecked,
            Padding = new Thickness(12, 7, 18, 7)
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetSort(char side, SaveSortField field, bool descending)
    {
        if (side == 'A')
        {
            _sortFieldA = field;
            _sortDescendingA = descending;
        }
        else
        {
            _sortFieldB = field;
            _sortDescendingB = descending;
        }

        ApplyFilter(side);
    }

    private void SearchA_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchClearButton(SearchA, ClearSearchAButton);
        ApplyFilter('A');
    }

    private void SearchB_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchClearButton(SearchB, ClearSearchBButton);
        ApplyFilter('B');
    }

    private void Ps1SearchA_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchClearButton(Ps1SearchA, ClearPs1SearchAButton);
        ApplyPs1Filter('A');
    }

    private void Ps1SearchB_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchClearButton(Ps1SearchB, ClearPs1SearchBButton);
        ApplyPs1Filter('B');
    }

    private void Ps1FilterA_Click(object sender, RoutedEventArgs e) =>
        ShowPs1SortMenu(Ps1FilterAButton, 'A');

    private void Ps1FilterB_Click(object sender, RoutedEventArgs e) =>
        ShowPs1SortMenu(Ps1FilterBButton, 'B');

    private void ShowPs1SortMenu(Button anchor, char side)
    {
        var field =
            side == 'A'
                ? _ps1SortFieldA
                : _ps1SortFieldB;

        var descending =
            side == 'A'
                ? _ps1SortDescendingA
                : _ps1SortDescendingB;

        var menu = new ContextMenu
        {
            Background =
                new SolidColorBrush(
                    Color.FromRgb(11, 18, 27)),
            Foreground = Brushes.White,
            BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(42, 60, 82)),
            BorderThickness = new Thickness(1),
            PlacementTarget = anchor,
            Placement =
                System.Windows.Controls.Primitives
                    .PlacementMode.Bottom
        };

        menu.Items.Add(
            CreateSortMenuItem(
                "Game Name",
                field == Ps1SortField.GameName,
                () => SetPs1Sort(
                    side,
                    Ps1SortField.GameName,
                    descending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Save Description",
                field == Ps1SortField.SaveDescription,
                () => SetPs1Sort(
                    side,
                    Ps1SortField.SaveDescription,
                    descending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Product Code",
                field == Ps1SortField.ProductCode,
                () => SetPs1Sort(
                    side,
                    Ps1SortField.ProductCode,
                    descending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Blocks Used",
                field == Ps1SortField.BlocksUsed,
                () => SetPs1Sort(
                    side,
                    Ps1SortField.BlocksUsed,
                    descending)));

        menu.Items.Add(new Separator());

        var ascendingLabel =
            field == Ps1SortField.BlocksUsed
                ? "Fewest to Most"
                : "A to Z";

        var descendingLabel =
            field == Ps1SortField.BlocksUsed
                ? "Most to Fewest"
                : "Z to A";

        menu.Items.Add(
            CreateSortMenuItem(
                ascendingLabel,
                !descending,
                () => SetPs1Sort(
                    side,
                    field,
                    false)));

        menu.Items.Add(
            CreateSortMenuItem(
                descendingLabel,
                descending,
                () => SetPs1Sort(
                    side,
                    field,
                    true)));

        menu.IsOpen = true;
    }

    private void SetPs1Sort(
        char side,
        Ps1SortField field,
        bool descending)
    {
        if (side == 'A')
        {
            _ps1SortFieldA = field;
            _ps1SortDescendingA = descending;
        }
        else
        {
            _ps1SortFieldB = field;
            _ps1SortDescendingB = descending;
        }

        ApplyPs1Filter(side);
    }

    private void ApplyPs1Filter(char side)
    {
        var query =
            (side == 'A'
                ? Ps1SearchA.Text
                : Ps1SearchB.Text)?.Trim()
            ?? string.Empty;

        var source =
            side == 'A'
                ? _ps1SavesA
                : _ps1SavesB;

        IEnumerable<Ps1SaveEntry> filtered =
            string.IsNullOrEmpty(query)
                ? source
                : source.Where(save =>
                    save.Title.Contains(
                        query,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    save.SaveTitle.Contains(
                        query,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    save.ProductCode.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase) ||
                    save.FileName.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase) ||
                    save.Region.Contains(
                        query,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    save.Status.Contains(
                        query,
                        StringComparison.CurrentCultureIgnoreCase));

        var field =
            side == 'A'
                ? _ps1SortFieldA
                : _ps1SortFieldB;

        var descending =
            side == 'A'
                ? _ps1SortDescendingA
                : _ps1SortDescendingB;

        filtered = field switch
        {
            Ps1SortField.SaveDescription =>
                descending
                    ? filtered
                        .OrderByDescending(
                            save => save.SaveTitle,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenByDescending(
                            save => save.Title,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenByDescending(
                            save => save.FileName,
                            StringComparer.OrdinalIgnoreCase)
                    : filtered
                        .OrderBy(
                            save => save.SaveTitle,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(
                            save => save.Title,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(
                            save => save.FileName,
                            StringComparer.OrdinalIgnoreCase),

            Ps1SortField.ProductCode =>
                descending
                    ? filtered
                        .OrderByDescending(
                            save => save.ProductCode,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenByDescending(
                            save => save.Title,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenByDescending(
                            save => save.SaveTitle,
                            StringComparer.CurrentCultureIgnoreCase)
                    : filtered
                        .OrderBy(
                            save => save.ProductCode,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            save => save.Title,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(
                            save => save.SaveTitle,
                            StringComparer.CurrentCultureIgnoreCase),

            Ps1SortField.BlocksUsed =>
                descending
                    ? filtered
                        .OrderByDescending(
                            save => save.BlocksUsed)
                        .ThenByDescending(
                            save => save.Title,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenByDescending(
                            save => save.SaveTitle,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenByDescending(
                            save => save.FileName,
                            StringComparer.OrdinalIgnoreCase)
                    : filtered
                        .OrderBy(
                            save => save.BlocksUsed)
                        .ThenBy(
                            save => save.Title,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(
                            save => save.SaveTitle,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(
                            save => save.FileName,
                            StringComparer.OrdinalIgnoreCase),

            _ =>
                descending
                    ? filtered
                        .OrderByDescending(
                            save => save.Title,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenByDescending(
                            save => save.SaveTitle,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenByDescending(
                            save => save.FileName,
                            StringComparer.OrdinalIgnoreCase)
                    : filtered
                        .OrderBy(
                            save => save.Title,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(
                            save => save.SaveTitle,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(
                            save => save.FileName,
                            StringComparer.OrdinalIgnoreCase)
        };

        var results = filtered.ToArray();

        if (side == 'A')
            Ps1CardAList.ItemsSource = results;
        else
            Ps1CardBList.ItemsSource = results;
    }


    private static void UpdateSearchClearButton(
        TextBox searchBox,
        Button clearButton)
    {
        clearButton.Visibility =
            string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private TextBox? GetSearchBoxByName(string? name) =>
        name switch
        {
            nameof(SearchA) => SearchA,
            nameof(SearchB) => SearchB,
            nameof(Ps1SearchA) => Ps1SearchA,
            nameof(Ps1SearchB) => Ps1SearchB,
            nameof(LibrarySearchBox) => LibrarySearchBox,
            _ => null
        };

    private void ClearSearchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var searchBox =
            GetSearchBoxByName(
                button.Tag?.ToString());

        if (searchBox is null)
            return;

        searchBox.Clear();
        searchBox.Focus();
        Keyboard.Focus(searchBox);
    }

    private void SearchBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Escape ||
            sender is not TextBox searchBox)
        {
            return;
        }

        searchBox.Clear();
        searchBox.Focus();
        e.Handled = true;
    }

    private bool IsSearchTextBox(TextBox textBox) =>
        ReferenceEquals(textBox, SearchA) ||
        ReferenceEquals(textBox, SearchB) ||
        ReferenceEquals(textBox, Ps1SearchA) ||
        ReferenceEquals(textBox, Ps1SearchB) ||
        ReferenceEquals(textBox, LibrarySearchBox);

    private void FocusNeutralMemoryCardSurface()
    {
        if (Ps1MemoryCardsTab.IsSelected)
        {
            Ps1NeutralFocusTarget.Focus();
            Keyboard.Focus(Ps1NeutralFocusTarget);
            return;
        }

        if (Ps2MemoryCardsTab.IsSelected)
        {
            Ps2NeutralFocusTarget.Focus();
            Keyboard.Focus(Ps2NeutralFocusTarget);
        }
    }

    private void MemoryCardTab_PreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not TabItem tabItem ||
            tabItem.IsSelected)
        {
            return;
        }

        FocusManager.SetFocusedElement(tabItem, null);

        if (Keyboard.FocusedElement is TextBox focusedTextBox &&
            IsSearchTextBox(focusedTextBox))
        {
            Keyboard.ClearFocus();
        }
    }

    private void Window_PreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var current = source;

        while (current is not null)
        {
            if (current is TextBox)
                return;

            if (current is Button button &&
                button.Tag?.ToString() is string tag &&
                GetSearchBoxByName(tag) is not null)
            {
                return;
            }

            current = GetInputParent(current);
        }

        if (Keyboard.FocusedElement is not TextBox focusedTextBox ||
            !IsSearchTextBox(focusedTextBox))
        {
            return;
        }

        if (Ps1MemoryCardsTab.IsSelected ||
            Ps2MemoryCardsTab.IsSelected)
        {
            FocusNeutralMemoryCardSurface();
        }
        else
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(MainTabs, null);
        }
    }

    private static DependencyObject? GetInputParent(
        DependencyObject current)
    {
        if (current is Visual ||
            current is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }

        if (current is FrameworkContentElement frameworkContent)
        {
            return frameworkContent.Parent ??
                LogicalTreeHelper.GetParent(frameworkContent);
        }

        if (current is ContentElement content)
        {
            return System.Windows.ContentOperations.GetParent(content);
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private void Window_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.F ||
            (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        TextBox? searchBox = null;

        if (SaveLibraryTab.IsSelected)
        {
            searchBox = LibrarySearchBox;
        }
        else if (Ps1MemoryCardsTab.IsSelected)
        {
            searchBox =
                Ps1CardBList.IsKeyboardFocusWithin
                    ? Ps1SearchB
                    : Ps1SearchA;
        }
        else if (MainTabs.SelectedIndex == 0)
        {
            searchBox =
                CardBList.IsKeyboardFocusWithin
                    ? SearchB
                    : SearchA;
        }

        if (searchBox is null)
            return;

        searchBox.Focus();
        Keyboard.Focus(searchBox);
        searchBox.SelectAll();
        e.Handled = true;
    }


    private void BrowseUniversalSource_Click(object sender, RoutedEventArgs e)
    {
        var choice =
            ShowFileOrFolderSourceDialog(
                "CHOOSE UNIVERSAL CONVERTER SOURCE",
                "Choose a supported file, PCSX2 folder card, or directory-ID save folder.",
                "Universal Converter Source");

        if (choice == 0)
            return;

        if (choice == 2)
        {
            var folderDialog =
                new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Choose PCSX2 Folder Card or Save Directory",
                    Multiselect = false
                };

            if (folderDialog.ShowDialog() == true)
                SelectUniversalSource(
                    folderDialog.FolderName);

            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FormatCatalog.SupportedPlayStationFilter
        };
        if (dialog.ShowDialog() == true) SelectUniversalSource(dialog.FileName);
    }

    private static UniversalSourceKind DetectUniversalSourceKind(
        string path)
    {
        if (Directory.Exists(path))
        {
            return File.Exists(
                    Path.Combine(
                        path,
                        "_pcsx2_superblock"))
                ? UniversalSourceKind.Ps2Card
                : UniversalSourceKind.Unsupported;
        }

        if (!File.Exists(path))
            return UniversalSourceKind.Unsupported;

        var extension =
            Path.GetExtension(path)
                .ToLowerInvariant();

        // PSM's PS1 package belongs in the Import Wizard.
        if (extension == ".ps1save")
            return UniversalSourceKind.Ps1Package;

        // PS2SAVE intentionally remains a Save Library/native archive format
        // and is not routed through Import Wizard or Universal Converter.
        if (extension == ".ps2save")
            return UniversalSourceKind.Unsupported;

        // Individual PS1 save wrappers are verified by the same parser used
        // by the actual import/conversion operations.
        if (Ps1ExternalSaveService.LooksLikePs1SingleSave(path))
            return UniversalSourceKind.Ps1SingleSave;

        // PS1 card wrappers are content-verified, not extension-only.
        if (Ps1MemoryCardService.LooksLikeSupportedCard(path))
            return UniversalSourceKind.Ps1Card;

        if (LooksLikePs2ImageCard(path))
            return UniversalSourceKind.Ps2Card;

        if (extension is ".psu" or
            ".max" or
            ".cbs" or
            ".xps" or
            ".sps" or
            ".psv" or
            ".npo" or
            ".p2m")
        {
            return UniversalSourceKind.Ps2Package;
        }

        return UniversalSourceKind.Unsupported;
    }

    private static bool LooksLikePs2ImageCard(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 32)
                return false;

            var header = new byte[28];
            if (stream.Read(header, 0, header.Length) != header.Length)
                return false;

            return Encoding.ASCII.GetString(header)
                .Equals(
                    "Sony PS2 Memory Card Format ",
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string GetUniversalSourceDisplayName(
        string path,
        UniversalSourceKind kind)
    {
        var extension = Directory.Exists(path)
            ? ".foldercard"
            : Path.GetExtension(path).ToLowerInvariant();

        if (kind == UniversalSourceKind.Ps1SingleSave)
        {
            if (Ps1ExternalSaveService.IsPs1Psv(path))
                return "PS3 PS1 Virtual Save";

            return extension switch
            {
                ".mcs" => "PS1 Individual Save (MCS)",
                ".ps1" => "PS1 Individual Save (PS1)",
                ".mcb" => "PS1 Individual Save (MCB)",
                ".mcx" => "PS1 Individual Save (MCX)",
                ".pda" => "PS1 Individual Save (PDA)",
                ".psx" => "PS1 Individual Save (PSX)",
                ".raw" => "PS1 Individual Save (RAW)",
                _ when string.IsNullOrWhiteSpace(extension) =>
                    "PS1 Individual Save (RAW)",
                _ => "PS1 Individual Save"
            };
        }

        if (kind == UniversalSourceKind.Ps2Card &&
            extension is ".bin" or ".mcd" or ".vmc")
        {
            return $"PS2 Memory Card ({extension.TrimStart('.').ToUpperInvariant()})";
        }

        if (kind == UniversalSourceKind.Ps1Card &&
            extension is ".bin" or ".mcd" or ".vmc")
        {
            return $"PS1 Memory Card ({extension.TrimStart('.').ToUpperInvariant()})";
        }

        if (kind == UniversalSourceKind.Ps2Package && extension == ".psv")
            return "PS3 PS2 Virtual Save";

        return UniversalFormats
            .FirstOrDefault(format => format.Extension == extension)
            ?.DisplayName
            ?? kind.ToString();
    }

    private void SelectUniversalSource(string path)
    {
        _universalSourcePath = path;
        UniversalSourcePath.Text = path;

        var kind = DetectUniversalSourceKind(path);
        var extension = Directory.Exists(path)
            ? ".foldercard"
            : Path.GetExtension(path).ToLowerInvariant();
        var displayName = GetUniversalSourceDisplayName(path, kind);

        if (kind == UniversalSourceKind.Unsupported)
        {
            UniversalDetectedFormat.Text =
                $"Detected format: Unsupported ({(string.IsNullOrWhiteSpace(extension) ? "unknown" : extension)})";
            UniversalModeText.Text = "Unsupported source";
            UniversalConversionReport.Text =
                "PSM could not verify this file as a supported PS1/PS2 save or memory card.";
            UniversalTargetFormat.ItemsSource = null;
            UniversalConvertButton.IsEnabled = false;
            return;
        }

        UniversalDetectedFormat.Text =
            $"Detected format: {displayName}" +
            (string.IsNullOrWhiteSpace(extension)
                ? string.Empty
                : $" ({extension.ToUpperInvariant()})");

        UniversalModeText.Text = kind switch
        {
            UniversalSourceKind.Ps1Card => "PS1 whole-memory-card conversion",
            UniversalSourceKind.Ps1SingleSave => "PS1 individual-save conversion",
            UniversalSourceKind.Ps1Package => "PS1 save-package conversion",
            UniversalSourceKind.Ps2Card => "PS2 whole-memory-card conversion",
            UniversalSourceKind.Ps2Package => "PS2 packaged-save conversion",
            _ => "Universal conversion"
        };

        var outputs = UniversalFormats
            .Where(format => format.CanWrite && kind switch
            {
                UniversalSourceKind.Ps1Card =>
                    Ps1CardExtensions.Contains(format.Extension),

                UniversalSourceKind.Ps1SingleSave =>
                    Ps1SingleSaveExtensions.Contains(format.Extension) ||
                    Ps1CardExtensions.Contains(format.Extension) ||
                    format.Extension == ".ps1save",

                UniversalSourceKind.Ps1Package =>
                    Ps1SingleSaveExtensions.Contains(format.Extension) ||
                    Ps1CardExtensions.Contains(format.Extension),

                UniversalSourceKind.Ps2Card =>
                    Ps2CardExtensions.Contains(format.Extension),

                UniversalSourceKind.Ps2Package =>
                    format.Extension is ".cbs" or ".max" or ".psu" or
                    ".psv" or ".sps" or ".xps" or
                    ".mc2" or ".ps2" or ".vm2" or ".vmc" or
                    ".bin" or ".mcd" or ".foldercard",

                _ => false
            })
            .ToArray();

        UniversalTargetFormat.ItemsSource = outputs;

        var preferred = kind switch
        {
            UniversalSourceKind.Ps1Card =>
                extension == ".mcr" ? ".srm" : ".mcr",
            UniversalSourceKind.Ps1SingleSave =>
                extension == ".mcs" ? ".psv" : ".mcs",
            UniversalSourceKind.Ps1Package => ".mcs",
            UniversalSourceKind.Ps2Card =>
                extension == ".mc2" ? ".ps2" : ".mc2",
            _ => ".psu"
        };

        UniversalTargetFormat.SelectedItem =
            outputs.FirstOrDefault(format => format.Extension == preferred)
            ?? outputs.FirstOrDefault();

        UniversalConversionReport.Text = kind switch
        {
            UniversalSourceKind.Ps1Card =>
                "Verified PS1 card targets are shown. Legacy wrappers are normalized internally and rebuilt on output.",
            UniversalSourceKind.Ps1SingleSave =>
                "Convert this individual PS1 save to another save wrapper, a one-save PS1 memory card, or PSM's library package.",
            UniversalSourceKind.Ps1Package =>
                "Export this PSM PS1 package as an individual PS1 save or verified single-save memory card.",
            UniversalSourceKind.Ps2Card =>
                "Verified PS2 memory-card targets are shown. ECC and no-ECC cards remain separated where required.",
            _ =>
                "Only compatible PS2 outputs are shown. Conversion uses a verified temporary card before output is committed."
        };

        UniversalConvertButton.IsEnabled =
            UniversalTargetFormat.SelectedItem is not null;
        UpdateUniversalOutputSuggestion();
    }

    private void UniversalTargetFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateUniversalOutputSuggestion();
        UniversalConvertButton.IsEnabled =
            _universalSourcePath is not null &&
            UniversalTargetFormat.SelectedItem is UniversalFormatOption;
    }

    private void UpdateUniversalOutputSuggestion()
    {
        if (_universalSourcePath is null ||
            UniversalTargetFormat.SelectedItem is not UniversalFormatOption target)
            return;
        var sourceDirectory =
            Directory.Exists(_universalSourcePath)
                ? Path.GetDirectoryName(_universalSourcePath)!
                : Path.GetDirectoryName(_universalSourcePath)!;

        var sourceName =
            Directory.Exists(_universalSourcePath)
                ? Path.GetFileName(_universalSourcePath)
                : Path.GetFileNameWithoutExtension(_universalSourcePath);

        UniversalOutputPath.Text =
            Path.Combine(
                sourceDirectory,
                target.Extension == ".foldercard"
                    ? sourceName + "_folder"
                    : sourceName + target.Extension);
    }

    private void BrowseUniversalOutput_Click(object sender, RoutedEventArgs e)
    {
        if (UniversalTargetFormat.SelectedItem is not UniversalFormatOption target)
            return;

        if (target.Extension == ".foldercard")
        {
            var folderDialog =
                new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Choose Parent Folder for Converted Folder Card",
                    Multiselect = false
                };

            if (folderDialog.ShowDialog() == true)
            {
                var sourceName =
                    _universalSourcePath is null
                        ? "Converted_folder"
                        : (Directory.Exists(_universalSourcePath)
                            ? Path.GetFileName(_universalSourcePath)
                            : Path.GetFileNameWithoutExtension(_universalSourcePath)) +
                          "_folder";

                UniversalOutputPath.Text =
                    Path.Combine(
                        folderDialog.FolderName,
                        sourceName);
            }

            return;
        }

        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = $"{target.DisplayName}|*{target.Extension}",
            DefaultExt = target.Extension,
            FileName = _universalSourcePath is null
                ? "Converted" + target.Extension
                : Path.GetFileNameWithoutExtension(_universalSourcePath) + target.Extension
        };
        if (saveDialog.ShowDialog() == true)
            UniversalOutputPath.Text = saveDialog.FileName;
    }

    private async void UniversalConvert_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_universalSourcePath is null ||
            (!File.Exists(_universalSourcePath) &&
             !Directory.Exists(_universalSourcePath)) ||
            UniversalTargetFormat.SelectedItem is not UniversalFormatOption target ||
            string.IsNullOrWhiteSpace(UniversalOutputPath.Text))
        {
            return;
        }

        var source = _universalSourcePath;
        var output = UniversalOutputPath.Text;
        var kind = DetectUniversalSourceKind(source);
        var sourceExtension = Directory.Exists(source)
            ? ".foldercard"
            : Path.GetExtension(source).ToLowerInvariant();
        var sourceDisplayName = GetUniversalSourceDisplayName(source, kind);

        if (kind == UniversalSourceKind.Unsupported)
        {
            MessageBox.Show(
                "PSM could not verify the selected source format.",
                "Universal Converter",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (File.Exists(source) &&
            Path.GetFullPath(source).Equals(
                Path.GetFullPath(output),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "The output path must differ from the source.",
                "Universal Converter",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (kind is UniversalSourceKind.Ps1Card or
            UniversalSourceKind.Ps1SingleSave or
            UniversalSourceKind.Ps1Package)
        {
            try
            {
                SetBusy(true, "Converting PlayStation save data...");
                UniversalConvertButton.IsEnabled = false;

                if (kind == UniversalSourceKind.Ps1Card)
                {
                    if (!Ps1CardExtensions.Contains(target.Extension))
                        throw new NotSupportedException(
                            "A complete PS1 card can only be converted to another PS1 memory-card format.");

                    await _ps1CardService.SaveCardAsAsync(source, output);
                }
                else if (kind == UniversalSourceKind.Ps1SingleSave)
                {
                    if (Ps1SingleSaveExtensions.Contains(target.Extension))
                    {
                        await Ps1ExternalSaveService.ConvertAsync(source, output);
                    }
                    else if (target.Extension == ".ps1save")
                    {
                        await _ps1CardService.CreateSavePackageFromExternalSaveAsync(
                            source,
                            output);
                    }
                    else if (Ps1CardExtensions.Contains(target.Extension))
                    {
                        await _ps1CardService.CreateSingleSaveCardFromExternalSaveAsync(
                            source,
                            output);
                    }
                    else
                    {
                        throw new NotSupportedException(
                            "That target is not compatible with a PS1 individual save.");
                    }
                }
                else
                {
                    if (Ps1CardExtensions.Contains(target.Extension))
                    {
                        await _ps1CardService.CreateSingleSaveCardFromPackageAsync(
                            source,
                            output);
                    }
                    else if (Ps1SingleSaveExtensions.Contains(target.Extension))
                    {
                        var temporaryRoot = Path.Combine(
                            Path.GetTempPath(),
                            "PSM-PS1-PACKAGE-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(temporaryRoot);
                        try
                        {
                            var card = Path.Combine(temporaryRoot, "package.mcr");
                            await _ps1CardService.CreateSingleSaveCardFromPackageAsync(
                                source,
                                card);
                            var read = await _ps1CardService.ReadAsync(card);
                            var save = read.Saves.Single(candidate => !candidate.IsDeleted);
                            await _ps1CardService.ExportExternalSaveAsync(
                                card,
                                save,
                                output);
                        }
                        finally
                        {
                            try { Directory.Delete(temporaryRoot, true); } catch { }
                        }
                    }
                    else
                    {
                        throw new NotSupportedException(
                            "That target is not compatible with a PSM PS1 save package.");
                    }
                }

                UniversalConversionReport.Text =
                    $"CONVERSION VERIFIED\n\nSource: {Path.GetFileName(source)}\n" +
                    $"Output: {Path.GetFileName(output)}\n\n" +
                    $"Source adapter: {sourceDisplayName}\n" +
                    $"Output adapter: {target.DisplayName}\nOriginal source preserved\nVerification passed";

                VerifiedText.Text =
                    $"UNIVERSAL CONVERSION VERIFIED - {Path.GetFileName(output)}";
                VerifiedBanner.Visibility = Visibility.Visible;
                Log($"PS1 universal conversion verified: {source} -> {output}");

                MessageBox.Show(
                    $"PS1 conversion completed and verified.\n\nOutput:\n{output}",
                    "Universal Conversion Verified",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UniversalConversionReport.Text =
                    "Conversion failed safely.\n\nThe source was not modified.\n\n" + ex.Message;
                Log("PS1 universal conversion failed: " + ex.Message);
                MessageBox.Show(
                    ex.Message,
                    "PS1 Conversion Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, "Ready.");
                UniversalConvertButton.IsEnabled =
                    _universalSourcePath is not null &&
                    UniversalTargetFormat.SelectedItem is UniversalFormatOption;
            }

            return;
        }

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-UNIVERSAL-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        if (target.Extension == ".mc2")
        {
            var preferredDirectoryId =
                await TryGetUniversalSourceDirectoryIdAsync(
                    source,
                    sourceExtension,
                    tempRoot);

            output = PromptForMemCardPro2ReadyOutput(
                output,
                preferredDirectoryId);

            if (string.IsNullOrWhiteSpace(output))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
                return;
            }

            UniversalOutputPath.Text = output;
        }

        UniversalTechnicalLog.Clear();

        try
        {
            SetBusy(true, "Universal conversion in progress...");
            UniversalConvertButton.IsEnabled = false;
            UniversalConversionReport.Text = "Preparing safe temporary workspace...";
            AppendUniversalLog($"Source: {source}");
            AppendUniversalLog($"Detected: {sourceDisplayName}");
            AppendUniversalLog($"Target: {target.DisplayName}");

            if (kind == UniversalSourceKind.Ps2Card)
                await ConvertUniversalCardAsync(source, output, target, tempRoot);
            else
                await ConvertUniversalPackageAsync(source, output, target, tempRoot);

            VerifiedText.Text =
                $"UNIVERSAL CONVERSION VERIFIED - {Path.GetFileName(output)}";
            VerifiedBanner.Visibility = Visibility.Visible;
            UniversalConversionReport.Text =
                $"CONVERSION VERIFIED\n\nSource: {Path.GetFileName(source)}\n" +
                $"Output: {Path.GetFileName(output)}\n\n" +
                $"Output adapter: {target.DisplayName}\nOriginal source preserved\nVerification passed";
            Log($"Universal conversion verified: {source} -> {output}");
            MessageBox.Show(
                $"Conversion completed and verified.\n\nOutput:\n{output}",
                "Universal Conversion Verified",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UniversalConversionReport.Text =
                "Conversion failed safely.\n\nThe source was not modified.\n\n" + ex.Message;
            AppendUniversalLog("ERROR: " + ex);
            Log("Universal conversion failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "Universal Conversion Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
            SetBusy(false, "Ready.");
            UniversalConvertButton.IsEnabled =
                _universalSourcePath is not null &&
                UniversalTargetFormat.SelectedItem is UniversalFormatOption;
        }
    }

    private async Task ConvertUniversalCardAsync(
        string source, string output, UniversalFormatOption target, string tempRoot)
    {
        if (!Ps2CardExtensions.Contains(target.Extension))
        {
            throw new NotSupportedException(
                "Complete PS2 cards can only convert to another supported PS2 memory-card format.");
        }

        if (target.Extension == ".foldercard")
        {
            if (Directory.Exists(source))
            {
                CopyDirectory(source, output);
            }
            else
            {
                await _engine.ConvertToPcsx2FolderCardAsync(
                    source,
                    output);
            }

            await _engine.CheckAsync(output);
            AppendUniversalLog(
                $"Committed verified folder card: {output}");
            return;
        }

        if (Directory.Exists(source))
        {
            await _engine.ConvertFolderCardToImageAsync(
                source,
                output,
                noEcc: target.Extension == ".mc2");

            await _engine.CheckAsync(output);
            AppendUniversalLog(
                $"Committed verified card image: {output}");
            return;
        }

        var sourceRead = await _engine.ReadCardAsync(source);
        var sourceSaves = sourceRead.Saves;
        AppendUniversalLog($"Source contains {sourceSaves.Count} saves.");

        var sourceMegabytes = sourceRead.TotalBytes.HasValue
            ? (int)Math.Round(
                sourceRead.TotalBytes.Value / 1024d / 1024d,
                MidpointRounding.AwayFromZero)
            : 8;
        var targetMegabytes = sourceMegabytes switch
        {
            <= 8 => 8,
            <= 16 => 16,
            <= 32 => 32,
            _ => 64
        };

        var temporaryCard = Path.Combine(tempRoot, "converted" + target.Extension);
        await _engine.CreateCardAsync(
            temporaryCard,
            targetMegabytes,
            target.Extension == ".mc2");

        for (var index = 0; index < sourceSaves.Count; index++)
        {
            var save = sourceSaves[index];
            StatusText.Text = $"Converting {index + 1} of {sourceSaves.Count}: {save.DirectoryId}";
            var psu = Path.Combine(tempRoot, $"{index:D4}-{SanitizeUniversalFileName(save.DirectoryId)}.psu");
            await _engine.ExportPsuAsync(source, save.DirectoryId, psu);
            await _engine.ImportAsync(temporaryCard, psu);
            AppendUniversalLog($"Copied: {save.DirectoryId}");
        }

        await _engine.CheckAsync(temporaryCard);
        var verified = await _engine.ReadDirectoryAsync(temporaryCard);
        if (verified.Count != sourceSaves.Count)
            throw new InvalidOperationException(
                $"Verification mismatch: source has {sourceSaves.Count} saves; output has {verified.Count}.");
        CommitUniversalOutput(temporaryCard, output);
    }

    private async Task ConvertUniversalPackageAsync(
        string source, string output, UniversalFormatOption target, string tempRoot)
    {
        var temporaryCard = Path.Combine(tempRoot, "package-work.ps2");
        await _engine.CreateCardAsync(temporaryCard, false);
        await _engine.ImportAsync(temporaryCard, source);
        await _engine.CheckAsync(temporaryCard);

        var saves = await _engine.ReadDirectoryAsync(temporaryCard);
        if (saves.Count != 1)
            throw new InvalidOperationException(
                $"Expected one packaged save, but the temporary card contains {saves.Count}.");

        var save = saves[0];
        AppendUniversalLog($"Imported directory: {save.DirectoryId}");

        if (Ps2CardExtensions.Contains(target.Extension))
        {
            if (target.Extension is ".ps2" or ".vm2" or ".vmc" or ".bin" or ".mcd")
            {
                CommitUniversalOutput(temporaryCard, output);
                return;
            }

            if (target.Extension == ".foldercard")
            {
                await _engine.ConvertToPcsx2FolderCardAsync(temporaryCard, output);
                await _engine.CheckAsync(output);
                AppendUniversalLog($"Committed verified folder card: {output}");
                return;
            }

            var noEccCard = Path.Combine(tempRoot, "converted.mc2");
            await _engine.CreateCardAsync(noEccCard, true);
            var psu = Path.Combine(tempRoot, "intermediate.psu");
            await _engine.ExportPsuAsync(temporaryCard, save.DirectoryId, psu);
            await _engine.ImportAsync(noEccCard, psu);
            await _engine.CheckAsync(noEccCard);
            CommitUniversalOutput(noEccCard, output);
            return;
        }

        if (target.Extension is
            ".cbs" or ".max" or ".psu" or
            ".psv" or ".sps" or ".xps")
        {
            var package =
                Path.Combine(
                    tempRoot,
                    "converted" +
                    target.Extension);

            await _engine.ExportPackageAsync(
                temporaryCard,
                save.DirectoryId,
                package);

            if (!File.Exists(package) ||
                new FileInfo(package).Length == 0)
            {
                throw new InvalidOperationException(
                    "The output package was not created correctly.");
            }

            CommitUniversalOutput(
                package,
                output);
            return;
        }

        throw new NotSupportedException(
            $"{target.DisplayName} is not a compatible PS2 output.");
    }

    private void CommitUniversalOutput(string temporaryOutput, string destination)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(destination))
        {
            var backup = CreateAutomaticBackup(destination);
            AppendUniversalLog(
                backup is not null
                    ? $"Existing destination backed up: {backup}"
                    : "Existing destination will be replaced; Automatic Backups is disabled.");
        }
        File.Copy(temporaryOutput, destination, true);
        AppendUniversalLog($"Committed verified output: {destination}");
    }

    private void AppendUniversalLog(string message)
    {
        UniversalTechnicalLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        UniversalTechnicalLog.ScrollToEnd();
    }

    private static string SanitizeUniversalFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private async Task LoadSaveLibraryIconsAsync(
        IEnumerable<SaveLibraryEntry> entries)
    {
        using var throttle = new SemaphoreSlim(2);

        var tasks = entries.Select(async entry =>
        {
            await throttle.WaitAsync();
            try
            {
                await LoadSaveLibraryIconAsync(entry);
            }
            catch (Exception ex)
            {
                Log($"Save Library icon failed for {entry.OriginalFileName}: {ex.Message}");
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        await Dispatcher.InvokeAsync(() =>
        {
            if (_saveLibraryContentMode ==
                SaveLibraryContentMode.GameSaves)
            {
                SaveLibraryList.Items.Refresh();
            }
        });
    }

    private static BitmapSource LoadFrozenBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static void SaveBitmapAsPng(
        BitmapSource bitmap,
        string destinationPath)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(destinationPath);
        encoder.Save(stream);
    }

    private static string GetSaveLibraryPreviewCachePath(
        SaveLibraryEntry entry)
    {
        var directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PlayStationSaveManager",
                "Cache",
                "GameSavePreviews");

        Directory.CreateDirectory(
            directory);

        return Path.Combine(
            directory,
            entry.Sha256 + "-front-v1.png");
    }

    private async Task LoadSaveLibraryIconAsync(
        SaveLibraryEntry entry)
    {
        var iconDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PlayStationSaveManager",
                "Cache",
                "GameSaveIcons");

        Directory.CreateDirectory(
            iconDirectory);

        var cachedIconPath =
            Path.Combine(
                iconDirectory,
                entry.Sha256 + "-front-v1.png");

        var previewCachePath =
            GetSaveLibraryPreviewCachePath(
                entry);

        if (File.Exists(cachedIconPath))
        {
            var cached =
                LoadFrozenBitmap(
                    cachedIconPath);

            _saveLibraryIconMemoryCache[entry.Sha256] =
                cached;

            await Dispatcher.InvokeAsync(() =>
            {
                entry.IconImage = cached;

                // The Game Saves view uses a normal List<T>. Force WPF to
                // redraw already-realized rows after a background icon arrives.
                if (_saveLibraryContentMode ==
                    SaveLibraryContentMode.GameSaves)
                {
                    SaveLibraryList.Items.Refresh();
                }

                if (_saveLibraryContentMode ==
                        SaveLibraryContentMode.GameSaves &&
                    ReferenceEquals(
                        SaveLibraryList.SelectedItem,
                        entry))
                {
                    // The large preview uses its own high-resolution
                    // still image or animated model, never this list thumbnail.
                }
            });

            return;
        }

        var packagePath =
            _saveLibraryService.GetStoredPath(
                entry);

        if (!File.Exists(packagePath))
            return;

        if (entry.Extension.Equals(
            ".ps1save",
            StringComparison.OrdinalIgnoreCase))
        {
            var ps1Icon =
                Ps1MemoryCardService.LoadPackageIcon(
                    packagePath);

            if (ps1Icon is not null)
            {
                SaveBitmapAsPng(
                    ps1Icon,
                    cachedIconPath);

                var cached =
                    LoadFrozenBitmap(
                        cachedIconPath);

                _saveLibraryIconMemoryCache[entry.Sha256] =
                    cached;

                await Dispatcher.InvokeAsync(() =>
                {
                    entry.IconImage = cached;

                // The Game Saves view uses a normal List<T>. Force WPF to
                // redraw already-realized rows after a background icon arrives.
                if (_saveLibraryContentMode ==
                    SaveLibraryContentMode.GameSaves)
                {
                    SaveLibraryList.Items.Refresh();
                }

                    if (_saveLibraryContentMode ==
                            SaveLibraryContentMode.GameSaves &&
                        ReferenceEquals(
                            SaveLibraryList.SelectedItem,
                            entry))
                    {
                        LibraryPreviewImage.Source =
                            cached;

                        LibraryPreviewPlaceholder.Visibility =
                            Visibility.Collapsed;
                    }
                });
            }

            return;
        }

        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-LIBRARY-ICON-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            temporaryRoot);

        try
        {
            var cardPath =
                Path.Combine(
                    temporaryRoot,
                    "icon-card.ps2");

            await _engine.CreateCardAsync(
                cardPath,
                false);

            var previewPackagePath = packagePath;
            if (entry.Extension.Equals(
                ".sps",
                StringComparison.OrdinalIgnoreCase))
            {
                previewPackagePath = Path.Combine(
                    temporaryRoot,
                    "normalized-preview.psu");

                await SpsPackageService.ConvertToPsuAsync(
                    packagePath,
                    previewPackagePath);
            }

            await _engine.ImportAsync(
                cardPath,
                previewPackagePath);

            await _engine.CheckAsync(
                cardPath);

            var saves =
                await _engine.ReadDirectoryAsync(
                    cardPath);

            var save =
                saves.FirstOrDefault(candidate =>
                    candidate.DirectoryId.Equals(
                        entry.DirectoryId,
                        StringComparison.OrdinalIgnoreCase))
                ?? saves.FirstOrDefault();

            if (save is null)
                return;

            BitmapSource? thumbnail;
            BitmapSource? sharpPreview = null;

            if (BuiltInSaveIcons.IsSystemConfiguration(
                save.DirectoryId,
                save.GameTitle))
            {
                thumbnail =
                    BuiltInSaveIcons
                        .RenderSystemConfiguration(
                            160,
                            160);

                sharpPreview =
                    BuiltInSaveIcons
                        .RenderSystemConfiguration(
                            512,
                            512);
            }
            else
            {
                var iconResult =
                    await _iconService.LoadResultAsync(
                        cardPath,
                        save.DirectoryId);

                thumbnail =
                    iconResult.Model is not null
                        ? await Task.Run(() =>
                            iconResult.Model.Render(
                                160,
                                160,
                                0,
                                Ps2IconFrontRotation))
                        : iconResult.IsCorrupted
                            ? await Task.Run(() =>
                                BuiltInSaveIcons
                                    .RenderCorruptedSave(
                                        160,
                                        160))
                            : null;

                sharpPreview =
                    iconResult.Model is not null
                        ? await Task.Run(() =>
                            iconResult.Model.Render(
                                512,
                                512,
                                0,
                                Ps2IconFrontRotation))
                        : iconResult.IsCorrupted
                            ? await Task.Run(() =>
                                BuiltInSaveIcons
                                    .RenderCorruptedSave(
                                        512,
                                        512))
                            : null;
            }

            if (thumbnail is null)
                return;

            SaveBitmapAsPng(
                thumbnail,
                cachedIconPath);

            if (sharpPreview is not null &&
                !File.Exists(previewCachePath))
            {
                SaveBitmapAsPng(
                    sharpPreview,
                    previewCachePath);
            }

            var cached =
                LoadFrozenBitmap(
                    cachedIconPath);

            _saveLibraryIconMemoryCache[entry.Sha256] =
                cached;

            await Dispatcher.InvokeAsync(() =>
            {
                entry.IconImage = cached;

                // The Game Saves view uses a normal List<T>. Force WPF to
                // redraw already-realized rows after a background icon arrives.
                if (_saveLibraryContentMode ==
                    SaveLibraryContentMode.GameSaves)
                {
                    SaveLibraryList.Items.Refresh();
                }

                if (_saveLibraryContentMode ==
                        SaveLibraryContentMode.GameSaves &&
                    ReferenceEquals(
                        SaveLibraryList.SelectedItem,
                        entry))
                {
                    // The large preview uses its own high-resolution
                    // still image or animated model, never this list thumbnail.
                }
            });
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryRoot,
                    true);
            }
            catch { }
        }
    }

    private async void AddLibraryA_Click(
        object sender,
        RoutedEventArgs e) =>
        await ShowStoreLibraryChoiceAsync(
            _pathA,
            GetSelectedPs2CardSaves(CardAList),
            Array.Empty<Ps1SaveEntry>(),
            'A',
            false);

    private async void AddLibraryB_Click(
        object sender,
        RoutedEventArgs e) =>
        await ShowStoreLibraryChoiceAsync(
            _pathB,
            GetSelectedPs2CardSaves(CardBList),
            Array.Empty<Ps1SaveEntry>(),
            'B',
            false);

    private static SaveEntry[] GetSelectedPs2CardSaves(
        ListView list)
    {
        var selected =
            list.SelectedItems
                .Cast<SaveEntry>()
                .ToArray();

        if (selected.Length == 0 &&
            list.SelectedItem is SaveEntry single)
        {
            selected = [single];
        }

        return selected;
    }

    private static Ps1SaveEntry[] GetSelectedPs1CardSaves(
        ListView list)
    {
        var selected =
            list.SelectedItems
                .Cast<Ps1SaveEntry>()
                .Where(save => !save.IsDeleted)
                .ToArray();

        if (selected.Length == 0 &&
            list.SelectedItem is Ps1SaveEntry single &&
            !single.IsDeleted)
        {
            selected = [single];
        }

        return selected;
    }

    private async Task ShowStoreLibraryChoiceAsync(
        string? cardPath,
        IReadOnlyList<SaveEntry> ps2Saves,
        IReadOnlyList<Ps1SaveEntry> ps1Saves,
        char side,
        bool isPs1)
    {
        if (cardPath is null)
            return;

        var selectedCount =
            isPs1
                ? ps1Saves.Count
                : ps2Saves.Count;

        var choice =
            ShowNewCardTypeDialog(
                "ADD TO SAVE LIBRARY",
                selectedCount > 1
                    ? $"Store the {selectedCount} selected saves or preserve the complete memory card."
                    : "Store an individual save or preserve the complete memory card.",
                new[]
                {
                    new CardChoice(
                        FindResource("IconStoreCard") as ImageSource,
                        selectedCount > 1 ? "Store Saves" : "Store Save",
                        selectedCount > 1
                            ? $"Export and store all {selectedCount} selected game saves."
                            : "Export and store the selected game save.",
                        1),
                    new CardChoice(
                        FindResource("IconStoreSave") as ImageSource,
                        "Store Card",
                        "Copy the complete memory card into the library.",
                        2)
                },
                "Add to Save Library");

        if (choice == 1)
        {
            if (selectedCount == 0)
            {
                MessageBox.Show(
                    isPs1
                        ? "Select one or more PS1 saves first."
                        : "Select one or more PS2 saves first.",
                    "Store Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (isPs1)
            {
                await AddPs1SavesToLibraryAsync(
                    cardPath,
                    ps1Saves);
            }
            else
            {
                await AddPs2SavesToLibraryAsync(
                    cardPath,
                    ps2Saves,
                    side);
            }
        }
        else if (choice == 2)
        {
            await StoreMemoryCardInLibraryAsync(
                cardPath,
                isPs1,
                side);
        }
    }

    private async Task AddPs2SavesToLibraryAsync(
        string cardPath,
        IReadOnlyList<SaveEntry> saves,
        char side)
    {
        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-CARD-LIBRARY-BATCH-" +
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        var added = 0;
        var duplicates = 0;

        try
        {
            SetBusy(
                true,
                $"Adding {saves.Count} PS2 save(s) to Save Library...");

            foreach (var save in saves)
            {
                var temporaryPackage =
                    Path.Combine(
                        temporaryRoot,
                        SanitizeUniversalFileName(
                            save.DirectoryId) +
                        ".ps2save");

                await _ps2PackageService.ExportFromCardAsync(
                    cardPath,
                    save,
                    temporaryPackage);

                var result =
                    await _saveLibraryService.ImportAsync(
                        temporaryPackage,
                        _saveLibraryIndex);

                if (result.Duplicate is not null)
                {
                    duplicates++;
                    Log(
                        $"Card {side} save already in library: " +
                        save.DirectoryId);
                    continue;
                }

                result.Entry.ImportedFrom =
                    GetLibrarySourceDisplayName(cardPath);
                result.Entry.OriginalFileName =
                    Path.GetFileName(
                        result.Entry.StoredFileName);

                await _saveLibraryService.SaveAsync(
                    _saveLibraryIndex);

                added++;
                await LoadSaveLibraryIconAsync(
                    result.Entry);

                Log(
                    $"Added Card {side} save to library: " +
                    save.DirectoryId);
            }

            ApplySaveLibraryFilter();

            LibraryFooterStatus.Text =
                $"Added {added} PS2 save(s) from Card {side}" +
                (duplicates > 0
                    ? $" • {duplicates} already in library"
                    : string.Empty) +
                ".";

            MessageBox.Show(
                $"Save Library update complete.\n\n" +
                $"Added: {added}\n" +
                $"Already in library: {duplicates}",
                "Save Library",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                $"Batch add from PS2 Card {side} failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "Add to Save Library Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryRoot,
                    true);
            }
            catch { }

            SetBusy(false, "Ready.");
        }
    }

    private string? PromptForLibraryCardName(
        string defaultName,
        string title,
        string? headingOverride = null,
        string? detailOverride = null)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(7, 11, 16)),
            ShowInTaskbar = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Width = 390
        };

        var isLibraryName =
            title.Contains("Library", StringComparison.OrdinalIgnoreCase);

        panel.Children.Add(new TextBlock
        {
            Text = headingOverride ??
                (isLibraryName
                    ? "Enter a name for this memory card in the Save Library."
                    : "Enter a name for the PCSX2 folder memory card."),
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = detailOverride ??
                (isLibraryName
                    ? "The original memory-card format is preserved automatically."
                    : "PSM will create a new folder with this name in the selected location."),
            Foreground = new SolidColorBrush(Color.FromRgb(159, 176, 197)),
            Margin = new Thickness(0, 5, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        var input = new TextBox
        {
            Text = defaultName,
            FontSize = 15,
            MinWidth = 350
        };
        input.SelectAll();
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 90
        };
        var ok = new Button
        {
            Content = "Save",
            MinWidth = 90,
            IsDefault = true
        };

        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) =>
        {
            var value = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show(
                    dialog,
                    "Enter a name for the memory card.",
                    "Memory Card Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            dialog.Tag = value;
            dialog.DialogResult = true;
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => input.Focus();

        return dialog.ShowDialog() == true
            ? dialog.Tag as string
            : null;
    }

    private async Task StoreMemoryCardInLibraryAsync(
        string cardPath, bool isPs1, char side)
    {
        var requestedName = PromptForLibraryCardName(
            isPs1 ? "PS1 Library Collection" : "PS2 Library Collection",
            isPs1 ? "Name PS1 Library Card" : "Name PS2 Library Card");

        if (string.IsNullOrWhiteSpace(requestedName))
            return;

        try
        {
            SetBusy(true,$"Storing Card {side} in the Memory Card Library...");

            string platform;
            string cardType;
            int saveCount;
            long? capacity;

            if (isPs1)
            {
                platform="PlayStation";
                cardType=GetPs1CardTypeName(Path.GetExtension(cardPath));
                saveCount=(side=='A' ? _ps1SavesA : _ps1SavesB).Count(save=>!save.IsDeleted);
                capacity=128*1024;
            }
            else
            {
                platform="PlayStation 2";
                saveCount=side=='A' ? _allA.Count : _allB.Count;
                if (Directory.Exists(cardPath))
                {
                    cardType =
                        FormatCatalog.GetPs2CardTypeName(cardPath);
                    capacity = null;
                }
                else
                {
                    cardType =
                        FormatCatalog.GetPs2CardTypeName(cardPath);
                    capacity =
                        (await _engine.ReadCardAsync(cardPath)).TotalBytes;
                }
            }

            var result=await _memoryCardLibraryService.StoreAsync(
                cardPath,platform,cardType,saveCount,capacity,requestedName);

            await LoadMemoryCardLibraryAsync();
            ShowMemoryCardLibraryMode();
            MemoryCardLibraryList.SelectedItem=result.Entry;
            MemoryCardLibraryList.ScrollIntoView(result.Entry);

            MessageBox.Show(
                result.Duplicate is null
                    ? $"{result.Entry.DisplayName} was added to the Memory Card Library."
                    : $"{result.Entry.DisplayName} is already in the Memory Card Library.",
                "Memory Card Library",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex)
        {
            Log("Store memory card failed: "+ex.Message);
            MessageBox.Show(ex.Message,"Store Card Failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        finally { SetBusy(false,"Ready."); }
    }

    private async Task AddCardSaveToLibraryAsync(
        string cardPath,
        SaveEntry save,
        char side)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-CARD-LIBRARY-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            SetBusy(
                true,
                $"Adding {save.GameTitle} to Save Library...");

            var temporaryPsu = Path.Combine(
                temporaryRoot,
                SanitizeUniversalFileName(save.DirectoryId) + ".psu");

            await _engine.ExportPsuAsync(
                cardPath,
                save.DirectoryId,
                temporaryPsu);

            var result = await _saveLibraryService.ImportAsync(
                temporaryPsu,
                _saveLibraryIndex);

            var entry = result.Entry;

            if (result.Duplicate is null)
            {
                entry.FormatName =
                    "EMS / Memory Linker PSU";
                entry.ImportedFrom =
                    GetLibrarySourceDisplayName(cardPath);

                await _saveLibraryService.SaveAsync(
                    _saveLibraryIndex);
            }

            if (result.Duplicate is not null)
            {
                LibraryFooterStatus.Text =
                    $"Already in library: {entry.DisplayTitle}";
                Log(
                    $"Card {side} save already in library: " +
                    save.DirectoryId);
            }
            else
            {
await LoadSaveLibraryIconAsync(entry);

                LibraryFooterStatus.Text =
                    $"Added {entry.DisplayTitle} from Card {side}.";
                Log(
                    $"Added Card {side} save to library: " +
                    save.DirectoryId);
            }

            ApplySaveLibraryFilter();

            MessageBox.Show(
                result.Duplicate is null
                    ? $"{entry.DisplayTitle} was added to the Save Library."
                    : $"{entry.DisplayTitle} is already in the Save Library.",
                "Save Library",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                $"Add Card {side} save to library failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "Add to Save Library Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); }
            catch { }

            SetBusy(false, "Ready.");
        }
    }

    private static string GetPs1CardTypeName(string extension) =>
        FormatCatalog.GetPs1CardTypeName(extension);

    private static string FormatSaveCount(int count) =>
        count == 1
            ? "1 save"
            : $"{count} saves";

    private async Task LoadMemoryCardLibraryAsync()
    {
        try
        {
            _memoryCardLibraryIndex =
                await _memoryCardLibraryService.LoadAsync();

            RefreshMemoryCardLibraryView();
            UpdateLibrarySummary();
        }
        catch(Exception ex)
        {
            LibraryFooterStatus.Text="Could not load Memory Card Library: "+ex.Message;
        }
    }

    private void RefreshMemoryCardLibraryView()
    {
        MemoryCardLibraryEntries.Clear();

        IEnumerable<MemoryCardLibraryEntry> entries =
            _memoryCardLibraryIndex.Entries;

        entries = _libraryPlatformFilter switch
        {
            LibraryPlatformFilter.Ps1 => entries.Where(IsPs1MemoryCardLibraryEntry),
            LibraryPlatformFilter.Ps2 => entries.Where(entry => !IsPs1MemoryCardLibraryEntry(entry)),
            _ => entries
        };

        foreach (var entry in
            entries
                .OrderByDescending(entry => entry.IsFavorite)
                .ThenByDescending(entry => entry.AddedUtc)
                .ThenBy(entry => entry.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase))
        {
            MemoryCardLibraryEntries.Add(entry);
        }
    }

    private void LibrarySavesMode_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowSaveLibraryMode();
    }

    private void LibraryCardsMode_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowMemoryCardLibraryMode();
    }

    private void ShowSaveLibraryMode()
    {
        _saveLibraryContentMode =
            SaveLibraryContentMode.GameSaves;

        MemoryCardLibraryList.SelectedItem = null;
        MemoryCardLibraryList.Visibility = Visibility.Collapsed;
        SaveLibraryList.Visibility = Visibility.Visible;
        LibrarySearchBox.IsEnabled = true;
        LibraryFilterButton.IsEnabled = true;
        LibraryMetadataHeading.Text = "SAVE METADATA";
        LibrarySaveStatusPanel.Visibility = Visibility.Visible;

        LibraryMetaSerialLabel.Text = "Game Serial";
        LibraryMetaCrc32Label.Visibility = Visibility.Visible;
        LibraryMetaCrc32.Visibility = Visibility.Visible;
        LibraryMetaHashLabel.Visibility = Visibility.Visible;
        LibraryMetaHash.Visibility = Visibility.Visible;
        LibraryExportButton.Visibility = Visibility.Visible;
        LibraryExportCardButton.Visibility = Visibility.Visible;
        LibraryInfoButton.Visibility = Visibility.Visible;
        LibraryRenameButton.Visibility = Visibility.Visible;
        LibraryRenameButton.IsEnabled = SaveLibraryList.SelectedItems.Count == 1;
        LibrarySlotAButtonText.Text = "Add to Card A";
        LibrarySlotBButtonText.Text = "Add to Card B";

        var selectedEntry =
            SaveLibraryList.SelectedItem as SaveLibraryEntry;

        UpdateSaveLibraryMetadata(
            selectedEntry);

        // UpdateSaveLibraryMetadata intentionally shows the temporary
        // "Checking..." state. Re-run the relationship calculation when
        // returning from Memory Cards so that state can never remain frozen.
        if (selectedEntry is not null)
            _ = UpdateSaveLibraryStatusAsync(selectedEntry);

        UpdateLibrarySummary();
    }

    private void ShowMemoryCardLibraryMode()
    {
        _saveLibraryContentMode =
            SaveLibraryContentMode.MemoryCards;

        _saveStatusGeneration++;

        // Keep the current game-save selection while the list is hidden.
        // The Save Information tab shares its animated preview model with that
        // selection, so clearing it here would dispose the live preview state.
        SaveLibraryList.Visibility = Visibility.Collapsed;
        MemoryCardLibraryList.Visibility = Visibility.Visible;
        LibrarySearchBox.IsEnabled = false;
        LibraryFilterButton.IsEnabled = true;
        LibraryMetadataHeading.Text = "MEMORY CARD METADATA";
        LibrarySaveStatusPanel.Visibility = Visibility.Collapsed;

        LibraryMetaSerialLabel.Text = "Game Saves";
        LibraryMetaCrc32Label.Visibility = Visibility.Collapsed;
        LibraryMetaCrc32.Visibility = Visibility.Collapsed;
        LibraryMetaHashLabel.Visibility = Visibility.Collapsed;
        LibraryMetaHash.Visibility = Visibility.Collapsed;
        LibraryExportButton.Visibility = Visibility.Collapsed;
        LibraryExportCardButton.Visibility = Visibility.Collapsed;
        LibraryInfoButton.Visibility = Visibility.Collapsed;
        LibraryRenameButton.Visibility = Visibility.Visible;
        LibrarySlotAButtonText.Text = "Open as Card A";
        LibrarySlotBButtonText.Text = "Open as Card B";

        RefreshMemoryCardLibraryView();
        ResetMemoryCardLibraryMetadata();
        UpdateLibrarySummary();
    }

    private void UpdateLibrarySummary()
    {
        if (SaveLibrarySummary is null)
            return;

        SaveLibrarySummary.Text =
            $"{_saveLibraryIndex.Entries.Count} game saves  •  " +
            $"{_memoryCardLibraryIndex.Entries.Count} memory cards";
    }

    private void ResetMemoryCardLibraryMetadata()
    {
        LibraryPreviewImage.Source = null;
        LibraryPreviewPlaceholder.Visibility = Visibility.Visible;
        LibraryPreviewPlaceholder.Text = "Select Memory Card";
        LibraryMetaTitle.Text = "Select Memory Card";
        LibraryMetaProfile.Text = "Memory card details appear here.";
        LibraryMetaDirectory.Text = "—";
        LibraryMetaSerial.Text = "—";
        LibraryMetaFormat.Text = "—";
        LibraryMetaSize.Text = "—";
        LibraryMetaAdded.Text = "—";
        LibraryMetaModified.Text = "—";
        LibraryMetaCrc32.Text = "—";
        LibraryMetaHash.Text = "—";
        LibraryDuplicateStatus.Text = "—";
        SetLibraryRelationships(null);
        LibraryFavoriteButtonText.Text = "Add Favorite";
        LibraryFavoriteButton.IsEnabled = false;
        LibraryExportButton.IsEnabled = false;
        LibraryExportCardButton.IsEnabled = false;
        LibraryInfoButton.IsEnabled = false;
        LibrarySlotAButton.IsEnabled = false;
        LibrarySlotBButton.IsEnabled = false;
        LibraryRenameButton.IsEnabled = false;
        LibraryResetNameButton.IsEnabled = false;
        LibraryRemoveButton.IsEnabled = false;
    }

    private void MemoryCardLibraryList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_saveLibraryContentMode !=
            SaveLibraryContentMode.MemoryCards)
        {
            return;
        }

        if (MemoryCardLibraryList.SelectedItem is not
            MemoryCardLibraryEntry entry)
        {
            if (MemoryCardLibraryList.Visibility == Visibility.Visible)
                ResetMemoryCardLibraryMetadata();
            return;
        }

        LibraryPreviewImage.Source =
            new BitmapImage(
                new Uri(
                    entry.LibraryIconPath,
                    UriKind.Absolute));
        LibraryPreviewPlaceholder.Visibility = Visibility.Collapsed;
        LibraryPreviewPlaceholder.Text = "Select Memory Card";
        LibraryMetaTitle.Text = entry.DisplayName;
        LibraryMetaProfile.Text = entry.DisplaySubtitle;
        LibraryMetaDirectory.Text = entry.OriginalPath;
        LibraryMetaSerial.Text = entry.SaveCountDisplay;
        LibraryMetaFormat.Text = entry.CardTypeDisplay;
        LibraryMetaSize.Text =
            $"{entry.SizeDisplay} • Stored: {entry.StoredSizeDisplay}";
        LibraryMetaAdded.Text =
            entry.AddedUtc.ToLocalTime().ToString("g");
        LibraryMetaModified.Text =
            entry.ModifiedUtc.ToLocalTime().ToString("g");
        LibraryFooterStatus.Text =
            $"{entry.DisplayName} • {entry.SaveCountDisplay}";

        LibraryFavoriteButtonText.Text =
            entry.IsFavorite
                ? "Remove Favorite"
                : "Add Favorite";
        LibraryFavoriteButton.IsEnabled = true;
        LibraryExportButton.IsEnabled = false;
        LibraryExportCardButton.IsEnabled = false;
        LibraryInfoButton.IsEnabled = false;
        LibrarySlotAButton.IsEnabled = true;
        LibrarySlotBButton.IsEnabled = true;
        LibraryRenameButton.IsEnabled = true;
        LibraryResetNameButton.IsEnabled =
            entry.IsUserRenamed ||
            IsCardNameDifferentFromOriginal(entry);
        LibraryRemoveButton.IsEnabled = true;
    }

    private static bool IsCardNameDifferentFromOriginal(
        MemoryCardLibraryEntry entry)
    {
        if (entry.IsUserRenamed)
            return true;

        if (string.IsNullOrWhiteSpace(entry.OriginalPath))
            return false;

        var trimmed = entry.OriginalPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);

        if (string.IsNullOrWhiteSpace(leaf))
            return false;

        var originalName = entry.IsFolderCard
            ? leaf
            : Path.GetFileNameWithoutExtension(leaf);

        return !entry.DisplayName.Equals(
            originalName,
            StringComparison.CurrentCultureIgnoreCase);
    }

    private async void LibraryResetName_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_saveLibraryContentMode == SaveLibraryContentMode.GameSaves)
        {
            if (SaveLibraryList.SelectedItem is not SaveLibraryEntry entry ||
                !entry.IsUserRenamed)
                return;

            var answer = MessageBox.Show(
                $"Reset the name of '{entry.DisplayTitle}' to its original PSM name?",
                "Reset Game Save Name",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                SetBusy(true, $"Resetting {entry.DisplayTitle}...");
                var selectedId = entry.Id;

                await _saveLibraryService.ResetNameAsync(
                    entry,
                    _saveLibraryIndex);

                ApplySaveLibraryFilter();

                var reset = _saveLibraryIndex.Entries.FirstOrDefault(
                    candidate => candidate.Id.Equals(
                        selectedId,
                        StringComparison.OrdinalIgnoreCase));

                if (reset is not null)
                {
                    SaveLibraryList.SelectedItem = reset;
                    SaveLibraryList.ScrollIntoView(reset);
                    UpdateSaveLibraryMetadata(reset);
                }

                LibraryFooterStatus.Text = "Game save name reset.";
            }
            catch (Exception ex)
            {
                Log($"Save Library reset name failed: {ex.Message}");
                MessageBox.Show(
                    ex.Message,
                    "Could Not Reset Game Save Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, "Ready.");
            }

            return;
        }

        if (MemoryCardLibraryList.SelectedItem is not MemoryCardLibraryEntry card ||
            !(card.IsUserRenamed || IsCardNameDifferentFromOriginal(card)))
            return;

        var cardAnswer = MessageBox.Show(
            $"Reset the name of '{card.DisplayName}' to its original card name?",
            "Reset Memory Card Name",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (cardAnswer != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(true, $"Resetting {card.DisplayName}...");
            var selectedId = card.Id;

            await _memoryCardLibraryService.ResetNameAsync(
                card,
                _memoryCardLibraryIndex);

            await LoadMemoryCardLibraryAsync();
            ShowMemoryCardLibraryMode();

            var reset = MemoryCardLibraryEntries.FirstOrDefault(
                candidate => candidate.Id.Equals(
                    selectedId,
                    StringComparison.OrdinalIgnoreCase));

            if (reset is not null)
            {
                MemoryCardLibraryList.SelectedItem = reset;
                MemoryCardLibraryList.ScrollIntoView(reset);
            }

            LibraryFooterStatus.Text = "Memory card name reset.";
        }
        catch (Exception ex)
        {
            Log($"Memory Card reset name failed: {ex.Message}");
            MessageBox.Show(
                ex.Message,
                "Could Not Reset Memory Card Name",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void LibraryResetAllNames_Click(
        object sender,
        RoutedEventArgs e)
    {
        var renamedSaves =
            _saveLibraryIndex.Entries
                .Where(entry => entry.IsUserRenamed)
                .ToArray();

        var renamedCards =
            _memoryCardLibraryIndex.Entries
                .Where(entry =>
                    entry.IsUserRenamed ||
                    IsCardNameDifferentFromOriginal(entry))
                .ToArray();

        if (renamedSaves.Length == 0 &&
            renamedCards.Length == 0)
        {
            MessageBox.Show(
                "There are no custom Library names to reset.",
                "Reset Library Names",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"Reset {renamedSaves.Length} renamed game save(s) and " +
            $"{renamedCards.Length} renamed memory card(s)?\n\n" +
            "This restores PSM's original/canonical Library names. Save and card data are not changed.",
            "Reset Entire Library Names",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        var failures = new List<string>();

        try
        {
            SetBusy(true, "Resetting Library names...");

            foreach (var entry in renamedSaves)
            {
                try
                {
                    await _saveLibraryService.ResetNameAsync(
                        entry,
                        _saveLibraryIndex);
                }
                catch (Exception ex)
                {
                    failures.Add($"{entry.DisplayTitle}: {ex.Message}");
                }
            }

            foreach (var card in renamedCards)
            {
                try
                {
                    await _memoryCardLibraryService.ResetNameAsync(
                        card,
                        _memoryCardLibraryIndex);
                }
                catch (Exception ex)
                {
                    failures.Add($"{card.DisplayName}: {ex.Message}");
                }
            }

            ApplySaveLibraryFilter();
            await LoadMemoryCardLibraryAsync();

            if (_saveLibraryContentMode == SaveLibraryContentMode.MemoryCards)
                ShowMemoryCardLibraryMode();
            else
                ShowSaveLibraryMode();

            LibraryFooterStatus.Text =
                failures.Count == 0
                    ? "All custom Library names reset."
                    : $"Library names reset with {failures.Count} failure(s).";

            if (failures.Count > 0)
            {
                MessageBox.Show(
                    string.Join(
                        Environment.NewLine,
                        failures.Take(10)),
                    "Some Names Could Not Be Reset",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void LibraryRename_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_saveLibraryContentMode ==
            SaveLibraryContentMode.GameSaves)
        {
            if (SaveLibraryList.SelectedItems.Count != 1 ||
                SaveLibraryList.SelectedItem is not SaveLibraryEntry saveEntry)
            {
                return;
            }

            var saveRenameDefaultName =
                !string.IsNullOrWhiteSpace(saveEntry.GameTitle)
                    ? saveEntry.GameTitle
                    : Path.GetFileNameWithoutExtension(
                        saveEntry.OriginalFileName);

            var saveRequestedName =
                PromptForLibraryCardName(
                    saveRenameDefaultName,
                    "Rename Game Save",
                    "Enter a new library name for this game save.",
                    "PSM will rename the stored library copy in the same location. The save format and internal save data are preserved.");

            if (string.IsNullOrWhiteSpace(saveRequestedName) ||
                saveRequestedName.Trim().Equals(
                    saveRenameDefaultName,
                    StringComparison.CurrentCulture))
            {
                return;
            }

            try
            {
                SetBusy(
                    true,
                    $"Renaming {saveEntry.DisplayTitle}...");

                var selectedId = saveEntry.Id;

                await _saveLibraryService.RenameAsync(
                    saveEntry,
                    _saveLibraryIndex,
                    saveRequestedName);

                ApplySaveLibraryFilter();

                var renamed =
                    _saveLibraryIndex.Entries.FirstOrDefault(
                        candidate =>
                            candidate.Id.Equals(
                                selectedId,
                                StringComparison.OrdinalIgnoreCase));

                if (renamed is not null)
                {
                    SaveLibraryList.SelectedItem = renamed;
                    SaveLibraryList.ScrollIntoView(renamed);
                    UpdateSaveLibraryMetadata(renamed);
                }

                LibraryFooterStatus.Text =
                    $"Renamed game save to {renamed?.DisplayTitle ?? saveRequestedName}.";
            }
            catch (Exception ex)
            {
                Log(
                    $"Save Library rename failed: {ex.Message}");

                MessageBox.Show(
                    ex.Message,
                    "Could Not Rename Game Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, "Ready.");
            }

            return;
        }

        if (_saveLibraryContentMode !=
                SaveLibraryContentMode.MemoryCards ||
            MemoryCardLibraryList.SelectedItem is not
                MemoryCardLibraryEntry entry)
        {
            return;
        }

        var renameDefaultName =
            !entry.IsFolderCard &&
            !string.IsNullOrWhiteSpace(entry.Extension) &&
            entry.DisplayName.EndsWith(
                entry.Extension,
                StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(entry.DisplayName)
                : entry.DisplayName;

        var renameKind =
            entry.IsFolderCard
                ? "PCSX2 folder memory card"
                : IsPs1MemoryCardLibraryEntry(entry)
                    ? "PS1 memory card"
                    : entry.Extension.Equals(
                        ".mc2",
                        StringComparison.OrdinalIgnoreCase)
                        ? "MemCard PRO2 memory card"
                        : "PS2 memory card";

        var requestedName =
            PromptForLibraryCardName(
                renameDefaultName,
                "Rename Memory Card",
                $"Enter a new name for this {renameKind}.",
                entry.IsFolderCard
                    ? "PSM will rename the stored folder with this name in the same location."
                    : "PSM will rename the stored library copy with this name in the same location. The memory-card format is preserved automatically.");

        if (string.IsNullOrWhiteSpace(requestedName) ||
            requestedName.Trim().Equals(
                entry.DisplayName,
                StringComparison.CurrentCulture))
        {
            return;
        }

        try
        {
            SetBusy(
                true,
                $"Renaming {entry.DisplayName}...");

            var selectedId = entry.Id;

            await _memoryCardLibraryService.RenameAsync(
                entry,
                _memoryCardLibraryIndex,
                requestedName);

            await LoadMemoryCardLibraryAsync();
            ShowMemoryCardLibraryMode();

            var renamed =
                MemoryCardLibraryEntries.FirstOrDefault(
                    candidate =>
                        candidate.Id.Equals(
                            selectedId,
                            StringComparison.OrdinalIgnoreCase));

            if (renamed is not null)
            {
                MemoryCardLibraryList.SelectedItem = renamed;
                MemoryCardLibraryList.ScrollIntoView(renamed);
            }

            LibraryFooterStatus.Text =
                $"Renamed memory card to {entry.DisplayName}.";
        }
        catch (Exception ex)
        {
            Log(
                $"Memory Card Library rename failed: {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "Could Not Rename Memory Card",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private void StartSaveLibraryIconLoading()
    {
        if (_saveLibraryIconsStarted || !_saveLibraryLoaded || !_engine.IsInstalled)
            return;

        _saveLibraryIconsStarted = true;
        _ = LoadSaveLibraryIconsAsync(_saveLibraryIndex.Entries);
    }

    private async Task LoadSaveLibraryAsync()
    {
        try
        {
            _saveLibraryIndex =
                await _saveLibraryService.LoadAsync();
            _saveLibraryLoaded = true;

            ApplySaveLibraryFilter();

            if (_engine.IsInstalled)
                StartSaveLibraryIconLoading();

            LibraryFooterStatus.Text =
                _saveLibraryIndex.Entries.Count == 0
                    ? "Import packaged saves to begin building your library."
                    : "Save Library loaded.";
        }
        catch (Exception ex)
        {
            LibraryFooterStatus.Text = "Could not load Save Library: " + ex.Message;
            Log("Save Library load failed: " + ex.Message);
        }
    }

    private async void LibraryImportCard_Click(
        object sender,
        RoutedEventArgs e)
    {
        var choice =
            ShowFileOrFolderSourceDialog(
                "IMPORT MEMORY CARDS",
                "Choose one or more supported memory-card files or PCSX2 folder cards.",
                "Import Memory Cards");

        if (choice == 0)
            return;

        string[] cardPaths;

        if (choice == 2)
        {
            var folderDialog =
                new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Choose PCSX2 Folder Memory Cards",
                    Multiselect = true
                };

            if (folderDialog.ShowDialog() != true)
                return;

            cardPaths = folderDialog.FolderNames;

            var invalidFolders =
                cardPaths.Where(
                    path =>
                        !File.Exists(
                            Path.Combine(
                                path,
                                "_pcsx2_superblock")))
                    .ToArray();

            if (invalidFolders.Length > 0)
            {
                MessageBox.Show(
                    $"{invalidFolders.Length} selected folder(s) are not PCSX2 Folder Cards because they do not contain _pcsx2_superblock.",
                    "Invalid Folder Card Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            var fileDialog =
                new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose Memory Cards",
                    Multiselect = true,
                    Filter = FormatCatalog.SupportedMemoryCardFilter
                };

            if (fileDialog.ShowDialog() != true)
                return;

            cardPaths = fileDialog.FileNames;
        }

        if (cardPaths.Length == 0)
            return;

        var imported = 0;
        var duplicates = 0;
        var formatted = 0;
        var skipped = 0;
        var failed = 0;
        MemoryCardLibraryEntry? lastEntry = null;

        SetBusy(
            true,
            $"Importing {cardPaths.Length} memory card(s)...");

        try
        {
            foreach (var cardPath in cardPaths)
            {
                string importPath =
                    cardPath;
                string? temporaryFormattedCard =
                    null;
                string? displayNameOverride =
                    null;
                string? originalPathOverride =
                    null;

                try
                {
                    var unformatted =
                        await DetectUnformattedPs2CardAsync(
                            cardPath);

                    if (unformatted is not null)
                    {
                        var answer =
                            MessageBox.Show(
                                $"{Path.GetFileName(cardPath)} appears to be an unformatted " +
                                $"{unformatted.Value.SizeMegabytes} MB PS2 memory card.\n\n" +
                                "Would you like PSM to format a safe copy and add that copy to the Memory Card Library?\n\n" +
                                "The original file will NOT be modified. Formatting creates a blank PS2 filesystem; " +
                                "any data in the formatted copy would be erased.",
                                "Unformatted PS2 Memory Card",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                        if (answer != MessageBoxResult.Yes)
                        {
                            skipped++;
                            Log(
                                $"Unformatted memory card skipped: {cardPath}");
                            continue;
                        }

                        var temporaryRoot =
                            Path.Combine(
                                Path.GetTempPath(),
                                "PSM-FORMAT-IMPORT-" +
                                Guid.NewGuid().ToString("N"));

                        Directory.CreateDirectory(
                            temporaryRoot);

                        temporaryFormattedCard =
                            Path.Combine(
                                temporaryRoot,
                                Path.GetFileName(cardPath));

                        // Make an actual byte-for-byte working copy of the
                        // user's unformatted card image first. Formatting is
                        // performed only against this copied file.
                        File.Copy(
                            cardPath,
                            temporaryFormattedCard,
                            overwrite: true);

                        if (unformatted.Value.NoEcc)
                        {
                            // myMC++ formats ECC-layout images directly. For
                            // a no-ECC source, format an ECC working copy and
                            // convert back to no-ECC so the imported Library
                            // card preserves the source layout.
                            var eccWorkingCard =
                                temporaryFormattedCard +
                                ".ecc-working.ps2";

                            await _engine.CreateCardAsync(
                                eccWorkingCard,
                                unformatted.Value.SizeMegabytes);

                            var convertedNoEcc =
                                temporaryFormattedCard +
                                ".formatted-noecc.ps2";

                            await _engine.CreateCardAsync(
                                convertedNoEcc,
                                unformatted.Value.SizeMegabytes,
                                noEcc: true);

                            File.Copy(
                                convertedNoEcc,
                                temporaryFormattedCard,
                                overwrite: true);

                            try
                            {
                                File.Delete(eccWorkingCard);
                                File.Delete(convertedNoEcc);
                            }
                            catch { }
                        }
                        else
                        {
                            await _engine.FormatExistingCardAsync(
                                temporaryFormattedCard,
                                unformatted.Value.SizeMegabytes);
                        }

                        var verification =
                            await _engine.ReadCardAsync(
                                temporaryFormattedCard);

                        if (verification.Saves.Count != 0)
                        {
                            throw new InvalidDataException(
                                "The formatted memory-card copy was expected to be blank, but verification found saves.");
                        }

                        // Verification above already proved this is now a
                        // valid blank PS2 card. Store that exact formatted copy
                        // directly instead of sending it back through the
                        // generic detector a second time.
                        var stored =
                            await _memoryCardLibraryService.StoreAsync(
                                temporaryFormattedCard,
                                "PlayStation 2",
                                FormatCatalog.GetPs2CardTypeName(
                                    temporaryFormattedCard),
                                verification.Saves.Count,
                                verification.TotalBytes,
                                Path.GetFileNameWithoutExtension(
                                    cardPath),
                                cardPath);

                        formatted++;
                        lastEntry =
                            stored.Entry;

                        if (stored.Duplicate is not null)
                            duplicates++;
                        else
                            imported++;

                        Log(
                            $"Formatted safe copy of unformatted PS2 card and imported it into the Library: {cardPath}");

                        continue;
                    }

                    var result =
                        await ImportMemoryCardIntoLibraryAsync(
                            importPath,
                            showMessage: false,
                            manageBusyState: false,
                            displayNameOverride:
                                displayNameOverride,
                            originalPathOverride:
                                originalPathOverride);

                    if (result is null)
                    {
                        failed++;
                        continue;
                    }

                    lastEntry =
                        result.Value.Entry;

                    if (result.Value.Duplicate)
                        duplicates++;
                    else
                        imported++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log(
                        $"Memory Card Library import failed for {cardPath}: {ex}");
                }
                finally
                {
                    if (temporaryFormattedCard is not null)
                    {
                        try
                        {
                            var temporaryRoot =
                                Path.GetDirectoryName(
                                    temporaryFormattedCard);

                            if (!string.IsNullOrWhiteSpace(
                                    temporaryRoot) &&
                                Directory.Exists(
                                    temporaryRoot))
                            {
                                Directory.Delete(
                                    temporaryRoot,
                                    true);
                            }
                        }
                        catch { }
                    }
                }
            }

            await LoadMemoryCardLibraryAsync();
            ShowMemoryCardLibraryMode();

            if (lastEntry is not null)
            {
                var selected =
                    MemoryCardLibraryEntries.FirstOrDefault(
                        entry =>
                            entry.Id.Equals(
                                lastEntry.Id,
                                StringComparison.OrdinalIgnoreCase));

                if (selected is not null)
                {
                    MemoryCardLibraryList.SelectedItem = selected;
                    MemoryCardLibraryList.ScrollIntoView(selected);
                }
            }

            LibraryFooterStatus.Text =
                $"Memory-card import complete: {imported} imported, " +
                $"{duplicates} duplicate(s), {formatted} formatted, " +
                $"{skipped} skipped, {failed} failed.";

            MessageBox.Show(
                $"Memory-card import complete.\n\n" +
                $"Imported: {imported}\n" +
                $"Already in library: {duplicates}\n" +
                $"Formatted before import: {formatted}\n" +
                $"Skipped: {skipped}\n" +
                $"Failed: {failed}",
                "Import Cards Complete",
                MessageBoxButton.OK,
                failed > 0 || skipped > 0
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private readonly record struct UnformattedPs2CardInfo(
        int SizeMegabytes,
        bool NoEcc);

    private static async Task<UnformattedPs2CardInfo?> DetectUnformattedPs2CardAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return null;

        var extension =
            Path.GetExtension(path)
                .ToLowerInvariant();

        if (extension is not ".ps2" and
            not ".mc2" and
            not ".mcd" and
            not ".vm2" and
            not ".vmc" and
            not ".bin")
        {
            return null;
        }

        var length =
            new FileInfo(path).Length;

        int? sizeMegabytes =
            null;
        var noEcc =
            false;

        foreach (var candidate in new[] { 8, 16, 32, 64 })
        {
            var logicalBytes =
                (long)candidate *
                1024 *
                1024;

            var eccBytes =
                logicalBytes /
                512 *
                528;

            if (length == logicalBytes)
            {
                sizeMegabytes =
                    candidate;
                noEcc =
                    true;
                break;
            }

            if (length == eccBytes)
            {
                sizeMegabytes =
                    candidate;
                noEcc =
                    false;
                break;
            }
        }

        if (sizeMegabytes is null)
            return null;

        // A brand-new/unformatted card image is erased flash: all bytes are FF.
        // Stream the check so even a 64 MB card does not need to be loaded at once.
        await using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

        var buffer =
            new byte[81920];

        while (true)
        {
            var read =
                await stream.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);

            if (read == 0)
                break;

            for (var index = 0; index < read; index++)
            {
                if (buffer[index] != 0xFF)
                    return null;
            }
        }

        return new UnformattedPs2CardInfo(
            sizeMegabytes.Value,
            noEcc);
    }

    private async Task<(MemoryCardLibraryEntry Entry, bool Duplicate)?> ImportMemoryCardIntoLibraryAsync(
        string cardPath,
        bool showMessage = true,
        bool manageBusyState = true,
        string? displayNameOverride = null,
        string? originalPathOverride = null)
    {
        try
        {
            if (manageBusyState)
            {
                SetBusy(
                    true,
                    $"Importing {Path.GetFileName(cardPath)} into the Memory Card Library...");
            }

            var isFolderCard =
                Directory.Exists(cardPath);

            var extension =
                isFolderCard
                    ? ".foldercard"
                    : Path.GetExtension(cardPath)
                        .ToLowerInvariant();

            string platform;
            string cardType;
            int saveCount;
            long? capacity;

            if (isFolderCard ||
                LooksLikePs2ImageCard(cardPath))
            {
                platform = "PlayStation 2";

                var result =
                    await _engine.ReadCardAsync(
                        cardPath);

                saveCount = result.Saves.Count;
                cardType =
                    FormatCatalog.GetPs2CardTypeName(
                        cardPath);
                capacity =
                    isFolderCard
                        ? null
                        : result.TotalBytes;
            }
            else if (Ps1MemoryCardService.LooksLikeSupportedCard(cardPath))
            {
                platform = "PlayStation";

                cardType =
                    GetPs1CardTypeName(
                        extension);

                var result =
                    await _ps1CardService.ReadAsync(
                        cardPath);

                saveCount =
                    result.Saves.Count(save =>
                        !save.IsDeleted);

                capacity =
                    Ps1MemoryCardService.CardSize;
            }
            else
            {
                throw new NotSupportedException(
                    "That file is not a supported PS1 or PS2 memory card.");
            }

            var stored =
                await _memoryCardLibraryService.StoreAsync(
                    cardPath,
                    platform,
                    cardType,
                    saveCount,
                    capacity,
                    displayNameOverride,
                    originalPathOverride);

            if (showMessage)
            {
                await LoadMemoryCardLibraryAsync();
                ShowMemoryCardLibraryMode();

                MemoryCardLibraryList.SelectedItem =
                    stored.Entry;

                MemoryCardLibraryList.ScrollIntoView(
                    stored.Entry);

                MessageBox.Show(
                    stored.Duplicate is null
                        ? $"{stored.Entry.DisplayName} was imported into the Memory Card Library."
                        : $"{stored.Entry.DisplayName} is already in the Memory Card Library.",
                    "Import Card",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return (stored.Entry, stored.Duplicate is not null);
        }
        catch (Exception ex)
        {
            LibraryFooterStatus.Text =
                "Memory-card import failed: " +
                ex.Message;

            Log(
                "Memory Card Library import failed: " +
                ex.Message);

            if (showMessage)
            {
                MessageBox.Show(
                    ex.Message,
                    "Import Card Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return null;
        }
        finally
        {
            if (manageBusyState)
                SetBusy(false, "Ready.");
        }
    }

    private async void LibraryImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = FormatCatalog.SaveLibraryImportFilter
        };

        if (dialog.ShowDialog() != true)
            return;

        var imported = 0;
        var duplicates = 0;

        SetBusy(true, "Importing saves into Save Library...");

        try
        {
            foreach (var path in dialog.FileNames)
            {
                LibraryFooterStatus.Text =
                    $"Inspecting {Path.GetFileName(path)}...";

                SaveLibraryImportResult result;
                string? temporaryPs1Package = null;

                try
                {
                    if (Ps1ExternalSaveService.LooksLikePs1SingleSave(path))
                    {
                        temporaryPs1Package = Path.Combine(
                            Path.GetTempPath(),
                            "PSM-LIBRARY-IMPORT-" + Guid.NewGuid().ToString("N") + ".ps1save");
                        await _ps1CardService.CreateSavePackageFromExternalSaveAsync(
                            path,
                            temporaryPs1Package);
                        result = await _saveLibraryService.ImportAsync(
                            temporaryPs1Package,
                            _saveLibraryIndex);
                    }
                    else
                    {
                        result = await _saveLibraryService.ImportAsync(
                            path,
                            _saveLibraryIndex);
                    }
                }
                finally
                {
                    if (temporaryPs1Package is not null)
                    {
                        try { File.Delete(temporaryPs1Package); } catch { }
                    }
                }

                if (result.Duplicate is not null)
                {
                    duplicates++;
                    Log($"Save Library duplicate skipped: {path}");
                    continue;
                }
await LoadSaveLibraryIconAsync(result.Entry);
                imported++;
                Log($"Save Library imported: {path}");
            }

            ApplySaveLibraryFilter();

            LibraryFooterStatus.Text =
                $"Imported {imported} save(s). " +
                $"{duplicates} exact duplicate(s) skipped.";

            MessageBox.Show(
                $"Save Library import completed.\n\n" +
                $"Imported: {imported}\n" +
                $"Exact duplicates skipped: {duplicates}",
                "Save Library",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LibraryFooterStatus.Text = "Import failed: " + ex.Message;
            Log("Save Library import failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "Save Library Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private void LibrarySearch_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateSearchClearButton(
            LibrarySearchBox,
            ClearLibrarySearchButton);
        ApplySaveLibraryFilter();
    }

    private void LibraryFilterButton_Click(
        object sender,
        RoutedEventArgs e) =>
        ShowLibraryFilterMenu(LibraryFilterButton);

    private void ShowLibraryFilterMenu(Button anchor)
    {
        var menu = new ContextMenu
        {
            Background =
                new SolidColorBrush(
                    Color.FromRgb(11, 18, 27)),
            Foreground = Brushes.White,
            BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(42, 60, 82)),
            BorderThickness = new Thickness(1),
            PlacementTarget = anchor,
            Placement =
                System.Windows.Controls.Primitives
                    .PlacementMode.Bottom
        };

        var filterHeader = new MenuItem
        {
            Header = "Show",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 7, 18, 7)
        };
        menu.Items.Add(filterHeader);

        var platformHeader = new MenuItem
        {
            Header = "Platform",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 7, 18, 7)
        };
        menu.Items.Add(platformHeader);
        menu.Items.Add(CreateSortMenuItem(
            "All PlayStation",
            _libraryPlatformFilter == LibraryPlatformFilter.All,
            () => SetLibraryPlatformFilter(LibraryPlatformFilter.All)));
        menu.Items.Add(CreateSortMenuItem(
            "PlayStation (PS1)",
            _libraryPlatformFilter == LibraryPlatformFilter.Ps1,
            () => SetLibraryPlatformFilter(LibraryPlatformFilter.Ps1)));
        menu.Items.Add(CreateSortMenuItem(
            "PlayStation 2 (PS2)",
            _libraryPlatformFilter == LibraryPlatformFilter.Ps2,
            () => SetLibraryPlatformFilter(LibraryPlatformFilter.Ps2)));

        if (_saveLibraryContentMode == SaveLibraryContentMode.MemoryCards)
        {
            menu.IsOpen = true;
            return;
        }

        menu.Items.Add(new Separator());

        menu.Items.Add(
            CreateSortMenuItem(
                "All Saves",
                _libraryFilterMode == LibraryFilterMode.All,
                () => SetLibraryFilter(
                    LibraryFilterMode.All)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Favorites",
                _libraryFilterMode == LibraryFilterMode.Favorites,
                () => SetLibraryFilter(
                    LibraryFilterMode.Favorites)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Duplicates",
                _libraryFilterMode == LibraryFilterMode.Duplicates,
                () => SetLibraryFilter(
                    LibraryFilterMode.Duplicates)));

        menu.Items.Add(new Separator());

        var sortHeader = new MenuItem
        {
            Header = "Sort By",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 7, 18, 7)
        };
        menu.Items.Add(sortHeader);

        menu.Items.Add(
            CreateSortMenuItem(
                "Game Name",
                _librarySortField == LibrarySortField.GameName,
                () => SetLibrarySort(
                    LibrarySortField.GameName,
                    _librarySortDescending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Directory ID",
                _librarySortField == LibrarySortField.DirectoryId,
                () => SetLibrarySort(
                    LibrarySortField.DirectoryId,
                    _librarySortDescending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Format",
                _librarySortField == LibrarySortField.Format,
                () => SetLibrarySort(
                    LibrarySortField.Format,
                    _librarySortDescending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Size",
                _librarySortField == LibrarySortField.Size,
                () => SetLibrarySort(
                    LibrarySortField.Size,
                    _librarySortDescending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Date Added",
                _librarySortField == LibrarySortField.DateAdded,
                () => SetLibrarySort(
                    LibrarySortField.DateAdded,
                    _librarySortDescending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Date Modified",
                _librarySortField == LibrarySortField.DateModified,
                () => SetLibrarySort(
                    LibrarySortField.DateModified,
                    _librarySortDescending)));

        menu.Items.Add(
            CreateSortMenuItem(
                "Favorites First",
                _librarySortField == LibrarySortField.FavoritesFirst,
                () => SetLibrarySort(
                    LibrarySortField.FavoritesFirst,
                    _librarySortDescending)));

        menu.Items.Add(new Separator());

        var ascendingLabel =
            _librarySortField switch
            {
                LibrarySortField.Size => "Small to Large",
                LibrarySortField.DateAdded or
                LibrarySortField.DateModified => "Oldest to Newest",
                LibrarySortField.FavoritesFirst => "Favorites First",
                _ => "A to Z"
            };

        var descendingLabel =
            _librarySortField switch
            {
                LibrarySortField.Size => "Large to Small",
                LibrarySortField.DateAdded or
                LibrarySortField.DateModified => "Newest to Oldest",
                LibrarySortField.FavoritesFirst => "Favorites Last",
                _ => "Z to A"
            };

        menu.Items.Add(
            CreateSortMenuItem(
                ascendingLabel,
                !_librarySortDescending,
                () => SetLibrarySort(
                    _librarySortField,
                    false)));

        menu.Items.Add(
            CreateSortMenuItem(
                descendingLabel,
                _librarySortDescending,
                () => SetLibrarySort(
                    _librarySortField,
                    true)));

        menu.IsOpen = true;
    }

    private void SetLibraryPlatformFilter(
        LibraryPlatformFilter mode)
    {
        _libraryPlatformFilter = mode;
        if (_saveLibraryContentMode == SaveLibraryContentMode.MemoryCards)
            RefreshMemoryCardLibraryView();
        else
            ApplySaveLibraryFilter();
        UpdateLibrarySummary();
    }

    private void SetLibraryFilter(
        LibraryFilterMode mode)
    {
        _libraryFilterMode = mode;
        ApplySaveLibraryFilter();
    }

    private void SetLibrarySort(
        LibrarySortField field,
        bool descending)
    {
        _librarySortField = field;
        _librarySortDescending = descending;
        ApplySaveLibraryFilter();
    }

    private void ApplySaveLibraryFilter()
    {
        if (SaveLibraryList is null || LibrarySearchBox is null)
            return;

        var query =
            LibrarySearchBox.Text?.Trim()
            ?? string.Empty;

        IEnumerable<SaveLibraryEntry> entries =
            _saveLibraryIndex.Entries;

        entries = _libraryPlatformFilter switch
        {
            LibraryPlatformFilter.Ps1 => entries.Where(IsPs1LibraryEntry),
            LibraryPlatformFilter.Ps2 => entries.Where(entry => !IsPs1LibraryEntry(entry)),
            _ => entries
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            entries = entries.Where(entry =>
                entry.DisplayTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.DisplaySubtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.DirectoryId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.FormatName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.OriginalFileName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        if (_libraryFilterMode ==
            LibraryFilterMode.Favorites)
        {
            entries = entries.Where(
                entry => entry.IsFavorite);
        }
        else if (_libraryFilterMode ==
                 LibraryFilterMode.Duplicates)
        {
            var duplicateHashes =
                _saveLibraryIndex.Entries
                    .GroupBy(
                        entry => entry.Sha256,
                        StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            entries = entries.Where(
                entry =>
                    duplicateHashes.Contains(
                        entry.Sha256));
        }

        entries = _librarySortField switch
        {
            LibrarySortField.DirectoryId =>
                _librarySortDescending
                    ? entries.OrderByDescending(
                        entry => entry.DirectoryId,
                        StringComparer.OrdinalIgnoreCase)
                    : entries.OrderBy(
                        entry => entry.DirectoryId,
                        StringComparer.OrdinalIgnoreCase),

            LibrarySortField.Format =>
                _librarySortDescending
                    ? entries.OrderByDescending(
                        entry => entry.FormatName,
                        StringComparer.CurrentCultureIgnoreCase)
                    : entries.OrderBy(
                        entry => entry.FormatName,
                        StringComparer.CurrentCultureIgnoreCase),

            LibrarySortField.Size =>
                _librarySortDescending
                    ? entries.OrderByDescending(
                        entry => entry.SizeBytes)
                    : entries.OrderBy(
                        entry => entry.SizeBytes),

            LibrarySortField.DateAdded =>
                _librarySortDescending
                    ? entries.OrderByDescending(
                        entry => entry.AddedUtc)
                    : entries.OrderBy(
                        entry => entry.AddedUtc),

            LibrarySortField.DateModified =>
                _librarySortDescending
                    ? entries.OrderByDescending(
                        entry => entry.ModifiedUtc)
                    : entries.OrderBy(
                        entry => entry.ModifiedUtc),

            LibrarySortField.FavoritesFirst =>
                _librarySortDescending
                    ? entries
                        .OrderBy(entry => entry.IsFavorite)
                        .ThenBy(
                            entry => entry.DisplayTitle,
                            StringComparer.CurrentCultureIgnoreCase)
                    : entries
                        .OrderByDescending(
                            entry => entry.IsFavorite)
                        .ThenBy(
                            entry => entry.DisplayTitle,
                            StringComparer.CurrentCultureIgnoreCase),

            _ =>
                _librarySortDescending
                    ? entries.OrderByDescending(
                        entry => entry.DisplayTitle,
                        StringComparer.CurrentCultureIgnoreCase)
                    : entries.OrderBy(
                        entry => entry.DisplayTitle,
                        StringComparer.CurrentCultureIgnoreCase)
        };

        _saveLibraryView.Clear();
        _saveLibraryView.AddRange(entries);

        SaveLibraryList.ItemsSource = null;
        SaveLibraryList.ItemsSource = _saveLibraryView;

        var favoriteCount = _saveLibraryIndex.Entries.Count(entry => entry.IsFavorite);
        SaveLibrarySummary.Text =
            $"{_saveLibraryIndex.Entries.Count} saves  •  {favoriteCount} favorites";
    }

    private async void SaveLibraryList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_saveLibraryContentMode !=
            SaveLibraryContentMode.GameSaves)
        {
            return;
        }

        var entry =
            SaveLibraryList.SelectedItem as SaveLibraryEntry;
        _saveInformationEntry = entry;
        UpdateSaveLibraryMetadata(entry);
        await UpdateSaveLibraryStatusAsync(entry);
        await UpdateSaveLibraryCrc32Async(entry);
        await LoadSaveLibraryPreviewAsync(entry);

        if (SaveInformationTab.IsSelected)
            await RefreshSaveInformationAsync();
    }

    private void SaveLibraryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SaveLibraryList.SelectedItem is SaveLibraryEntry)
            LibraryExport_Click(sender, new RoutedEventArgs());
    }

    private void UpdateSaveLibraryMetadata(SaveLibraryEntry? entry)
    {
        var selectedCount = SaveLibraryList?.SelectedItems.Count ?? (entry is null ? 0 : 1);
        var enabled = entry is not null;
        LibraryFavoriteButton.IsEnabled = selectedCount > 0;
        LibraryExportButton.IsEnabled = enabled && selectedCount == 1;
        LibraryExportCardButton.IsEnabled = selectedCount > 0;
        LibraryInfoButton.IsEnabled = enabled && selectedCount == 1;
        RefreshLibrarySlotButtons();
        LibraryRenameButton.IsEnabled = selectedCount == 1;
        LibraryResetNameButton.IsEnabled =
            selectedCount == 1 &&
            entry is not null &&
            entry.IsUserRenamed;
        LibraryRemoveButton.IsEnabled = selectedCount > 0;

        if (entry is null)
        {
            LibraryPreviewImage.Source = null;
            LibraryPreviewPlaceholder.Visibility = Visibility.Visible;
            LibraryPreviewPlaceholder.Text = "Select a save";
            _libraryPreviewModel = null;
            _libraryPreviewFallback = null;
            LibraryMetaTitle.Text = "Select a save";
            LibraryMetaProfile.Text = "Metadata appears here.";
            LibraryMetaDirectory.Text = "—";
            LibraryMetaSerial.Text = "—";
            LibraryMetaFormat.Text = "—";
            LibraryMetaFormat.ToolTip = null;
            LibraryMetaSize.Text = "—";
            LibraryMetaAdded.Text = "—";
            LibraryMetaModified.Text = "—";
            LibraryMetaCrc32.Text = "—";
            LibraryMetaHash.Text = "—";
            LibraryDuplicateStatus.Text = "—";
            SetLibraryRelationships(null);
            LibraryFavoriteButtonText.Text = "Add Favorite";
            return;
        }

        var isPs1Package =
            entry.Extension.Equals(
                ".ps1save",
                StringComparison.OrdinalIgnoreCase);

        if (isPs1Package)
        {
            // PS1 packages do not use the animated PS2 model path.
            LibraryPreviewImage.Source = entry.IconImage;
            LibraryPreviewPlaceholder.Visibility =
                entry.IconImage is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            LibraryPreviewPlaceholder.Text =
                entry.IconImage is null
                    ? "Loading preview..."
                    : "Select a save";
        }
        else
        {
            var previewCachePath =
                GetSaveLibraryPreviewCachePath(
                    entry);

            if (File.Exists(previewCachePath))
            {
                LibraryPreviewImage.Source =
                    LoadFrozenBitmap(
                        previewCachePath);

                LibraryPreviewPlaceholder.Visibility =
                    Visibility.Collapsed;
            }
            else
            {
                LibraryPreviewImage.Source = null;
                LibraryPreviewPlaceholder.Text =
                    "Loading preview...";
                LibraryPreviewPlaceholder.Visibility =
                    Visibility.Visible;
            }
        }
        LibraryMetaTitle.Text = entry.DisplayTitle;
        LibraryMetaProfile.Text = entry.DisplaySubtitle;
        LibraryMetaDirectory.Text =
            string.IsNullOrWhiteSpace(entry.DirectoryId) ? "—" : entry.DirectoryId;

        var gameSerial = ExtractGameSerial(entry.DirectoryId);
        LibraryMetaSerial.Text =
            string.IsNullOrWhiteSpace(gameSerial) ? "Unknown" : gameSerial;

        LibraryMetaFormat.Text =
            GetSaveLibraryFormatDisplay(entry);
        LibraryMetaFormat.ToolTip =
            $"Imported From: {GetSaveLibraryImportedFromDisplay(entry)}";
        LibraryMetaSize.Text = entry.SizeDisplay;
        LibraryMetaAdded.Text =
            entry.AddedUtc.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
        LibraryMetaModified.Text =
            entry.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
        LibraryMetaCrc32.Text = "Calculating...";
        LibraryMetaHash.Text = entry.Sha256;
        var selectedFavorites =
            SaveLibraryList?.SelectedItems.Cast<SaveLibraryEntry>().ToArray()
            ?? Array.Empty<SaveLibraryEntry>();

        if (LibraryFavoriteButtonText is not null)
        {
            LibraryFavoriteButtonText.Text =
                selectedCount > 1
                    ? (selectedFavorites.Length > 0 &&
                       selectedFavorites.All(candidate => candidate.IsFavorite)
                        ? "Remove Favorites"
                        : "Add Favorites")
                    : (entry.IsFavorite ? "Remove Favorite" : "Add Favorite");
        }

        LibraryDuplicateStatus.Text = "Checking save relationships...";
        SetLibraryRelationships(null);
    }

    private static string GetSaveLibraryFormatDisplay(
        SaveLibraryEntry entry)
    {
        var isPs1 =
            entry.Platform.Equals(
                "PlayStation",
                StringComparison.OrdinalIgnoreCase);

        return entry.Extension.ToLowerInvariant() switch
        {
            ".cbs" => "PS2 Individual Save - CBS • CodeBreaker (*.cbs)",
            ".max" => "PS2 Individual Save - MAX • Action Replay MAX (*.max)",
            ".psu" => "PS2 Individual Save - PSU • EMS / uLaunchELF (*.psu)",
            ".psv" => isPs1
                ? "PS1 Individual Save - PSV • PS3 Virtual Save (*.psv)"
                : "PS2 Individual Save - PSV • PS3 Virtual Save (*.psv)",
            ".sps" => "PS2 Individual Save - SPS • SharkPort (*.sps)",
            ".xps" => "PS2 Individual Save - XPS • X-Port / Xploder (*.xps)",

            ".mcb" => "PS1 Individual Save - MCB • Smart Link (*.mcb)",
            ".mcs" => "PS1 Individual Save - MCS • PSXGameEdit (*.mcs)",
            ".mcx" => "PS1 Individual Save - MCX • Datel (*.mcx)",
            ".pda" => "PS1 Individual Save - PDA • Datel (*.pda)",
            ".ps1" => "PS1 Individual Save - PS1 • Memory Juggler (*.ps1)",
            ".psx" => "PS1 Individual Save - PSX • X-Port / AR / GameShark (*.psx)",
            ".raw" => "PS1 Individual Save - RAW (*.raw)",
            ".ps1save" => "PSM PlayStation Save Package (*.ps1save)",
            ".ps2save" => "PSM PlayStation Save Package (*.ps2save)",
            _ => entry.FormatName
        };
    }

    private static string GetSaveLibraryImportedFromDisplay(
        SaveLibraryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(
                entry.ImportedFrom))
        {
            // Old direct-from-card entries only recorded this generic marker.
            // The precise source filename was never persisted, so do not invent one.
            if (entry.ImportedFrom.Equals(
                    "PS2 Memory Card",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "PS2 Memory Card • legacy source name not recorded";
            }

            return entry.ImportedFrom;
        }

        // Very old library entries did not persist ImportedFrom at all.
        if (entry.FormatName.Equals(
                "Native PS2 Memory Card Save",
                StringComparison.OrdinalIgnoreCase) ||
            entry.FormatName.Equals(
                "PS2 Memory Card Save Directory",
                StringComparison.OrdinalIgnoreCase))
        {
            return "PS2 Memory Card • legacy source name not recorded";
        }

        if (!string.IsNullOrWhiteSpace(
                entry.OriginalFileName))
        {
            return entry.OriginalFileName;
        }

        return "Legacy library entry • source not recorded";
    }

    private static string GetLibrarySourceDisplayName(
        string sourcePath)
    {
        if (Directory.Exists(sourcePath))
        {
            var trimmed =
                sourcePath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            var folderName =
                Path.GetFileName(trimmed);

            return string.IsNullOrWhiteSpace(folderName)
                ? "PCSX2 Folder Card"
                : $"{folderName} • PCSX2 Folder Card";
        }

        var fileName =
            Path.GetFileName(sourcePath);

        return string.IsNullOrWhiteSpace(fileName)
            ? sourcePath
            : fileName;
    }

    private async Task UpdateSaveLibraryStatusAsync(
        SaveLibraryEntry? entry)
    {
        var generation =
            ++_saveStatusGeneration;

        if (entry is null)
        {
            LibraryDuplicateStatus.Text = "—";
            SetLibraryRelationships(null);
            return;
        }

        bool IsCurrent()
        {
            return generation == _saveStatusGeneration &&
                   _saveLibraryContentMode ==
                       SaveLibraryContentMode.GameSaves &&
                   ReferenceEquals(
                       SaveLibraryList.SelectedItem,
                       entry);
        }

        try
        {
            var selectedFingerprint =
                await GetSavePayloadFingerprintAsync(entry);

            if (!IsCurrent())
                return;

            var matching = new List<SaveLibraryEntry>();
            var related = new List<SaveLibraryEntry>();

            foreach (var candidate in _saveLibraryIndex.Entries)
            {
                if (!IsCurrent())
                    return;

                if (candidate.Id.Equals(
                        entry.Id,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!AreRelatedSaveIdentities(entry, candidate))
                    continue;

                var candidateFingerprint =
                    await GetSavePayloadFingerprintAsync(candidate);

                if (!IsCurrent())
                    return;

                if (!string.IsNullOrWhiteSpace(selectedFingerprint) &&
                    selectedFingerprint.Equals(
                        candidateFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matching.Add(candidate);
                }
                else
                {
                    related.Add(candidate);
                }
            }

            if (!IsCurrent())
                return;

            var links =
                matching.Select(
                    candidate =>
                        new SaveRelationshipLink(
                            candidate.Id,
                            $"{candidate.DisplayTitle} ({candidate.Extension.ToUpperInvariant()})",
                            "Same underlying save data in another library entry. Click to open it.",
                            candidate.IconImage))
                .Concat(
                    related.Select(
                        candidate =>
                            new SaveRelationshipLink(
                                candidate.Id,
                                $"{candidate.DisplayTitle} ({candidate.Extension.ToUpperInvariant()})",
                                "Same game, different underlying save data. Click to open it.",
                                candidate.IconImage)))
                .ToArray();

            if (!IsCurrent())
                return;

            LibraryDuplicateStatus.Text =
                matching.Count == 0 && related.Count == 0
                    ? "Unique Save • No matching or related saves found."
                    : matching.Count > 0 && related.Count > 0
                        ? $"Matching Save • {matching.Count} match(es)  •  Related Saves • {related.Count}"
                        : matching.Count > 0
                            ? $"Matching Save • {matching.Count} match(es)"
                            : $"Related Saves • {related.Count}";

            SetLibraryRelationships(
                links.Length == 0 ? null : links);
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                LibraryDuplicateStatus.Text =
                    "Save relationship status unavailable.";
                SetLibraryRelationships(null);
            }

            Log(
                $"Save Status failed for {entry.OriginalFileName}: {ex.Message}");
        }
    }

    private static bool AreRelatedSaveIdentities(
        SaveLibraryEntry left,
        SaveLibraryEntry right)
    {
        if (!left.Platform.Equals(
                right.Platform,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var leftSerial =
            ExtractGameSerial(
                left.DirectoryId);

        var rightSerial =
            ExtractGameSerial(
                right.DirectoryId);

        // Game serial is the strongest relationship signal. This intentionally
        // groups separate save directories from the same title, such as:
        // BASLUS-21004Options and BASLUS-21004BEAMIN -> SLUS-21004.
        if (!string.IsNullOrWhiteSpace(
                leftSerial) &&
            !string.IsNullOrWhiteSpace(
                rightSerial))
        {
            return leftSerial.Equals(
                rightSerial,
                StringComparison.OrdinalIgnoreCase);
        }

        // If a reliable serial cannot be extracted, matching full directory IDs
        // still identify alternate versions/snapshots of the same save family.
        if (!string.IsNullOrWhiteSpace(
                left.DirectoryId) &&
            !string.IsNullOrWhiteSpace(
                right.DirectoryId) &&
            left.DirectoryId.Equals(
                right.DirectoryId,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Final fallback for legacy/odd packages where only the title is usable.
        return !string.IsNullOrWhiteSpace(
                   left.GameTitle) &&
               !string.IsNullOrWhiteSpace(
                   right.GameTitle) &&
               left.GameTitle.Equals(
                   right.GameTitle,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private async Task<string> GetSavePayloadFingerprintAsync(
        SaveLibraryEntry entry)
    {
        if (_savePayloadFingerprintCache.TryGetValue(
                entry.Id,
                out var cached))
        {
            return cached;
        }

        var storedPath =
            _saveLibraryService.GetStoredPath(entry);

        if (!File.Exists(storedPath))
            return string.Empty;

        string fingerprint;

        if (entry.Extension.Equals(
                ".ps1save",
                StringComparison.OrdinalIgnoreCase))
        {
            fingerprint =
                await ComputePs1PackagePayloadFingerprintAsync(
                    storedPath);
        }
        else
        {
            fingerprint =
                await ComputePs2PackagePayloadFingerprintAsync(
                    entry,
                    storedPath);
        }

        _savePayloadFingerprintCache[entry.Id] =
            fingerprint;

        return fingerprint;
    }

    private static async Task<string>
        ComputePs1PackagePayloadFingerprintAsync(
            string packagePath)
    {
        using var sha =
            System.Security.Cryptography.SHA256.Create();

        await using var stream =
            File.OpenRead(packagePath);

        using var archive =
            new System.IO.Compression.ZipArchive(
                stream,
                System.IO.Compression.ZipArchiveMode.Read,
                leaveOpen: false);

        var blocks =
            archive.GetEntry("save-blocks.bin")
            ?? throw new InvalidDataException(
                "The PS1 package has no save-block payload.");

        await using var payload =
            blocks.Open();

        var hash =
            await sha.ComputeHashAsync(payload);

        return Convert.ToHexString(hash);
    }

    private async Task<string>
        ComputePs2PackagePayloadFingerprintAsync(
            SaveLibraryEntry entry,
            string packagePath)
    {
        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-SAVE-STATUS-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            temporaryRoot);

        try
        {
            var cardPath =
                Path.Combine(
                    temporaryRoot,
                    "status.ps2");

            await _engine.CreateCardAsync(
                cardPath,
                false);

            var importPath =
                packagePath;

            if (entry.Extension.Equals(
                    ".sps",
                    StringComparison.OrdinalIgnoreCase))
            {
                importPath =
                    Path.Combine(
                        temporaryRoot,
                        "status.psu");

                await SpsPackageService.ConvertToPsuAsync(
                    packagePath,
                    importPath);
            }

            await _engine.ImportAsync(
                cardPath,
                importPath);

            await _engine.CheckAsync(
                cardPath);

            var saves =
                await _engine.ReadDirectoryAsync(
                    cardPath);

            var save =
                saves.FirstOrDefault(
                    candidate =>
                        candidate.DirectoryId.Equals(
                            entry.DirectoryId,
                            StringComparison.OrdinalIgnoreCase))
                ?? saves.FirstOrDefault()
                ?? throw new InvalidDataException(
                    "The package did not contain a readable PS2 save.");

            var canonicalPsu =
                Path.Combine(
                    temporaryRoot,
                    "canonical.psu");

            await _engine.ExportPsuAsync(
                cardPath,
                save.DirectoryId,
                canonicalPsu);

            var bytes =
                await File.ReadAllBytesAsync(
                    canonicalPsu);

            // PSU timestamps live in the directory-entry metadata and can
            // differ when the same save is wrapped by another package format.
            // Hash the canonical PSU payload after its fixed 0x200-byte header.
            var payloadOffset =
                Math.Min(
                    0x200,
                    bytes.Length);

            var hash =
                System.Security.Cryptography.SHA256.HashData(
                    bytes.AsSpan(payloadOffset));

            return Convert.ToHexString(hash);
        }
        finally
        {
            try
            {
                if (Directory.Exists(
                        temporaryRoot))
                {
                    Directory.Delete(
                        temporaryRoot,
                        recursive: true);
                }
            }
            catch { }
        }
    }

    private void SetLibraryRelationships(
        IReadOnlyCollection<SaveRelationshipLink>? links)
    {
        LibrarySaveRelationships.ItemsSource = links;

        if (links is null ||
            links.Count == 0)
        {
            LibraryRelationshipsButton.Visibility =
                Visibility.Collapsed;
            LibraryRelationshipsButton.Content =
                "View Saves";
            return;
        }

        LibraryRelationshipsButton.Content =
            links.Count == 1
                ? "View 1 Save"
                : $"View {links.Count} Saves";

        LibraryRelationshipsButton.Visibility =
            Visibility.Visible;
    }

    private void LibraryRelationshipsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var links =
            LibrarySaveRelationships.ItemsSource
                as IEnumerable<SaveRelationshipLink>;

        if (links is null)
            return;

        var items =
            links.ToArray();

        if (items.Length == 0)
            return;

        var window =
            new Window
            {
                Title =
                    items.Length == 1
                        ? "Related Save"
                        : $"Related Saves ({items.Length})",
                Owner = this,
                Width = 470,
                Height = Math.Min(
                    520,
                    145 + (items.Length * 58)),
                MinWidth = 390,
                MinHeight = 220,
                MaxHeight = 650,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(7, 11, 16)),
                Foreground = Brushes.White
            };

        var root =
            new Grid
            {
                Margin =
                    new Thickness(18)
            };

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var heading =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        12)
            };

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    "SAVE RELATIONSHIPS",
                FontSize = 20,
                FontWeight =
                    FontWeights.Bold
            });

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    "Select a save to open it in the Save Library.",
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            159,
                            176,
                            197)),
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0)
            });

        Grid.SetRow(
            heading,
            0);
        root.Children.Add(
            heading);

        var listPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Vertical
            };

        foreach (var link in items)
        {
            var row =
                new Grid
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            7)
                };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(48)
                });
            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            var iconHost =
                new Border
                {
                    Width = 42,
                    Height = 42,
                    CornerRadius =
                        new CornerRadius(5),
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                5,
                                9,
                                14)),
                    BorderBrush =
                        new SolidColorBrush(
                            Color.FromRgb(
                                34,
                                48,
                                68)),
                    BorderThickness =
                        new Thickness(1),
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            if (link.IconImage is not null)
            {
                iconHost.Child =
                    new Image
                    {
                        Source =
                            link.IconImage,
                        Width = 36,
                        Height = 36,
                        Stretch =
                            Stretch.Uniform,
                        HorizontalAlignment =
                            HorizontalAlignment.Center,
                        VerticalAlignment =
                            VerticalAlignment.Center,
                        SnapsToDevicePixels =
                            true
                    };
            }

            Grid.SetColumn(
                iconHost,
                0);
            row.Children.Add(
                iconHost);

            var item =
                new Button
                {
                    Content =
                        link.Label,
                    Tag =
                        link.EntryId,
                    ToolTip =
                        link.ToolTip,
                    Height = 46,
                    Margin =
                        new Thickness(
                            6,
                            0,
                            0,
                            0),
                    Padding =
                        new Thickness(
                            12,
                            0,
                            12,
                            0),
                    HorizontalContentAlignment =
                        HorizontalAlignment.Left,
                    VerticalContentAlignment =
                        VerticalAlignment.Center,
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                13,
                                19,
                                27)),
                    Foreground =
                        Brushes.White,
                    BorderBrush =
                        new SolidColorBrush(
                            Color.FromRgb(
                                34,
                                48,
                                68)),
                    BorderThickness =
                        new Thickness(1)
                };

            item.Click +=
                (_, _) =>
                {
                    var button =
                        new Button
                        {
                            Tag =
                                link.EntryId
                        };

                    LibrarySaveRelationship_Click(
                        button,
                        new RoutedEventArgs());

                    window.Close();
                };

            Grid.SetColumn(
                item,
                1);
            row.Children.Add(
                item);

            listPanel.Children.Add(
                row);
        }

        var scrollViewer =
            new ScrollViewer
            {
                Content =
                    listPanel,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled
            };

        Grid.SetRow(
            scrollViewer,
            1);
        root.Children.Add(
            scrollViewer);

        window.Content =
            root;

        window.Show();
    }

    private void LibrarySaveRelationship_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string entryId)
        {
            return;
        }

        var target =
            _saveLibraryIndex.Entries.FirstOrDefault(
                candidate =>
                    candidate.Id.Equals(
                        entryId,
                        StringComparison.OrdinalIgnoreCase));

        if (target is null)
            return;

        if (!_saveLibraryView.Any(
                candidate =>
                    candidate.Id.Equals(
                        target.Id,
                        StringComparison.OrdinalIgnoreCase)))
        {
            LibrarySearchBox.Text = string.Empty;
            _libraryPlatformFilter =
                LibraryPlatformFilter.All;
            ApplySaveLibraryFilter();
        }

        SaveLibraryList.SelectedItem =
            target;

        SaveLibraryList.ScrollIntoView(
            target);

        SaveLibraryList.Focus();
    }

    private async Task<string> GetSaveInformationStatusTextAsync(
        SaveLibraryEntry entry)
    {
        try
        {
            var selectedFingerprint =
                await GetSavePayloadFingerprintAsync(entry);

            var matchingCount = 0;
            var relatedCount = 0;

            foreach (var candidate in _saveLibraryIndex.Entries)
            {
                if (candidate.Id.Equals(
                        entry.Id,
                        StringComparison.OrdinalIgnoreCase) ||
                    !AreRelatedSaveIdentities(
                        entry,
                        candidate))
                {
                    continue;
                }

                var candidateFingerprint =
                    await GetSavePayloadFingerprintAsync(candidate);

                if (!string.IsNullOrWhiteSpace(
                        selectedFingerprint) &&
                    selectedFingerprint.Equals(
                        candidateFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchingCount++;
                }
                else
                {
                    relatedCount++;
                }
            }

            return matchingCount == 0 &&
                   relatedCount == 0
                ? "Unique Save • No matching or related saves found."
                : matchingCount > 0 &&
                  relatedCount > 0
                    ? $"Matching Save • {matchingCount} match(es) • Related Saves • {relatedCount}"
                    : matchingCount > 0
                        ? $"Matching Save • {matchingCount} match(es)"
                        : $"Related Saves • {relatedCount}";
        }
        catch
        {
            return "Save relationship status unavailable.";
        }
    }

    private async Task UpdateSaveLibraryCrc32Async(
        SaveLibraryEntry? entry)
    {
        if (entry is null)
        {
            LibraryMetaCrc32.Text = "—";
            return;
        }

        var storedPath = _saveLibraryService.GetStoredPath(entry);

        if (!File.Exists(storedPath))
        {
            if (ReferenceEquals(
                SaveLibraryList.SelectedItem,
                entry))
            {
                LibraryMetaCrc32.Text = "Package file missing";
            }

            return;
        }

        try
        {
            var crc32 = await Task.Run(
                () => ComputeCrc32(storedPath));

            if (ReferenceEquals(
                SaveLibraryList.SelectedItem,
                entry))
            {
                LibraryMetaCrc32.Text = crc32;
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(
                SaveLibraryList.SelectedItem,
                entry))
            {
                LibraryMetaCrc32.Text = "Unavailable";
            }

            Log(
                $"Save Library CRC32 failed for " +
                $"{entry.OriginalFileName}: {ex.Message}");
        }
    }

    private async Task LoadSaveLibraryPreviewAsync(SaveLibraryEntry? entry)
    {
        _libraryPreviewModel = null;
        _libraryPreviewFallback = null;

        if (entry is null)
            return;

        var packagePath = _saveLibraryService.GetStoredPath(entry);
        if (!File.Exists(packagePath))
            return;

        if (entry.Extension.Equals(
            ".ps1save",
            StringComparison.OrdinalIgnoreCase))
        {
            _libraryPreviewModel = null;
            _libraryPreviewFallback = null;
            LibraryPreviewImage.Source =
                Ps1MemoryCardService.LoadPackageIcon(packagePath)
                ?? entry.IconImage;
            LibraryPreviewPlaceholder.Visibility =
                LibraryPreviewImage.Source is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            return;
        }

        var previewCachePath =
            GetSaveLibraryPreviewCachePath(
                entry);

        if (File.Exists(previewCachePath))
        {
            LibraryPreviewImage.Source =
                LoadFrozenBitmap(
                    previewCachePath);

            LibraryPreviewPlaceholder.Visibility =
                Visibility.Collapsed;
        }
        else
        {
            LibraryPreviewImage.Source = null;
            LibraryPreviewPlaceholder.Text =
                "Loading preview...";
            LibraryPreviewPlaceholder.Visibility =
                Visibility.Visible;
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-LIBRARY-PREVIEW-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var cardPath = Path.Combine(temporaryRoot, "preview-card.ps2");
            await _engine.CreateCardAsync(cardPath, false);

            var previewPackagePath = packagePath;
            if (entry.Extension.Equals(
                ".sps",
                StringComparison.OrdinalIgnoreCase))
            {
                previewPackagePath = Path.Combine(
                    temporaryRoot,
                    "normalized-preview.psu");

                await SpsPackageService.ConvertToPsuAsync(
                    packagePath,
                    previewPackagePath);
            }

            await _engine.ImportAsync(cardPath, previewPackagePath);
            await _engine.CheckAsync(cardPath);

            var saves = await _engine.ReadDirectoryAsync(cardPath);
            var save = saves.FirstOrDefault(candidate =>
                candidate.DirectoryId.Equals(
                    entry.DirectoryId,
                    StringComparison.OrdinalIgnoreCase))
                ?? saves.FirstOrDefault();

            if (save is null ||
                !ReferenceEquals(SaveLibraryList.SelectedItem, entry))
                return;

            if (BuiltInSaveIcons.IsSystemConfiguration(
                save.DirectoryId,
                save.GameTitle))
            {
                _libraryPreviewFallback =
                    BuiltInSaveIcons.GetSystemModel();

                if (!File.Exists(previewCachePath))
                {
                    var systemStill =
                        BuiltInSaveIcons
                            .RenderSystemConfiguration(
                                512,
                                512);

                    SaveBitmapAsPng(
                        systemStill,
                        previewCachePath);
                }

                LibraryPreviewPlaceholder.Visibility =
                    Visibility.Collapsed;
                return;
            }

            var iconResult = await _iconService.LoadResultAsync(
                cardPath,
                save.DirectoryId);

            if (!ReferenceEquals(SaveLibraryList.SelectedItem, entry))
                return;

            _libraryPreviewModel = iconResult.Model;
            _libraryPreviewRotationStart = _iconAnimationClock.Elapsed.TotalSeconds;
            _libraryPreviewFallback = iconResult.IsCorrupted
                ? BuiltInSaveIcons.GetCorruptedModel()
                : null;

            BitmapSource? still =
                _libraryPreviewModel is not null
                    ? await Task.Run(() =>
                        _libraryPreviewModel.Render(
                            512,
                            512,
                            0,
                            Ps2IconFrontRotation))
                    : _libraryPreviewFallback is not null
                        ? await Task.Run(() =>
                            BuiltInSaveIcons
                                .RenderCorruptedSave(
                                    512,
                                    512))
                        : null;

            if (still is not null)
            {
                LibraryPreviewImage.Source = still;
                LibraryPreviewPlaceholder.Visibility = Visibility.Collapsed;
                SaveBitmapAsPng(still, previewCachePath);
            }

            if (_libraryPreviewModel is not null ||
                _libraryPreviewFallback is not null)
            {
                LibraryPreviewPlaceholder.Visibility =
                    Visibility.Collapsed;
            }
            else
            {
                LibraryPreviewPlaceholder.Text =
                    "Preview unavailable";
                LibraryPreviewPlaceholder.Visibility =
                    Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Log(
                $"Save Library animated preview failed for " +
                $"{entry.OriginalFileName}: {ex.Message}");

            if (ReferenceEquals(
                SaveLibraryList.SelectedItem,
                entry))
            {
                LibraryPreviewImage.Source = null;
                LibraryPreviewPlaceholder.Text =
                    "Preview unavailable";
                LibraryPreviewPlaceholder.Visibility =
                    Visibility.Visible;
            }
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); }
            catch { }
        }
    }

    private async void LibraryInfo_Click(object sender, RoutedEventArgs e)
    {
        _saveInformationEntry =
            SaveLibraryList.SelectedItem as SaveLibraryEntry
            ?? _saveInformationEntry;

        if (_saveInformationEntry is null)
            return;

        await RefreshSaveInformationAsync();
    }

    private async void OpenPs1A_Click(
        object sender,
        RoutedEventArgs e)
    {
        var path = PickPs1Card();
        if (path is not null)
            await LoadPs1CardAsync(path, 'A');
    }

    private async void OpenPs1B_Click(
        object sender,
        RoutedEventArgs e)
    {
        var path = PickPs1Card();
        if (path is not null)
            await LoadPs1CardAsync(path, 'B');
    }

    private static string? PickPs1Card()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Ps1MemoryCardService.FileDialogFilter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    private static string CleanPs1GameTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned =
            System.Text.RegularExpressions.Regex.Replace(
                value,
                @"\s*\([^)]*\)",
                string.Empty);

        return System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"\s+",
            " ").Trim();
    }

    private async Task LoadPs1CardAsync(
        string path,
        char side,
        string? highlightFileName = null)
    {
        try
        {
            SetBusy(
                true,
                $"Reading PS1 card {Path.GetFileName(path)}...");

            var result = await _ps1CardService.ReadAsync(path);
            var target = side == 'A'
                ? _ps1SavesA
                : _ps1SavesB;

            target.Clear();

            foreach (var save in result.Saves.Where(save => !save.IsDeleted))
            {
                var metadata =
                    _ps1GameMetadataService.Lookup(
                        save.ProductCode,
                        save.Title,
                        save.Region);

                if (metadata is not null)
                {
                    if (!string.IsNullOrWhiteSpace(
                        metadata.Title))
                    {
                        save.Title =
                            CleanPs1GameTitle(
                                metadata.Title);
                    }

                    if (!string.IsNullOrWhiteSpace(
                        metadata.Region))
                    {
                        save.Region = metadata.Region;
                    }
                }

                target.Add(save);
            }

            ApplyPs1Filter(side);

            if (side == 'A')
            {
                _ps1PathA = path;
                Ps1CardAInfo.Text =
                    $"{Path.GetFileName(path)} • {result.FormatName} • " +
                    $"{FormatSaveCount(result.Saves.Count(save => !save.IsDeleted))}";
                Ps1CapacityAText.Text =
                    $"{result.UsedBlocks} of 15 blocks used • " +
                    $"{result.FreeBlocks} free";
                Ps1CapacityAProgress.Value = result.UsedBlocks;
                Ps1CapacityAPlaceholder.Visibility = Visibility.Collapsed;
                Ps1CapacityADetails.Visibility = Visibility.Visible;
            }
            else
            {
                _ps1PathB = path;
                Ps1CardBInfo.Text =
                    $"{Path.GetFileName(path)} • {result.FormatName} • " +
                    $"{FormatSaveCount(result.Saves.Count(save => !save.IsDeleted))}";
                Ps1CapacityBText.Text =
                    $"{result.UsedBlocks} of 15 blocks used • " +
                    $"{result.FreeBlocks} free";
                Ps1CapacityBProgress.Value = result.UsedBlocks;
                Ps1CapacityBPlaceholder.Visibility = Visibility.Collapsed;
                Ps1CapacityBDetails.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrWhiteSpace(highlightFileName))
            {
                var list = side == 'A'
                    ? Ps1CardAList
                    : Ps1CardBList;

                var match = target.FirstOrDefault(save =>
                    save.FileName.Equals(
                        highlightFileName,
                        StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    list.SelectedItem = match;
                    list.ScrollIntoView(match);
                }
            }

            Log(
                $"Loaded PS1 Card {side}: {path} " +
                $"({result.Saves.Count} directory entries).");
        }
        catch (Exception ex)
        {
            Log("PS1 card load failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "PS1 Memory Card Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
            RefreshButtons();
        }
    }

    private void ClosePs1A_Click(
        object sender,
        RoutedEventArgs e)
    {
        _ps1PathA = null;
        _ps1SavesA.Clear();
        ApplyPs1Filter('A');
        Ps1CardAInfo.Text = "Open or drop a PS1 memory card";
        Ps1CapacityAText.Text = string.Empty;
        Ps1CapacityAProgress.Value = 0;
        Ps1CapacityAPlaceholder.Visibility = Visibility.Visible;
        Ps1CapacityADetails.Visibility = Visibility.Collapsed;
        Ps1PreviewA.Source = null;
        Ps1PreviewAPlaceholder.Visibility = Visibility.Visible;
        RefreshButtons();
    }

    private void ClosePs1B_Click(
        object sender,
        RoutedEventArgs e)
    {
        _ps1PathB = null;
        _ps1SavesB.Clear();
        ApplyPs1Filter('B');
        Ps1CardBInfo.Text = "Open or drop a second PS1 memory card";
        Ps1CapacityBText.Text = string.Empty;
        Ps1CapacityBProgress.Value = 0;
        Ps1CapacityBPlaceholder.Visibility = Visibility.Visible;
        Ps1CapacityBDetails.Visibility = Visibility.Collapsed;
        Ps1PreviewB.Source = null;
        Ps1PreviewBPlaceholder.Visibility = Visibility.Visible;
        RefreshButtons();
    }

    private void Ps1CardSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Ps1CardAList))
        {
            var save = Ps1CardAList.SelectedItem as Ps1SaveEntry;
            Ps1PreviewA.Source = save?.IconImage;
            Ps1PreviewAPlaceholder.Visibility =
                save?.IconImage is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            Ps1PreviewATitle.Text =
                save?.Title ?? "PS1 Save Preview";
            Ps1PreviewACode.Text =
                save is null
                    ? "Select a save to view its details."
                    : !string.IsNullOrWhiteSpace(save.SaveTitle)
                        ? save.SaveTitle
                        : $"{save.ProductCode} • {save.Region}";
            Ps1PreviewABlocks.Text =
                save is null
                    ? string.Empty
                    : $"{save.ProductCode} • {save.Region}\n" +
                      $"{save.BlocksDisplay} • Starts at block {save.StartingBlock}";
            Ps1PreviewABlocks.Visibility =
                save is null
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            Ps1PreviewAStatus.Text =
                save is null
                    ? string.Empty
                    : $"Native PS1 Memory Card Save • {save.FileName}";
            Ps1PreviewAStatus.Visibility =
                save is null
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }
        else
        {
            var save = Ps1CardBList.SelectedItem as Ps1SaveEntry;
            Ps1PreviewB.Source = save?.IconImage;
            Ps1PreviewBPlaceholder.Visibility =
                save?.IconImage is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            Ps1PreviewBTitle.Text =
                save?.Title ?? "PS1 Save Preview";
            Ps1PreviewBCode.Text =
                save is null
                    ? "Select a save to view its details."
                    : !string.IsNullOrWhiteSpace(save.SaveTitle)
                        ? save.SaveTitle
                        : $"{save.ProductCode} • {save.Region}";
            Ps1PreviewBBlocks.Text =
                save is null
                    ? string.Empty
                    : $"{save.ProductCode} • {save.Region}\n" +
                      $"{save.BlocksDisplay} • Starts at block {save.StartingBlock}";
            Ps1PreviewBBlocks.Visibility =
                save is null
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            Ps1PreviewBStatus.Text =
                save is null
                    ? string.Empty
                    : $"Native PS1 Memory Card Save • {save.FileName}";
            Ps1PreviewBStatus.Visibility =
                save is null
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        RefreshButtons();
    }

    private async void CopyPs1AToB_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathA is not null &&
            _ps1PathB is not null)
        {
            await CopySelectedPs1SavesAsync(
                _ps1PathA,
                GetSelectedPs1CardSaves(Ps1CardAList),
                _ps1PathB,
                'B');
        }
    }

    private async void CopyPs1BToA_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathB is not null &&
            _ps1PathA is not null)
        {
            await CopySelectedPs1SavesAsync(
                _ps1PathB,
                GetSelectedPs1CardSaves(Ps1CardBList),
                _ps1PathA,
                'A');
        }
    }

    private async Task CopySelectedPs1SavesAsync(
        string sourcePath,
        IReadOnlyList<Ps1SaveEntry> saves,
        string destinationPath,
        char destinationSide)
    {
        if (saves.Count == 0)
            return;

        if (saves.Count == 1)
        {
            await CopyPs1SaveAsync(
                sourcePath,
                saves[0],
                destinationPath,
                destinationSide);
            return;
        }

        var requiredBlocks =
            saves.Sum(save => save.BlocksUsed);

        var confirmation =
            MessageBox.Show(
                $"Copy {saves.Count} selected PS1 saves ({requiredBlocks} blocks) " +
                $"to Card {destinationSide}?\n\n" +
                "PSM will verify the destination after all selected saves are copied.",
                "Confirm PS1 Save Transfer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(
                true,
                $"Copying {saves.Count} PS1 saves...");

            foreach (var save in saves)
            {
                await _ps1CardService.CopySaveAsync(
                    sourcePath,
                    save,
                    destinationPath,
                    ReplaceExistingPs1.IsChecked == true);
            }

            var verified =
                await _ps1CardService.ReadAsync(
                    destinationPath);

            var active =
                verified.Saves
                    .Where(save => !save.IsDeleted)
                    .ToArray();

            var missing =
                saves.Where(
                    selected =>
                        !active.Any(
                            candidate =>
                                candidate.FileName.Equals(
                                    selected.FileName,
                                    StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrWhiteSpace(selected.ProductCode) &&
                                 candidate.ProductCode.Equals(
                                    selected.ProductCode,
                                    StringComparison.OrdinalIgnoreCase))))
                    .ToArray();

            if (missing.Length > 0)
            {
                throw new InvalidDataException(
                    $"{missing.Length} selected PS1 save(s) were not present after transfer verification.");
            }

            await LoadPs1CardAsync(
                destinationPath,
                destinationSide,
                saves[^1].FileName);

            VerifiedText.Text =
                $"PS1 TRANSFER VERIFIED • {saves.Count} saves copied successfully";
            VerifiedBanner.Visibility =
                Visibility.Visible;

            Log(
                $"PS1 batch transfer verified: {saves.Count} saves -> {destinationPath}");

            MessageBox.Show(
                $"{saves.Count} PS1 saves were copied and verified.\n\n" +
                (_automaticBackupsEnabled
                    ? "Timestamped destination backups were created."
                    : "Automatic backups are disabled."),
                "PS1 Transfer Verified",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                "PS1 batch transfer failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "PS1 Transfer Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task CopyPs1SaveAsync(
        string sourcePath,
        Ps1SaveEntry save,
        string destinationPath,
        char destinationSide)
    {
        var confirmation = MessageBox.Show(
            $"Copy {save.Title}\n" +
            $"{save.ProductCode}\n" +
            $"{save.BlocksDisplay}\n\n" +
            $"to PS1 Card {destinationSide}?",
            "Confirm PS1 Save Transfer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
            return;

        var destinationCard =
            await _ps1CardService.ReadAsync(
                destinationPath);

        var existingSave =
            destinationCard.Saves.FirstOrDefault(
                candidate =>
                    !candidate.IsDeleted &&
                    candidate.FileName.Equals(
                        save.FileName,
                        StringComparison.OrdinalIgnoreCase));

        if (existingSave is not null &&
            ReplaceExistingPs1.IsChecked != true)
        {
            MessageBox.Show(
                this,
                $"{save.Title}\n{save.FileName}\n\nalready exists on {Path.GetFileName(destinationPath)}.\n\n" +
                "Enable \"Replace save if it already exists\" to overwrite it.",
                "Save Already Exists",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            SetBusy(true, $"Copying {save.Title}...");

            await _ps1CardService.CopySaveAsync(
                sourcePath,
                save,
                destinationPath,
                ReplaceExistingPs1.IsChecked == true);

            await LoadPs1CardAsync(
                destinationPath,
                destinationSide,
                save.FileName);

            VerifiedText.Text =
                $"PS1 TRANSFER VERIFIED • {save.Title}";
            VerifiedBanner.Visibility = Visibility.Visible;

            Log(
                $"PS1 transfer verified: {save.FileName} -> " +
                destinationPath);

            MessageBox.Show(
                "The PS1 save was copied and verified.\n\n" +
                (_automaticBackupsEnabled
                    ? "A timestamped backup of the destination card was created."
                    : "Automatic backups are disabled."),
                "PS1 Transfer Verified",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("PS1 transfer failed: " + ex.Message);

            if (ex is InvalidOperationException &&
                ex.Message.Contains(
                    "already contains this save",
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    $"{save.Title}\n{save.FileName}\n\nalready exists on {Path.GetFileName(destinationPath)}.\n\n" +
                    "Enable \"Replace save if it already exists\" to overwrite it.",
                    "Save Already Exists",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "PS1 Transfer Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void DeletePs1A_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathA is null)
            return;

        await DeletePs1SavesAsync(
            _ps1PathA,
            GetSelectedPs1CardSaves(Ps1CardAList),
            'A');
    }

    private async void DeletePs1B_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathB is null)
            return;

        await DeletePs1SavesAsync(
            _ps1PathB,
            GetSelectedPs1CardSaves(Ps1CardBList),
            'B');
    }

    private async Task DeletePs1SavesAsync(
        string cardPath,
        IReadOnlyList<Ps1SaveEntry> saves,
        char side)
    {
        if (saves.Count == 0)
            return;

        var description =
            saves.Count == 1
                ? $"{saves[0].Title}\n\n{saves[0].FileName}\n{saves[0].BlocksDisplay}"
                : $"{saves.Count} selected PS1 saves";

        var confirmation =
            MessageBox.Show(
                $"Delete {description}?\n\n" +
                (_automaticBackupsEnabled
                    ? "PSM will create one timestamped backup of the card before committing the deletion."
                    : "Automatic backups are disabled. PSM will still verify every deletion before committing it."),
                saves.Count == 1
                    ? "Delete PS1 Save"
                    : "Delete Selected PS1 Saves",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(
                true,
                saves.Count == 1
                    ? $"Deleting {saves[0].Title}..."
                    : $"Deleting {saves.Count} selected PS1 saves...");

            await _ps1CardService.DeleteSavesAsync(
                cardPath,
                saves);

            await LoadPs1CardAsync(
                cardPath,
                side);

            Log(
                saves.Count == 1
                    ? $"PS1 save deleted and verified: {saves[0].FileName}"
                    : $"{saves.Count} PS1 saves deleted and verified.");

            MessageBox.Show(
                $"{saves.Count} PS1 save{(saves.Count == 1 ? "" : "s")} deleted and verified.\n\n" +
                (_automaticBackupsEnabled
                    ? "One timestamped card backup was created."
                    : "Automatic backups are disabled."),
                "PS1 Delete Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("PS1 delete failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "PS1 Delete Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void ExportPs1A_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathA is not null &&
            Ps1CardAList.SelectedItem is Ps1SaveEntry save)
        {
            await ExportPs1SaveAsync(_ps1PathA, save);
        }
    }

    private async void ExportPs1B_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathB is not null &&
            Ps1CardBList.SelectedItem is Ps1SaveEntry save)
        {
            await ExportPs1SaveAsync(_ps1PathB, save);
        }
    }

    private async Task ExportPs1SaveAsync(
        string cardPath,
        Ps1SaveEntry save)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export PS1 Save",
            Filter = FormatCatalog.Ps1SaveExportFilter,
            DefaultExt = ".ps1save",
            FileName = save.FileName + ".ps1save"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(true, $"Exporting {save.Title}...");

            var destinationExtension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            if (destinationExtension == ".ps1save")
            {
                await _ps1CardService.ExportSavePackageAsync(
                    cardPath,
                    save,
                    dialog.FileName);
            }
            else if (Ps1SingleSaveExtensions.Contains(destinationExtension))
            {
                await _ps1CardService.ExportExternalSaveAsync(
                    cardPath,
                    save,
                    dialog.FileName);
            }
            else
            {
                var temporaryPackage = Path.Combine(
                    Path.GetTempPath(),
                    "PSM-PS1-EXPORT-" + Guid.NewGuid().ToString("N") + ".ps1save");
                try
                {
                    await _ps1CardService.ExportSavePackageAsync(
                        cardPath, save, temporaryPackage);
                    await _ps1CardService.CreateSingleSaveCardFromPackageAsync(
                        temporaryPackage, dialog.FileName);
                }
                finally
                {
                    try { File.Delete(temporaryPackage); } catch { }
                }
            }

            MessageBox.Show(
                $"PS1 save exported and verified.\n\n{dialog.FileName}",
                "PS1 Save Exported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("PS1 save export failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "PS1 Save Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void AddLibraryPs1A_Click(
        object sender,
        RoutedEventArgs e) =>
        await ShowStoreLibraryChoiceAsync(
            _ps1PathA,
            Array.Empty<SaveEntry>(),
            GetSelectedPs1CardSaves(Ps1CardAList),
            'A',
            true);

    private async void AddLibraryPs1B_Click(
        object sender,
        RoutedEventArgs e) =>
        await ShowStoreLibraryChoiceAsync(
            _ps1PathB,
            Array.Empty<SaveEntry>(),
            GetSelectedPs1CardSaves(Ps1CardBList),
            'B',
            true);

    private async Task AddPs1SavesToLibraryAsync(
        string cardPath,
        IReadOnlyList<Ps1SaveEntry> saves)
    {
        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-PS1-LIBRARY-BATCH-" +
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        var added = 0;
        var duplicates = 0;

        try
        {
            SetBusy(
                true,
                $"Adding {saves.Count} PS1 save(s) to Save Library...");

            foreach (var save in saves)
            {
                var safeTitle =
                    SanitizeUniversalFileName(
                        string.IsNullOrWhiteSpace(
                            save.Title)
                            ? save.ProductCode
                            : save.Title);

                var packagePath =
                    Path.Combine(
                        temporaryRoot,
                        safeTitle +
                        "-" +
                        Guid.NewGuid().ToString("N") +
                        ".ps1save");

                await _ps1CardService.ExportSavePackageAsync(
                    cardPath,
                    save,
                    packagePath);

                var result =
                    await _saveLibraryService.ImportAsync(
                        packagePath,
                        _saveLibraryIndex);

                if (result.Duplicate is not null)
                {
                    duplicates++;
                    Log(
                        $"PS1 save already in library: " +
                        save.FileName);
                    continue;
                }

                result.Entry.IconImage =
                    save.IconImage;
                result.Entry.ImportedFrom =
                    GetLibrarySourceDisplayName(cardPath);

                await _saveLibraryService.SaveAsync(
                    _saveLibraryIndex);

                added++;

                Log(
                    $"Added PS1 save to library: " +
                    save.FileName);
            }

            ApplySaveLibraryFilter();

            LibraryFooterStatus.Text =
                $"Added {added} PS1 save(s)" +
                (duplicates > 0
                    ? $" • {duplicates} already in library"
                    : string.Empty) +
                ".";

            MessageBox.Show(
                $"Save Library update complete.\n\n" +
                $"Added: {added}\n" +
                $"Already in library: {duplicates}",
                "Save Library",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                "Batch add from PS1 card failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "Add to Save Library Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryRoot,
                    true);
            }
            catch { }

            SetBusy(false, "Ready.");
        }
    }

    private async Task AddPs1SaveToLibraryAsync(
        string cardPath,
        Ps1SaveEntry save)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-PS1-LIBRARY-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            SetBusy(
                true,
                $"Adding {save.Title} to Save Library...");

            var safeTitle = SanitizeUniversalFileName(
                string.IsNullOrWhiteSpace(save.Title)
                    ? save.ProductCode
                    : save.Title);

            var packagePath = Path.Combine(
                temporaryRoot,
                safeTitle + ".ps1save");

            await _ps1CardService.ExportSavePackageAsync(
                cardPath,
                save,
                packagePath);

            var result =
                await _saveLibraryService.ImportAsync(
                    packagePath,
                    _saveLibraryIndex);

            if (result.Duplicate is not null)
            {
                MessageBox.Show(
                    "This exact PS1 save is already in the Save Library.",
                    "Duplicate Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                SaveLibraryList.SelectedItem =
                    result.Duplicate;
                return;
            }

            result.Entry.IconImage = save.IconImage;
            result.Entry.ImportedFrom =
                GetLibrarySourceDisplayName(cardPath);

            await _saveLibraryService.SaveAsync(
                _saveLibraryIndex);

            ApplySaveLibraryFilter();
            SaveLibraryList.SelectedItem = result.Entry;
            SaveLibraryList.ScrollIntoView(result.Entry);

            MessageBox.Show(
                $"{save.Title} was added to the Save Library.",
                "PS1 Save Added",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Log(
                $"Added PS1 save to library: {save.FileName}");
        }
        catch (Exception ex)
        {
            Log("Add PS1 save to library failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "Add to Library Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); }
            catch { }

            SetBusy(false, "Ready.");
        }
    }

    private async void BackupPs1A_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathA is not null)
            await BackupPs1CardAsync(_ps1PathA);
    }

    private async void BackupPs1B_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathB is not null)
            await BackupPs1CardAsync(_ps1PathB);
    }

    private async Task BackupPs1CardAsync(string path)
    {
        try
        {
            SetBusy(true, "Backing up PS1 memory card...");
            await _ps1CardService.BackupAsync(path);

            MessageBox.Show(
                "A timestamped PS1 memory-card backup was created " +
                "beside the original.",
                "PS1 Card Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "PS1 Backup Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void SaveAsPs1A_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathA is not null)
            await SavePs1CardAsAsync(_ps1PathA);
    }

    private async void SaveAsPs1B_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathB is not null)
            await SavePs1CardAsAsync(_ps1PathB);
    }

    private async Task SavePs1CardAsAsync(string sourcePath)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Ps1MemoryCardService.FileDialogFilter,
            FileName = Path.GetFileNameWithoutExtension(sourcePath) + ".mcr",
            DefaultExt = ".mcr"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(true, "Exporting PS1 memory card...");
            await _ps1CardService.SaveCardAsAsync(
                sourcePath,
                dialog.FileName);

            MessageBox.Show(
                $"PS1 card exported and verified.\n\n{dialog.FileName}",
                "PS1 Card Exported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "PS1 Card Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private static string? GetFirstDroppedPath(
        DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            paths.Length == 0)
        {
            return null;
        }

        return paths[0];
    }

    private void Ps1CardSlot_DragOver(
        object sender,
        DragEventArgs e)
    {
        var path =
            GetFirstDroppedPath(e);

        e.Effects =
            path is not null
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        e.Handled = true;
    }

    private async void Ps1CardA_Drop(
        object sender,
        DragEventArgs e)
    {
        var path =
            GetFirstDroppedPath(e);

        if (path is null)
            return;

        e.Handled = true;

        var kind =
            DetectUniversalSourceKind(path);

        if (kind == UniversalSourceKind.Ps1Card)
        {
            PreloadImportWizardSource(path);

            await LoadPs1CardAsync(
                path,
                'A');
            return;
        }

        if (kind is UniversalSourceKind.Ps1SingleSave or
            UniversalSourceKind.Ps1Package)
        {
            PreloadImportWizardSource(path);

            if (_ps1PathA is null)
            {
                MainTabs.SelectedItem =
                    UniversalImportWizardTab;
                return;
            }

            var answer =
                ShowOwnedDropConfirmation(
                    $"You're trying to import {Path.GetFileName(path)} into PS1 Card A.\n\n" +
                    "Do you want to proceed?",
                    "Import Save to Card A");

            if (answer != MessageBoxResult.Yes)
                return;

            if (kind == UniversalSourceKind.Ps1Package)
            {
                await ImportWizardPs1PackageAsync(
                    path,
                    'A');
            }
            else
            {
                await ImportWizardPs1SingleSaveAsync(
                    path,
                    'A');
            }

            return;
        }

        RouteDropToImportWizard(path);
    }

    private async void Ps1CardB_Drop(
        object sender,
        DragEventArgs e)
    {
        var path =
            GetFirstDroppedPath(e);

        if (path is null)
            return;

        e.Handled = true;

        var kind =
            DetectUniversalSourceKind(path);

        if (kind == UniversalSourceKind.Ps1Card)
        {
            PreloadImportWizardSource(path);

            await LoadPs1CardAsync(
                path,
                'B');
            return;
        }

        if (kind is UniversalSourceKind.Ps1SingleSave or
            UniversalSourceKind.Ps1Package)
        {
            PreloadImportWizardSource(path);

            if (_ps1PathB is null)
            {
                MainTabs.SelectedItem =
                    UniversalImportWizardTab;
                return;
            }

            var answer =
                ShowOwnedDropConfirmation(
                    $"You're trying to import {Path.GetFileName(path)} into PS1 Card B.\n\n" +
                    "Do you want to proceed?",
                    "Import Save to Card B");

            if (answer != MessageBoxResult.Yes)
                return;

            if (kind == UniversalSourceKind.Ps1Package)
            {
                await ImportWizardPs1PackageAsync(
                    path,
                    'B');
            }
            else
            {
                await ImportWizardPs1SingleSaveAsync(
                    path,
                    'B');
            }

            return;
        }

        RouteDropToImportWizard(path);
    }

    private static bool LooksLikeDirectPs2CardDrop(
        string path)
    {
        if (Directory.Exists(path))
        {
            return File.Exists(
                Path.Combine(
                    path,
                    "_pcsx2_superblock"));
        }

        return LooksLikePs2ImageCard(path);
    }

    private void Ps2CardSlot_DragOver(
        object sender,
        DragEventArgs e)
    {
        var path =
            GetFirstDroppedPath(e);

        e.Effects =
            path is not null
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        e.Handled = true;
    }

    private async void Ps2CardA_Drop(
        object sender,
        DragEventArgs e)
    {
        var path =
            GetFirstDroppedPath(e);

        if (path is null)
            return;

        e.Handled = true;

        var kind =
            DetectUniversalSourceKind(path);

        if (kind == UniversalSourceKind.Ps2Card)
        {
            PreloadImportWizardSource(path);

            await LoadCardAsync(
                path,
                'A');
            return;
        }

        if (kind == UniversalSourceKind.Ps2Package)
        {
            PreloadImportWizardSource(path);

            if (_pathA is null)
            {
                MainTabs.SelectedItem =
                    UniversalImportWizardTab;
                return;
            }

            var answer =
                ShowOwnedDropConfirmation(
                    $"You're trying to import {Path.GetFileName(path)} into PS2 Card A.\n\n" +
                    "Do you want to proceed?",
                    "Import Save to Card A");

            if (answer != MessageBoxResult.Yes)
                return;

            SelectPackage(path);

            await ImportPackageAsync(
                _pathA,
                'A',
                askForConfirmation: false);

            return;
        }

        RouteDropToImportWizard(path);
    }

    private async void Ps2CardB_Drop(
        object sender,
        DragEventArgs e)
    {
        var path =
            GetFirstDroppedPath(e);

        if (path is null)
            return;

        e.Handled = true;

        var kind =
            DetectUniversalSourceKind(path);

        if (kind == UniversalSourceKind.Ps2Card)
        {
            PreloadImportWizardSource(path);

            await LoadCardAsync(
                path,
                'B');
            return;
        }

        if (kind == UniversalSourceKind.Ps2Package)
        {
            PreloadImportWizardSource(path);

            if (_pathB is null)
            {
                MainTabs.SelectedItem =
                    UniversalImportWizardTab;
                return;
            }

            var answer =
                ShowOwnedDropConfirmation(
                    $"You're trying to import {Path.GetFileName(path)} into PS2 Card B.\n\n" +
                    "Do you want to proceed?",
                    "Import Save to Card B");

            if (answer != MessageBoxResult.Yes)
                return;

            SelectPackage(path);

            await ImportPackageAsync(
                _pathB,
                'B',
                askForConfirmation: false);

            return;
        }

        RouteDropToImportWizard(path);
    }

    private async Task LoadPs1GameMetadataDatabaseAsync()
    {
        try
        {
            _ps1GameDatabaseStatus =
                await _ps1GameMetadataService.LoadAsync();

            UpdatePs1GameDatabaseStatusText();
        }
        catch (Exception ex)
        {
            SaveInfoPs1DatabaseStatus.Text =
                "PS1 database load failed: " + ex.Message;

            Log(
                "PS1 game database load failed: " +
                ex.Message);
        }
    }

    private void UpdatePs1GameDatabaseStatusText()
    {
        if (_ps1GameDatabaseStatus is null)
        {
            SaveInfoPs1DatabaseStatus.Text =
                "PS1: no database loaded";
            return;
        }

        var gameDb =
            _ps1GameDatabaseStatus.GameDbAvailable
                ? $"GameDB-PSX: " +
                  $"{_ps1GameDatabaseStatus.GameDbEntries:N0} serials"
                : "GameDB-PSX: not installed";

        var launchBox =
            _ps1GameDatabaseStatus.LaunchBoxAvailable
                ? $"LaunchBox PS1: " +
                  $"{_ps1GameDatabaseStatus.LaunchBoxEntries:N0} titles"
                : "LaunchBox PS1: not installed";

        SaveInfoPs1DatabaseStatus.Text =
            gameDb + "  •  " + launchBox;
    }

    private async void UpdatePs1GameDb_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            SetBusy(
                true,
                "Updating GameDB-PSX...");

            var progress =
                new Progress<string>(message =>
                {
                    SaveInfoStatus.Text = message;
                    StatusText.Text = message;
                });

            _ps1GameDatabaseStatus =
                await _ps1GameMetadataService
                    .UpdateGameDbAsync(progress);

            UpdatePs1GameDatabaseStatusText();

            if (_ps1PathA is not null)
                await LoadPs1CardAsync(_ps1PathA, 'A');

            if (_ps1PathB is not null)
                await LoadPs1CardAsync(_ps1PathB, 'B');

            await RefreshSaveInformationAsync();

            MessageBox.Show(
                "GameDB-PSX was downloaded, validated, and loaded.",
                "PS1 Game Database Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                "GameDB-PSX update failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "GameDB-PSX Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task LoadGameMetadataDatabaseAsync()
    {
        try
        {
            _gameDatabaseStatus =
                await _gameMetadataService.LoadAsync();
            UpdateGameDatabaseStatusText();
        }
        catch (Exception ex)
        {
            SaveInfoDatabaseStatus.Text =
                "Database load failed: " + ex.Message;
            Log("Game database load failed: " + ex.Message);
        }
    }

    private void UpdateGameDatabaseStatusText()
    {
        if (_gameDatabaseStatus is null)
        {
            SaveInfoDatabaseStatus.Text = "No database loaded";
            return;
        }

        var gameDb = _gameDatabaseStatus.GameDbAvailable
            ? $"GameDB: {_gameDatabaseStatus.GameDbEntries:N0} serials"
            : "GameDB: not installed";

        var launchBox = _gameDatabaseStatus.LaunchBoxAvailable
            ? $"LaunchBox: {_gameDatabaseStatus.LaunchBoxEntries:N0} PS2 titles"
            : "LaunchBox: not installed";

        SaveInfoDatabaseStatus.Text = gameDb + "  •  " + launchBox;
    }

    private async void UpdateGameDb_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Updating GameDB-PS2...");
            var progress = new Progress<string>(message =>
            {
                SaveInfoStatus.Text = message;
                StatusText.Text = message;
            });

            _gameDatabaseStatus =
                await _gameMetadataService.UpdateGameDbAsync(progress);

            UpdateGameDatabaseStatusText();
            await RefreshSaveInformationAsync();

            MessageBox.Show(
                "GameDB-PS2 was downloaded, validated, and loaded.",
                "Game Database Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("GameDB update failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "GameDB Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void LoadLaunchBoxMetadata_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose LaunchBox Metadata.xml",
            Filter = "LaunchBox Metadata|Metadata.xml;*.xml|XML files|*.xml"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(true, "Loading LaunchBox metadata...");
            _gameDatabaseStatus =
                await _gameMetadataService.ImportLaunchBoxAsync(
                    dialog.FileName);

            UpdateGameDatabaseStatusText();
            _ps1GameDatabaseStatus =
                await _ps1GameMetadataService.LoadAsync();
            UpdatePs1GameDatabaseStatusText();
            await RefreshSaveInformationAsync();

            MessageBox.Show(
                "LaunchBox PlayStation and PlayStation 2 metadata was validated and loaded.",
                "LaunchBox Metadata Loaded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("LaunchBox metadata import failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "LaunchBox Metadata Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void DownloadLaunchBoxMetadata_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Downloading LaunchBox database...");
            var progress = new Progress<string>(message =>
            {
                SaveInfoStatus.Text = message;
                StatusText.Text = message;
            });

            _gameDatabaseStatus =
                await _gameMetadataService.DownloadLaunchBoxAsync(
                    progress);

            UpdateGameDatabaseStatusText();
            _ps1GameDatabaseStatus =
                await _ps1GameMetadataService.LoadAsync();
            UpdatePs1GameDatabaseStatusText();
            await RefreshSaveInformationAsync();

            MessageBox.Show(
                "LaunchBox PlayStation and PlayStation 2 metadata was downloaded, validated, and loaded.",
                "LaunchBox Database Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("LaunchBox database download failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "LaunchBox Download Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private void OpenGameDatabaseFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _gameMetadataService.DatabaseRoot,
            UseShellExecute = true
        });
    }

    private async void MainTabs_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs))
            return;

        if (!SaveInformationTab.IsSelected)
            return;

        _saveInformationEntry =
            SaveLibraryList.SelectedItem as SaveLibraryEntry
            ?? _saveInformationEntry;

        await RefreshSaveInformationAsync();
    }

    private async Task RefreshSaveInformationAsync()
    {
        await ShowSaveInformationAsync(_saveInformationEntry);
    }

    private async Task ShowSaveInformationAsync(SaveLibraryEntry? entry)
    {
        if (entry is null)
        {
            SaveInfoStatus.Text = "Choose a save from the Save Library.";
            SaveInfoTitle.Text = "No save selected";
            SaveInfoProfile.Text =
                "Select a Save Library entry and choose Save Information.";
            SaveInfoDirectory.Text = "—";
            SaveInfoFormat.Text = "—";
            SaveInfoSize.Text = "—";
            SaveInfoAdded.Text = "—";
            SaveInfoModified.Text = "—";
            SaveInfoCrc32.Text = "—";
            SaveInfoSha256.Text = "—";
            SaveInfoDuplicate.Text = "—";
            SaveInfoSerial.Text = "—";
            SaveInfoRegion.Text = "—";
            SaveInfoReleaseDate.Text = "Not available yet";
            SaveInfoDeveloper.Text = "Not available yet";
            SaveInfoPublisher.Text = "Not available yet";
            SaveInfoMetadataSource.Text = "No save selected";
            SaveInfoVerification.Text = "—";
            UpdateGameDatabaseStatusText();
            UpdatePs1GameDatabaseStatusText();
            SaveInfoPlayTime.Text = "Not supported for this title";
            SaveInfoParserStatus.Text =
                "No game-specific parser is available yet.";
            SaveInfoPreviewImage.Source = null;
            SaveInfoPreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }
        _saveInformationEntry = entry;
        SaveInfoTitle.Text = entry.DisplayTitle;
        SaveInfoProfile.Text = entry.DisplaySubtitle;
        SaveInfoDirectory.Text =
            string.IsNullOrWhiteSpace(entry.DirectoryId) ? "—" : entry.DirectoryId;
        SaveInfoFormat.Text =
            GetSaveLibraryFormatDisplay(entry);

        SaveInfoSize.Text = entry.SizeDisplay;
        SaveInfoAdded.Text =
            entry.AddedUtc.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
        SaveInfoModified.Text =
            entry.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
        SaveInfoSha256.Text = entry.Sha256;

        SaveInfoDuplicate.Text =
            await GetSaveInformationStatusTextAsync(entry);

        var serial = ExtractGameSerial(entry.DirectoryId);
        SaveInfoSerial.Text = string.IsNullOrWhiteSpace(serial) ? "Unknown" : serial;
        SaveInfoRegion.Text = InferRegion(serial, entry.DirectoryId);

        var isPs1Entry =
            entry.Extension.Equals(
                ".ps1save",
                StringComparison.OrdinalIgnoreCase) ||
            entry.Platform.Equals(
                "PlayStation",
                StringComparison.OrdinalIgnoreCase);

        var gameMetadata = isPs1Entry
            ? _ps1GameMetadataService.Lookup(
                serial,
                entry.DisplayTitle,
                SaveInfoRegion.Text)
            : _gameMetadataService.Lookup(
                serial,
                entry.DisplayTitle,
                SaveInfoRegion.Text);

        if (gameMetadata is not null)
        {
            if (!string.IsNullOrWhiteSpace(gameMetadata.Title))
                SaveInfoTitle.Text = gameMetadata.Title;

            if (!string.IsNullOrWhiteSpace(gameMetadata.Region))
                SaveInfoRegion.Text = gameMetadata.Region;

            SaveInfoReleaseDate.Text =
                string.IsNullOrWhiteSpace(gameMetadata.ReleaseDate)
                    ? "Not available in local database"
                    : gameMetadata.ReleaseDate;

            SaveInfoDeveloper.Text =
                string.IsNullOrWhiteSpace(gameMetadata.Developer)
                    ? "Not available in local database"
                    : gameMetadata.Developer;

            SaveInfoPublisher.Text =
                string.IsNullOrWhiteSpace(gameMetadata.Publisher)
                    ? "Not available in local database"
                    : gameMetadata.Publisher;

            SaveInfoMetadataSource.Text =
                string.IsNullOrWhiteSpace(gameMetadata.Source)
                    ? "Local game database"
                    : gameMetadata.Source;

            SaveInfoVerification.Text =
                gameMetadata.Verification;
        }
        else
        {
            SaveInfoReleaseDate.Text = "No matching database entry";
            SaveInfoDeveloper.Text = "No matching database entry";
            SaveInfoPublisher.Text = "No matching database entry";
            SaveInfoMetadataSource.Text = "No match";
            SaveInfoVerification.Text = "—";
        }

        UpdateGameDatabaseStatusText();
        UpdatePs1GameDatabaseStatusText();
        SaveInfoPlayTime.Text = "Not supported for this title";
        SaveInfoParserStatus.Text =
            "No game-specific parser is available yet.";

        SaveInfoPreviewImage.Source = entry.IconImage;
        SaveInfoPreviewPlaceholder.Visibility =
            entry.IconImage is null ? Visibility.Visible : Visibility.Collapsed;

        SaveInfoCrc32.Text = "Calculating...";
        SaveInfoStatus.Text = "Reading verified save information...";
        MainTabs.SelectedItem = SaveInformationTab;

        var storedPath = _saveLibraryService.GetStoredPath(entry);
        try
        {
            SaveInfoCrc32.Text = File.Exists(storedPath)
                ? await Task.Run(() => ComputeCrc32(storedPath))
                : "Package file missing";

            SaveInfoStatus.Text =
                "Universal metadata loaded. Game database and parser fields are shown only when verified.";
        }
        catch (Exception ex)
        {
            SaveInfoCrc32.Text = "Unavailable";
            SaveInfoStatus.Text = "Some information could not be read.";
            Log("Save Information CRC32 failed: " + ex.Message);
        }
    }

    private void ReturnToLibrary_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = SaveLibraryTab;

        if (_saveInformationEntry is not null)
        {
            SaveLibraryList.SelectedItem = _saveInformationEntry;
            SaveLibraryList.ScrollIntoView(_saveInformationEntry);
        }
    }

    private string PromptForMemCardPro2ReadyOutput(
        string requestedOutput,
        string? preferredDirectoryId)
    {
        var result = MessageBox.Show(
            "Would you like PSM to create a MemCard PRO2-ready folder and filename?\n\n" +
            "Example:\nSLUS-20144\\SLUS-20144-1.mc2\n\n" +
            "Choose No to save a normal loose .mc2 file.",
            "MemCard PRO2 Ready",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
            return string.Empty;

        if (result != MessageBoxResult.Yes)
            return requestedOutput;

        var defaultSerial = ExtractGameSerial(preferredDirectoryId ?? string.Empty);
        var serial = PromptForGameSerial(defaultSerial);
        if (string.IsNullOrWhiteSpace(serial))
            return string.Empty;

        var parent = Path.GetDirectoryName(requestedOutput);
        if (string.IsNullOrWhiteSpace(parent))
            parent = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var folder = Path.Combine(parent, serial);
        Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, serial + "-1.mc2");

        if (File.Exists(output))
        {
            var overwrite = MessageBox.Show(
                $"The MemCard PRO2 card already exists:\n\n{output}\n\nReplace it?",
                "Replace MemCard PRO2 Card",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (overwrite != MessageBoxResult.Yes)
                return string.Empty;
        }

        return output;
    }

    private string? PromptForGameSerial(string defaultSerial)
    {
        var dialog = new Window
        {
            Title = "MemCard PRO2 Game Serial",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(7, 11, 16)),
            ShowInTaskbar = false
        };

        var panel = new StackPanel { Margin = new Thickness(20), Width = 390 };
        panel.Children.Add(new TextBlock
        {
            Text = "Enter the game serial used by MemCard PRO2.",
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Example: SLUS-20144",
            Foreground = new SolidColorBrush(Color.FromRgb(159, 176, 197)),
            Margin = new Thickness(0, 5, 0, 12)
        });

        var input = new TextBox
        {
            Text = defaultSerial,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            MinWidth = 350
        };
        input.SelectAll();
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var ok = new Button { Content = "Create", MinWidth = 90, IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) =>
        {
            var normalized = NormalizeGameSerial(input.Text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                MessageBox.Show(
                    dialog,
                    "Enter a valid PlayStation 2 serial such as SLUS-20144.",
                    "Invalid Game Serial",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            input.Text = normalized;
            dialog.Tag = normalized;
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => input.Focus();

        return dialog.ShowDialog() == true
            ? dialog.Tag as string
            : null;
    }

    private static string NormalizeGameSerial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var match = System.Text.RegularExpressions.Regex.Match(
            value.ToUpperInvariant(),
            @"(?<prefix>S[A-Z]{3,4})[-_ ]?(?<number>\d{5})");

        return match.Success
            ? $"{match.Groups["prefix"].Value}-{match.Groups["number"].Value}"
            : string.Empty;
    }

    private async Task<string?> TryGetUniversalSourceDirectoryIdAsync(
        string source,
        string sourceExtension,
        string tempRoot)
    {
        try
        {
            if (Ps2CardExtensions.Contains(sourceExtension) || Directory.Exists(source))
            {
                var saves = await _engine.ReadDirectoryAsync(source);
                return saves
                    .Select(save => save.DirectoryId)
                    .FirstOrDefault(id =>
                        !string.IsNullOrWhiteSpace(ExtractGameSerial(id)));
            }

            var probeCard = Path.Combine(tempRoot, "memcardpro2-probe.ps2");
            await _engine.CreateCardAsync(probeCard, false);
            await _engine.ImportAsync(probeCard, source);
            var imported = await _engine.ReadDirectoryAsync(probeCard);
            return imported.FirstOrDefault()?.DirectoryId;
        }
        catch (Exception ex)
        {
            AppendUniversalLog(
                "MemCard PRO2 serial detection could not read the source: " +
                ex.Message);
            return null;
        }
    }

    private static string ExtractGameSerial(string directoryId)
    {
        if (string.IsNullOrWhiteSpace(directoryId))
            return string.Empty;

        var match = System.Text.RegularExpressions.Regex.Match(
            directoryId.ToUpperInvariant(),
            @"(?:BA|BE|BI|BU)?(?<serial>S[A-Z]{3,4})[-_ ]?(?<number>\d{5})");

        if (!match.Success)
            return string.Empty;

        return $"{match.Groups["serial"].Value}-{match.Groups["number"].Value}";
    }

    private static string InferRegion(string serial, string directoryId)
    {
        var value = (serial + " " + directoryId).ToUpperInvariant();

        if (value.Contains("SLUS") || value.Contains("SCUS") ||
            value.Contains("BASLUS") || value.Contains("BASCUS"))
            return "North America";

        if (value.Contains("SLES") || value.Contains("SCES") ||
            value.Contains("BESLES") || value.Contains("BESCES"))
            return "Europe / PAL";

        if (value.Contains("SLPS") || value.Contains("SCPS") ||
            value.Contains("SLPM") || value.Contains("SCPM") ||
            value.Contains("BISLPS") || value.Contains("BISCPS"))
            return "Japan";

        if (value.Contains("SCKA") || value.Contains("SLKA"))
            return "Korea";

        return "Unknown";
    }

    private static string ComputeCrc32(string path)
    {
        uint crc = 0xFFFFFFFF;
        var table = CreateCrc32Table();
        var buffer = new byte[81920];

        using var stream = File.OpenRead(path);
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                crc = table[(crc ^ buffer[index]) & 0xFF] ^ (crc >> 8);
            }
        }

        return (~crc).ToString("X8");
    }

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];

        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;

            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? 0xEDB88320 ^ (value >> 1)
                    : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private async void LibraryFavorite_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_saveLibraryContentMode ==
            SaveLibraryContentMode.MemoryCards)
        {
            var selectedCards =
                MemoryCardLibraryList.SelectedItems
                    .Cast<MemoryCardLibraryEntry>()
                    .ToArray();

            if (selectedCards.Length == 0 &&
                MemoryCardLibraryList.SelectedItem is
                    MemoryCardLibraryEntry singleCard)
            {
                selectedCards = [singleCard];
            }

            if (selectedCards.Length == 0)
                return;

            try
            {
                var makeFavorite =
                    selectedCards.Any(
                        entry => !entry.IsFavorite);

                foreach (var entry in
                    selectedCards.Where(
                        entry =>
                            entry.IsFavorite != makeFavorite))
                {
                    await _memoryCardLibraryService
                        .ToggleFavoriteAsync(
                            entry,
                            _memoryCardLibraryIndex);
                }

                RefreshMemoryCardLibraryView();

                foreach (var entry in selectedCards)
                {
                    if (MemoryCardLibraryList.Items.Contains(entry))
                        MemoryCardLibraryList.SelectedItems.Add(entry);
                }

                LibraryFavoriteButtonText.Text =
                    makeFavorite
                        ? "Remove Favorite"
                        : "Add Favorite";

                LibraryFooterStatus.Text =
                    selectedCards.Length == 1
                        ? (makeFavorite
                            ? $"Added {selectedCards[0].DisplayName} to favorites."
                            : $"Removed {selectedCards[0].DisplayName} from favorites.")
                        : (makeFavorite
                            ? $"Added {selectedCards.Length} selected memory cards to favorites."
                            : $"Removed {selectedCards.Length} selected memory cards from favorites.");
            }
            catch (Exception ex)
            {
                Log(
                    "Memory Card Library favorite update failed: " +
                    ex.Message);

                MessageBox.Show(
                    ex.Message,
                    "Favorite Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        var selectedEntries =
            SaveLibraryList.SelectedItems.Cast<SaveLibraryEntry>().ToArray();

        if (selectedEntries.Length == 0 &&
            SaveLibraryList.SelectedItem is SaveLibraryEntry singleEntry)
        {
            selectedEntries = [singleEntry];
        }

        if (selectedEntries.Length == 0)
            return;

        if (selectedEntries.Length == 1)
        {
            await ToggleLibraryFavoriteAsync(selectedEntries[0]);
            return;
        }

        try
        {
            var makeFavorite = selectedEntries.Any(entry => !entry.IsFavorite);

            foreach (var entry in selectedEntries.Where(entry => entry.IsFavorite != makeFavorite))
            {
                await _saveLibraryService.ToggleFavoriteAsync(
                    entry,
                    _saveLibraryIndex);
            }

            ApplySaveLibraryFilter();

            foreach (var entry in selectedEntries)
            {
                if (SaveLibraryList.Items.Contains(entry))
                    SaveLibraryList.SelectedItems.Add(entry);
            }

            UpdateSaveLibraryMetadata(
                SaveLibraryList.SelectedItem as SaveLibraryEntry);

            LibraryFooterStatus.Text =
                makeFavorite
                    ? $"Added {selectedEntries.Length} selected saves to favorites."
                    : $"Removed {selectedEntries.Length} selected saves from favorites.";
        }
        catch (Exception ex)
        {
            Log("Save Library multi-favorite update failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "Favorite Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void LibraryStar_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SaveLibraryEntry entry)
            return;

        e.Handled = true;
        await ToggleLibraryFavoriteAsync(entry);
    }

    private async void MemoryCardLibraryStar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not
            MemoryCardLibraryEntry entry)
        {
            return;
        }

        e.Handled = true;

        try
        {
            await _memoryCardLibraryService.ToggleFavoriteAsync(
                entry,
                _memoryCardLibraryIndex);

            RefreshMemoryCardLibraryView();
            MemoryCardLibraryList.SelectedItem = entry;
            MemoryCardLibraryList.ScrollIntoView(entry);

            LibraryFavoriteButtonText.Text =
                entry.IsFavorite
                    ? "Remove Favorite"
                    : "Add Favorite";

            LibraryFooterStatus.Text =
                entry.IsFavorite
                    ? $"Added {entry.DisplayName} to favorites."
                    : $"Removed {entry.DisplayName} from favorites.";
        }
        catch (Exception ex)
        {
            Log(
                "Memory Card Library favorite update failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "Favorite Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ToggleLibraryFavoriteAsync(SaveLibraryEntry entry)
    {
        try
        {
            await _saveLibraryService.ToggleFavoriteAsync(
                entry,
                _saveLibraryIndex);

            ApplySaveLibraryFilter();
            SaveLibraryList.SelectedItem = entry;
            SaveLibraryList.ScrollIntoView(entry);
            UpdateSaveLibraryMetadata(entry);

            LibraryFooterStatus.Text = entry.IsFavorite
                ? $"Added {entry.DisplayTitle} to favorites."
                : $"Removed {entry.DisplayTitle} from favorites.";
        }
        catch (Exception ex)
        {
            Log("Save Library favorite update failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "Favorite Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static bool IsPs1LibraryEntry(SaveLibraryEntry entry) =>
        entry.Extension.Equals(".ps1save", StringComparison.OrdinalIgnoreCase) ||
        (entry.Platform.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) &&
         !entry.Platform.Contains("2", StringComparison.OrdinalIgnoreCase));

    private static bool IsPs1MemoryCardLibraryEntry(MemoryCardLibraryEntry entry) =>
        entry.Platform.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) &&
        !entry.Platform.Contains("2", StringComparison.OrdinalIgnoreCase);

    private SaveLibraryEntry[] GetSelectedLibrarySavesForPlatformAction(string actionText)
    {
        var selected = SaveLibraryList.SelectedItems.Cast<SaveLibraryEntry>().ToArray();
        if (selected.Length == 0 && SaveLibraryList.SelectedItem is SaveLibraryEntry single)
            selected = [single];

        var hasPs1 = selected.Any(IsPs1LibraryEntry);
        var hasPs2 = selected.Any(entry => !IsPs1LibraryEntry(entry));
        if (!hasPs1 || !hasPs2)
            return selected;

        var choice = ShowNewCardTypeDialog(
            "PS1 AND PS2 SAVES SELECTED",
            $"PS1 and PS2 saves cannot be placed on the same memory card. Choose which console's selected saves to {actionText}.",
            new[]
            {
                new CardChoice(FindResource("IconStandardPs2Card") as ImageSource,
                    "PlayStation (PS1)", $"Use the {selected.Count(IsPs1LibraryEntry)} selected PS1 save(s).", 1),
                new CardChoice(FindResource("IconStandardPs2Card") as ImageSource,
                    "PlayStation 2 (PS2)", $"Use the {selected.Count(entry => !IsPs1LibraryEntry(entry))} selected PS2 save(s).", 2)
            },
            "Choose Memory Card Console");

        return choice switch
        {
            1 => selected.Where(IsPs1LibraryEntry).ToArray(),
            2 => selected.Where(entry => !IsPs1LibraryEntry(entry)).ToArray(),
            _ => []
        };
    }

    private async void LibraryExportCard_Click(object sender, RoutedEventArgs e)
    {
        var entries = GetSelectedLibrarySavesForPlatformAction("export");
        if (entries.Length == 0) return;
        if (IsPs1LibraryEntry(entries[0]))
            await ExportPs1LibrarySelectionAsCardAsync(entries);
        else
            await ExportPs2LibrarySelectionAsCardAsync(entries);
    }

    private async Task ExportPs1LibrarySelectionAsCardAsync(IReadOnlyList<SaveLibraryEntry> entries)
    {
        var manifests = new List<Ps1SavePackageManifest>();
        foreach (var entry in entries)
            manifests.Add(await Ps1MemoryCardService.InspectSavePackageAsync(_saveLibraryService.GetStoredPath(entry)));

        var requiredBlocks = manifests.Sum(manifest => manifest.BlocksUsed);
        if (requiredBlocks > 15)
        {
            MessageBox.Show(
                $"The selected PS1 saves require {requiredBlocks} blocks, but a PS1 memory card has only 15 usable blocks.\n\nRemove one or more saves and try again.",
                "PS1 Memory Card Capacity", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Selected PS1 Saves as Memory Card",
            Filter = Ps1MemoryCardService.FileDialogFilter,
            DefaultExt = ".mcr",
            FileName = "PS1 Library Collection.mcr",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;

        var destination = Path.GetFullPath(dialog.FileName);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "PSM-LIBRARY-PS1-CARD-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            SetBusy(true, $"Creating PS1 memory card from {entries.Count} selected saves...");
            var combined = Path.Combine(temporaryRoot, "combined.mcr");
            await _ps1CardService.CreateEmptyCardAsync(combined);
            for (var index = 0; index < entries.Count; index++)
            {
                var package = _saveLibraryService.GetStoredPath(entries[index]);
                var singleCard = Path.Combine(temporaryRoot, $"source-{index}.mcr");
                await _ps1CardService.CreateSingleSaveCardFromPackageAsync(package, singleCard);
                var source = await _ps1CardService.ReadAsync(singleCard);
                var save = source.Saves.Single(candidate => !candidate.IsDeleted);
                await _ps1CardService.CopySaveAsync(singleCard, save, combined);
            }
            var verified = await _ps1CardService.ReadAsync(combined);
            if (verified.Saves.Count(save => !save.IsDeleted) != entries.Count)
                throw new InvalidDataException("The combined PS1 memory card failed save-count verification.");
            if (File.Exists(destination)) File.Delete(destination);
            await _ps1CardService.SaveCardAsAsync(combined, destination);
            LibraryFooterStatus.Text = $"Created PS1 memory card with {entries.Count} saves ({requiredBlocks}/15 blocks).";
            MessageBox.Show($"PS1 memory card created and verified.\n\n{entries.Count} saves • {requiredBlocks} of 15 blocks used\n\n{destination}",
                "Memory Card Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("PS1 multi-save card export failed: " + ex.Message);
            MessageBox.Show(ex.Message, "PS1 Memory Card Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); } catch { }
            SetBusy(false, "Ready.");
        }
    }

    private async Task ExportPs2LibrarySelectionAsCardAsync(IReadOnlyList<SaveLibraryEntry> entries)
    {
        var cardTypeChoice = ShowNewCardTypeDialog(
            "EXPORT SELECTED PS2 SAVES",
            "Choose the PS2 memory-card format for the selected saves.",
            new[]
            {
                new CardChoice(
                    FindResource("IconStandardPs2Card") as ImageSource,
                    "PS2 Image Card",
                    "Create a .ps2, .mc2, .vm2, .vmc, .bin, or .mcd memory-card image.",
                    1),
                new CardChoice(
                    FindResource("IconPcsx2FolderCard") as ImageSource,
                    "PCSX2 Folder Card",
                    "Create a PCSX2 folder memory card containing the selected saves.",
                    2)
            },
            "Export as Card");

        if (cardTypeChoice == 0)
            return;

        if (cardTypeChoice == 2)
        {
            await ExportPs2LibrarySelectionAsFolderCardAsync(entries);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Selected PS2 Saves as Memory Card",
            Filter = FormatCatalog.Ps2MemoryCardFilter,
            DefaultExt = ".ps2",
            FileName = "PS2 Library Collection.ps2",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;

        var destination = Path.GetFullPath(dialog.FileName);
        var extension = Path.GetExtension(destination).ToLowerInvariant();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "PSM-LIBRARY-PS2-CARD-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            SetBusy(true, $"Creating PS2 memory card from {entries.Count} selected saves...");
            var estimatedBytes = entries.Sum(entry => Math.Max(0L, entry.SizeBytes));
            var sizeOptions = new[] { 8, 16, 32, 64 };
            var startingSize = sizeOptions.FirstOrDefault(size => estimatedBytes <= (long)size * 1024 * 1024 * 9 / 10);
            if (startingSize == 0) startingSize = 64;

            Exception? lastError = null;
            string? completedCard = null;
            var completedSize = 0;
            foreach (var sizeMb in sizeOptions.Where(size => size >= startingSize))
            {
                var candidate = Path.Combine(temporaryRoot, $"combined-{sizeMb}{extension}");
                try
                {
                    await _engine.CreateCardAsync(
                        candidate,
                        sizeMb,
                        extension == ".mc2");
                    foreach (var entry in entries)
                        await _engine.ImportAsync(candidate, _saveLibraryService.GetStoredPath(entry));
                    await _engine.CheckAsync(candidate);
                    var saves = await _engine.ReadDirectoryAsync(candidate);
                    var expectedIds = entries.Select(entry => entry.DirectoryId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (expectedIds.Any(id => !saves.Any(save => save.DirectoryId.Equals(id, StringComparison.OrdinalIgnoreCase))))
                        throw new InvalidDataException("One or more selected saves were not present after card verification.");
                    completedCard = candidate;
                    completedSize = sizeMb;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try { if (File.Exists(candidate)) File.Delete(candidate); } catch { }
                }
            }
            if (completedCard is null)
                throw new InvalidOperationException("The selected saves could not be packaged onto a PS2 memory card of 64 MB or less." +
                    (lastError is null ? string.Empty : $"\n\nLast error: {lastError.Message}"));
            if (File.Exists(destination)) File.Delete(destination);
            File.Copy(completedCard, destination, true);
            await _engine.CheckAsync(destination);
            LibraryFooterStatus.Text = $"Created {completedSize} MB PS2 memory card with {entries.Count} selected saves.";
            MessageBox.Show($"PS2 memory card created and verified.\n\n{entries.Count} saves • {completedSize} MB\n\n{destination}",
                "Memory Card Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("PS2 multi-save card export failed: " + ex.Message);
            MessageBox.Show(ex.Message, "PS2 Memory Card Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); } catch { }
            SetBusy(false, "Ready.");
        }
    }

    private async Task ExportPs2LibrarySelectionAsFolderCardAsync(
        IReadOnlyList<SaveLibraryEntry> entries)
    {
        var parentDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose Where to Create the PCSX2 Folder Card",
            Multiselect = false
        };

        if (parentDialog.ShowDialog() != true)
            return;

        var folderName = PromptForLibraryCardName(
            "PS2 Library Collection",
            "Name PCSX2 Folder Card");

        if (string.IsNullOrWhiteSpace(folderName))
            return;

        foreach (var invalid in Path.GetInvalidFileNameChars())
            folderName = folderName.Replace(invalid, '_');

        folderName = folderName.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = "PS2 Library Collection";

        var destination = Path.Combine(
            parentDialog.FolderName,
            folderName);

        if (Directory.Exists(destination) &&
            Directory.EnumerateFileSystemEntries(destination).Any())
        {
            var replace = MessageBox.Show(
                $"The folder already exists and is not empty:\n\n{destination}\n\nReplace it?",
                "Replace PCSX2 Folder Card",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (replace != MessageBoxResult.Yes)
                return;

            Directory.Delete(destination, recursive: true);
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-LIBRARY-PS2-FOLDER-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            SetBusy(
                true,
                $"Creating PCSX2 folder card from {entries.Count} selected saves...");

            var estimatedBytes =
                entries.Sum(entry => Math.Max(0L, entry.SizeBytes));
            var sizeOptions = new[] { 8, 16, 32, 64 };
            var startingSize = sizeOptions.FirstOrDefault(
                size => estimatedBytes <= (long)size * 1024 * 1024 * 9 / 10);
            if (startingSize == 0)
                startingSize = 64;

            Exception? lastError = null;
            string? completedCard = null;

            foreach (var sizeMb in sizeOptions.Where(size => size >= startingSize))
            {
                var candidate = Path.Combine(
                    temporaryRoot,
                    $"combined-{sizeMb}.ps2");

                try
                {
                    await _engine.CreateCardAsync(candidate, sizeMb);

                    foreach (var entry in entries)
                    {
                        await _engine.ImportAsync(
                            candidate,
                            _saveLibraryService.GetStoredPath(entry));
                    }

                    await _engine.CheckAsync(candidate);

                    var saves = await _engine.ReadDirectoryAsync(candidate);
                    var expectedIds = entries
                        .Select(entry => entry.DirectoryId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (expectedIds.Any(id =>
                        !saves.Any(save =>
                            save.DirectoryId.Equals(
                                id,
                                StringComparison.OrdinalIgnoreCase))))
                    {
                        throw new InvalidDataException(
                            "One or more selected saves were not present after card verification.");
                    }

                    completedCard = candidate;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try
                    {
                        if (File.Exists(candidate))
                            File.Delete(candidate);
                    }
                    catch { }
                }
            }

            if (completedCard is null)
            {
                throw new InvalidOperationException(
                    "The selected saves could not be packaged onto a PS2 memory card of 64 MB or less." +
                    (lastError is null
                        ? string.Empty
                        : $"\n\nLast error: {lastError.Message}"));
            }

            await _engine.ConvertToPcsx2FolderCardAsync(
                completedCard,
                destination);

            await _engine.CheckAsync(destination);

            var verified = await _engine.ReadDirectoryAsync(destination);
            var requiredIds = entries
                .Select(entry => entry.DirectoryId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (requiredIds.Any(id =>
                !verified.Any(save =>
                    save.DirectoryId.Equals(
                        id,
                        StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidDataException(
                    "The PCSX2 folder card failed final save verification.");
            }

            LibraryFooterStatus.Text =
                $"Created PCSX2 folder card with {entries.Count} selected saves.";

            MessageBox.Show(
                $"PCSX2 folder card created and verified.\n\n" +
                $"{entries.Count} saves\n\n{destination}",
                "Folder Card Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                "PS2 multi-save folder-card export failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "PCSX2 Folder Card Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryRoot, true);
            }
            catch { }

            SetBusy(false, "Ready.");
        }
    }

    private async void LibrarySlotA_Click(object sender, RoutedEventArgs e) => await LibrarySlotActionAsync('A');
    private async void LibrarySlotB_Click(object sender, RoutedEventArgs e) => await LibrarySlotActionAsync('B');

    private async Task LibrarySlotActionAsync(char side)
    {
        if (_saveLibraryContentMode == SaveLibraryContentMode.MemoryCards)
        {
            if (MemoryCardLibraryList.SelectedItem is not MemoryCardLibraryEntry card) return;
            var storedPath = _memoryCardLibraryService.GetStoredPath(card);
            if (IsPs1MemoryCardLibraryEntry(card))
            {
                await LoadPs1CardAsync(storedPath, side);
                Ps1MemoryCardsTab.IsSelected = true;
            }
            else
            {
                await LoadCardAsync(storedPath, side);
                Ps2MemoryCardsTab.IsSelected = true;
            }
            return;
        }

        var entries = GetSelectedLibrarySavesForPlatformAction($"add to Card {side}");
        if (entries.Length == 0) return;
        if (IsPs1LibraryEntry(entries[0]))
            await ImportLibraryPs1SavesToSlotAsync(entries, side);
        else
            await ImportLibraryPs2SavesToSlotAsync(entries, side);
    }

    private async Task ImportLibraryPs2SavesToSlotAsync(IReadOnlyList<SaveLibraryEntry> entries, char side)
    {
        var destination = side == 'A' ? _pathA : _pathB;
        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show($"Open a PS2 memory card in Card {side} first.", "No PS2 Destination Card",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "PSM-LIBRARY-SLOT-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            SetBusy(true, $"Adding {entries.Count} library save(s) to PS2 Card {side}...");
            var folder = Directory.Exists(destination);
            var temporaryCard = folder ? Path.Combine(temporaryRoot, "FolderCard") : Path.Combine(temporaryRoot, Path.GetFileName(destination));
            if (folder) CopyDirectory(destination, temporaryCard); else File.Copy(destination, temporaryCard, true);
            foreach (var entry in entries)
                await _engine.ImportAsync(temporaryCard, _saveLibraryService.GetStoredPath(entry));
            await _engine.CheckAsync(temporaryCard);
            var backup = folder ? CreateAutomaticFolderBackup(destination) : CreateAutomaticBackup(destination);
            if (folder)
            {
                Directory.Delete(destination, true);
                CopyDirectory(temporaryCard, destination);
            }
            else File.Copy(temporaryCard, destination, true);
            await LoadCardAsync(destination, side, entries.Last().DirectoryId, allowWhileBusy: true);
            Ps2MemoryCardsTab.IsSelected = true;
            MessageBox.Show(
                $"{entries.Count} save(s) were added and verified.\n\n{AutomaticBackupDetails(backup)}",
                "Library Import Verified", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("Library PS2 slot import failed: " + ex.Message);
            MessageBox.Show(ex.Message, "Library Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); } catch { }
            SetBusy(false, "Ready.");
        }
    }

    private async Task ImportLibraryPs1SavesToSlotAsync(IReadOnlyList<SaveLibraryEntry> entries, char side)
    {
        var destination = side == 'A' ? _ps1PathA : _ps1PathB;
        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show($"Open a PS1 memory card in Card {side} first.", "No PS1 Destination Card",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "PSM-LIBRARY-PS1-SLOT-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            SetBusy(true, $"Adding {entries.Count} library save(s) to PS1 Card {side}...");
            var temporaryCard = Path.Combine(temporaryRoot, Path.GetFileName(destination));
            File.Copy(destination, temporaryCard, true);
            for (var index = 0; index < entries.Count; index++)
            {
                var singleCard = Path.Combine(temporaryRoot, $"source-{index}.mcr");
                await _ps1CardService.CreateSingleSaveCardFromPackageAsync(_saveLibraryService.GetStoredPath(entries[index]), singleCard);
                var source = await _ps1CardService.ReadAsync(singleCard);
                var save = source.Saves.Single(candidate => !candidate.IsDeleted);
                await _ps1CardService.CopySaveAsync(singleCard, save, temporaryCard);
            }
            string? backup = null;
            if (_automaticBackupsEnabled)
            {
                await _ps1CardService.BackupAsync(destination);
                backup = "A timestamped PS1 memory-card backup was created.";
            }

            File.Copy(temporaryCard, destination, true);
            await LoadPs1CardAsync(destination, side);
            Ps1MemoryCardsTab.IsSelected = true;
            MessageBox.Show(
                $"{entries.Count} PS1 save(s) were added and verified.\n\n" +
                (backup ?? "Automatic backups are disabled."),
                "Library Import Verified", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("Library PS1 slot import failed: " + ex.Message);
            MessageBox.Show(ex.Message, "Library Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); } catch { }
            SetBusy(false, "Ready.");
        }
    }

    private async void LibraryExport_Click(object sender, RoutedEventArgs e)
    {
        if (SaveLibraryList.SelectedItem is not SaveLibraryEntry entry)
            return;

        var isPs1 = entry.Extension.Equals(".ps1save", StringComparison.OrdinalIgnoreCase) ||
                    entry.Platform.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) &&
                    !entry.Platform.Contains("2", StringComparison.OrdinalIgnoreCase);

        var filter = isPs1
            ? FormatCatalog.Ps1SaveExportFilter
            : FormatCatalog.Ps2SaveExportFilter;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Save",
            Filter = filter,
            DefaultExt = entry.Extension,
            FileName =
                entry.Extension.Equals(
                    ".ps2save",
                    StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(
                        entry.OriginalFileName) +
                      ".ps2save"
                    : entry.OriginalFileName,
            FilterIndex =
                entry.Extension.Equals(
                    ".ps2save",
                    StringComparison.OrdinalIgnoreCase)
                    ? 4
                    : 1
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(true, "Exporting library save...");
            var destinationPath = Path.GetFullPath(dialog.FileName);
            var destinationExtension = Path.GetExtension(destinationPath).ToLowerInvariant();
            var storedPath = _saveLibraryService.GetStoredPath(entry);

            if (!isPs1 && destinationExtension == ".mc2")
            {
                destinationPath = PromptForMemCardPro2ReadyOutput(
                    destinationPath,
                    entry.DirectoryId);
                if (string.IsNullOrWhiteSpace(destinationPath))
                    return;
            }

            if (destinationExtension.Equals(entry.Extension, StringComparison.OrdinalIgnoreCase))
            {
                await _saveLibraryService.ExportAsync(entry, destinationPath);
            }
            else if (isPs1)
            {
                if (Ps1SingleSaveExtensions.Contains(destinationExtension))
                {
                    var temporaryRoot = Path.Combine(
                        Path.GetTempPath(),
                        "PSM-LIBRARY-PS1-EXPORT-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(temporaryRoot);
                    try
                    {
                        var sourceCard = Path.Combine(temporaryRoot, "source.mcr");
                        await _ps1CardService.CreateSingleSaveCardFromPackageAsync(
                            storedPath,
                            sourceCard);
                        var sourceRead = await _ps1CardService.ReadAsync(sourceCard);
                        var sourceSave = sourceRead.Saves.Single(candidate => !candidate.IsDeleted);
                        await _ps1CardService.ExportExternalSaveAsync(
                            sourceCard,
                            sourceSave,
                            destinationPath);
                    }
                    finally
                    {
                        try { Directory.Delete(temporaryRoot, true); } catch { }
                    }
                }
                else
                {
                    await _ps1CardService.CreateSingleSaveCardFromPackageAsync(
                        storedPath,
                        destinationPath);
                }
            }
            else if (!isPs1 &&
                     destinationExtension == ".ps2save")
            {
                var temporaryRoot =
                    Path.Combine(
                        Path.GetTempPath(),
                        "PSM-LIBRARY-PS2SAVE-" +
                        Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(
                    temporaryRoot);

                try
                {
                    var sourceCard =
                        Path.Combine(
                            temporaryRoot,
                            "source.ps2");

                    await _engine.CreateCardAsync(
                        sourceCard,
                        false);

                    await _engine.ImportAsync(
                        sourceCard,
                        storedPath);

                    await _engine.CheckAsync(
                        sourceCard);

                    var saves =
                        await _engine.ReadDirectoryAsync(
                            sourceCard);

                    var save =
                        saves.FirstOrDefault(
                            candidate =>
                                candidate.DirectoryId.Equals(
                                    entry.DirectoryId,
                                    StringComparison.OrdinalIgnoreCase))
                        ?? saves.SingleOrDefault()
                        ?? throw new InvalidDataException(
                            "The stored save could not be verified.");

                    await _ps2PackageService.ExportFromCardAsync(
                        sourceCard,
                        save,
                        destinationPath,
                        entry.OriginalFileName,
                        entry.FormatName);
                }
                finally
                {
                    try
                    {
                        Directory.Delete(
                            temporaryRoot,
                            true);
                    }
                    catch { }
                }
            }
            else if (destinationExtension is ".ps2" or ".mc2" or ".vm2" or ".vmc" or ".bin" or ".mcd")
            {
                var temporaryRoot = Path.Combine(
                    Path.GetTempPath(), "PSM-LIBRARY-CARD-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                try
                {
                    var sourceCard = Path.Combine(temporaryRoot, "source.ps2");
                    await _engine.CreateCardAsync(sourceCard, false);
                    await _engine.ImportAsync(sourceCard, storedPath);
                    await _engine.CheckAsync(sourceCard);
                    var saves = await _engine.ReadDirectoryAsync(sourceCard);
                    var save = saves.FirstOrDefault(candidate =>
                        candidate.DirectoryId.Equals(entry.DirectoryId, StringComparison.OrdinalIgnoreCase))
                        ?? saves.SingleOrDefault()
                        ?? throw new InvalidDataException("The stored save could not be verified.");

                    await CreateSingleSaveCardAsync(
                        sourceCard,
                        save.DirectoryId,
                        destinationPath,
                        destinationExtension == ".mc2");
                }
                finally
                {
                    try { Directory.Delete(temporaryRoot, true); } catch { }
                }
            }
            else
            {
                var temporaryRoot = Path.Combine(
                    Path.GetTempPath(), "PSM-LIBRARY-EXPORT-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                try
                {
                    var card = Path.Combine(temporaryRoot, "export.ps2");
                    await _engine.CreateCardAsync(card, false);
                    await _engine.ImportAsync(card, storedPath);
                    await _engine.CheckAsync(card);
                    var saves = await _engine.ReadDirectoryAsync(card);
                    var save = saves.FirstOrDefault(candidate =>
                        candidate.DirectoryId.Equals(entry.DirectoryId, StringComparison.OrdinalIgnoreCase))
                        ?? saves.SingleOrDefault()
                        ?? throw new InvalidDataException("The stored save could not be verified.");
                    await _engine.ExportPackageAsync(card, save.DirectoryId, destinationPath);
                }
                finally
                {
                    try { Directory.Delete(temporaryRoot, true); } catch { }
                }
            }

            LibraryFooterStatus.Text =
                $"Export verified: {Path.GetFileName(destinationPath)}";
            Log($"Save Library export verified: {destinationPath}");
            MessageBox.Show(
                $"Save exported and verified.\n\n{destinationPath}",
                "Save Library Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("Save Library export failed: " + ex.Message);
            MessageBox.Show(ex.Message, "Save Library Export Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false, "Ready."); }
    }

    private async void LibraryRemove_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_saveLibraryContentMode ==
            SaveLibraryContentMode.MemoryCards)
        {
            var selectedCards =
                MemoryCardLibraryList.SelectedItems
                    .Cast<MemoryCardLibraryEntry>()
                    .ToArray();

            if (selectedCards.Length == 0 &&
                MemoryCardLibraryList.SelectedItem is
                    MemoryCardLibraryEntry singleCard)
            {
                selectedCards = [singleCard];
            }

            if (selectedCards.Length == 0)
                return;

            var cardDescription =
                selectedCards.Length == 1
                    ? selectedCards[0].DisplayName
                    : $"{selectedCards.Length} selected memory cards";

            var cardConfirmation =
                MessageBox.Show(
                    $"Remove {cardDescription} from the Memory Card Library?\n\n" +
                    "This permanently removes the library copies only. " +
                    "The original memory cards are not changed.",
                    selectedCards.Length == 1
                        ? "Remove Library Memory Card"
                        : "Remove Library Memory Cards",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (cardConfirmation !=
                MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                foreach (var entry in selectedCards)
                {
                    await _memoryCardLibraryService.RemoveAsync(
                        entry,
                        _memoryCardLibraryIndex);
                }

                RefreshMemoryCardLibraryView();
                ResetMemoryCardLibraryMetadata();
                UpdateLibrarySummary();

                LibraryFooterStatus.Text =
                    selectedCards.Length == 1
                        ? "Memory card removed from library."
                        : $"{selectedCards.Length} memory cards removed from library.";

                Log(
                    $"Memory Card Library removed " +
                    $"{selectedCards.Length} entr" +
                    $"{(selectedCards.Length == 1 ? "y" : "ies")}.");
            }
            catch (Exception ex)
            {
                Log(
                    "Memory Card Library removal failed: " +
                    ex.Message);

                MessageBox.Show(
                    ex.Message,
                    "Remove Memory Card Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        var entries = SaveLibraryList.SelectedItems.Cast<SaveLibraryEntry>().ToArray();
        if (entries.Length == 0 && SaveLibraryList.SelectedItem is SaveLibraryEntry singleEntry)
            entries = [singleEntry];
        if (entries.Length == 0) return;

        var description = entries.Length == 1 ? entries[0].DisplayTitle : $"{entries.Length} selected saves";
        var confirmation = MessageBox.Show(
            $"Remove {description} from the Save Library?\n\nThis removes the library copies only. Memory cards and original files are not changed.",
            entries.Length == 1 ? "Remove Library Save" : "Remove Library Saves",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        foreach (var entry in entries)
            await _saveLibraryService.RemoveAsync(entry, _saveLibraryIndex);

        ApplySaveLibraryFilter();
        UpdateSaveLibraryMetadata(null);
        LibraryFooterStatus.Text = entries.Length == 1 ? "Save removed from library." : $"{entries.Length} saves removed from library.";
        Log($"Save Library removed {entries.Length} entr{(entries.Length == 1 ? "y" : "ies")}.");
    }

    private void LibraryOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _saveLibraryService.LibraryRoot,
            UseShellExecute = true
        });
    }


    private void BrowseImportWizard_Click(object sender, RoutedEventArgs e)
    {
        var choice =
            ShowFileOrFolderSourceDialog(
                "CHOOSE IMPORT WIZARD SOURCE",
                "Choose a supported file or a PCSX2 folder memory card.",
                "Import Wizard Source");

        if (choice == 0)
            return;

        if (choice == 2)
        {
            var folderDialog =
                new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Choose PCSX2 Folder Memory Card",
                    Multiselect = false
                };

            if (folderDialog.ShowDialog() == true)
                SelectImportWizardSource(
                    folderDialog.FolderName);

            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FormatCatalog.SupportedPlayStationFilter
        };

        if (dialog.ShowDialog() == true)
            SelectImportWizardSource(dialog.FileName);
    }

    private void PreloadImportWizardSource(
        string path)
    {
        SelectImportWizardSource(path);
    }

    private MessageBoxResult ShowOwnedDropConfirmation(
        string message,
        string title)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        return MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
    }

    private void RouteDropToImportWizard(
        string path)
    {
        PreloadImportWizardSource(path);
        MainTabs.SelectedItem =
            UniversalImportWizardTab;
    }

    private void ImportWizard_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ImportWizard_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            SelectImportWizardSource(files[0]);
        }

        e.Handled = true;
    }

    private static bool TryGetPcsx2FolderSave(
        string path,
        out string cardPath,
        out string saveId)
    {
        cardPath = string.Empty;
        saveId = string.Empty;

        if (!Directory.Exists(path))
            return false;

        var parent = Directory.GetParent(path)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) ||
            !File.Exists(Path.Combine(parent, "_pcsx2_superblock")) ||
            !File.Exists(Path.Combine(path, "_pcsx2_index")))
        {
            return false;
        }

        cardPath = parent;
        saveId = Path.GetFileName(path);
        return !string.IsNullOrWhiteSpace(saveId);
    }

    private async Task<string> ExportWizardFolderSaveAsync()
    {
        if (!_wizardSourceIsFolderSave ||
            string.IsNullOrWhiteSpace(_wizardFolderCardPath) ||
            string.IsNullOrWhiteSpace(_wizardFolderSaveId))
        {
            throw new InvalidOperationException(
                "No PCSX2 folder-card save is selected.");
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-WIZARD-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        var destination = Path.Combine(
            temporaryRoot,
            SanitizeUniversalFileName(_wizardFolderSaveId) + ".psu");

        await _engine.ExportPsuAsync(
            _wizardFolderCardPath,
            _wizardFolderSaveId,
            destination);

        return destination;
    }

    private void SelectImportWizardSource(string path)
    {
        var folderSave = TryGetPcsx2FolderSave(
            path,
            out var folderCardPath,
            out var folderSaveId);
        var folderCard =
            !folderSave &&
            Directory.Exists(path) &&
            File.Exists(Path.Combine(path, "_pcsx2_superblock"));

        if (!File.Exists(path) && !folderCard && !folderSave)
            return;

        var kind = folderSave
            ? UniversalSourceKind.Ps2Package
            : DetectUniversalSourceKind(path);
        var extension = folderSave
            ? ".foldersave"
            : folderCard
                ? ".foldercard"
                : Path.GetExtension(path).ToLowerInvariant();

        _wizardSourcePath = path;
        _wizardSourceIsFolderSave = folderSave;
        _wizardFolderCardPath = folderSave ? folderCardPath : null;
        _wizardFolderSaveId = folderSave ? folderSaveId : null;
        _wizardSourceIsPs1Card = kind == UniversalSourceKind.Ps1Card;
        _wizardSourceIsPs1SingleSave = kind == UniversalSourceKind.Ps1SingleSave;
        _wizardSourceIsPs1Package = kind == UniversalSourceKind.Ps1Package;
        _wizardSourceIsCard =
            kind is UniversalSourceKind.Ps1Card or UniversalSourceKind.Ps2Card;
        _wizardSourceIsReadablePackage =
            folderSave ||
            kind is UniversalSourceKind.Ps1Package or UniversalSourceKind.Ps2Package;

        var displayName = folderSave
            ? "PCSX2 Folder Save"
            : GetUniversalSourceDisplayName(path, kind);

        WizardFilePanel.Visibility = Visibility.Visible;
        WizardFileName.Text = Path.GetFileName(path);
        WizardFilePath.Text = path;
        WizardFileDetails.Text = folderSave
            ? $"PCSX2 Folder Save  •  {folderSaveId}"
            : folderCard
                ? "PCSX2 Folder Memory Card  •  Folder"
                : $"{displayName}  •  {FormatBytes(new FileInfo(path).Length)}";

        WizardDropTitle.Text = folderSave ? "Folder save detected" : "File detected";
        WizardDropSubtitle.Text = "Choose an action from the panel on the right.";

        if (kind == UniversalSourceKind.Unsupported && !folderSave)
        {
            WizardDetectedType.Text =
                $"Unsupported file: {(string.IsNullOrWhiteSpace(extension) ? "UNKNOWN" : extension.ToUpperInvariant())}";
            WizardExplanation.Text =
                "PSM could not verify this file as a supported PS1/PS2 save or memory card.";
            WizardCardAText.Text = "Import into Card A";
            WizardCardBText.Text = "Import into Card B";
            SetWizardActions(false, false, false, false);
            return;
        }

        if (folderSave)
        {
            WizardDetectedType.Text =
                $"PCSX2 folder-card save detected: {folderSaveId}";
            WizardCardAText.Text = "Import into Card A";
            WizardCardBText.Text = "Import into Card B";
            WizardExplanation.Text =
                "PSM can repackage this PCSX2 directory-ID folder as a standard PSU save, " +
                "import it into either loaded PS2 card, send it to Universal Converter, " +
                "or add it directly to the Save Library.";
            SetWizardActions(_pathA is not null, _pathB is not null, true, true);
        }
        else if (_wizardSourceIsCard)
        {
            WizardDetectedType.Text =
                $"Complete memory card detected: {displayName}";
            WizardCardAText.Text = "Open as Card A";
            WizardCardBText.Text = "Open as Card B";
            WizardExplanation.Text =
                "This is a complete memory card. You can open it directly, convert it, " +
                "or preserve it in the Memory Card Library.";
            SetWizardActions(true, true, true, true);
        }
        else if (_wizardSourceIsPs1SingleSave)
        {
            WizardDetectedType.Text =
                $"PS1 individual save detected: {displayName}";
            WizardCardAText.Text = "Import into PS1 Card A";
            WizardCardBText.Text = "Import into PS1 Card B";
            WizardExplanation.Text =
                "This individual PS1 save can be imported into either loaded PS1 card, " +
                "converted to another PS1 save/card format, or packaged into the Save Library.";
            SetWizardActions(
                _ps1PathA is not null,
                _ps1PathB is not null,
                true,
                true);
        }
        else if (_wizardSourceIsPs1Package)
        {
            WizardDetectedType.Text =
                "PSM PS1 save package detected";
            WizardCardAText.Text = "Import into PS1 Card A";
            WizardCardBText.Text = "Import into PS1 Card B";
            WizardExplanation.Text =
                "This PSM PS1 package can be imported into either loaded PS1 card or converted " +
                "to a supported PS1 individual-save or memory-card format.";
            SetWizardActions(
                _ps1PathA is not null,
                _ps1PathB is not null,
                true,
                true);
        }
        else
        {
            WizardDetectedType.Text =
                $"Packaged save detected: {displayName}";
            WizardCardAText.Text = "Import into Card A";
            WizardCardBText.Text = "Import into Card B";
            WizardExplanation.Text =
                "This packaged PS2 save can be safely imported into either loaded PS2 card, " +
                "converted to another verified format, or added to the Save Library.";
            SetWizardActions(_pathA is not null, _pathB is not null, true, true);
        }

        Log($"Universal Import Wizard detected: {path}");
    }

    private void SetWizardActions(bool cardA, bool cardB, bool convert, bool library)
    {
        WizardCardAButton.IsEnabled = cardA;
        WizardCardBButton.IsEnabled = cardB;
        WizardConvertButton.IsEnabled = convert;
        WizardLibraryButton.IsEnabled = library;
    }

    private void ClearImportWizard_Click(object sender, RoutedEventArgs e) =>
        ResetImportWizard();

    private void ResetImportWizard()
    {
        _wizardSourcePath = null;
        _wizardSourceIsCard = false;
        _wizardSourceIsPs1Card = false;
        _wizardSourceIsPs1SingleSave = false;
        _wizardSourceIsPs1Package = false;
        _wizardSourceIsReadablePackage = false;
        _wizardSourceIsFolderSave = false;
        _wizardFolderCardPath = null;
        _wizardFolderSaveId = null;
        WizardFilePanel.Visibility = Visibility.Collapsed;
        WizardFileName.Text = string.Empty;
        WizardFileDetails.Text = string.Empty;
        WizardFilePath.Text = string.Empty;
        WizardDropTitle.Text = "Drop a save or memory card here";
        WizardDropSubtitle.Text =
            "PS1 Saves: .MCS • .PS1 • .MCB • .MCX • .PDA • .PSX • .RAW • .PSV   |   PS1 Cards: .MCR • .SRM • .BIN • .MCD • .MC • .GME • .MEM/.VGS • .DDF • .PS • .PSM • .MCI • .VMP • .VMC • .SAV • .VM1   |   PS2: .PSU • .MAX • .CBS • .XPS • .SPS • .PSV • .NPO • .P2M • .MC2 • .PS2 • .VM2 • .VMC • .BIN • .MCD";
        WizardDetectedType.Text = "Choose a file to see available actions.";
        WizardExplanation.Text = "No source selected.";
        WizardCardAText.Text = "Import into Card A";
        WizardCardBText.Text = "Import into Card B";
        SetWizardActions(false, false, false, false);
    }

    private async void WizardCardA_Click(object sender, RoutedEventArgs e)
    {
        if (_wizardSourcePath is null)
            return;

        if (_wizardSourceIsFolderSave)
        {
            if (_pathA is null)
            {
                MessageBox.Show(
                    "Open a destination in Card A first.",
                    "Universal Import Wizard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var temporaryPsu = await ExportWizardFolderSaveAsync();
            SelectPackage(temporaryPsu);
            await ImportPackageAsync(_pathA, 'A');
            return;
        }

        if (_wizardSourceIsCard)
        {
            if (_wizardSourceIsPs1Card)
            {
                await LoadPs1CardAsync(_wizardSourcePath, 'A');
                MainTabs.SelectedItem = Ps1MemoryCardsTab;
            }
            else
            {
                await LoadCardAsync(_wizardSourcePath, 'A');
                MainTabs.SelectedIndex = 0;
            }
            return;
        }

        if (_wizardSourceIsPs1SingleSave)
        {
            await ImportWizardPs1SingleSaveAsync(_wizardSourcePath, 'A');
            return;
        }

        if (_wizardSourceIsPs1Package)
        {
            await ImportWizardPs1PackageAsync(_wizardSourcePath, 'A');
            return;
        }

        if (_pathA is null)
        {
            MessageBox.Show(
                "Open a destination in Card A first.",
                "Universal Import Wizard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectPackage(_wizardSourcePath);
        await ImportPackageAsync(_pathA, 'A');
    }

    private async void WizardCardB_Click(object sender, RoutedEventArgs e)
    {
        if (_wizardSourcePath is null)
            return;

        if (_wizardSourceIsFolderSave)
        {
            if (_pathB is null)
            {
                MessageBox.Show(
                    "Open a destination in Card B first.",
                    "Universal Import Wizard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var temporaryPsu = await ExportWizardFolderSaveAsync();
            SelectPackage(temporaryPsu);
            await ImportPackageAsync(_pathB, 'B');
            return;
        }

        if (_wizardSourceIsCard)
        {
            if (_wizardSourceIsPs1Card)
            {
                await LoadPs1CardAsync(_wizardSourcePath, 'B');
                MainTabs.SelectedItem = Ps1MemoryCardsTab;
            }
            else
            {
                await LoadCardAsync(_wizardSourcePath, 'B');
                MainTabs.SelectedIndex = 0;
            }
            return;
        }

        if (_wizardSourceIsPs1SingleSave)
        {
            await ImportWizardPs1SingleSaveAsync(_wizardSourcePath, 'B');
            return;
        }

        if (_wizardSourceIsPs1Package)
        {
            await ImportWizardPs1PackageAsync(_wizardSourcePath, 'B');
            return;
        }

        if (_pathB is null)
        {
            MessageBox.Show(
                "Open a destination in Card B first.",
                "Universal Import Wizard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectPackage(_wizardSourcePath);
        await ImportPackageAsync(_pathB, 'B');
    }

    private async Task ImportWizardPs1SingleSaveAsync(
        string sourcePath,
        char side)
    {
        var destination = side == 'A' ? _ps1PathA : _ps1PathB;
        if (destination is null)
        {
            MessageBox.Show(
                $"Open a PS1 destination in Card {side} first.",
                "Universal Import Wizard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            SetBusy(true, $"Importing PS1 save into Card {side}...");
            await _ps1CardService.ImportExternalSaveAsync(
                sourcePath,
                destination,
                ReplaceExistingPs1.IsChecked == true);
            await LoadPs1CardAsync(destination, side);
            MainTabs.SelectedItem = Ps1MemoryCardsTab;
            MessageBox.Show(
                $"PS1 save imported and verified in Card {side}.",
                "PS1 Import Verified",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("PS1 individual-save import failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "PS1 Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async Task ImportWizardPs1PackageAsync(
        string packagePath,
        char side)
    {
        var destination = side == 'A' ? _ps1PathA : _ps1PathB;
        if (destination is null)
        {
            MessageBox.Show(
                $"Open a PS1 destination in Card {side} first.",
                "Universal Import Wizard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PSM-WIZARD-PS1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            SetBusy(true, $"Importing PS1 package into Card {side}...");
            var sourceCard = Path.Combine(temporaryRoot, "source.mcr");
            await _ps1CardService.CreateSingleSaveCardFromPackageAsync(
                packagePath,
                sourceCard);
            var read = await _ps1CardService.ReadAsync(sourceCard);
            var save = read.Saves.Single(candidate => !candidate.IsDeleted);
            await _ps1CardService.CopySaveAsync(
                sourceCard,
                save,
                destination,
                ReplaceExistingPs1.IsChecked == true);
            await LoadPs1CardAsync(destination, side, save.FileName);
            MainTabs.SelectedItem = Ps1MemoryCardsTab;
            MessageBox.Show(
                $"PS1 package imported and verified in Card {side}.",
                "PS1 Import Verified",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("PS1 package import failed: " + ex.Message);
            MessageBox.Show(
                ex.Message,
                "PS1 Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); } catch { }
            SetBusy(false, "Ready.");
        }
    }

    private async void WizardConvert_Click(object sender, RoutedEventArgs e)
    {
        if (_wizardSourcePath is null) return;

        var source = _wizardSourceIsFolderSave
            ? await ExportWizardFolderSaveAsync()
            : _wizardSourcePath;

        SelectUniversalSource(source);
        MainTabs.SelectedItem = UniversalConverterTab;
    }

    private async void WizardLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_wizardSourcePath is null)
            return;

        try
        {
            if (_wizardSourceIsCard)
            {
                SetBusy(true, "Adding memory card to Memory Card Library...");

                string platform;
                string cardType;
                int saveCount;
                long? capacity;

                if (_wizardSourceIsPs1Card)
                {
                    var card = await _ps1CardService.ReadAsync(_wizardSourcePath);
                    platform = "PlayStation";
                    cardType = GetPs1CardTypeName(Path.GetExtension(_wizardSourcePath));
                    saveCount = card.Saves.Count(save => !save.IsDeleted);
                    capacity = Ps1MemoryCardService.CardSize;
                }
                else
                {
                    var card = await _engine.ReadCardAsync(_wizardSourcePath);
                    platform = "PlayStation 2";
                    saveCount = card.Saves.Count;
                    if (Directory.Exists(_wizardSourcePath))
                    {
                        cardType = "PCSX2 Folder Memory Card";
                        capacity = null;
                    }
                    else
                    {
                        cardType =
                            FormatCatalog.GetPs2CardTypeName(
                                _wizardSourcePath);
                        capacity = card.TotalBytes;
                    }
                }

                var stored = await _memoryCardLibraryService.StoreAsync(
                    _wizardSourcePath,
                    platform,
                    cardType,
                    saveCount,
                    capacity);

                await LoadMemoryCardLibraryAsync();
                ShowMemoryCardLibraryMode();
                MemoryCardLibraryList.SelectedItem = stored.Entry;
                MemoryCardLibraryList.ScrollIntoView(stored.Entry);
                MainTabs.SelectedItem = SaveLibraryTab;

                MessageBox.Show(
                    stored.Duplicate is null
                        ? $"{stored.Entry.DisplayName} was added to the Memory Card Library."
                        : $"{stored.Entry.DisplayName} is already in the Memory Card Library.",
                    "Memory Card Library",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_wizardSourceIsPs1SingleSave)
            {
                SetBusy(true, "Packaging PS1 save for the Save Library...");
                var temporaryRoot = Path.Combine(
                    Path.GetTempPath(),
                    "PSM-WIZARD-LIBRARY-PS1-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                try
                {
                    var package = Path.Combine(
                        temporaryRoot,
                        Path.GetFileNameWithoutExtension(_wizardSourcePath) + ".ps1save");
                    await _ps1CardService.CreateSavePackageFromExternalSaveAsync(
                        _wizardSourcePath,
                        package);
                    var ps1Result = await _saveLibraryService.ImportAsync(
                        package,
                        _saveLibraryIndex);

                    if (ps1Result.Duplicate is not null)
                    {
                        LibraryFooterStatus.Text =
                            $"Exact duplicate already exists: {ps1Result.Duplicate.DisplayTitle}";
                        MessageBox.Show(
                            "This exact PS1 save is already in the Save Library.",
                            "Duplicate Save",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        await LoadSaveLibraryIconAsync(ps1Result.Entry);
                        LibraryFooterStatus.Text =
                            $"Added {ps1Result.Entry.DisplayTitle} to Save Library.";
                    }

                    ApplySaveLibraryFilter();
                    ShowSaveLibraryMode();
                    MainTabs.SelectedItem = SaveLibraryTab;
                    SaveLibraryList.SelectedItem = ps1Result.Entry;
                    SaveLibraryList.ScrollIntoView(ps1Result.Entry);
                }
                finally
                {
                    try { Directory.Delete(temporaryRoot, true); } catch { }
                }
                return;
            }

            if (!_wizardSourceIsReadablePackage)
                return;

            SetBusy(true, "Adding save to Save Library...");
            var source = _wizardSourceIsFolderSave
                ? await ExportWizardFolderSaveAsync()
                : _wizardSourcePath;
            var result = await _saveLibraryService.ImportAsync(
                source,
                _saveLibraryIndex);

            if (result.Duplicate is not null)
            {
                LibraryFooterStatus.Text =
                    $"Exact duplicate already exists: {result.Duplicate.DisplayTitle}";
                MessageBox.Show(
                    "This exact save is already in the Save Library.",
                    "Duplicate Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                await LoadSaveLibraryIconAsync(result.Entry);
                LibraryFooterStatus.Text =
                    $"Added {result.Entry.DisplayTitle} to Save Library.";
            }

            ApplySaveLibraryFilter();
            ShowSaveLibraryMode();
            MainTabs.SelectedItem = SaveLibraryTab;
            SaveLibraryList.SelectedItem = result.Entry;
            SaveLibraryList.ScrollIntoView(result.Entry);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Library Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private static string? PickPackage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FormatCatalog.Ps2PackageImportFilter
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void SelectPackage(string path)
    {
        if (!File.Exists(path)) return;
        var supported = new[] { ".psu", ".max", ".cbs", ".sps", ".xps", ".psv" };
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!supported.Contains(extension))
        {
            MessageBox.Show("This build can import PSU, MAX, CBS, SPS, XPS, and PSV packages.", "Unsupported Package", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _selectedPackagePath = path;
        var info = new FileInfo(path);
        LibraryFooterStatus.Text =
            $"Selected {info.Name} ({FormatBytes(info.Length)}) for memory-card import.";
        Log($"Selected save package: {path}");
        RefreshButtons();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:N2} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:N1} KB";
        return $"{bytes:N0} bytes";
    }

    private async Task<SaveEntry> InspectPs2PackageForImportAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var temporaryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "PSM-PS2-IMPORT-INSPECT-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            temporaryRoot);

        try
        {
            var cardPath =
                Path.Combine(
                    temporaryRoot,
                    "inspect.ps2");

            await _engine.CreateCardAsync(
                cardPath,
                cancellationToken);

            await _engine.ImportAsync(
                cardPath,
                packagePath,
                cancellationToken);

            await _engine.CheckAsync(
                cardPath,
                cancellationToken);

            var saves =
                await _engine.ReadDirectoryAsync(
                    cardPath,
                    cancellationToken);

            if (saves.Count != 1)
            {
                throw new InvalidDataException(
                    $"The package contains {saves.Count} saves; exactly one was expected.");
            }

            return saves[0];
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryRoot,
                    true);
            }
            catch { }
        }
    }

    private async Task ImportPackageAsync(
        string destination,
        char destinationSide,
        bool askForConfirmation = true)
    {
        if (_selectedPackagePath is null) return;
        var packageName =
            Path.GetFileName(
                _selectedPackagePath);

        if (askForConfirmation)
        {
            var confirm =
                MessageBox.Show(
                    $"Import {packageName}\n\ninto {Path.GetFileName(destination)}?",
                    "Confirm Import",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        var incomingSave =
            await InspectPs2PackageForImportAsync(
                _selectedPackagePath);

        var destinationSaves =
            await _engine.ReadDirectoryAsync(
                destination);

        var existing =
            destinationSaves.FirstOrDefault(
                save =>
                    save.DirectoryId.Equals(
                        incomingSave.DirectoryId,
                        StringComparison.OrdinalIgnoreCase));

        if (existing is not null &&
            ReplaceExisting.IsChecked != true)
        {
            MessageBox.Show(
                $"{incomingSave.Title}\n{incomingSave.DirectoryId}\n\nalready exists on {Path.GetFileName(destination)}.\n\n" +
                "Enable \"Replace save if it already exists\" to overwrite it.",
                "Save Already Exists",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "PSAM-Import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            SetBusy(true, $"Importing {packageName}...");
            VerifiedBanner.Visibility = Visibility.Hidden;
            var before = await _engine.ReadDirectoryAsync(destination);
            var temporaryCard = Path.Combine(temporaryDirectory, Path.GetFileName(destination));
            File.Copy(destination, temporaryCard, true);

            if (existing is not null &&
                ReplaceExisting.IsChecked == true)
            {
                Log(
                    $"Removing existing destination save before package import: {incomingSave.DirectoryId}");

                await _engine.DeleteAsync(
                    temporaryCard,
                    incomingSave.DirectoryId);
            }

            Log($"Importing package into temporary destination: {_selectedPackagePath}");
            await _engine.ImportAsync(temporaryCard, _selectedPackagePath);
            Log("Verifying temporary destination card.");
            await _engine.CheckAsync(temporaryCard);
            var after = await _engine.ReadDirectoryAsync(temporaryCard);

            var imported =
                after.FirstOrDefault(
                    candidate =>
                        candidate.DirectoryId.Equals(
                            incomingSave.DirectoryId,
                            StringComparison.OrdinalIgnoreCase));

            if (imported is null)
            {
                throw new InvalidDataException(
                    "The imported PS2 save was not present after verification.");
            }

            var backup = CreateAutomaticBackup(destination);
            File.Copy(temporaryCard, destination, true);
            LogAutomaticBackup("Package import committed.", backup);

            await LoadCardAsync(destination, destinationSide, imported?.DirectoryId, allowWhileBusy: true);
            var displayName = imported?.Title ?? packageName;
            VerifiedText.Text = $"IMPORT VERIFIED - {displayName} added successfully";
            VerifiedBanner.Visibility = Visibility.Visible;
            LibraryFooterStatus.Text = $"Import verified. {displayName} was added and the destination card was rescanned.";
            StatusText.Text = "Import verified.";
            MessageBox.Show($"Save imported and verified successfully.\n\n{AutomaticBackupDetails(backup)}", "Import Verified", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Package import failed: {ex.Message}");
            LibraryFooterStatus.Text = "Import failed. The original card was not replaced.";
            MessageBox.Show(ex.Message, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, true); } catch { }
            SetBusy(false, "Ready.");
            RefreshButtons();
        }
    }

    private void ChoosePackage_Click(object sender, RoutedEventArgs e)
    {
        var path = PickPackage();
        if (path is not null) SelectPackage(path);
    }

    private async void ImportPackageA_Click(object sender, RoutedEventArgs e)
    {
        if (_pathA is not null) await ImportPackageAsync(_pathA, 'A');
    }

    private async void ImportPackageB_Click(object sender, RoutedEventArgs e)
    {
        if (_pathB is not null) await ImportPackageAsync(_pathB, 'B');
    }

    private void OpenPackageFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPackagePath is null) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_selectedPackagePath}\"",
            UseShellExecute = true
        });
    }

    private void SaveLibrary_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectPackage(files[0]);
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files ||
            files.Length == 0)
        {
            return;
        }

        RouteDropToImportWizard(
            files[0]);

        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_StateChanged(
        object? sender,
        EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            // Borderless WindowChrome can otherwise extend behind the
            // Windows taskbar. Keep maximized PSM inside the usable
            // desktop work area while leaving normal sizing unrestricted.
            MaxWidth =
                SystemParameters.WorkArea.Width;

            MaxHeight =
                SystemParameters.WorkArea.Height;
        }
        else
        {
            MaxWidth =
                double.PositiveInfinity;

            MaxHeight =
                double.PositiveInfinity;
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();


}
