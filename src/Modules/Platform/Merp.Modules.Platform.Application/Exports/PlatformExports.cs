using System.Globalization;
using System.Text.Json;
using Merp.Application.Abstractions.Exports;
using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Application.CustomFields;
using Merp.Modules.Platform.Application.Users;
using Merp.SharedKernel.CustomFields;

namespace Merp.Modules.Platform.Application.Exports;

/// <summary>
/// Columns of the Platform lists that can be exported (D35). Keys and headers are those of the list columns on the
/// screen, so the file has the columns the user sees.
/// </summary>
public static class PlatformExports
{
    private const string StatusPrefix = "web.status.";

    public static readonly IReadOnlyList<ExportColumn<BranchListItem>> Branches =
    [
        ExportColumn<BranchListItem>.Of("code", "web.platform.fields.code", b => b.Code),
        ExportColumn<BranchListItem>.Of("name", "web.platform.branches.name", b => b.Name),
        ExportColumn<BranchListItem>.Of("companyName", "web.platform.branches.company", b => b.CompanyName),
        ExportColumn<BranchListItem>.Of("type", "web.platform.branches.type", b => b.Type, "web.platform.branch_type."),
        ExportColumn<BranchListItem>.Of("city", "web.platform.address.province", b => b.City),
        ExportColumn<BranchListItem>.Of("district", "web.platform.address.district", b => b.District),
        ExportColumn<BranchListItem>.Of("isActive", "web.platform.fields.status", b => Status(b.IsActive), StatusPrefix),
    ];

    public static readonly IReadOnlyList<ExportColumn<UserListItem>> Users =
    [
        ExportColumn<UserListItem>.Of("userName", "web.platform.users.user_name", u => u.UserName),
        ExportColumn<UserListItem>.Of("firstName", "web.platform.users.name", u => u.DisplayName),
        ExportColumn<UserListItem>.Of("email", "web.platform.fields.email", u => u.Email),
        ExportColumn<UserListItem>.Of("roles", "web.platform.users.roles", u => u.Roles),
        ExportColumn<UserListItem>.Of("isActive", "web.platform.fields.status", u => Status(u.IsActive), StatusPrefix),
    ];

    private static readonly IReadOnlyList<ExportColumn<CompanyListItem>> CompanyColumns =
    [
        ExportColumn<CompanyListItem>.Of("code", "web.platform.fields.code", c => c.Code),
        ExportColumn<CompanyListItem>.Of("shortName", "web.platform.companies.short_name", c => c.ShortName),
        ExportColumn<CompanyListItem>.Of("title", "web.platform.companies.title_field", c => c.Title),
        ExportColumn<CompanyListItem>.Of("taxNumber", "web.platform.companies.tax_number", c => c.TaxNumber),
        ExportColumn<CompanyListItem>.Of("city", "web.platform.address.province", c => c.City),
        ExportColumn<CompanyListItem>.Of("isActive", "web.platform.fields.status", c => Status(c.IsActive), StatusPrefix),
    ];

    /// <summary>The company columns and one column per custom field (<c>customFields.{code}</c>), as on the screen.</summary>
    public static IReadOnlyList<ExportColumn<CompanyListItem>> Companies(IReadOnlyList<CustomFieldFormItem> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return [.. CompanyColumns, .. fields.Select(field => ExportColumn<CompanyListItem>.Named(
            $"customFields.{field.Code}",
            field.Label,
            c => c.CustomFields.TryGetValue(field.Code, out var value) ? CustomFieldValue(field, value) : null))];
    }

    /// <summary>A custom field value as a cell: numbers, dates and booleans keep their type, options show their labels.</summary>
    public static object? CustomFieldValue(CustomFieldFormItem field, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(field);
        string Option(string? code) => field.Options.FirstOrDefault(o => string.Equals(o.Code, code, StringComparison.Ordinal))?.Label ?? code ?? string.Empty;

        return (field.Type, value.ValueKind) switch
        {
            (_, JsonValueKind.Null or JsonValueKind.Undefined) => null,
            (CustomFieldType.Number, JsonValueKind.Number) => value.GetDecimal(),
            (CustomFieldType.Bool, JsonValueKind.True or JsonValueKind.False) => value.GetBoolean(),
            (CustomFieldType.Date, JsonValueKind.String) when DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => date,
            (CustomFieldType.Select, JsonValueKind.String) => Option(value.GetString()),
            (CustomFieldType.MultiSelect, JsonValueKind.Array) => string.Join(", ", value.EnumerateArray().Select(v => Option(v.GetString()))),
            (_, JsonValueKind.String) => value.GetString(),
            _ => value.GetRawText(),
        };
    }

    private static string Status(bool isActive) => isActive ? "active" : "inactive";
}
