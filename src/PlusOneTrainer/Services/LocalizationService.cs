using System.Globalization;
using System.Windows;

namespace PlusOneTrainer.Services;

public static class LocalizationService
{
    private const string LanguageSetting = "PLUS_ONE_TRAINER_LANGUAGE";

    public static string CurrentLanguage { get; private set; } = "zh-CN";

    public static void ApplySavedLanguage()
    {
        var requested = Environment.GetEnvironmentVariable(LanguageSetting, EnvironmentVariableTarget.User);
        SetLanguage(requested is "en-US" ? "en-US" : "zh-CN", persist: false);
    }

    public static void SetLanguage(string language, bool persist = true)
    {
        CurrentLanguage = language == "en-US" ? "en-US" : "zh-CN";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(x =>
            x.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{CurrentLanguage}.xaml", UriKind.Relative)
        };
        if (existing is null)
            dictionaries.Add(replacement);
        else
            dictionaries[dictionaries.IndexOf(existing)] = replacement;

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CurrentLanguage);
        if (persist)
            Environment.SetEnvironmentVariable(LanguageSetting, CurrentLanguage, EnvironmentVariableTarget.User);
    }

    public static string Text(string key, string fallback = "") =>
        Application.Current.TryFindResource(key) as string ?? fallback;
}
