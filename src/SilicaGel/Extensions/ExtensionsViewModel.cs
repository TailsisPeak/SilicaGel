using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using ReactiveUI;

namespace SilicaGel;

public sealed class ExtensionsViewModel : ViewModelBase
{
    private readonly ExtensionManager _mgr;

    public ObservableCollection<LoadedExtension> Items { get; } = new();
    public ICommand ReloadCommand { get; }
    public ICommand OpenFolderCommand { get; }

    private string _search = "";
    public string Search { get => _search; set { if (SetField(ref _search, value)) Refresh(); } }

    public string FolderHint => "Drop extensions into:\n" + (System.Environment.GetEnvironmentVariable("SILICAGEL_EXT_DIR") ?? AppSettings.DefaultExtDir);

    public ExtensionsViewModel(ExtensionManager mgr)
    {
        _mgr = mgr;
        ReloadCommand     = ReactiveCommand.Create(() => { _mgr.ReloadAll(); Refresh(); });
        OpenFolderCommand = ReactiveCommand.Create(() =>
        {
            var dir = string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SILICAGEL_EXT_DIR"))
                ? AppSettings.DefaultExtDir : System.Environment.GetEnvironmentVariable("SILICAGEL_EXT_DIR")!;
            Directory.CreateDirectory(dir);
            try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
        });
        Refresh();
    }

    public void Refresh()
    {
        Items.Clear();
        var q = _search?.Trim().ToLowerInvariant() ?? "";
        foreach (var e in _mgr.Extensions)
        {
            if (q.Length > 0 && !e.DisplayName.ToLowerInvariant().Contains(q) && !e.Id.ToLowerInvariant().Contains(q)) continue;
            Items.Add(e);
        }
    }
}
