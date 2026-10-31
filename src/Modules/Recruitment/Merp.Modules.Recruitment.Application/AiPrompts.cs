using System.Globalization;

namespace Merp.Modules.Recruitment.Application;

/// <summary>Lines shared by the instructions and inputs of the module's AI features.</summary>
internal static class AiPrompts
{
    /// <summary>The language the model writes in, e.g. "Output language: Turkish, tr-TR".</summary>
    public static string OutputLanguage(string culture)
    {
        var language = CultureInfo.GetCultureInfo(culture);
        return $"Output language: {language.EnglishName}, {language.Name}";
    }
}
