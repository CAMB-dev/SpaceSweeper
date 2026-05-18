using System.Windows;
using SpaceSweeper.App.Wpf.Localization;
using SpaceSweeper.App.Wpf.ViewModels;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WinForms = System.Windows.Forms;

namespace SpaceSweeper.App.Wpf;

public sealed class WpfDialogService : IAppDialogService
{
    private readonly Window _owner;

    public WpfDialogService(Window owner)
    {
        _owner = owner;
    }

    public string? PickFolder(string initialPath)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = LanguageManager.GetString("FolderDialogDescription"),
            UseDescriptionForTitle = true,
            SelectedPath = System.IO.Directory.Exists(initialPath) ? initialPath : string.Empty
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? PickSaveFile(string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".csv",
            FileName = defaultFileName,
            Filter = filter,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message)
    {
        return System.Windows.MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;
    }

    public void ShowSettings(ScanSettings settings)
    {
        var window = new SettingsWindow
        {
            Owner = _owner,
            DataContext = settings
        };
        window.ShowDialog();
    }

    public void ShowInfo(string title, string message)
    {
        System.Windows.MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowError(string title, string message)
    {
        System.Windows.MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
