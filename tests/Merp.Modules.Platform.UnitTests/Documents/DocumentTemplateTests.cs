using Merp.Modules.Platform.Application.Documents;
using Merp.Modules.Platform.Domain.Documents;

namespace Merp.Modules.Platform.UnitTests.Documents;

/// <summary>Document templates (PLAN P12, D36).</summary>
public sealed class DocumentTemplateTests
{
    [Fact]
    public void Create_StartsAtRevision1_Active_WithTheCanonicalCulture()
    {
        var template = DocumentTemplate.Create(PlatformDocumentTypes.Letter, null, "tr-tr", "<p>{{ letter.subject }}</p>", "  ");

        template.Revision.ShouldBe(1);
        template.IsActive.ShouldBeTrue();
        template.Culture.ShouldBe("tr-TR");
        template.Css.ShouldBeNull();
    }

    [Fact]
    public void Update_RaisesTheRevision_OnlyWhenTheContentChanges()
    {
        var template = DocumentTemplate.Create(PlatformDocumentTypes.Letter, Guid.CreateVersion7(), "tr-TR", "<p>a</p>", "p { color: red; }");

        template.Update("<p>a</p>", "p { color: red; }", isActive: false);
        template.Revision.ShouldBe(1);
        template.IsActive.ShouldBeFalse();

        template.Update("<p>b</p>", "p { color: red; }", isActive: true);
        template.Revision.ShouldBe(2);

        template.Update("<p>b</p>", null, isActive: true);
        template.Revision.ShouldBe(3);
        template.Css.ShouldBeNull();
    }

    [Fact]
    public void Body_IsRequired_AndLimited()
    {
        Should.Throw<ArgumentException>(() => DocumentTemplate.Create(PlatformDocumentTypes.Letter, null, "tr-TR", " ", null));
        Should.Throw<ArgumentException>(() => DocumentTemplate.Create(PlatformDocumentTypes.Letter, null, "tr-TR", new string('x', DocumentTemplate.BodyMaxLength + 1), null));
    }

    [Theory]
    [InlineData("platform.letter", "platform.documents.letter.texts.")]
    [InlineData("hr.leave_form", "hr.documents.leave_form.texts.")]
    public void Texts_OfAType_ComeFromItsModule(string key, string prefix) =>
        DocumentTypeRegistry.TextsPrefix(key).ShouldBe(prefix);

    [Fact]
    public void Registry_FindsTheLetter_WithItsSampleAndFields()
    {
        var registry = new DocumentTypeRegistry([new PlatformDocumentTypes()]);

        var letter = registry.Find(PlatformDocumentTypes.Letter).ShouldNotBeNull();

        letter.SampleModel.ShouldContainKey("letter");
        letter.Fields.ShouldContain(f => f.Path == "letter.subject");
        letter.DefaultBody.ShouldContain("{% render 'letterhead' %}");
        registry.Find("platform.unknown").ShouldBeNull();
    }
}
