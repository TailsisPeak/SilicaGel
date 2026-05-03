using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SilicaGel;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.InitializeAsync(this);
        };
    }
}
