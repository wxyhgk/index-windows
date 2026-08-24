using Microsoft.UI.Xaml;

namespace Ketcher.WinUI3.Demo;

public sealed class App : Application
{
    public App()
    {
        this.UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        mainWindow.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
    }
}
