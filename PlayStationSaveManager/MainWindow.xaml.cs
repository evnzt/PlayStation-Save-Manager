using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
    private string? _selectedPackagePath;
    private string? _universalSourcePath;
    private string? _wizardSourcePath;
    private bool _wizardSourceIsCard;
    private bool _wizardSourceIsPs1Card;
    private bool _wizardSourceIsReadablePackage;
    private bool _wizardSourceIsFolderSave;
    private string? _wizardFolderCardPath;
    private string? _wizardFolderSaveId;


    private static readonly UniversalFormatOption[] UniversalFormats =
    [
        new(".mcr", "ePSXe / PSEmu Pro Memory Card", true, true),
        new(".srm", "RetroArch / Libretro PS1 Memory Card", true, true),
        new(".bin", "pSX / AdriPSX Memory Card", true, true),
        new(".mcd", "Bleem! Memory Card", true, true),
        new(".mc", "PSXGame Edit Memory Card", true, true),
        new(".gme", "DexDrive Memory Card", true, true),
        new(".mem", "VGS Memory Card", false, false),
        new(".vgs", "VGS Memory Card", false, false),
        new(".ddf", "DataDeck Memory Card", false, false),
        new(".ps", "WinPSM Memory Card", false, false),
        new(".psm", "Smart Link Memory Card", false, false),
        new(".mci", "MCExplorer Memory Card", false, false),
        new(".vmp", "PSP Virtual Memory Card", false, false),
        new(".vm1", "PS3 Virtual Memory Card", true, true),
        new(".ps1save", "PSM PlayStation Save Package", true, true),
        new(".psu", "EMS / Memory Linker PSU", true, true),
        new(".max", "ARMAX V3", true, true),
        new(".cbs", "CodeBreaker Save", true, false),
        new(".xps", "X-Port / Xploder Save", true, false),
        new(".sps", "SharkPort Save", true, false),
        new(".psv", "PlayStation 3 Virtual Save", true, false),
        new(".npo", "nPort Save", false, false),
        new(".p2m", "Xploder 4 Pro Save", false, false),
        new(".mc2", "MemCard PRO2 Memory Card", true, true),
        new(".ps2", "PCSX2 Memory Card", true, true),
        new(".foldercard", "PCSX2 Folder Memory Card", true, true)
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
            _ = LoadThumbnailsAsync(path, saves, side);

            if (side == 'A')
            {
                _pathA = path;
                CardAInfo.Text = $"{Path.GetFileName(path)} - {saves.Count} saves";
                if (Directory.Exists(path))
                    UpdateFolderCapacityDisplay('A');
                else
                    UpdateCapacityDisplay('A', cardResult);
            }
            else
            {
                _pathB = path;
                CardBInfo.Text = $"{Path.GetFileName(path)} - {saves.Count} saves";
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

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "PSAM-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            SetBusy(true, $"Transferring {save.DirectoryId}...");
            VerifiedBanner.Visibility = Visibility.Collapsed;
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
                    ? CreateFolderBackup(destination)
                    : CreateBackup(destination);

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

            Log($"Transfer committed. Backup: {backup}");

            await LoadCardAsync(destination, destinationSide, save.DirectoryId, allowWhileBusy: true);
            VerifiedText.Text = $"TRANSFER VERIFIED - {save.Title} copied successfully";
            VerifiedBanner.Visibility = Visibility.Visible;
            StatusText.Text = "Transfer verified.";
            MessageBox.Show($"Save copied and verified successfully.\n\nBackup:\n{backup}", "Transfer Verified", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (_wizardSourcePath is not null && !_wizardSourceIsCard && _wizardSourceIsReadablePackage)
        {
            WizardCardAButton.IsEnabled = !_busy && _pathA is not null;
            WizardCardBButton.IsEnabled = !_busy && _pathB is not null;
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

    private void Log(string message)
    {
        ActivityLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        ActivityLog.ScrollToEnd();
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
        text.Text = $"{FormatBytes(result.UsedBytes.Value)} used  •  {FormatBytes(result.FreeBytes.Value)} free  •  {FormatBytes(result.TotalBytes.Value)} total";
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
                Filter = "PS2 memory cards|*.mc2;*.ps2;*.mcd;*.vm2;*.bin|All files|*.*",
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
                    "Open a .ps2, .mc2, or another supported card-image file.",
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
                        "Create a .ps2 or .mc2 card in 8, 16, 32, or 64 MB.",
                        1),
                    new CardChoice(
                        FindResource("IconPcsx2FolderCard") as ImageSource,
                        "PCSX2 Folder Memory Card",
                        "Creates an infinite-capacity PCSX2 folder card.",
                        2)
                });

        if (choice == 1)
        {
            var format =
                ShowNewCardTypeDialog(
                    "SELECT PS2 CARD FORMAT",
                    "Choose the file format for the new memory card.",
                    new[]
                    {
                        new CardChoice(
                            FindResource("IconStandardPs2Card") as ImageSource,
                            "PCSX2 (.ps2)",
                            "Create a standard PCSX2 memory-card image.",
                            1),
                        new CardChoice(
                            FindResource("IconStandardPs2Card") as ImageSource,
                            "MemCard PRO2 (.mc2)",
                            "Create a MemCard PRO2-compatible memory-card image.",
                            2)
                    },
                    "PS2 Memory Card Format");

            if (format == 0)
                return;

            var sizeMb =
                ShowNewCardTypeDialog(
                    "SELECT PS2 CARD SIZE",
                    $"Choose the capacity for the new {(format == 2 ? ".mc2" : ".ps2")} memory card. 8 MB offers the widest game compatibility.",
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
                await CreateNewPs2FileCardAsync(sideText[0], sizeMb, format == 2);
        }
        else if (choice == 2)
        {
            await CreateNewPs2FolderCardAsync(sideText[0]);
        }
    }

    private async Task CreateNewPs2FileCardAsync(
        char side,
        int sizeMb,
        bool createMc2)
    {
        var extension = createMc2 ? ".mc2" : ".ps2";
        var formatName = createMc2 ? "MemCard PRO2" : "PCSX2";

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Create {sizeMb} MB {formatName} Memory Card",
            Filter = createMc2
                ? "MemCard PRO2 Memory Card (*.mc2)|*.mc2"
                : "PCSX2 Memory Card (*.ps2)|*.ps2",
            DefaultExt = extension,
            AddExtension = true,
            FileName = createMc2
                ? "MemoryCard1-1.mc2"
                : (side == 'A'
                    ? $"PS2 Card A - {sizeMb}MB.ps2"
                    : $"PS2 Card B - {sizeMb}MB.ps2"),
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(
                true,
                $"Creating {sizeMb} MB {formatName} memory card...");

            await _engine.CreateCardAsync(
                dialog.FileName,
                sizeMb);

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
            Title = "Create Standard PlayStation Memory Card",
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
            PreviewImageB.Source = null;
            PreviewPlaceholderB.Visibility = Visibility.Visible;
            _previewModelB = null;
            _previewFallbackB = null;
            ResetCapacityDisplay('B');
        }
        VerifiedBanner.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Card {side} closed. Browse or drop another card.";
        RefreshButtons();
    }

    private async void PreviewA_Click(object sender, RoutedEventArgs e) =>
        await SelectPreviewAsync('A', CardAList.SelectedItem as SaveEntry);
    private async void PreviewB_Click(object sender, RoutedEventArgs e) =>
        await SelectPreviewAsync('B', CardBList.SelectedItem as SaveEntry);

    private async void CopyAToB_Click(object sender, RoutedEventArgs e) { if (_pathA is not null && _pathB is not null && CardAList.SelectedItem is SaveEntry save) await TransferAsync(_pathA, _pathB, save, 'B'); }
    private async void CopyBToA_Click(object sender, RoutedEventArgs e) { if (_pathA is not null && _pathB is not null && CardBList.SelectedItem is SaveEntry save) await TransferAsync(_pathB, _pathA, save, 'A'); }

    private async void ExportA_Click(object sender, RoutedEventArgs e) => await ExportSelectedAsync(_pathA, CardAList.SelectedItem as SaveEntry);
    private async void ExportB_Click(object sender, RoutedEventArgs e) => await ExportSelectedAsync(_pathB, CardBList.SelectedItem as SaveEntry);

    private async Task ExportSelectedAsync(string? card, SaveEntry? save)
    {
        if (card is null || save is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export PS2 Save",
            Filter =
                "EMS / Memory Linker PSU (*.psu)|*.psu|" +
                "Action Replay MAX (*.max)|*.max|" +
                "PCSX2 Memory Card (*.ps2)|*.ps2|" +
                "MemCard PRO2 Memory Card (*.mc2)|*.mc2",
            DefaultExt = ".psu",
            FileName = save.DirectoryId + ".psu",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) return;

        var output = Path.GetFullPath(dialog.FileName);
        var extension = Path.GetExtension(output).ToLowerInvariant();

        if (extension == ".mc2")
        {
            output = PromptForMemCardPro2ReadyOutput(output, save.DirectoryId);
            if (string.IsNullOrWhiteSpace(output)) return;
        }

        try
        {
            SetBusy(true, "Exporting save...");

            if (extension is ".ps2" or ".mc2")
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
            try { if (File.Exists(output)) File.Delete(output); } catch { }
            MessageBox.Show(ex.Message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private async Task SavePs2CardAsAsync(string sourcePath, SaveEntry? selectedSave)
    {
        var sourceExtension =
            Path.GetExtension(sourcePath)
                .ToLowerInvariant();

        var defaultExtension =
            sourceExtension is ".mc2" or ".ps2"
                ? sourceExtension
                : ".ps2";

        var sourceBaseName =
            Directory.Exists(sourcePath)
                ? Path.GetFileName(sourcePath)
                : Path.GetFileNameWithoutExtension(sourcePath);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save PS2 Memory Card As",
            Filter =
                "PS2 memory cards (*.ps2;*.mc2)|*.ps2;*.mc2|" +
                "PCSX2 memory card (*.ps2)|*.ps2|" +
                "Memory Card Annihilator (*.mc2)|*.mc2|" +
                "PCSX2 Folder Memory Card (folder)|*.*",
            DefaultExt = defaultExtension,
            FileName =
                sourceBaseName +
                "_converted" +
                defaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            FilterIndex = 1
        };

        if (dialog.ShowDialog() != true)
            return;

        var destinationPath =
            Path.GetFullPath(dialog.FileName);

        var folderCard =
            dialog.FilterIndex == 4;

        if (!folderCard &&
            Path.GetExtension(destinationPath).Equals(
                ".mc2",
                StringComparison.OrdinalIgnoreCase))
        {
            var preferredDirectoryId = selectedSave?.DirectoryId;

            if (string.IsNullOrWhiteSpace(preferredDirectoryId))
            {
                try
                {
                    var saves = await _engine.ReadDirectoryAsync(sourcePath);
                    preferredDirectoryId = saves
                        .Select(save => save.DirectoryId)
                        .FirstOrDefault(id =>
                            !string.IsNullOrWhiteSpace(ExtractGameSerial(id)));
                }
                catch
                {
                    // The normal Save Card As path can still continue.
                }
            }

            destinationPath = PromptForMemCardPro2ReadyOutput(
                destinationPath,
                preferredDirectoryId);

            if (string.IsNullOrWhiteSpace(destinationPath))
                return;
        }

        if (Path.GetFullPath(sourcePath).Equals(
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

        try
        {
            if (folderCard)
            {
                SetBusy(
                    true,
                    "Converting to a PCSX2 folder memory card...");

                await _engine.ConvertToPcsx2FolderCardAsync(
                    sourcePath,
                    destinationPath);

                Log(
                    $"PCSX2 folder card created: {destinationPath}");

                MessageBox.Show(
                    "The PCSX2 folder memory card was created and verified.\n\n" +
                    destinationPath,
                    "Folder Card Created",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                SetBusy(
                    true,
                    Directory.Exists(sourcePath)
                        ? "Converting folder card to a PS2 memory-card image..."
                        : "Saving PS2 memory card copy...");

                if (Directory.Exists(sourcePath))
                {
                    var noEcc =
                        Path.GetExtension(destinationPath)
                            .Equals(
                                ".mc2",
                                StringComparison.OrdinalIgnoreCase);

                    await _engine.ConvertFolderCardToImageAsync(
                        sourcePath,
                        destinationPath,
                        noEcc);
                }
                else
                {
                    await Task.Run(
                        () => File.Copy(
                            sourcePath,
                            destinationPath,
                            true));
                }

                await _engine.CheckAsync(destinationPath);

                Log(
                    $"PS2 memory card saved and verified: " +
                    destinationPath);

                MessageBox.Show(
                    $"PS2 memory card saved and verified.\n\n" +
                    destinationPath,
                    "PS2 Card Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (folderCard &&
                    Directory.Exists(destinationPath))
                {
                    Directory.Delete(
                        destinationPath,
                        recursive: true);
                }
                else if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                else if (Directory.Exists(destinationPath))
                {
                    Directory.Delete(
                        destinationPath,
                        recursive: true);
                }
            }
            catch { }

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
            SetBusy(false, "Ready.");
        }
    }

    private async void DeleteA_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pathA is not null &&
            CardAList.SelectedItem is SaveEntry save)
        {
            await DeletePs2SaveAsync(
                _pathA,
                save,
                'A');
        }
    }

    private async void DeleteB_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_pathB is not null &&
            CardBList.SelectedItem is SaveEntry save)
        {
            await DeletePs2SaveAsync(
                _pathB,
                save,
                'B');
        }
    }

    private async Task DeletePs2SaveAsync(
        string cardPath,
        SaveEntry save,
        char side)
    {
        var confirmation =
            MessageBox.Show(
                $"Delete {save.Title}?\n\n" +
                $"{save.DirectoryId}\n\n" +
                "PSM will create a timestamped backup first.",
                "Delete Save",
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
                $"Deleting {save.DirectoryId}...");

            var folderCard =
                Directory.Exists(cardPath);

            var temporaryCard =
                folderCard
                    ? Path.Combine(
                        temporaryRoot,
                        "FolderCard")
                    : Path.Combine(
                        temporaryRoot,
                        Path.GetFileName(cardPath));

            if (folderCard)
                CopyDirectory(cardPath, temporaryCard);
            else
                File.Copy(cardPath, temporaryCard, true);

            await _engine.DeleteAsync(
                temporaryCard,
                save.DirectoryId);

            await _engine.CheckAsync(
                temporaryCard);

            var backup =
                folderCard
                    ? CreateFolderBackup(cardPath)
                    : CreateBackup(cardPath);

            if (folderCard)
            {
                Directory.Delete(
                    cardPath,
                    recursive: true);

                CopyDirectory(
                    temporaryCard,
                    cardPath);
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

            Log(
                $"Deleted {save.DirectoryId}. Backup: {backup}");

            MessageBox.Show(
                $"Save deleted and verified.\n\nBackup:\n{backup}",
                "Delete Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                $"Delete failed: {ex.Message}");

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
                    Directory.Delete(
                        temporaryRoot,
                        recursive: true);
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
                $"{save.ProfileName}\n\n" +
                $"Directory ID: {save.DirectoryId}\n" +
                $"Game Serial: " +
                $"{(string.IsNullOrWhiteSpace(gameSerial) ? "Unknown" : gameSerial)}\n" +
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

            current = VisualTreeHelper.GetParent(current);
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
            Filter =
                "Supported PlayStation saves and cards|*.psu;*.max;*.cbs;*.xps;*.sps;*.psv;*.npo;*.p2m;*.mc2;*.ps2;*.mcr;*.srm;*.bin;*.mcd;*.mc;*.gme;*.mem;*.vgs;*.ddf;*.ps;*.psm;*.mci;*.vmp;*.vm1|" +
                "PS1 memory cards|*.mcr;*.srm;*.bin;*.mcd;*.mc;*.gme;*.mem;*.vgs;*.ddf;*.ps;*.psm;*.mci;*.vmp;*.vm1|" +
                "Packaged PS2 saves|*.psu;*.max;*.cbs;*.xps;*.sps;*.psv;*.npo;*.p2m|" +
                "PS2 memory cards|*.mc2;*.ps2|All files|*.*"
        };
        if (dialog.ShowDialog() == true) SelectUniversalSource(dialog.FileName);
    }

    private void SelectUniversalSource(string path)
    {
        _universalSourcePath = path;
        UniversalSourcePath.Text = path;

        var folderCard =
            Directory.Exists(path) &&
            File.Exists(
                Path.Combine(
                    path,
                    "_pcsx2_superblock"));

        var extension =
            folderCard
                ? ".foldercard"
                : Path.GetExtension(path).ToLowerInvariant();

        var sourceFormat =
            UniversalFormats.FirstOrDefault(
                format =>
                    format.Extension == extension);

        if (sourceFormat is null)
        {
            UniversalDetectedFormat.Text = $"Detected format: Unsupported ({extension})";
            UniversalModeText.Text = "Unsupported source";
            UniversalConversionReport.Text = "Choose one of the listed PS2 save or memory-card formats.";
            UniversalTargetFormat.ItemsSource = null;
            UniversalConvertButton.IsEnabled = false;
            return;
        }

        UniversalDetectedFormat.Text = $"Detected format: {sourceFormat.DisplayName} ({extension.ToUpperInvariant()})";
        var isPs1Card = extension is ".mcr" or ".srm" or ".bin" or ".mcd" or ".mc" or ".gme" or ".vm1";
        var isCard =
            isPs1Card ||
            extension is ".mc2" or ".ps2" or ".foldercard";
        UniversalModeText.Text = isCard
            ? "Whole-memory-card conversion"
            : extension == ".ps1save"
                ? "PlayStation save-package conversion"
                : "PlayStation 2 packaged-save conversion";

        var isPs1Package = extension == ".ps1save";
        var isPs2Card = extension is ".mc2" or ".ps2" or ".foldercard";
        var isPs2Package = !isPs1Card && !isPs1Package && !isPs2Card;

        // Never offer cross-console conversions.  Targets are limited to formats
        // for which PSM has a verified writer, not merely a recognized extension.
        var outputs = UniversalFormats.Where(format => format.CanWrite &&
            (isPs1Card
                ? format.Extension is ".mcr" or ".srm" or ".bin" or ".mcd" or ".mc" or ".gme" or ".vm1"
                : isPs1Package
                    ? format.Extension is ".mcr" or ".srm" or ".bin" or ".mcd" or ".mc" or ".gme" or ".vm1"
                    : isPs2Card
                        ? format.Extension is ".mc2" or ".ps2" or ".foldercard"
                        : format.Extension is ".psu" or ".max" or ".mc2" or ".ps2" or ".foldercard"))
            .ToArray();

        UniversalTargetFormat.ItemsSource = outputs;
        var preferred = isPs1Card || isPs1Package
            ? (extension == ".mcr" ? ".srm" : ".mcr")
            : isPs2Card
                ? extension == ".foldercard"
                    ? ".ps2"
                    : (extension == ".mc2" ? ".ps2" : ".mc2")
                : ".psu";
        UniversalTargetFormat.SelectedItem =
            outputs.FirstOrDefault(format => format.Extension == preferred) ?? outputs.FirstOrDefault();

        if (!sourceFormat.CanRead)
        {
            UniversalConversionReport.Text =
                $"{sourceFormat.DisplayName} is recognized, but its safe parser is not yet integrated. " +
                "PSM will not rewrite it until the legacy adapter can preserve every file and attribute.";
            UniversalConvertButton.IsEnabled = false;
        }
        else
        {
            UniversalConversionReport.Text = isCard
                ? "Only same-console memory-card targets are shown. Every output is verified before it is committed."
                : extension == ".ps1save"
                    ? "This individual PS1 save can be exported as a verified single-save PS1 memory card."
                    : "Only compatible PS2 outputs are shown. The package is imported into a temporary card and verified before export.";
            UniversalConvertButton.IsEnabled = UniversalTargetFormat.SelectedItem is not null;
        }
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

    private async void UniversalConvert_Click(object sender, RoutedEventArgs e)
    {
        if (_universalSourcePath is null ||
            (!File.Exists(_universalSourcePath) &&
             !Directory.Exists(_universalSourcePath)) ||
            UniversalTargetFormat.SelectedItem is not UniversalFormatOption target ||
            string.IsNullOrWhiteSpace(UniversalOutputPath.Text))
            return;

        var source = _universalSourcePath;
        var output = UniversalOutputPath.Text;
        var sourceExtension =
            Directory.Exists(source)
                ? ".foldercard"
                : Path.GetExtension(source).ToLowerInvariant();

        var sourceFormat =
            UniversalFormats.First(
                format =>
                    format.Extension == sourceExtension);

        var rawPs1Extensions =
            new[] { ".mcr", ".srm", ".bin", ".mcd", ".mc", ".gme", ".vm1" };

        if (sourceExtension == ".ps1save" &&
            rawPs1Extensions.Contains(target.Extension))
        {
            try
            {
                SetBusy(true, "Creating single-save PS1 memory card...");
                await _ps1CardService.CreateSingleSaveCardFromPackageAsync(
                    source, output);
                UniversalConversionReport.Text =
                    "PS1 save exported to a verified single-save memory card.";
                MessageBox.Show(
                    "PS1 save converted and verified.",
                    "Conversion Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UniversalConversionReport.Text = ex.Message;
                MessageBox.Show(ex.Message, "PS1 Conversion Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { SetBusy(false, "Ready."); }
            return;
        }

        if (rawPs1Extensions.Contains(sourceExtension) &&
            rawPs1Extensions.Contains(target.Extension))
        {
            try
            {
                SetBusy(true, "Converting PS1 memory card...");

                await _ps1CardService.SaveCardAsAsync(
                    source,
                    output);

                UniversalConversionReport.Text =
                    "PS1 memory card converted and verified successfully.";

                MessageBox.Show(
                    "PS1 memory card converted and verified.",
                    "Conversion Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UniversalConversionReport.Text = ex.Message;

                MessageBox.Show(
                    ex.Message,
                    "PS1 Conversion Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, "Ready.");
            }

            return;
        }

        if (Path.GetFullPath(source).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("The output path must differ from the source.", "Universal Converter",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "PSM-UNIVERSAL-" + Guid.NewGuid().ToString("N"));
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
            AppendUniversalLog($"Detected: {sourceFormat.DisplayName}");
            AppendUniversalLog($"Target: {target.DisplayName}");

            if (sourceExtension is ".mc2" or ".ps2" or ".foldercard")
                await ConvertUniversalCardAsync(source, output, target, tempRoot);
            else
                await ConvertUniversalPackageAsync(source, output, target, tempRoot);

            VerifiedText.Text = $"UNIVERSAL CONVERSION VERIFIED - {Path.GetFileName(output)}";
            VerifiedBanner.Visibility = Visibility.Visible;
            UniversalConversionReport.Text =
                $"CONVERSION VERIFIED\n\nSource: {Path.GetFileName(source)}\n" +
                $"Output: {Path.GetFileName(output)}\n\n" +
                $"Output adapter: {target.DisplayName}\nOriginal source preserved\nVerification passed";
            Log($"Universal conversion verified: {source} -> {output}");
            MessageBox.Show($"Conversion completed and verified.\n\nOutput:\n{output}",
                "Universal Conversion Verified", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UniversalConversionReport.Text =
                "Conversion failed safely.\n\nThe source was not modified.\n\n" + ex.Message;
            AppendUniversalLog("ERROR: " + ex);
            Log("Universal conversion failed: " + ex.Message);
            MessageBox.Show(ex.Message, "Universal Conversion Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (target.Extension is not ".mc2" and
            not ".ps2" and
            not ".foldercard")
        {
            throw new NotSupportedException(
                "Complete cards currently convert to MC2, PS2, or PCSX2 Folder Card.");
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

        var sourceSaves = await _engine.ReadDirectoryAsync(source);
        AppendUniversalLog($"Source contains {sourceSaves.Count} saves.");
        var temporaryCard = Path.Combine(tempRoot, "converted" + target.Extension);
        await _engine.CreateCardAsync(temporaryCard, target.Extension == ".mc2");

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

        if (target.Extension is ".mc2" or ".ps2" or ".foldercard")
        {
            if (target.Extension == ".ps2")
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

        if (target.Extension is ".psu" or ".max")
        {
            var package = Path.Combine(tempRoot, "converted" + target.Extension);
            await _engine.ExportPackageAsync(temporaryCard, save.DirectoryId, package);
            if (!File.Exists(package) || new FileInfo(package).Length == 0)
                throw new InvalidOperationException("The output package was not created correctly.");
            CommitUniversalOutput(package, output);
            return;
        }

        throw new NotSupportedException($"{target.DisplayName} is currently input-only.");
    }

    private void CommitUniversalOutput(string temporaryOutput, string destination)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(destination))
        {
            var backup = CreateBackup(destination);
            AppendUniversalLog($"Existing destination backed up: {backup}");
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

    private async void AddLibraryA_Click(object sender, RoutedEventArgs e) =>
        await ShowStoreLibraryChoiceAsync(_pathA, CardAList.SelectedItem as SaveEntry, null, 'A', false);

    private async void AddLibraryB_Click(object sender, RoutedEventArgs e) =>
        await ShowStoreLibraryChoiceAsync(_pathB, CardBList.SelectedItem as SaveEntry, null, 'B', false);

    private async Task ShowStoreLibraryChoiceAsync(
        string? cardPath, SaveEntry? ps2Save, Ps1SaveEntry? ps1Save,
        char side, bool isPs1)
    {
        if (cardPath is null) return;

        var choice = ShowNewCardTypeDialog(
            "ADD TO SAVE LIBRARY",
            "Store an individual save or preserve the complete memory card.",
            new[]
            {
                new CardChoice(FindResource("IconStoreCard") as ImageSource,
                    "Store Save", "Export and store the selected game save.", 1),
                new CardChoice(FindResource("IconStoreSave") as ImageSource,
                    "Store Card", "Copy the complete memory card into the library.", 2)
            },
            "Add to Save Library");

        if (choice == 1)
        {
            if (isPs1)
            {
                if (ps1Save is null)
                {
                    MessageBox.Show("Select a PS1 save first.","Store Save",
                        MessageBoxButton.OK,MessageBoxImage.Information);
                    return;
                }
                await AddPs1SaveToLibraryAsync(cardPath,ps1Save);
            }
            else
            {
                if (ps2Save is null)
                {
                    MessageBox.Show("Select a PS2 save first.","Store Save",
                        MessageBoxButton.OK,MessageBoxImage.Information);
                    return;
                }
                await AddCardSaveToLibraryAsync(cardPath,ps2Save,side);
            }
        }
        else if (choice == 2)
        {
            await StoreMemoryCardInLibraryAsync(cardPath,isPs1,side);
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
                    cardType="PCSX2 Folder Memory Card";
                    capacity=null;
                }
                else
                {
                    cardType=Path.GetExtension(cardPath).Equals(".mc2",StringComparison.OrdinalIgnoreCase)
                        ? "MemCard PRO2 Memory Card" : "Standard PS2 Memory Card";
                    capacity=(await _engine.ReadCardAsync(cardPath)).TotalBytes;
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
        extension.ToLowerInvariant() switch
        {
            ".mcr" => "ePSXe / PSEmu Pro Memory Card",
            ".srm" => "RetroArch / Libretro PS1 Memory Card",
            ".bin" => "pSX / AdriPSX Memory Card",
            ".mcd" => "Bleem! Memory Card",
            ".mc" => "PSXGame Edit Memory Card",
            ".gme" => "DexDrive Memory Card",
            ".vm1" => "PS3 Virtual Memory Card",
            _ => "Standard PS1 Memory Card"
        };

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

        LibraryMetaSerialLabel.Text = "Game Serial";
        LibraryMetaCrc32Label.Visibility = Visibility.Visible;
        LibraryMetaCrc32.Visibility = Visibility.Visible;
        LibraryMetaHashLabel.Visibility = Visibility.Visible;
        LibraryMetaHash.Visibility = Visibility.Visible;
        LibraryExportButton.Visibility = Visibility.Visible;
        LibraryExportCardButton.Visibility = Visibility.Visible;
        LibraryInfoButton.Visibility = Visibility.Visible;
        LibraryRenameButton.Visibility = Visibility.Collapsed;
        LibraryRenameButton.IsEnabled = false;
        LibrarySlotAButtonText.Text = "Add to Card A";
        LibrarySlotBButtonText.Text = "Add to Card B";

        UpdateSaveLibraryMetadata(
            SaveLibraryList.SelectedItem as SaveLibraryEntry);
        UpdateLibrarySummary();
    }

    private void ShowMemoryCardLibraryMode()
    {
        _saveLibraryContentMode =
            SaveLibraryContentMode.MemoryCards;

        SaveLibraryList.SelectedItem = null;
        SaveLibraryList.Visibility = Visibility.Collapsed;
        MemoryCardLibraryList.Visibility = Visibility.Visible;
        LibrarySearchBox.IsEnabled = false;
        LibraryFilterButton.IsEnabled = true;
        LibraryMetadataHeading.Text = "MEMORY CARD METADATA";

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
        LibraryFavoriteButtonText.Text = "Add Favorite";
        LibraryFavoriteButton.IsEnabled = false;
        LibraryExportButton.IsEnabled = false;
        LibraryExportCardButton.IsEnabled = false;
        LibraryInfoButton.IsEnabled = false;
        LibrarySlotAButton.IsEnabled = false;
        LibrarySlotBButton.IsEnabled = false;
        LibraryRenameButton.IsEnabled = false;
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
        LibraryMetaFormat.Text = entry.CardType;
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
        LibraryRemoveButton.IsEnabled = true;
    }

    private async void LibraryRename_Click(
        object sender,
        RoutedEventArgs e)
    {
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
                "IMPORT MEMORY CARD",
                "Choose a supported memory-card file or a PCSX2 folder card.",
                "Import Memory Card");

        if (choice == 0)
            return;

        string cardPath;

        if (choice == 2)
        {
            var folderDialog =
                new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Choose PCSX2 Folder Memory Card",
                    Multiselect = false
                };

            if (folderDialog.ShowDialog() != true)
                return;

            cardPath = folderDialog.FolderName;

            if (!File.Exists(
                Path.Combine(
                    cardPath,
                    "_pcsx2_superblock")))
            {
                MessageBox.Show(
                    "That folder does not contain _pcsx2_superblock.",
                    "Not a PCSX2 Folder Card",
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
                    Title = "Choose Memory Card",
                    Multiselect = false,
                    Filter =
                        "Supported memory cards|" +
                        "*.ps2;*.mc2;*.mcr;*.srm;*.bin;*.mcd;*.mc;*.gme;*.vm1|" +
                        "PS2 memory cards|*.ps2;*.mc2|" +
                        "PS1 memory cards|*.mcr;*.srm;*.bin;*.mcd;*.mc;*.gme;*.vm1|" +
                        "All files|*.*"
                };

            if (fileDialog.ShowDialog() != true)
                return;

            cardPath = fileDialog.FileName;
        }

        await ImportMemoryCardIntoLibraryAsync(
            cardPath);
    }

    private async Task ImportMemoryCardIntoLibraryAsync(
        string cardPath)
    {
        try
        {
            SetBusy(
                true,
                $"Importing {Path.GetFileName(cardPath)} into the Memory Card Library...");

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
                extension is ".ps2" or ".mc2")
            {
                platform = "PlayStation 2";

                var result =
                    await _engine.ReadCardAsync(
                        cardPath);

                saveCount = result.Saves.Count;

                if (isFolderCard)
                {
                    cardType =
                        "PCSX2 Folder Memory Card";
                    capacity = null;
                }
                else
                {
                    cardType =
                        extension == ".mc2"
                            ? "MemCard PRO2 Memory Card"
                            : "Standard PS2 Memory Card";

                    capacity = result.TotalBytes;
                }
            }
            else if (extension is
                ".mcr" or ".srm" or ".bin" or ".mcd" or ".mc" or ".gme" or ".vm1")
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
                    capacity);

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
        catch (Exception ex)
        {
            LibraryFooterStatus.Text =
                "Memory-card import failed: " +
                ex.Message;

            Log(
                "Memory Card Library import failed: " +
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "Import Card Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "Ready.");
        }
    }

    private async void LibraryImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter =
                "Packaged PlayStation saves|*.ps1save;*.psu;*.max;*.cbs;*.xps;*.sps;*.psv|" +
                "Packaged PS1 saves|*.ps1save|" +
                "Packaged PS2 saves|*.psu;*.max;*.cbs;*.xps;*.sps;*.psv|" +
                "All files|*.*"
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

                var result = await _saveLibraryService.ImportAsync(
                    path,
                    _saveLibraryIndex);

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
        LibraryRenameButton.IsEnabled = false;
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
            LibraryMetaSize.Text = "—";
            LibraryMetaAdded.Text = "—";
            LibraryMetaModified.Text = "—";
            LibraryMetaCrc32.Text = "—";
            LibraryMetaHash.Text = "—";
            LibraryDuplicateStatus.Text = "—";
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

        LibraryMetaFormat.Text = entry.FormatName;
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

        var duplicates = _saveLibraryIndex.Entries
            .Where(candidate =>
                !ReferenceEquals(candidate, entry) &&
                candidate.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        LibraryDuplicateStatus.Text =
            duplicates.Length == 0
                ? "No exact duplicates detected."
                : $"{duplicates.Length} exact duplicate(s) detected.";
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
                    $"{result.Saves.Count(save => !save.IsDeleted)} active saves";
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
                    $"{result.Saves.Count(save => !save.IsDeleted)} active saves";
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
            Ps1PreviewAStatus.Text =
                save is null
                    ? string.Empty
                    : $"{save.Status} • {save.FileName}";
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
            Ps1PreviewBStatus.Text =
                save is null
                    ? string.Empty
                    : $"{save.Status} • {save.FileName}";
        }

        RefreshButtons();
    }

    private async void CopyPs1AToB_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathA is not null &&
            _ps1PathB is not null &&
            Ps1CardAList.SelectedItem is Ps1SaveEntry save)
        {
            await CopyPs1SaveAsync(
                _ps1PathA,
                save,
                _ps1PathB,
                'B');
        }
    }

    private async void CopyPs1BToA_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathB is not null &&
            _ps1PathA is not null &&
            Ps1CardBList.SelectedItem is Ps1SaveEntry save)
        {
            await CopyPs1SaveAsync(
                _ps1PathB,
                save,
                _ps1PathA,
                'A');
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
                "A timestamped backup of the destination card was created.",
                "PS1 Transfer Verified",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log("PS1 transfer failed: " + ex.Message);
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

    private async void DeletePs1A_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathA is not null &&
            Ps1CardAList.SelectedItem is Ps1SaveEntry save)
        {
            await DeletePs1SaveAsync(
                _ps1PathA,
                save,
                'A');
        }
    }

    private async void DeletePs1B_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ps1PathB is not null &&
            Ps1CardBList.SelectedItem is Ps1SaveEntry save)
        {
            await DeletePs1SaveAsync(
                _ps1PathB,
                save,
                'B');
        }
    }

    private async Task DeletePs1SaveAsync(
        string cardPath,
        Ps1SaveEntry save,
        char side)
    {
        var confirmation =
            MessageBox.Show(
                $"Delete {save.Title}?\n\n" +
                $"{save.FileName}\n" +
                $"{save.BlocksDisplay}\n\n" +
                "PSM will create a timestamped backup first.",
                "Delete PS1 Save",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(
                true,
                $"Deleting {save.Title}...");

            await _ps1CardService.DeleteSaveAsync(
                cardPath,
                save);

            await LoadPs1CardAsync(
                cardPath,
                side);

            Log(
                $"PS1 save deleted and verified: {save.FileName}");

            MessageBox.Show(
                "The PS1 save was deleted and verified.\n\n" +
                "A timestamped backup was created.",
                "PS1 Delete Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log(
                "PS1 delete failed: " +
                ex.Message);

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
        var safeTitle = SanitizeUniversalFileName(
            string.IsNullOrWhiteSpace(save.Title)
                ? save.ProductCode
                : save.Title);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export PS1 Save",
            Filter =
                "PSM PlayStation Save Package (*.ps1save)|*.ps1save|" +
                "ePSXe / PSEmu Pro Memory Card (*.mcr)|*.mcr|" +
                "RetroArch / Libretro Memory Card (*.srm)|*.srm|" +
                "pSX / AdriPSX Memory Card (*.bin)|*.bin|" +
                "Bleem! Memory Card (*.mcd)|*.mcd|" +
                "PSXGame Edit Memory Card (*.mc)|*.mc|" +
                "PS3 Virtual Memory Card (*.vm1)|*.vm1",
            DefaultExt = ".ps1save",
            FileName = safeTitle + ".ps1save"
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

    private async void AddLibraryPs1A_Click(object sender, RoutedEventArgs e) =>
        await ShowStoreLibraryChoiceAsync(_ps1PathA, null, Ps1CardAList.SelectedItem as Ps1SaveEntry, 'A', true);

    private async void AddLibraryPs1B_Click(object sender, RoutedEventArgs e) =>
        await ShowStoreLibraryChoiceAsync(_ps1PathB, null, Ps1CardBList.SelectedItem as Ps1SaveEntry, 'B', true);

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

    private void Ps1Cards_DragOver(
        object sender,
        DragEventArgs e)
    {
        e.Effects =
            e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Ps1Cards_Drop(
        object sender,
        DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        foreach (var path in files.Take(2))
        {
            if (!Ps1MemoryCardService.LooksLikeSupportedCard(path))
                continue;

            if (_ps1PathA is null)
                await LoadPs1CardAsync(path, 'A');
            else
                await LoadPs1CardAsync(path, 'B');
        }

        e.Handled = true;
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
            string.IsNullOrWhiteSpace(entry.Platform)
                ? entry.FormatName
                : $"{entry.Platform} • {entry.FormatName}";
        SaveInfoSize.Text = entry.SizeDisplay;
        SaveInfoAdded.Text =
            entry.AddedUtc.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
        SaveInfoModified.Text =
            entry.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
        SaveInfoSha256.Text = entry.Sha256;

        var duplicateCount = _saveLibraryIndex.Entries.Count(candidate =>
            !ReferenceEquals(candidate, entry) &&
            candidate.Sha256.Equals(
                entry.Sha256,
                StringComparison.OrdinalIgnoreCase));

        SaveInfoDuplicate.Text = duplicateCount == 0
            ? "No exact duplicates detected."
            : $"{duplicateCount} exact duplicate(s) detected.";

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
            if (sourceExtension is ".mc2" or ".ps2" or ".foldercard")
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
            if (MemoryCardLibraryList.SelectedItem is not
                MemoryCardLibraryEntry cardEntry)
            {
                return;
            }

            try
            {
                await _memoryCardLibraryService
                    .ToggleFavoriteAsync(
                        cardEntry,
                        _memoryCardLibraryIndex);

                RefreshMemoryCardLibraryView();
                MemoryCardLibraryList.Items.Refresh();
                MemoryCardLibraryList.SelectedItem =
                    cardEntry;
                MemoryCardLibraryList.ScrollIntoView(
                    cardEntry);

                LibraryFavoriteButtonText.Text =
                    cardEntry.IsFavorite
                        ? "Remove Favorite"
                        : "Add Favorite";

                LibraryFooterStatus.Text =
                    cardEntry.IsFavorite
                        ? $"Added {cardEntry.DisplayName} to favorites."
                        : $"Removed {cardEntry.DisplayName} from favorites.";
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
                    "Create a standard .ps2 or MemCard PRO2 .mc2 memory-card image.",
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
            Filter = "PCSX2 Memory Card (*.ps2)|*.ps2|MemCard PRO2 Memory Card (*.mc2)|*.mc2",
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
                    await _engine.CreateCardAsync(candidate, sizeMb);
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
            var backup = folder ? CreateFolderBackup(destination) : CreateBackup(destination);
            if (folder)
            {
                Directory.Delete(destination, true);
                CopyDirectory(temporaryCard, destination);
            }
            else File.Copy(temporaryCard, destination, true);
            await LoadCardAsync(destination, side, entries.Last().DirectoryId, allowWhileBusy: true);
            Ps2MemoryCardsTab.IsSelected = true;
            MessageBox.Show($"{entries.Count} save(s) were added and verified.\n\nBackup:\n{backup}",
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
            await _ps1CardService.BackupAsync(destination);
            File.Copy(temporaryCard, destination, true);
            await LoadPs1CardAsync(destination, side);
            Ps1MemoryCardsTab.IsSelected = true;
            MessageBox.Show($"{entries.Count} PS1 save(s) were added and verified.\n\nA timestamped backup was created.",
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
            ? "PSM PlayStation Save Package (*.ps1save)|*.ps1save|" +
              "ePSXe / PSEmu Pro Memory Card (*.mcr)|*.mcr|" +
              "RetroArch / Libretro Memory Card (*.srm)|*.srm|" +
              "pSX / AdriPSX Memory Card (*.bin)|*.bin|" +
              "Bleem! Memory Card (*.mcd)|*.mcd|" +
              "PSXGame Edit Memory Card (*.mc)|*.mc|" +
              "PS3 Virtual Memory Card (*.vm1)|*.vm1"
            : "Original Save Package|*" + entry.Extension + "|" +
              "EMS / Memory Linker PSU (*.psu)|*.psu|" +
              "Action Replay MAX (*.max)|*.max|" +
              "PCSX2 Memory Card (*.ps2)|*.ps2|" +
              "MemCard PRO2 Memory Card (*.mc2)|*.mc2";

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Save",
            Filter = filter,
            DefaultExt = entry.Extension,
            FileName = entry.OriginalFileName
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
                await _ps1CardService.CreateSingleSaveCardFromPackageAsync(
                    storedPath, destinationPath);
            }
            else if (destinationExtension is ".ps2" or ".mc2")
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
            if (MemoryCardLibraryList.SelectedItem is not
                MemoryCardLibraryEntry cardEntry)
            {
                return;
            }

            var cardConfirmation =
                MessageBox.Show(
                    $"Remove {cardEntry.DisplayName} from the Memory Card Library?\n\n" +
                    "This permanently removes the library copy only. " +
                    "The original memory card is not changed.",
                    "Remove Library Memory Card",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (cardConfirmation !=
                MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await _memoryCardLibraryService.RemoveAsync(
                    cardEntry,
                    _memoryCardLibraryIndex);

                RefreshMemoryCardLibraryView();
                ResetMemoryCardLibraryMetadata();
                UpdateLibrarySummary();

                LibraryFooterStatus.Text =
                    "Memory card removed from library.";

                Log(
                    $"Memory Card Library removed: " +
                    $"{cardEntry.DisplayName}");
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
            Filter =
                "Supported PlayStation saves and cards|*.psu;*.max;*.cbs;*.xps;*.sps;*.psv;*.npo;*.p2m;*.mc2;*.ps2;*.mcr;*.srm;*.bin;*.mcd;*.mc;*.gme;*.mem;*.vgs;*.ddf;*.ps;*.psm;*.mci;*.vmp;*.vm1|" +
                "PS1 memory cards|*.mcr;*.srm;*.bin;*.mcd;*.mc;*.gme;*.mem;*.vgs;*.ddf;*.ps;*.psm;*.mci;*.vmp;*.vm1|" +
                "Packaged PS2 saves|*.psu;*.max;*.cbs;*.xps;*.sps;*.psv;*.npo;*.p2m|" +
                "PS2 memory cards|*.mc2;*.ps2|All files|*.*"
        };

        if (dialog.ShowDialog() == true)
            SelectImportWizardSource(dialog.FileName);
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
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectImportWizardSource(files[0]);
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

        var extension =
            folderSave
                ? ".foldersave"
                : folderCard
                    ? ".foldercard"
                    : Path.GetExtension(path).ToLowerInvariant();
        var format =
            folderSave
                ? new UniversalFormatOption(
                    ".psu",
                    "PCSX2 Folder Save",
                    false,
                    true)
                : folderCard
                    ? new UniversalFormatOption(
                        ".foldercard",
                        "PCSX2 Folder Memory Card",
                        true,
                        true)
                    : UniversalFormats.FirstOrDefault(
                        candidate => candidate.Extension == extension);

        _wizardSourcePath = path;
        _wizardSourceIsFolderSave = folderSave;
        _wizardFolderCardPath = folderSave ? folderCardPath : null;
        _wizardFolderSaveId = folderSave ? folderSaveId : null;
        _wizardSourceIsPs1Card =
            !folderSave &&
            extension is ".mcr" or ".srm" or ".bin" or ".mcd" or ".mc" or ".gme" or ".vm1";
        _wizardSourceIsCard =
            folderCard ||
            _wizardSourceIsPs1Card ||
            extension is ".mc2" or ".ps2";
        _wizardSourceIsReadablePackage =
            folderSave ||
            (!_wizardSourceIsCard &&
             extension is ".psu" or ".max" or ".cbs" or ".xps" or ".sps" or ".psv");

        WizardFilePanel.Visibility = Visibility.Visible;
        WizardFileName.Text = Path.GetFileName(path);
        WizardFilePath.Text = path;
        WizardFileDetails.Text =
            folderSave
                ? $"PCSX2 Folder Save  •  {folderSaveId}"
                : folderCard
                    ? "PCSX2 Folder Memory Card  •  Folder"
                    : format is null
                        ? $"Unknown format  •  {FormatBytes(new FileInfo(path).Length)}"
                        : $"{format.DisplayName}  •  {FormatBytes(new FileInfo(path).Length)}";

        WizardDropTitle.Text = folderSave ? "Folder save detected" : "File detected";
        WizardDropSubtitle.Text = "Choose an action from the panel on the right.";

        if (format is null)
        {
            WizardDetectedType.Text = $"Unsupported file: {extension.ToUpperInvariant()}";
            WizardExplanation.Text =
                "This file is not one of the supported save or memory-card formats.";
            SetWizardActions(false, false, false, false);
            return;
        }

        if (folderSave)
        {
            WizardDetectedType.Text =
                $"PCSX2 folder-card save detected: {folderSaveId}";
            WizardCardAButton.Content = "Import into Card A";
            WizardCardBButton.Content = "Import into Card B";
            WizardExplanation.Text =
                "PSM can repackage this PCSX2 directory-ID folder as a standard PSU save, " +
                "import it into either loaded PS2 card, send it to Universal Converter, " +
                "or add it directly to the Save Library.";
            SetWizardActions(_pathA is not null, _pathB is not null, true, true);
        }
        else if (_wizardSourceIsCard)
        {
            WizardDetectedType.Text = $"Complete memory card detected: {format.DisplayName}";
            WizardCardAButton.Content = "Open as Card A";
            WizardCardBButton.Content = "Open as Card B";
            WizardExplanation.Text =
                "This is a complete memory card. You can open it directly, convert it, " +
                "or preserve it in the Memory Card Library.";
            SetWizardActions(true, true, true, true);
        }
        else if (_wizardSourceIsReadablePackage)
        {
            WizardDetectedType.Text = $"Packaged save detected: {format.DisplayName}";
            WizardCardAButton.Content = "Import into Card A";
            WizardCardBButton.Content = "Import into Card B";
            WizardExplanation.Text =
                "This packaged save can be safely imported into either loaded card, " +
                "converted to another verified format, or added to the Save Library.";
            SetWizardActions(_pathA is not null, _pathB is not null, true, true);
        }
        else
        {
            WizardDetectedType.Text = $"Legacy format recognized: {format.DisplayName}";
            WizardCardAButton.Content = "Import into Card A";
            WizardCardBButton.Content = "Import into Card B";
            WizardExplanation.Text =
                "PSM recognizes this legacy format, but its safe parser is not integrated yet. " +
                "The file can be sent to Universal Converter to view its current adapter status.";
            SetWizardActions(false, false, true, false);
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
            ".PSU  •  .MAX  •  .CBS  •  .XPS  •  .SPS  •  .PSV  •  .NPO  •  .P2M  •  .MC2  •  .PS2  •  .MCR  •  .SRM  •  .BIN  •  .MCD  •  .MC  •  .GME  •  .MEM  •  .VGS  •  .DDF  •  .PS  •  .PSM  •  .MCI  •  .VMP  •  .VM1";
        WizardDetectedType.Text = "Choose a file to see available actions.";
        WizardExplanation.Text = "No source selected.";
        WizardCardAButton.Content = "Import into Card A";
        WizardCardBButton.Content = "Import into Card B";
        SetWizardActions(false, false, false, false);
    }

    private async void WizardCardA_Click(object sender, RoutedEventArgs e)
    {
        if (_wizardSourcePath is null) return;

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
        if (_wizardSourcePath is null) return;

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
                        cardType = Path.GetExtension(_wizardSourcePath)
                            .Equals(".mc2", StringComparison.OrdinalIgnoreCase)
                                ? "MemCard PRO2 Memory Card"
                                : "Standard PS2 Memory Card";
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
            Filter = "PS2 save packages|*.psu;*.max;*.cbs;*.sps;*.xps;*.psv|All files|*.*"
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

    private async Task ImportPackageAsync(string destination, char destinationSide)
    {
        if (_selectedPackagePath is null) return;
        var packageName = Path.GetFileName(_selectedPackagePath);
        var confirm = MessageBox.Show(
            $"Import {packageName}\n\ninto {Path.GetFileName(destination)}?",
            "Confirm Import", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "PSAM-Import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            SetBusy(true, $"Importing {packageName}...");
            VerifiedBanner.Visibility = Visibility.Collapsed;
            var before = await _engine.ReadDirectoryAsync(destination);
            var temporaryCard = Path.Combine(temporaryDirectory, Path.GetFileName(destination));
            File.Copy(destination, temporaryCard, true);

            Log($"Importing package into temporary destination: {_selectedPackagePath}");
            await _engine.ImportAsync(temporaryCard, _selectedPackagePath);
            Log("Verifying temporary destination card.");
            await _engine.CheckAsync(temporaryCard);
            var after = await _engine.ReadDirectoryAsync(temporaryCard);

            var imported = after.FirstOrDefault(candidate => !before.Any(old => old.DirectoryId.Equals(candidate.DirectoryId, StringComparison.OrdinalIgnoreCase)));
            if (imported is null && after.Count > 0)
                imported = after.OrderByDescending(save => save.SizeBytes).FirstOrDefault();

            var backup = CreateBackup(destination);
            File.Copy(temporaryCard, destination, true);
            Log($"Package import committed. Backup: {backup}");

            await LoadCardAsync(destination, destinationSide, imported?.DirectoryId, allowWhileBusy: true);
            var displayName = imported?.Title ?? packageName;
            VerifiedText.Text = $"IMPORT VERIFIED - {displayName} added successfully";
            VerifiedBanner.Visibility = Visibility.Visible;
            LibraryFooterStatus.Text = $"Import verified. {displayName} was added and the destination card was rescanned.";
            StatusText.Text = "Import verified.";
            MessageBox.Show($"Save imported and verified successfully.\n\nBackup:\n{backup}", "Import Verified", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        SelectImportWizardSource(files[0]);
        MainTabs.SelectedItem = UniversalImportWizardTab;
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

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();


}
