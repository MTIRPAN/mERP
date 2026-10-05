using Merp.Application.Abstractions.Security;

namespace Merp.Modules.Platform.Application.Access;

/// <summary>
/// The system roles seeded in every tenant (docs/domain/yetki-ve-onay.md §1.4). Default permissions are patterns
/// matched against the permission catalog, so permissions added by later releases join the matching system roles
/// automatically. Names are localization keys <c>roles.{code}</c>.
/// </summary>
public static class SystemRoles
{
    public static readonly SystemRoleDefinition SystemAdministrator = new("system-admin", AccessScopeTypes.Tenant, ["platform.*"]);
    public static readonly SystemRoleDefinition HrManager = new("hr-manager", AccessScopeTypes.Company, ["hr.*", "recruitment.*"]);
    public static readonly SystemRoleDefinition HrSpecialist = new(
        "hr-specialist",
        AccessScopeTypes.Company,
        ["hr.employees.*", "!hr.employees.sensitive.*", "hr.leave.*", "hr.org.read"]);
    public static readonly SystemRoleDefinition Manager = new(
        "manager",
        AccessScopeTypes.ManagedTeam,
        ["hr.employees.read", "hr.leave.requests.read", "hr.leave.calendar.read"]);
    public static readonly SystemRoleDefinition Employee = new("employee", AccessScopeTypes.Self, ["hr.self.*"]);
    public static readonly SystemRoleDefinition Recruiter = new("recruiter", AccessScopeTypes.Company, ["recruitment.*"]);
    public static readonly SystemRoleDefinition Interviewer = new(
        "interviewer",
        AccessScopeTypes.Self,
        ["recruitment.interviews.read", "recruitment.feedback.submit"]);

    public static readonly IReadOnlyList<SystemRoleDefinition> All =
        [SystemAdministrator, HrManager, HrSpecialist, Manager, Employee, Recruiter, Interviewer];

    public static SystemRoleDefinition? Find(string code) => All.SingleOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <param name="Code">Role code, lower case (role codes are not entered by users for system roles).</param>
/// <param name="SuggestedScope">Scope type the role is usually assigned with.</param>
/// <param name="DefaultPatterns">
/// <c>module.*</c> grants every permission under the prefix, an exact code grants one, a leading <c>!</c> excludes.
/// </param>
public sealed record SystemRoleDefinition(string Code, string SuggestedScope, IReadOnlyList<string> DefaultPatterns)
{
    public string NameKey => "roles." + Code;

    /// <summary>The permissions of <paramref name="catalog"/> the default patterns grant.</summary>
    public IEnumerable<string> DefaultPermissions(IEnumerable<string> catalog) => catalog.Where(Grants);

    public bool Grants(string permission)
    {
        var included = DefaultPatterns.Any(p => !p.StartsWith('!') && Matches(p, permission));
        var excluded = DefaultPatterns.Any(p => p.StartsWith('!') && Matches(p[1..], permission));
        return included && !excluded;
    }

    private static bool Matches(string pattern, string permission) =>
        pattern.EndsWith(".*", StringComparison.Ordinal)
            ? permission.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : string.Equals(pattern, permission, StringComparison.Ordinal);
}
