using System.Globalization;
using System.Windows;
using WpfApplication = System.Windows.Application;
using WpfResourceDictionary = System.Windows.ResourceDictionary;

namespace SpaceSweeper.App.Wpf.Localization;

public static class LanguageManager
{
    private const string DictionaryPrefix = "Resources/Strings.";

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("zh-CN", "中文"),
        new("en-US", "English")
    ];

    public static void Apply(string cultureName)
    {
        var normalized = cultureName.Equals("en-US", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : "zh-CN";

        Thread.CurrentThread.CurrentCulture = new CultureInfo(normalized);
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(normalized);

        if (WpfApplication.Current is null)
        {
            return;
        }

        var dictionaries = WpfApplication.Current.Resources.MergedDictionaries;
        var existing = dictionaries
            .Where(static dictionary => dictionary.Source?.OriginalString.StartsWith(DictionaryPrefix, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        foreach (var dictionary in existing)
        {
            dictionaries.Remove(dictionary);
        }

        dictionaries.Add(new WpfResourceDictionary
        {
            Source = new Uri($"{DictionaryPrefix}{normalized}.xaml", UriKind.Relative)
        });
    }

    public static string GetString(string key)
    {
        return WpfApplication.Current?.TryFindResource(key) as string ?? key;
    }
}
