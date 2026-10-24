using System.Text.Json;
using Merp.Application.Abstractions.Settings;
using Merp.Modules.Platform.Application.Settings;
using Merp.Modules.Platform.Domain.Settings;

namespace Merp.Modules.Platform.UnitTests.Settings;

/// <summary>Setting values and their checks (D35).</summary>
public sealed class SettingsTests
{
    [Fact]
    public void Profile_SetsAndRemovesValues()
    {
        var profile = SettingsProfile.Create(null);

        profile.Apply(new Dictionary<string, string?> { ["a"] = "1", ["b"] = "true" });
        profile.Apply(new Dictionary<string, string?> { ["a"] = null, ["c"] = "\"x\"" });

        profile.Values.ShouldBe(new Dictionary<string, string> { ["b"] = "true", ["c"] = "\"x\"" }, ignoreOrder: true);
    }

    [Theory]
    [InlineData("true", "true", null)]
    [InlineData("1", null, SettingErrors.InvalidValueCode)]
    public void Boolean_TakesOnlyTrueOrFalse(string json, string? stored, string? error) =>
        Check(SettingDefinition.Boolean("m.a.b", false, "l"), json).ShouldBe((stored, error));

    [Theory]
    [InlineData("5", "5", null)]
    [InlineData("5.0", "5.0", null)]
    [InlineData("5.5", null, SettingErrors.InvalidValueCode)]
    [InlineData("0", null, SettingErrors.OutOfRangeCode)]
    [InlineData("51", null, SettingErrors.OutOfRangeCode)]
    [InlineData("\"5\"", null, SettingErrors.InvalidValueCode)]
    public void Number_KeepsToItsLimits(string json, string? stored, string? error) =>
        Check(SettingDefinition.Number("m.a.b", 3, "l", min: 1, max: 50, wholeNumber: true), json).ShouldBe((stored, error));

    [Fact]
    public void Text_IsTrimmedAndLimited()
    {
        var definition = SettingDefinition.Text("m.a.b", "", "l", maxLength: 3);

        Check(definition, "\" ab \"").ShouldBe(("\"ab\"", null));
        Check(definition, "\"abcd\"").Error.ShouldBe(SettingErrors.TooLongCode);
    }

    [Fact]
    public void Choice_TakesOnlyItsOptions()
    {
        var definition = SettingDefinition.Choice("m.a.b", "x", [new("x", "lx"), new("y", "ly")], "l");

        Check(definition, "\"y\"").ShouldBe(("\"y\"", null));
        Check(definition, "\"z\"").Error.ShouldBe(SettingErrors.InvalidValueCode);
    }

    [Fact]
    public void Date_TakesADay_AndIsEmptyUntilSet()
    {
        var definition = DateSettings.Create("hr.leave.accrual_start", "label");

        Check(definition, "\"2026-01-01\"").ShouldBe(("\"2026-01-01\"", null));
        Check(definition, "\"01.01.2026\"").Error.ShouldBe(SettingErrors.InvalidValueCode);
        Check(definition, "5").Error.ShouldBe(SettingErrors.InvalidValueCode);
        definition.DefaultValue.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Registry_KnowsTheModuleOfAKey()
    {
        var registry = new SettingDefinitionRegistry([new PlatformSettingDefinitions()]);

        registry.Get(PlatformSettingDefinitions.EmailNotificationsEnabled).Module.ShouldBe("platform");
        registry.Find("platform.nope").ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => registry.Get("platform.nope"));
    }

    private static (string? Json, string? Error) Check(SettingDefinition definition, string json)
    {
        var (stored, error, _) = SettingRules.Check(definition, JsonDocument.Parse(json).RootElement);
        return (stored, error);
    }
}
