using Merp.Application.Abstractions.Notifications;
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
    public void Html_WritesLineBreaksAsCharacterReferences()
    {
        // A written e-mail body keeps its paragraphs under white-space:pre-line (D58)
        var model = new Dictionary<string, object?>(StringComparer.Ordinal) { ["body"] = "Sayın Ayşe,\n\nTeşekkürler." };

        _renderer.Render("<p>{{ body }}</p>", model, html: true).Value.ShouldBe("<p>Sayın Ayşe,&#xA;&#xA;Teşekkürler.</p>");
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

    [Fact]
    public void Partials_SeeTheModel_AndAreEncoded()
    {
        var partials = new Dictionary<string, string> { ["letterhead"] = "<header>{{ company.name }} – {{ body }}</header>" };

        var result = _renderer.Render("{% render 'letterhead' %}<p>{{ title }}</p>", Model, new TemplateRenderOptions(Html: true, Partials: partials));

        result.Value.ShouldBe("<header>Acme – &lt;b&gt;Ayşe&lt;/b&gt; &amp; Çağrı</header><p>İzin onaylandı</p>");
    }

    [Fact]
    public void UnknownPartial_Fails_AndFilesAreNotReachable()
    {
        var options = new TemplateRenderOptions(Html: true, Partials: new Dictionary<string, string> { ["logo"] = "L" });

        var unknown = _renderer.Render("{% render 'letterhead' %}", Model, options);
        var file = _renderer.Render("{% render '../../appsettings.json' %}", Model, options);

        unknown.Error.Code.ShouldBe("common.templates.errors.invalid");
        file.Error.Code.ShouldBe("common.templates.errors.invalid");
    }

    [Fact]
    public void Dates_FollowTheCulture()
    {
        var model = new Dictionary<string, object?> { ["day"] = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Unspecified) };

        var result = _renderer.Render("{{ day | date: '%d %B %Y' }}", model, new TemplateRenderOptions(Html: true, Culture: new System.Globalization.CultureInfo("tr-TR")));

        result.Value.ShouldBe("30 Ağustos 2026");
    }

    [Fact]
    public void EndlessLoop_IsStopped()
    {
        var result = _renderer.Render(
            "{% for i in (1..1000000) %}{% for j in (1..1000000) %}x{% endfor %}{% endfor %}",
            Model,
            new TemplateRenderOptions(Html: true, Partials: new Dictionary<string, string>()));

        result.Error.Code.ShouldBe("common.templates.errors.too_complex");
    }

    private sealed record Secret(string Password);
}
