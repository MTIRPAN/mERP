using Merp.Modules.Platform.Domain.CustomFields;
using Merp.SharedKernel.CustomFields;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.UnitTests.CustomFields;

public sealed class CustomFieldDefinitionTests
{
    private static readonly LocalizedString Label = LocalizedString.Of("tr-TR", "Beden");

    private static CustomFieldSettings Settings(IReadOnlyList<CustomFieldOption>? options = null, int? maxLength = null, decimal? min = null, decimal? max = null) =>
        new(Label, Required: false, options ?? [], maxLength, min, max, WholeNumber: false, Group: null, Order: 0, IsActive: true);

    private static CustomFieldOption Option(string code) => new(code, new Dictionary<string, string> { ["tr-TR"] = code.ToUpperInvariant() });

    private static IEnumerable<string> FieldsOf(Merp.SharedKernel.Results.Result result) => result.Error.Fields.Select(f => f.Field);

    [Theory]
    [InlineData("shoe_size", true)]
    [InlineData("a1", true)]
    [InlineData("Shoe", false)]
    [InlineData("1size", false)]
    [InlineData("ayakkabı", false)]
    [InlineData("shoe-size", false)]
    public void Codes_AreLowerCaseIdentifiers(string code, bool valid) =>
        CustomFieldDefinition.IsValidCode(code).ShouldBe(valid);

    [Fact]
    public void SelectFields_NeedUniqueOptions_OtherTypesNone()
    {
        FieldsOf(CustomFieldDefinition.Create("platform.company", "size", CustomFieldType.Select, null, Settings())).ShouldBe(["options"]);
        FieldsOf(CustomFieldDefinition.Create("platform.company", "size", CustomFieldType.Select, null, Settings([Option("s"), Option("s")]))).ShouldBe(["options"]);
        FieldsOf(CustomFieldDefinition.Create("platform.company", "note", CustomFieldType.Text, null, Settings([Option("s")]))).ShouldBe(["options"]);

        var size = CustomFieldDefinition.Create("platform.company", "size", CustomFieldType.MultiSelect, null, Settings([Option("s"), Option("m")])).Value;
        size.Rule.Options.ShouldBe(["s", "m"]);
    }

    [Fact]
    public void Limits_BelongToTheirType()
    {
        FieldsOf(CustomFieldDefinition.Create("platform.company", "count", CustomFieldType.Number, null, Settings(maxLength: 10))).ShouldBe(["maxLength"]);
        FieldsOf(CustomFieldDefinition.Create("platform.company", "note", CustomFieldType.Text, null, Settings(min: 1))).ShouldBe(["min"]);
        FieldsOf(CustomFieldDefinition.Create("platform.company", "count", CustomFieldType.Number, null, Settings(min: 5, max: 1))).ShouldBe(["max"]);
        CustomFieldDefinition.Create("platform.company", "count", CustomFieldType.Number, null, Settings(min: 1, max: 5)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_KeepsCodeTypeAndCompany()
    {
        var company = Guid.NewGuid();
        var definition = CustomFieldDefinition.Create("platform.company", "note", CustomFieldType.Text, company, Settings()).Value;

        definition.Update(Settings(maxLength: 20) with { IsActive = false, Required = true }).IsSuccess.ShouldBeTrue();

        (definition.Code, definition.Type, definition.CompanyId, definition.IsActive, definition.Required, definition.MaxLength)
            .ShouldBe(("note", CustomFieldType.Text, (Guid?)company, false, true, (int?)20));
    }
}
