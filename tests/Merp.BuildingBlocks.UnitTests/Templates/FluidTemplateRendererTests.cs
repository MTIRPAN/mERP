using Merp.Infrastructure.Templates;

namespace Merp.BuildingBlocks.UnitTests.Templates;

public sealed class FluidTemplateRendererTests
{
    private readonly FluidTemplateRenderer _renderer = new();

    private static readonly Dictionary<string, object?> Model = new(StringComparer.Ordinal)
    {
        ["title"] = "İzin onaylandı",
        ["body"] = "<b>Ayşe</b> & Çağrı",
        ["link"] = null,
        ["company"] = new Dictionary<string, object?> { ["name"] = "Acme" },
        ["args"] = new Dictionary<string, object?> { ["number"] = "IZN-2026-000042" },
        ["lines"] = new List<object?> { "bir", "iki" },
    };

    [Fact]
    public void Html_EncodesValues_AndReadsNestedObjectsAndLists()
    {
        var result = _renderer.Render(
            "<h1>{{ title }}</h1><p>{{ body }}</p>{{ company.name }} {{ args.number }}{% for line in lines %}-{{ line }}{% endfor %}",
            Model,
            html: true);

        result.Value.ShouldBe("<h1>İzin onaylandı</h1><p>&lt;b&gt;Ayşe&lt;/b&gt; &amp; Çağrı</p>Acme IZN-2026-000042-bir-iki");
    }

    [Fact]
    public void PlainText_IsNotEncoded()
    {
        _renderer.Render("{{ title }}: {{ body }}", Model, html: false).Value.ShouldBe("İzin onaylandı: <b>Ayşe</b> & Çağrı");
    }

    [Fact]
    public void MissingAndNullValues_RenderEmpty_AndAreFalsy()
    {
        _renderer.Render("[{{ unknown }}]{% if link %}link{% else %}none{% endif %}", Model, html: false).Value.ShouldBe("[]none");
    }

    [Fact]
    public void DotNetMembers_AreNotReachable()
    {
        var model = new Dictionary<string, object?> { ["user"] = new Secret("s3cret") };

        _renderer.Render("[{{ user.Password }}]", model, html: false).Value.ShouldBe("[]");
    }

    [Fact]
    public void BrokenTemplate_ReportsTheLine()
    {
        var result = _renderer.Validate("<p>\n{% if title %}\nno end");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("common.templates.errors.invalid");
        result.Error.Args["line"].ShouldBeOfType<int>().ShouldBeGreaterThanOrEqualTo(2);
        result.Error.Args["message"].ShouldBeOfType<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Render_OfABrokenTemplate_Fails()
    {
        _renderer.Render("{{ title ", Model, html: true).IsFailure.ShouldBeTrue();
    }

    private sealed record Secret(string Password);
}
