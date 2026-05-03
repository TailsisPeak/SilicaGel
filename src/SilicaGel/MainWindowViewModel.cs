using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using Axion.Scripting;

namespace SilicaGel;

public sealed class FileEntry
{
    public string FullPath { get; init; } = "";
    public string DisplayName => Path.GetFileName(FullPath);
    public string Icon => Path.GetExtension(FullPath).ToLowerInvariant() switch
    {
        ".gel"  => "◆",
        ".sil"  => "◇",
        ".cs"   => "▣",
        ".json" => "≣",
        ".md"   => "¶",
        _       => "•",
    };
    public string LangBadge => Path.GetExtension(FullPath).TrimStart('.').ToLowerInvariant();
}

public enum SidebarMode { Explorer, Search, Extensions, SourceControl, Run }

public sealed class MainWindowViewModel : ViewModelBase
{
    private TopLevel? _topLevel;
    public AppSettings Settings { get; }
    public SettingsViewModel SettingsVm { get; }
    public ExtensionManager ExtMgr { get; }
    public ExtensionsViewModel ExtensionsVm { get; }

    public ObservableCollection<FileEntry>          Files { get; } = new();
    public ObservableCollection<EditorTabViewModel> Tabs  { get; } = new();

    /// <summary>
    /// Files queued by App.OnFrameworkInitializationCompleted from the process command
    /// line.  Drained by <see cref="InitializeAsync"/> after the TopLevel is wired up.
    /// </summary>
    public List<string> PendingOpenFiles { get; } = new();

    private string _root = Directory.GetCurrentDirectory();
    public string RootFolderDisplay => "📁  " + (_root.Length > 36 ? "…" + _root[^36..] : _root);

    private FileEntry? _selectedFile;
    public FileEntry? SelectedFile
    {
        get => _selectedFile;
        set { if (SetField(ref _selectedFile, value) && value != null) OpenInTab(value.FullPath); }
    }

