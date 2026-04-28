using Avalonia.Controls;
using PcLun.ViewModels;

namespace PcLun.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(this);
    }
}
