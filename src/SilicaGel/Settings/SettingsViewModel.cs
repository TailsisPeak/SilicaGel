using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ReactiveUI;

namespace SilicaGel;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _s;
    public AppSettings Raw => _s;

    public SettingsViewModel(AppSettings s)
    {
        _s = s;
        Themes = new ObservableCollection<string> { "Dark+ (default dark)", "Light+ (default light)", "Monokai", "Solarized Dark", "Solarized Light" };
        ResetCommand     = ReactiveCommand.Create(Reset);
        OpenConfigCommand= ReactiveCommand.Create(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppSettings.ConfigDir) { UseShellExecute = true }));
    }

    public ObservableCollection<string> Themes { get; }

    public string FontFamily { get => _s.FontFamily; set { _s.FontFamily = value; _s.Save(); OnPropertyChanged(); } }
    public double FontSize   { get => _s.FontSize;   set { _s.FontSize   = value; _s.Save(); OnPropertyChanged(); } }
    public int    TabSize    { get => _s.TabSize;    set { _s.TabSize    = value; _s.Save(); OnPropertyChanged(); } }
    public bool   WordWrap   { get => _s.WordWrap;   set { _s.WordWrap   = value; _s.Save(); OnPropertyChanged(); } }
    public bool   LineNumbers{ get => _s.LineNumbers;set { _s.LineNumbers= value; _s.Save(); OnPropertyChanged(); } }
    public bool   Minimap    { get => _s.Minimap;    set { _s.Minimap    = value; _s.Save(); OnPropertyChanged(); } }
    public bool   AutoSave   { get => _s.AutoSave;   set { _s.AutoSave   = value; _s.Save(); OnPropertyChanged(); } }
    public bool   RunOnSave  { get => _s.RunOnSave;  set { _s.RunOnSave  = value; _s.Save(); OnPropertyChanged(); } }
    public string Theme      { get => _s.Theme;      set { _s.Theme      = value; _s.Save(); OnPropertyChanged(); } }
    public string ExtensionsDir { get => _s.ExtensionsDir; set { _s.ExtensionsDir = value; _s.Save(); OnPropertyChanged(); } }
    public string ConfigDir  => AppSettings.ConfigDir;

    public ICommand ResetCommand { get; }
    public ICommand OpenConfigCommand { get; }

    private void Reset()
    {
        var d = new AppSettings();
        FontFamily = d.FontFamily; FontSize = d.FontSize; TabSize = d.TabSize;
        WordWrap = d.WordWrap; LineNumbers = d.LineNumbers; Minimap = d.Minimap;
        AutoSave = d.AutoSave; RunOnSave = d.RunOnSave; Theme = d.Theme;
    }
}
