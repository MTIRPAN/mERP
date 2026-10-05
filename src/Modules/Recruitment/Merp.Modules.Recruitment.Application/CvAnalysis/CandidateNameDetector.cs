using Merp.SharedKernel.Privacy;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

/// <summary>
/// Best effort guess of the candidate's name, so it can be redacted before analysis: CVs usually start with the name.
/// Only a short line of capitalized words near the top that is not a heading or a job title counts.
/// </summary>
internal static class CandidateNameDetector
{
    private const int LinesToInspect = 3;

    // Folded (see PersonalDataRedactor.Fold) words that appear in headings and job titles, never in names.
    private static readonly HashSet<string> NotNameWords = new(StringComparer.Ordinal)
    {
        "cv", "curriculum", "vitae", "ozgecmis", "resume", "kisisel", "bilgiler", "personal", "profile", "profil",
        "iletisim", "contact", "hakkimda", "about", "summary", "ozet", "deneyim", "experience", "egitim", "education",
        "senior", "junior", "kidemli", "lead", "head", "chief", "principal", "staff", "intern", "stajyer",
        "engineer", "muhendis", "muhendisi", "developer", "gelistirici", "gelistiricisi", "manager", "mudur", "muduru",
        "yonetici", "yoneticisi", "uzman", "uzmani", "specialist", "analyst", "analist", "analisti", "consultant",
        "danisman", "danismani", "director", "direktor", "direktoru", "designer", "tasarimci", "architect", "mimar",
        "officer", "coordinator", "koordinator", "koordinatoru", "assistant", "asistan", "asistani", "teknisyen",
        "technician", "software", "yazilim", "data", "veri", "sales", "satis", "marketing", "pazarlama", "insan",
        "kaynaklari", "human", "resources", "finance", "finans", "accountant", "muhasebe", "muhasebeci", "operator",
        "operatoru", "teacher", "ogretmen", "doctor", "nurse", "hemsire", "avukat", "lawyer", "full", "stack",
        "frontend", "backend", "devops", "product", "urun", "project", "proje", "team", "takim", "ekip",
    };

    public static IReadOnlyList<string> Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(LinesToInspect))
        {
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is >= 2 and <= 4 && line.Length <= 60 && words.All(IsNameWord))
            {
                return words;
            }
        }

        return [];
    }

    private static bool IsNameWord(string word) =>
        word.Count(char.IsLetter) >= 2
        && char.IsUpper(word[0])
        && word.All(c => char.IsLetter(c) || c is '-' or '\'' or '.')
        && !NotNameWords.Contains(PersonalDataRedactor.Fold(word.TrimEnd('.')));
}
