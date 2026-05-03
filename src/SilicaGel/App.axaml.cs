using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SilicaGel;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            // Forward any files passed on the command line (e.g. when the Axion editor
            // launches us with `SilicaGel "path/to/script.gel"`) to the view-model so
            // they open in tabs after the window finishes loading.
            if (desktop.Args is { Length: > 0 } args)
                vm.PendingOpenFiles.AddRange(args);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
