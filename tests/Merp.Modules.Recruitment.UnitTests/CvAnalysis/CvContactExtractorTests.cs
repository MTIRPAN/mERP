using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Modules.Recruitment.Domain.Applications;

namespace Merp.Modules.Recruitment.UnitTests.CvAnalysis;

/// <summary>The contact details read from a CV without the AI provider, and the text the provider gets (PLAN A3, D51).</summary>
public sealed class CvContactExtractorTests
{
    [Fact]
    public void TheContactDetails_AreReadFromTheText()
    {
        var contact = CvContactExtractor.Extract(
            "Ayşe Nur Yılmaz\nAyse.Yilmaz@Example.com | +90 532 123 45 67\nlinkedin.com/in/ayseyilmaz · https://ayse.dev/portfolio.\nSatış Uzmanı, 2019 - 2024");

        contact.ShouldSatisfyAllConditions(
            c => c.FirstName.ShouldBe("Ayşe Nur"),
            c => c.LastName.ShouldBe("Yılmaz"),
            c => c.Email.ShouldBe("ayse.yilmaz@example.com"),
            c => c.Phone.ShouldBe("+90 532 123 45 67"),
            c => c.LinkedInUrl.ShouldBe("https://linkedin.com/in/ayseyilmaz"),
            c => c.PortfolioUrl.ShouldBe("https://ayse.dev/portfolio"));
    }

    [Fact]
    public void LandlinesAndLocalMobiles_AreNumbers_ButYearsAreNot()
    {
        CvContactExtractor.Extract("Tel: 0212 345 67 89").Phone.ShouldBe("0212 345 67 89");
        CvContactExtractor.Extract("GSM 05321234567").Phone.ShouldBe("05321234567");
        CvContactExtractor.Extract("Deneyim 2015 - 2024, 12345 TL").Phone.ShouldBeNull();
    }

    [Fact]
    public void AnEmailDomain_IsNotAWebLink_AndAHeadingIsNotAName()
    {
        var contact = CvContactExtractor.Extract("Özgeçmiş\nKişisel Bilgiler\nmehmet@kuzey.com.tr");

        contact.ShouldSatisfyAllConditions(
            c => c.FirstName.ShouldBeNull(),
            c => c.LastName.ShouldBeNull(),
            c => c.Email.ShouldBe("mehmet@kuzey.com.tr"),
            c => c.PortfolioUrl.ShouldBeNull());
    }

    [Fact]
    public void TheRequirementsOfAnOpening_AreSentAsPlainText()
    {
        RichText.ToPlainText("<h2>Aranan nitelikler</h2><ul><li>5 yıl <strong>B2B</strong> satış</li><li>İngilizce &amp; Almanca</li></ul><p>Ehliyet<br>B sınıfı</p>")
            .ShouldBe("Aranan nitelikler\n- 5 yıl B2B satış\n- İngilizce & Almanca\nEhliyet\nB sınıfı");
        RichText.ToPlainText("<p></p>").ShouldBeNull();
        RichText.ToPlainText(null).ShouldBeNull();
    }

    [Fact]
    public void AnAnalysis_IsReplaced_AndAFailureKeepsOnlyItsCode()
    {
        var at = new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
        var file = Guid.CreateVersion7();
        var analysis = ApplicationCvAnalysis.Of(JobApplicationId.From(Guid.CreateVersion7()));

        analysis.Succeed(file, """{"analysis":{}}""", "claude-sonnet-5-5", "cv-analysis-v1", null, at);
        analysis.ShouldSatisfyAllConditions(a => a.Succeeded.ShouldBeTrue(), a => a.ErrorCode.ShouldBeNull(), a => a.Model.ShouldBe("claude-sonnet-5-5"));

        analysis.Fail(file, "common.errors.ai_budget_exceeded", Guid.CreateVersion7(), at.AddDays(1));
        analysis.ShouldSatisfyAllConditions(
            a => a.Succeeded.ShouldBeFalse(),
            a => a.Result.ShouldBeNull(),
            a => a.Model.ShouldBeNull(),
            a => a.ErrorCode.ShouldBe("common.errors.ai_budget_exceeded"),
            a => a.AnalyzedAt.ShouldBe(at.AddDays(1)),
            a => a.RequestedByUserId.ShouldNotBeNull());
        Should.Throw<ArgumentException>(() => analysis.Fail(Guid.Empty, "x", null, at));
    }
}
