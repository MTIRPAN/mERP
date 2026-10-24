using System.Globalization;
using Merp.Application.Abstractions.Notifications;
using Merp.Infrastructure.Email;
using Merp.Modules.HR.Application.Leave;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>The built-in templates of the leave documents render their sample models (PLAN L6, D46).</summary>
public sealed class LeaveDocumentTests
{
    private static readonly ITemplateRenderer Renderer = new ServiceCollection()
        .AddMerpEmail(new ConfigurationBuilder().Build())
        .BuildServiceProvider()
        .GetRequiredService<ITemplateRenderer>();

    [Theory]
    [InlineData(HrDocumentTypes.LeaveRecord, "Ayşe Yılmaz", "IZN-2026-000012")]
    [InlineData(HrDocumentTypes.LeaveForm, "Ayşe Yılmaz", "Çiçek Sok.")]
    public void TheBuiltInTemplate_RendersItsSample(string key, string name, string detail)
    {
        var type = new HrDocumentTypes().GetDocumentTypes().Single(t => t.Key == key);
        var model = new Dictionary<string, object?>(type.SampleModel, StringComparer.Ordinal)
        {
            ["texts"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["title"] = "BAŞLIK", ["employee_signature"] = "İşçi" },
        };

        var html = Renderer.Render(
            type.DefaultBody,
            model,
            new TemplateRenderOptions(true, CultureInfo.GetCultureInfo("tr-TR"), new Dictionary<string, string> { ["letterhead"] = "<header></header>" }));

        html.IsSuccess.ShouldBeTrue(html.IsFailure ? html.Error.Code + " " + string.Join(",", html.Error.Args.Values) : null);
        html.Value.ShouldContain("BAŞLIK");
        html.Value.ShouldContain(name);
        html.Value.ShouldContain(detail);
        type.Fields.ShouldNotBeEmpty();
    }
}
