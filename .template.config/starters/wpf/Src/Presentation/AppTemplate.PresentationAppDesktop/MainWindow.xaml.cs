using System.Windows;
using AppTemplate.PresentationAppDesktop.Core.ViewModels;

namespace AppTemplate.PresentationAppDesktop;

public partial class MainWindow : Window
{
    //#if (localAuth)
    private readonly MainViewModel _main;

    public MainWindow(MainViewModel main)
    {
        InitializeComponent();
        DataContext = _main = main;
        main.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(MainViewModel.Password) && main.Password.Length == 0)
            {
                PasswordInput.Clear();
            }
        };
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _main.Password = PasswordInput.Password;
    //#else
    public MainWindow(MainViewModel main)
    {
        InitializeComponent();
        DataContext = main;
    }
    //#endif
}
