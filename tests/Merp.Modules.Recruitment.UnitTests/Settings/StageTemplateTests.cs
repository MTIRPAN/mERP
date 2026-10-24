using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.UnitTests.Settings;

/// <summary>The rules of a hiring pipeline and of the KVKK text versions (PLAN R1, D47).</summary>
public sealed class StageTemplateTests
{
    private static readonly LocalizedString Name = LocalizedString.Of("tr-TR", "Şablon");

    private static Stage S(StageType type, int? sla = null) => new(Guid.Empty, LocalizedString.Of("tr-TR", type.ToString()), type, sla);

    private static IReadOnlyList<Stage> Pipeline() =>
    [
        S(StageType.Applied, 3),
        S(StageType.Screening, 5),
        S(StageType.Interview, 7),
        S(StageType.Interview, 7),
        S(StageType.Offer, 5),
        S(StageType.Hired),
        S(StageType.Rejected),
        S(StageType.Withdrawn),
    ];

    [Fact]
    public void ADefaultPipeline_IsValid_AndBecomesTheDefault()
    {
        var template = StageTemplate.Create(Name, Pipeline(), isDefault: true).Value;

        template.IsDefault.ShouldBeTrue();
        template.DomainEvents.OfType<StageTemplateMadeDefault>().ShouldHaveSingleItem();
        template.Stages.Select(s => s.Id).Distinct().Count().ShouldBe(8);
    }

    [Fact]
    public void NewStages_GetIds_AndKeptOnesKeepTheirs()
    {
        var kept = Guid.CreateVersion7();
        var template = StageTemplate.Create(
            Name,
            [S(StageType.Applied) with { Id = kept }, S(StageType.Interview, 5), S(StageType.Hired), S(StageType.Rejected), S(StageType.Withdrawn)],
            isDefault: false).Value;

        template.Stages[0].Id.ShouldBe(kept);
        template.Stages.ShouldAllBe(s => s.Id != Guid.Empty);
        template.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Applied_ComesFirst_AndEachFinalStageOnce()
    {
        StageTemplate.Create(Name, [S(StageType.Screening), S(StageType.Applied), S(StageType.Hired), S(StageType.Rejected), S(StageType.Withdrawn)], false)
            .Error.ShouldBe(StageTemplateErrors.RequiredStages);
        StageTemplate.Create(Name, [S(StageType.Applied), S(StageType.Hired), S(StageType.Rejected)], false)
            .Error.ShouldBe(StageTemplateErrors.RequiredStages);
        StageTemplate.Create(Name, [S(StageType.Applied), S(StageType.Hired), S(StageType.Hired), S(StageType.Rejected), S(StageType.Withdrawn)], false)
            .Error.ShouldBe(StageTemplateErrors.RequiredStages);
    }

    [Fact]
    public void TheFinalStages_CloseTheList_WithoutATimeLimit()
    {
        StageTemplate.Create(Name, [S(StageType.Applied), S(StageType.Hired), S(StageType.Interview), S(StageType.Rejected), S(StageType.Withdrawn)], false)
            .Error.ShouldBe(StageTemplateErrors.FinalStagesLast);
        StageTemplate.Create(Name, [S(StageType.Applied), S(StageType.Hired, 3), S(StageType.Rejected), S(StageType.Withdrawn)], false)
            .Error.ShouldBe(StageTemplateErrors.InvalidSla);
        StageTemplate.Create(Name, [S(StageType.Applied, 0), S(StageType.Hired), S(StageType.Rejected), S(StageType.Withdrawn)], false)
            .Error.ShouldBe(StageTemplateErrors.InvalidSla);
    }

    [Fact]
    public void TheDefault_CannotBeRetired()
    {
        var template = StageTemplate.Create(Name, Pipeline(), isDefault: true).Value;

        template.Update(Name, template.Stages, isDefault: true, isActive: false).Error.ShouldBe(StageTemplateErrors.DefaultInactive);
        template.Update(Name, template.Stages, isDefault: false, isActive: false).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AKvkkText_IsPublishedAsTheNextRevision()
    {
        var at = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

        var first = ConsentText.Publish(ConsentKind.Disclosure, null, "tr-TR", 0, " Metin ", at);
        var third = ConsentText.Publish(ConsentKind.Disclosure, null, "tr-TR", 2, "Metin", at);

        first.ShouldSatisfyAllConditions(t => t.Revision.ShouldBe(1), t => t.Body.ShouldBe("Metin"));
        third.Revision.ShouldBe(3);
        Should.Throw<ArgumentException>(() => ConsentText.Publish(ConsentKind.Disclosure, null, "tr-TR", 0, " ", at));
    }
}
