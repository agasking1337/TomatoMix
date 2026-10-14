using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TomatoMix.ViewModels;
using TomatoMix.Views;

namespace TomatoMix;

public partial class App : Application
{
    private MainWindowViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainViewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };
            desktop.Exit += (_, _) => _mainViewModel?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
