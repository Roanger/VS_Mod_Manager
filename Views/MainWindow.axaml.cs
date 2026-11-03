using Avalonia.Controls;
using Avalonia.Interactivity;
using VSModManager.Services;
using VSModManager.ViewModels;

namespace VSModManager.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var dialog = new SettingsWindow(new SettingsViewModel(vm.Paths));
        if (await dialog.ShowDialog<AppSettings?>(this) is { } settings)
            await vm.ApplySettingsAsync(settings);
    }
}
