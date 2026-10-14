using System.Text.Json;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Application.CustomFields;
using Merp.Modules.Platform.Application.Exports;
using Merp.SharedKernel.CustomFields;

namespace Merp.Modules.Platform.UnitTests.Exports;

public sealed class PlatformExportsTests
{
    [Fact]
    public void CustomFieldValues_KeepTheirType_AndOptionsShowTheirLabels()
    {
        PlatformExports.CustomFieldValue(Field(CustomFieldType.Number), Json("12.5")).ShouldBe(12.5m);
        PlatformExports.CustomFieldValue(Field(CustomFieldType.Bool), Json("true")).ShouldBe(true);
        PlatformExports.CustomFieldValue(Field(CustomFieldType.Date), Json("\"2026-10-13\"")).ShouldBe(new DateOnly(2026, 10, 13));
        PlatformExports.CustomFieldValue(Field(CustomFieldType.Select), Json("\"north\"")).ShouldBe("Kuzey");
        PlatformExports.CustomFieldValue(Field(CustomFieldType.MultiSelect), Json("[\"north\",\"gone\"]")).ShouldBe("Kuzey, gone");
        PlatformExports.CustomFieldValue(Field(CustomFieldType.Text), Json("\"metin\"")).ShouldBe("metin");
        PlatformExports.CustomFieldValue(Field(CustomFieldType.Text), Json("null")).ShouldBeNull();
    }

    [Fact]
    public void CompanyColumns_AddOneColumnPerCustomField_WithItsLabel()
    {
        var columns = PlatformExports.Companies([Field(CustomFieldType.Select)]);

        var column = columns.Single(c => c.Key == "customFields.region");
        column.Header.ShouldBe("Bölge");
        var company = new CompanyListItem(Guid.CreateVersion7(), "ACME", "Acme", "Acme A.Ş.", "1234567890", "İstanbul", true,
            new Dictionary<string, JsonElement> { ["region"] = Json("\"north\"") });
        column.Value(company).ShouldBe("Kuzey");
        columns[0].Key.ShouldBe("code");
    }

    private static CustomFieldFormItem Field(CustomFieldType type) =>
        new("region", "Bölge", type, false, [new CustomFieldFormOption("north", "Kuzey")], null, null, null, false, null);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
