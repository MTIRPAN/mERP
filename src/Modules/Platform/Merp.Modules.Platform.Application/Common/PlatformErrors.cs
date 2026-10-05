using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Application.Common;

/// <summary>Business errors of the Platform module; texts in the module's <c>tr-TR.json</c>.</summary>
public static class PlatformErrors
{
    public static readonly Error CompanyCodeTaken = Error.Conflict("platform.companies.code_taken");
    public static readonly Error BranchCodeTaken = Error.Conflict("platform.branches.code_taken");

    public static readonly Error UnknownProvince = Error.Validation("platform.locations.unknown_province").ForField("provinceCode");
    public static readonly Error UnknownDistrict = Error.Validation("platform.locations.unknown_district").ForField("districtId");
    public static readonly Error UnknownCompany = Error.Validation("platform.branches.unknown_company").ForField("companyId");
    public static readonly Error InactiveCompany = Error.Validation("platform.branches.inactive_company").ForField("companyId");
}
