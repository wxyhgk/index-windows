using Ketcher.WinUI3.Demo;
using Microsoft.UI.Xaml;

namespace Ketcher.WinUI3.Demo;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.Start((app) =>
        {
            _ = new App();
        });
    }
}
