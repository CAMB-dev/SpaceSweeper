using SpaceSweeper.App.Wpf.ViewModels;

namespace SpaceSweeper.App.Wpf.Localization;

public sealed class WpfTextProvider : ITextProvider
{
    public IReadOnlyList<LanguageOption> Languages => LanguageManager.SupportedLanguages;

    public void Apply(string cultureName)
    {
        LanguageManager.Apply(cultureName);
    }

    public string Get(string key)
    {
        return LanguageManager.GetString(key);
    }
}
