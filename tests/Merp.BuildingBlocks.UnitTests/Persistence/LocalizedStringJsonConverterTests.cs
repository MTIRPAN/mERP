using System.Text.Json;
using Merp.Infrastructure.Persistence.Conventions;
using Merp.SharedKernel.ValueObjects;

namespace Merp.BuildingBlocks.UnitTests.Persistence;

/// <summary>Localized names inside jsonb values keep the shape of their own columns (D11, D47).</summary>
public sealed class LocalizedStringJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new LocalizedStringJsonConverter() } };

    private sealed record Named(LocalizedString Name);

    [Fact]
    public void ALocalizedString_IsWrittenAsItsCultures_AndReadBack()
    {
        var value = new Named(LocalizedString.Create(new Dictionary<string, string?> { ["tr-TR"] = "Ön eleme", ["en-US"] = "Screening" }).Value);

        var json = JsonSerializer.Serialize(value, Options);
        var back = JsonSerializer.Deserialize<Named>(json, Options)!;

        json.ShouldContain("\"tr-TR\"");
        back.Name.Get("en-US", "tr-TR").ShouldBe("Screening");
        back.Name.ShouldBe(value.Name);
    }
}
