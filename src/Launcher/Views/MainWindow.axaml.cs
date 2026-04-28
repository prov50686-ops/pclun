using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PcLun.ViewModels;

namespace PcLun.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(this);
    }

    private void OpenTelegram_Click(object? sender, RoutedEventArgs e) => OpenUrl("https://t.me/damirov666");
    private void OpenGithub_Click(object? sender, RoutedEventArgs e) => OpenUrl("https://github.com/prov50686-ops/pclun");

    private static void OpenUrl(string url)
    {
        try
        {
            var psi = new ProcessStartInfo(url) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch { }
    }
}
