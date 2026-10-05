using Merp.SharedKernel.Privacy;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public sealed class PersonalDataRedactorTests
{
    [Theory]
    [InlineData("İletişim: ayse.yilmaz@example.com.tr", "İletişim: [EMAIL]")]
    [InlineData("Mail: JOHN_DOE+cv@Mail.Example.COM", "Mail: [EMAIL]")]
    public void Redacts_EmailAddresses(string input, string expected) =>
        PersonalDataRedactor.Redact(input).Text.ShouldBe(expected);

    [Theory]
    [InlineData("Tel: 0532 123 45 67", "Tel: [PHONE]")]
    [InlineData("Tel: +90 532 123 45 67", "Tel: [PHONE]")]
    [InlineData("Tel: (0212) 123-45-67", "Tel: [PHONE]")]
    [InlineData("Tel: 05321234567", "Tel: [PHONE]")]
    [InlineData("Phone: +1 (415) 555-0132", "Phone: [PHONE]")]
    [InlineData("Phone: 415-555-0132", "Phone: [PHONE]")]
    [InlineData("GSM 0532.123.45.67 (iş)", "GSM [PHONE] (iş)")]
    public void Redacts_PhoneNumbers(string input, string expected) =>
        PersonalDataRedactor.Redact(input).Text.ShouldBe(expected);

    [Theory]
    [InlineData("01.2015 - 06.2020 Yazılım Geliştirici")]
    [InlineData("2015-2020 ABC Holding")]
    [InlineData("Maaş beklentisi: 85.000 - 100.000 TL")]
    [InlineData("2010 2012 2015 2018 yıllarında projeler")]
    [InlineData("ISO 27001:2022 sertifikası")]
    [InlineData("Language: English (C1), German (B2)")]
    public void DoesNotRedact_DatesAmountsOrOrdinaryText(string input)
    {
        var result = PersonalDataRedactor.Redact(input);

        result.Text.ShouldBe(input);
        result.Total.ShouldBe(0);
    }

    [Fact]
    public void Redacts_OnlyValidIdentityNumbers()
    {
        var result = PersonalDataRedactor.Redact("TCKN: 10000000146, sipariş no 12345678901");

        result.Text.ShouldStartWith("TCKN: [NATIONAL_ID], sipariş no ");
        result.Counts[RedactionKind.NationalId].ShouldBe(1);
    }

    [Theory]
    [InlineData("IBAN: TR33 0006 1005 1978 6457 8413 26 (maaş hesabı)", "IBAN: [IBAN] (maaş hesabı)")]
    [InlineData("TR330006100519786457841326", "[IBAN]")]
    public void Redacts_ValidIbans(string input, string expected) =>
        PersonalDataRedactor.Redact(input).Text.ShouldBe(expected);

    [Theory]
    [InlineData("Profil: https://www.linkedin.com/in/ayse-yilmaz", "Profil: [URL]")]
    [InlineData("Kod: github.com/ayseyilmaz", "Kod: [URL]")]
    [InlineData("Portfolyo www.ayseyilmaz.dev", "Portfolyo [URL]")]
    public void Redacts_ProfileLinks(string input, string expected) =>
        PersonalDataRedactor.Redact(input).Text.ShouldBe(expected);

    [Theory]
    [InlineData("Doğum Tarihi: 01.01.1990", "Doğum Tarihi: [REDACTED]")]
    [InlineData("DOĞUM TARİHİ : 1990", "DOĞUM TARİHİ : [REDACTED]")]
    [InlineData("Medeni Durum: Evli, 2 çocuk", "Medeni Durum: [REDACTED]")]
    [InlineData("Cinsiyet: Kadın | Uyruk: T.C.", "Cinsiyet: [REDACTED] | Uyruk: [REDACTED]")]
    [InlineData("Askerlik Durumu: Tamamlandı", "Askerlik Durumu: [REDACTED]")]
    [InlineData("Sağlık Durumu: Kronik rahatsızlık yok", "Sağlık Durumu: [REDACTED]")]
    [InlineData("Kan Grubu: 0 Rh+", "Kan Grubu: [REDACTED]")]
    [InlineData("Dini: İslam", "Dini: [REDACTED]")]
    [InlineData("Adres: Moda Cad. No: 5 Kadıköy", "Adres: [REDACTED]")]
    [InlineData("Date of birth: 1 Jan 1990", "Date of birth: [REDACTED]")]
    [InlineData("MARITAL STATUS: Single", "MARITAL STATUS: [REDACTED]")]
    [InlineData("• Gender: Male", "• Gender: [REDACTED]")]
    public void Redacts_ValuesOfSensitiveFields(string input, string expected)
    {
        var result = PersonalDataRedactor.Redact(input);

        result.Text.ShouldBe(expected);
        result.Counts[RedactionKind.SensitiveField].ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Redacts_GivenNames_IgnoringTurkishCasing()
    {
        var result = PersonalDataRedactor.Redact("İSMAİL ŞAHİN\nYazılım mühendisi. İsmail ekip lideriydi; Şahin ailesi şirketinde değil.", ["İsmail Şahin"]);

        result.Text.ShouldBe("[NAME] [NAME]\nYazılım mühendisi. [NAME] ekip lideriydi; [NAME] ailesi şirketinde değil.");
        result.Counts[RedactionKind.Name].ShouldBe(4);
    }

    [Fact]
    public void Names_MatchWholeWordsOnly() =>
        PersonalDataRedactor.Redact("Alican Aliyev ile çalıştı", ["Ali"]).Text.ShouldBe("Alican Aliyev ile çalıştı");

    [Fact]
    public void Counts_EveryKind()
    {
        var cv = """
            Ayşe Yılmaz
            ayse@example.com · 0532 123 45 67 · linkedin.com/in/ayse
            Doğum Tarihi: 01.01.1990
            Kıdemli yazılım mühendisi, 8 yıl deneyim.
            """;

        var result = PersonalDataRedactor.Redact(cv, ["Ayşe Yılmaz"]);

        result.Counts[RedactionKind.Email].ShouldBe(1);
        result.Counts[RedactionKind.Url].ShouldBe(1);
        result.Counts[RedactionKind.Phone].ShouldBe(1);
        result.Counts[RedactionKind.SensitiveField].ShouldBe(1);
        result.Counts[RedactionKind.Name].ShouldBe(2);
        result.Total.ShouldBe(6);
        result.Text.ShouldContain("Kıdemli yazılım mühendisi, 8 yıl deneyim.");
        result.Text.ShouldNotContain("Ayşe");
        result.Text.ShouldNotContain("1990");
    }

    [Fact]
    public void Fold_KeepsLength_AndFoldsTurkishLetters()
    {
        const string input = "İIıiŞşĞğÜüÖöÇç";

        var folded = PersonalDataRedactor.Fold(input);

        folded.ShouldBe("iiiissgguuoocc");
        folded.Length.ShouldBe(input.Length);
    }
}
