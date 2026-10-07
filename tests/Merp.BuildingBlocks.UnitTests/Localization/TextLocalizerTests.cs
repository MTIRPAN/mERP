using Merp.Application.Abstractions.Localization;
using Merp.Infrastructure.Localization;

namespace Merp.BuildingBlocks.UnitTests.Localization;

/// <summary>Culture chain and database overrides of the localizer (D11, D28).</summary>
public sealed class TextLocalizerTests
{
    private const string NotFound = "common.errors.not_found";
    private const string Forbidden = "common.errors.forbidden";

    private static readonly EmbeddedTexts Embedded = new(new LocalizationResources().Add(typeof(global::Merp.SharedKernel.Results.Error).Assembly));

    private static LocalizationSnapshot Snapshot(params (string Culture, string Key, string Text)[] texts) => new(
        "tr-TR",
        [new("tr-TR", "Türkçe"), new("en-US", "English"), new("en-GB", "English (UK)", FallbackCode: "en-US"), new("de-DE", "Deutsch")],
        texts.GroupBy(t => t.Culture).ToDictionary(
            g => g.Key,
            g => (IReadOnlyDictionary<string, string>)g.ToDictionary(t => t.Key, t => t.Text)));

    private static TextLocalizer Localizer(LocalizationSnapshot snapshot)
    {
        var context = new LocalizationContext();
        context.Set(snapshot);
        return new TextLocalizer(Embedded, context);
    }

    [Fact]
    public void Translation_WinsInItsCulture_MissingOnesFallBackToTheDefault()
    {
        var localizer = Localizer(Snapshot(("en-US", NotFound, "Record not found.")));

        localizer.Get(NotFound, culture: "en-US").ShouldBe("Record not found.");
        localizer.Get(Forbidden, culture: "en-US").ShouldBe(Embedded.Get("tr-TR", Forbidden));
        localizer.Get(NotFound, culture: "tr-TR").ShouldBe(Embedded.Get("tr-TR", NotFound));
    }

    [Fact]
    public void Chain_FollowsTheFallback_BeforeTheDefault()
    {
        var localizer = Localizer(Snapshot(("en-US", NotFound, "Record not found."), ("tr-TR", Forbidden, "Yetkiniz yok (özel).")));

        localizer.Get(NotFound, culture: "en-GB").ShouldBe("Record not found.");
        localizer.Get(Forbidden, culture: "de-DE").ShouldBe("Yetkiniz yok (özel).");
        localizer.Get("no.such.key", culture: "en-US").ShouldBe("no.such.key");
    }

    [Fact]
    public void Placeholders_AreFilledInTheCulture()
    {
        var localizer = Localizer(Snapshot(("en-US", "common.errors.max_length", "At most {maxLength} characters.")));

        localizer.Get("common.errors.max_length", new Dictionary<string, object?> { ["maxLength"] = 1500 }, "en-US").ShouldBe("At most 1500 characters.");
    }

    [Fact]
    public void GetAll_MergesTheChain_NearestCultureWinning()
    {
        var localizer = Localizer(Snapshot(("en-US", NotFound, "Record not found."), ("en-GB", NotFound, "Record not found (UK).")));

        var texts = localizer.GetAll("common.errors.", "en-GB");

        texts[NotFound].ShouldBe("Record not found (UK).");
        texts[Forbidden].ShouldBe(Embedded.Get("tr-TR", Forbidden));
        texts.Keys.ShouldAllBe(k => k.StartsWith("common.errors.", StringComparison.Ordinal));
    }

    [Fact]
    public void Snapshot_MatchesALanguage_AndStopsFallbackCycles()
    {
        var snapshot = new LocalizationSnapshot(
            "tr-TR",
            [new("tr-TR", "Türkçe"), new("en-US", "English", FallbackCode: "en-GB"), new("en-GB", "English (UK)", FallbackCode: "en-US")],
            new Dictionary<string, IReadOnlyDictionary<string, string>>());

        snapshot.Match("en").ShouldBe("en-US");
        snapshot.Match("EN-gb").ShouldBe("en-GB");
        snapshot.Match("fr-FR").ShouldBeNull();
        snapshot.Chain("en-US").ShouldBe(["en-US", "en-GB", "tr-TR"]);
        snapshot.Chain("fr-FR").ShouldBe(["fr-FR", "tr-TR"]);
    }
}
