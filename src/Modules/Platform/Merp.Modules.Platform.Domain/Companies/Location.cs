using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Domain.Companies;

/// <summary>
/// Turkish postal address resolved against the province and district reference data: the codes are kept for
/// filtering, the names inside <see cref="Address"/> for documents.
/// </summary>
public sealed record Location(Address Address, int ProvinceCode, int DistrictId);
