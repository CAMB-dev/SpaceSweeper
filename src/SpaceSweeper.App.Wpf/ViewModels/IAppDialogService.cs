namespace SpaceSweeper.App.Wpf.ViewModels;

public interface IAppDialogService
{
    string? PickFolder(string initialPath);

    bool Confirm(string title, string message);

    void ShowInfo(string title, string message);

    void ShowError(string title, string message);
}
