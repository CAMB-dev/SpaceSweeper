using SpaceSweeper.App.Wpf.Localization;

namespace SpaceSweeper.App.Wpf;

public partial class App : System.Windows.Application
{
    private void OnStartup(object sender, System.Windows.StartupEventArgs e)
    {
        LanguageManager.Apply("zh-CN");
    }
}
