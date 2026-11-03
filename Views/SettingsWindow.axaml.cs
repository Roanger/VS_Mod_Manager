using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VSModManager.ViewModels;

namespace VSModManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += () => Close(vm.Result);
    }

    private SettingsViewModel Vm => (SettingsViewModel)DataContext!;

    private async void BrowseInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder("Select the Vintage Story install folder", Vm.InstallDir) is { } path)
            Vm.InstallDir = path;
    }

    private async void BrowseData_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFolder("Select the Vintage Story data folder", Vm.DataDir) is { } path)
            Vm.DataDir = path;
    }

    private async Task<string?> PickFolder(string title, string startAt)
    {
        var start = Directory.Exists(startAt)
            ? await StorageProvider.TryGetFolderFromPathAsync(startAt)
            : null;

        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