    private EditorTabViewModel? _activeTab;
    public EditorTabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (_activeTab != null) _activeTab.DiagnosticsChanged -= RefreshProblems;
            if (SetField(ref _activeTab, value))
            {
                UpdateStatus();
                if (_activeTab != null) _activeTab.DiagnosticsChanged += RefreshProblems;
                RefreshProblems();
                SettingsOpen = false;
            }
        }
    }

    // ── UI mode flags ──────────────────────────────────────────────────────────
    private SidebarMode _sidebar = SidebarMode.Explorer;
    public SidebarMode Sidebar { get => _sidebar; set { if (SetField(ref _sidebar, value)) { OnPropertyChanged(nameof(SidebarIsExplorer)); OnPropertyChanged(nameof(SidebarIsSearch)); OnPropertyChanged(nameof(SidebarIsExtensions)); OnPropertyChanged(nameof(SidebarIsSourceControl)); OnPropertyChanged(nameof(SidebarIsRun)); OnPropertyChanged(nameof(SidebarTitle)); } } }
    public bool SidebarIsExplorer       => Sidebar == SidebarMode.Explorer;
    public bool SidebarIsSearch         => Sidebar == SidebarMode.Search;
    public bool SidebarIsExtensions     => Sidebar == SidebarMode.Extensions;
    public bool SidebarIsSourceControl  => Sidebar == SidebarMode.SourceControl;
    public bool SidebarIsRun            => Sidebar == SidebarMode.Run;
    public string SidebarTitle => Sidebar switch
    {
        SidebarMode.Explorer => "EXPLORER",
        SidebarMode.Search => "SEARCH",
        SidebarMode.Extensions => "EXTENSIONS",
        SidebarMode.SourceControl => "SOURCE CONTROL",
        SidebarMode.Run => "RUN AND DEBUG",
        _ => "",
    };

    private bool _settingsOpen;
    public bool SettingsOpen { get => _settingsOpen; set { if (SetField(ref _settingsOpen, value)) OnPropertyChanged(nameof(EditorVisible)); } }
    public bool EditorVisible => !SettingsOpen;

    private bool _problemsPanelOpen;
    public bool ProblemsPanelOpen { get => _problemsPanelOpen; set => SetField(ref _problemsPanelOpen, value); }

    public ObservableCollection<Diagnostic> Problems { get; } = new();
    public string ProblemsBadge
    {
        get
        {
            int e = 0, w = 0;
            foreach (var d in Problems)
                if (d.Severity == DiagnosticSeverity.Error) e++;
                else if (d.Severity == DiagnosticSeverity.Warning) w++;
            return $"⊘ {e}    ⚠ {w}";
        }
    }
    private void RefreshProblems()
    {
        Problems.Clear();
        if (_activeTab != null) foreach (var d in _activeTab.Diagnostics) Problems.Add(d);
        OnPropertyChanged(nameof(ProblemsBadge));
    }

    private string _console = "Welcome to Silica Gel.\n";
    public string ConsoleText { get => _console; set => SetField(ref _console, value); }

    private string _status = "Ready.";
    public string StatusBarText { get => _status; set => SetField(ref _status, value); }

    // ── File menu commands ────────────────────────────────────────────────────
    public ICommand NewFileCommand        { get; }
    public ICommand OpenFileCommand       { get; }
    public ICommand OpenFolderCommand     { get; }
    public ICommand SaveCommand           { get; }
    public ICommand SaveAsCommand         { get; }
    public ICommand SaveAllCommand        { get; }
    public ICommand CloseTabCommand       { get; }
    public ICommand CloseAllTabsCommand   { get; }
    public ICommand ExitCommand           { get; }

    // ── Edit menu commands ────────────────────────────────────────────────────
    public ICommand UndoCommand           { get; }
    public ICommand RedoCommand           { get; }
    public ICommand CutCommand            { get; }
    public ICommand CopyCommand           { get; }
    public ICommand PasteCommand          { get; }
    public ICommand SelectAllCommand      { get; }
    public ICommand FindCommand           { get; }

    // ── View menu commands ────────────────────────────────────────────────────
    public ICommand ToggleProblemsCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ShowExplorerCommand   { get; }
    public ICommand ShowSearchCommand     { get; }
    public ICommand ShowExtensionsCommand { get; }
    public ICommand ShowSourceControlCommand { get; }
    public ICommand ShowRunCommand        { get; }
    public ICommand ZoomInCommand         { get; }
    public ICommand ZoomOutCommand        { get; }
    public ICommand ResetZoomCommand      { get; }
    public ICommand ToggleWordWrapCommand { get; }

    // ── Run / Convert / Window menu ───────────────────────────────────────────
    public ICommand RunCommand            { get; }
    public ICommand ClearConsoleCommand   { get; }
    public ICommand ConvertToGelCommand   { get; }
    public ICommand ConvertToSilicaCommand{ get; }
    public ICommand ConvertToCSharpCommand{ get; }
    public ICommand ReloadExtensionsCommand{ get; }
    public ICommand AboutCommand          { get; }

    public MainWindowViewModel()
    {
        Settings    = AppSettings.Load();
        SettingsVm  = new SettingsViewModel(Settings);
        ExtMgr      = new ExtensionManager(Settings, Log);
        ExtensionsVm= new ExtensionsViewModel(ExtMgr);

        NewFileCommand          = ReactiveCommand.Create(NewFile);
        OpenFileCommand         = ReactiveCommand.CreateFromTask(OpenFileAsync);
        OpenFolderCommand       = ReactiveCommand.CreateFromTask(OpenFolderAsync);
        SaveCommand             = ReactiveCommand.Create(SaveActive);
        SaveAsCommand           = ReactiveCommand.CreateFromTask(SaveAsAsync);
        SaveAllCommand          = ReactiveCommand.Create(SaveAll);
        CloseTabCommand         = ReactiveCommand.Create(CloseActiveTab);
        CloseAllTabsCommand     = ReactiveCommand.Create(() => { while (Tabs.Count > 0) Tabs.RemoveAt(0); ActiveTab = null; });
        ExitCommand             = ReactiveCommand.Create(() => Environment.Exit(0));

        UndoCommand             = ReactiveCommand.Create(() => CodeEditorView.Active?.Undo());
        RedoCommand             = ReactiveCommand.Create(() => CodeEditorView.Active?.Redo());
        CutCommand              = ReactiveCommand.Create(() => CodeEditorView.Active?.Cut());
        CopyCommand             = ReactiveCommand.Create(() => CodeEditorView.Active?.Copy());
        PasteCommand            = ReactiveCommand.Create(() => CodeEditorView.Active?.Paste());
        SelectAllCommand        = ReactiveCommand.Create(() => CodeEditorView.Active?.SelectAll());
        FindCommand             = ReactiveCommand.Create(() =>
        {
            if (CodeEditorView.Active == null) return;
            try { AvaloniaEdit.Search.SearchPanel.Install(CodeEditorView.Active).Open(); }
            catch (Exception ex) { Log("Find unavailable: " + ex.Message); }
        });

        ToggleProblemsCommand   = ReactiveCommand.Create(() => ProblemsPanelOpen = !ProblemsPanelOpen);
        ToggleSettingsCommand   = ReactiveCommand.Create(() => SettingsOpen = !SettingsOpen);
        ShowExplorerCommand     = ReactiveCommand.Create(() => Sidebar = SidebarMode.Explorer);
        ShowSearchCommand       = ReactiveCommand.Create(() => Sidebar = SidebarMode.Search);
        ShowExtensionsCommand   = ReactiveCommand.Create(() => Sidebar = SidebarMode.Extensions);
        ShowSourceControlCommand= ReactiveCommand.Create(() => Sidebar = SidebarMode.SourceControl);
        ShowRunCommand          = ReactiveCommand.Create(() => Sidebar = SidebarMode.Run);
        ZoomInCommand           = ReactiveCommand.Create(() => { Settings.FontSize = Math.Min(Settings.FontSize + 1, 48); Settings.Save(); SettingsVm.FontSize = Settings.FontSize; ApplyEditorSettings(); });
        ZoomOutCommand          = ReactiveCommand.Create(() => { Settings.FontSize = Math.Max(Settings.FontSize - 1, 8);  Settings.Save(); SettingsVm.FontSize = Settings.FontSize; ApplyEditorSettings(); });
        ResetZoomCommand        = ReactiveCommand.Create(() => { Settings.FontSize = 13; Settings.Save(); SettingsVm.FontSize = 13; ApplyEditorSettings(); });
        ToggleWordWrapCommand   = ReactiveCommand.Create(() => { Settings.WordWrap = !Settings.WordWrap; Settings.Save(); SettingsVm.WordWrap = Settings.WordWrap; ApplyEditorSettings(); });

        RunCommand              = ReactiveCommand.Create(RunActive);
        ClearConsoleCommand     = ReactiveCommand.Create(() => { ConsoleText = ""; OnPropertyChanged(nameof(ConsoleText)); });
        ConvertToGelCommand     = ReactiveCommand.Create(() => Convert(AxionLanguage.Gel));
        ConvertToSilicaCommand  = ReactiveCommand.Create(() => Convert(AxionLanguage.Silica));
        ConvertToCSharpCommand  = ReactiveCommand.Create(() => Convert(AxionLanguage.CSharp));
        ReloadExtensionsCommand = ReactiveCommand.Create(() => { ExtMgr.ReloadAll(); ExtensionsVm.Refresh(); Sidebar = SidebarMode.Extensions; });
        AboutCommand            = ReactiveCommand.Create(() => Log("Silica Gel — © Austin Shoop 2026.  IDE for the Axion engine (Gel, Silica, Blocks).  Standalone interpreter, VS Code-style UX."));

        SilicaGelHighlighting.Register();
        ExtMgr.ReloadAll();
        ExtensionsVm.Refresh();
    }

    private static void ApplyEditorSettings()
    {
        if (CodeEditorView.Active == null) return;
        var s = AppSettings.Load();
        CodeEditorView.Active.FontFamily = new Avalonia.Media.FontFamily(s.FontFamily);
        CodeEditorView.Active.FontSize   = s.FontSize;
        CodeEditorView.Active.WordWrap   = s.WordWrap;
        CodeEditorView.Active.ShowLineNumbers = s.LineNumbers;
        CodeEditorView.Active.Options.IndentationSize = s.TabSize;
    }

    public Task InitializeAsync(TopLevel topLevel)
    {
        _topLevel = topLevel;

        // 1. If we were launched with file arguments (e.g. by the Axion editor opening
        //    a .gel script), use the first file's directory as the project root and
        //    open every passed file in a tab.
        if (PendingOpenFiles.Count > 0)
        {
            var firstExisting = PendingOpenFiles.FirstOrDefault(File.Exists);
            if (firstExisting != null)
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(firstExisting));
                if (!string.IsNullOrEmpty(dir)) LoadFolder(dir);
            }
            foreach (var f in PendingOpenFiles)
                if (File.Exists(f)) OpenInTab(Path.GetFullPath(f));
            PendingOpenFiles.Clear();
            return Task.CompletedTask;
        }

        // 2. Otherwise fall back to discovering a sibling axion-engine/samples folder.
        foreach (var probe in new[]
        {
            Path.GetFullPath("../../axion-engine/samples"),
            Path.GetFullPath("../../../axion-engine/samples"),
            Path.GetFullPath("../../../../axion-engine/samples"),
            Path.GetFullPath("../../../../../axion-engine/samples"),
        })
            if (Directory.Exists(probe)) { LoadFolder(probe); break; }
        if (Files.Count == 0) LoadFolder(_root);
        return Task.CompletedTask;
    }

    private void LoadFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        _root = path;
        OnPropertyChanged(nameof(RootFolderDisplay));
        Files.Clear();
        IEnumerable<string> all = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\bin\\") && !f.Contains("/bin/") && !f.Contains("\\obj\\") && !f.Contains("/obj/"))
            .OrderBy(f => f);
        foreach (var f in all)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".gel" or ".sil" or ".cs" or ".json" or ".md" or ".txt")
                Files.Add(new FileEntry { FullPath = f });
        }
        StatusBarText = $"Loaded {Files.Count} file(s) from {path}";
    }

    private async Task OpenFolderAsync()
    {
        if (_topLevel == null) return;
        var result = await _topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (result.Count > 0) LoadFolder(result[0].Path.LocalPath);
    }

    private async Task OpenFileAsync()
    {
        if (_topLevel == null) return;
        var result = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Gel / Silica") { Patterns = new[] { "*.gel", "*.sil" } },
                new FilePickerFileType("All files")    { Patterns = new[] { "*" } },
            }
        });
        if (result.Count > 0) OpenInTab(result[0].Path.LocalPath);
    }

    private async Task SaveAsAsync()
    {
        if (_topLevel == null || ActiveTab == null) return;
        var result = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = Path.GetFileName(ActiveTab.FilePath),
        });
        if (result == null) return;
        File.WriteAllText(result.Path.LocalPath, ActiveTab.Document.Text);
        Log($"saved as {result.Path.LocalPath}");
    }

    private void NewFile()
    {
        int n = 1;
        while (Tabs.Any(t => t.FilePath == $"untitled-{n}.gel")) n++;
        var path = $"untitled-{n}.gel";
        var tab = new EditorTabViewModel(path,
            "# new Gel script\n\nfunc start() {\n    print(\"hello from silica gel\")\n}\n");
        WireTab(tab);
        Tabs.Add(tab); ActiveTab = tab;
    }

    private void OpenInTab(string path)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == path);
        if (existing != null) { ActiveTab = existing; return; }
        if (!File.Exists(path)) { Log("file not found: " + path); return; }
        var tab = new EditorTabViewModel(path, File.ReadAllText(path));
        WireTab(tab);
        Tabs.Add(tab); ActiveTab = tab;
        StatusBarText = "Opened " + Path.GetFileName(path);
    }

    private void WireTab(EditorTabViewModel tab) => tab.RequestClose += t =>
    {
        Tabs.Remove(t);
        if (ActiveTab == t) ActiveTab = Tabs.FirstOrDefault();
    };

    private void CloseActiveTab()
    {
        var t = ActiveTab; if (t == null) return;
        Tabs.Remove(t);
        ActiveTab = Tabs.FirstOrDefault();
    }

    private void SaveActive()
    {
        var t = ActiveTab; if (t == null) return;
        t.Save();
        Log($"saved {t.FilePath}");
        if (Settings.RunOnSave && (t.Language == "Gel" || t.Language == "Silica")) RunActive();
    }

    private void SaveAll()
    {
        foreach (var t in Tabs) t.Save();
        Log($"saved {Tabs.Count} file(s)");
    }

    private void Convert(AxionLanguage to)
    {
        var t = ActiveTab; if (t == null) { Log("no active tab"); return; }
        var from = t.Language switch
        {
            "Gel"    => AxionLanguage.Gel,
            "Silica" => AxionLanguage.Silica,
            _ => (AxionLanguage?)null,
        };
        if (from == null) { Log($"cannot convert {t.Language} → {to}"); return; }
        try
        {
            var output = LanguageConverter.Convert(t.Document.Text, from.Value, to);
            var ext = to switch { AxionLanguage.Gel => ".gel", AxionLanguage.Silica => ".sil", AxionLanguage.CSharp => ".cs", _ => ".txt" };
            var newPath = Path.ChangeExtension(t.FilePath, ext);
            var conv = new EditorTabViewModel(newPath, output);
            WireTab(conv);
            Tabs.Add(conv); ActiveTab = conv;
            Log($"converted {Path.GetFileName(t.FilePath)} → {to}");
        }
        catch (Exception ex) { Log("convert failed: " + ex.Message); }
    }

    private void UpdateStatus()
        => StatusBarText = ActiveTab == null ? "Ready." : $"{ActiveTab.Language}  •  {ActiveTab.FilePath}";

    private void RunActive()
    {
        var t = ActiveTab; if (t == null) { Log("no active tab to run"); return; }
        var output = t.Run();
        ConsoleText += output;
        // Notify any extension OnRun handlers
        foreach (var h in ExtMgr.RunHandlers) try { h(t.Document.Text); } catch { }
        OnPropertyChanged(nameof(ConsoleText));
        StatusBarText = $"Ran {Path.GetFileName(t.FilePath)}";
    }

    private void Log(string line)
    {
        ConsoleText += $"[{DateTime.Now:HH:mm:ss}] {line}\n";
        OnPropertyChanged(nameof(ConsoleText));
    }
}
