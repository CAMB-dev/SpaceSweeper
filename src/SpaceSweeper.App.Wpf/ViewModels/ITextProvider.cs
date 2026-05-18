using SpaceSweeper.App.Wpf.Localization;

namespace SpaceSweeper.App.Wpf.ViewModels;

public interface ITextProvider
{
    IReadOnlyList<LanguageOption> Languages { get; }

    void Apply(string cultureName);

    string Get(string key);
}
