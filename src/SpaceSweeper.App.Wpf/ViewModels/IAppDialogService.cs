namespace SpaceSweeper.App.Wpf.ViewModels;

public interface IAppDialogService
{
    string? PickFolder(string initialPath);

    string? PickSaveFile(string defaultFileName, string filter);

    bool Confirm(string title, string message);

    void ShowSettings(ScanSettings settings);

    void ShowInfo(string title, string message);

    void ShowError(string title, string message);
}
