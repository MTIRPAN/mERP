using Merp.Modules.Recruitment.Application.CvAnalysis;

namespace Merp.Modules.Recruitment.UnitTests.CvAnalysis;

public sealed class CandidateNameDetectorTests
{
    [Theory]
    [InlineData("Ayşe Yılmaz\nKıdemli Yazılım Mühendisi", new[] { "Ayşe", "Yılmaz" })]
    [InlineData("\n\n  İSMAİL ŞAHİN  \n[EMAIL]", new[] { "İSMAİL", "ŞAHİN" })]
    [InlineData("Özgeçmiş\nMehmet Ali Öztürk\nİstanbul", new[] { "Mehmet", "Ali", "Öztürk" })]
    [InlineData("John O'Neil-Smith\nSoftware Engineer", new[] { "John", "O'Neil-Smith" })]
    public void Detects_NameOnTheFirstLines(string text, string[] expected) =>
        CandidateNameDetector.Detect(text).ShouldBe(expected);

    [Theory]
    [InlineData("Senior Software Engineer\nAyşe Yılmaz")]
    [InlineData("Kıdemli Yazılım Mühendisi\n8 yıl deneyim")]
    [InlineData("CURRICULUM VITAE")]
    [InlineData("Profil\nDeneyim\nEğitim")]
    [InlineData("ayşe yılmaz")]
    [InlineData("A. B.")]
    [InlineData("Ankara 2015 - 2020")]
    public void IgnoresHeadingsTitlesAndOtherLines(string text)
    {
        var detected = CandidateNameDetector.Detect(text);

        detected.ShouldNotContain("Senior");
        detected.ShouldNotContain("Kıdemli");
        if (!text.Contains("Ayşe Yılmaz", StringComparison.Ordinal))
        {
            detected.ShouldBeEmpty();
        }
    }
}
