using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.SharedKernel.Numbering;

namespace Merp.Modules.HR.Application;

/// <summary>Permissions of the HR module: <c>hr.{resource}.{action}</c> (docs/domain/yetki-ve-onay.md §1.5).</summary>
public static class HrPermissions
{
    public const string Module = "hr";

    public static class Org
    {
        /// <summary>See units, positions and jobs in the caller's scope (D37).</summary>
        public const string Read = "hr.org.read";

        /// <summary>Create and change units and positions of the companies in scope; jobs with the Tenant scope (D37).</summary>
        public const string Manage = "hr.org.manage";
    }

    public static class Employees
    {
        /// <summary>See employees of the scope (personel kartı, D38).</summary>
        public const string Read = "hr.employees.read";

        /// <summary>Hire and rehire.</summary>
        public const string Create = "hr.employees.create";

        /// <summary>Change the card, assignments and group transfers.</summary>
        public const string Update = "hr.employees.update";

        public const string Terminate = "hr.employees.terminate";

        public const string Export = "hr.employees.export";

        /// <summary>Create a system user for a person, link or unlink one (D39); gives only the Employee role.</summary>
        public const string User = "hr.employees.user";

        public static class Sensitive
        {
            /// <summary>See TCKN and IBAN unmasked and the special category data (D13).</summary>
            public const string Read = "hr.employees.sensitive.read";

            /// <summary>Change the IBAN and the special category data.</summary>
            public const string Update = "hr.employees.sensitive.update";
        }

        public static class Documents
        {
            public const string Read = "hr.employees.documents.read";

            public const string Manage = "hr.employees.documents.manage";
        }
    }

    public static class Leave
    {
        public static class Settings
        {
            /// <summary>See leave types, policies and their assignment rules, and which policy applies to an employee (D41).</summary>
            public const string Read = "hr.leave.settings.read";

            /// <summary>Change them; records of every company need the Tenant scope.</summary>
            public const string Manage = "hr.leave.settings.manage";
        }
    }

    /// <summary>Self service of the person linked to the user (D39); the Employee system role holds <c>hr.self.*</c>.</summary>
    public static class Self
    {
        /// <summary>See one's own card ("Kartım").</summary>
        public const string ProfileRead = "hr.self.profile.read";

        /// <summary>Ask for changes of one's contacts, family and IBAN.</summary>
        public const string ProfileRequest = "hr.self.profile.request";
    }
}

/// <summary>Permission catalog of the HR module (docs/domain/yetki-ve-onay.md §1.5).</summary>
public sealed class HrPermissionDefinitions : IPermissionDefinitionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(HrPermissions.Org.Read),
        new(HrPermissions.Org.Manage),
        new(HrPermissions.Employees.Read),
        new(HrPermissions.Employees.Create),
        new(HrPermissions.Employees.Update),
        new(HrPermissions.Employees.Terminate),
        new(HrPermissions.Employees.Export),
        new(HrPermissions.Employees.User),
        new(HrPermissions.Employees.Sensitive.Read),
        new(HrPermissions.Employees.Sensitive.Update),
        new(HrPermissions.Employees.Documents.Read),
        new(HrPermissions.Employees.Documents.Manage),
        new(HrPermissions.Leave.Settings.Read),
        new(HrPermissions.Leave.Settings.Manage),
        new(HrPermissions.Self.ProfileRead),
        new(HrPermissions.Self.ProfileRequest),
    ];
}

/// <summary>Scope types HR defines and resolves (docs/domain/yetki-ve-onay.md §1.2, D37).</summary>
public sealed class HrScopeTypes : IScopeTypeProvider
{
    public IEnumerable<ScopeTypeDefinition> GetScopeTypes() =>
    [
        new(AccessScopeTypes.OrgUnit, HrPermissions.Module, RequiresId: true, SupportsSubUnits: true, Order: 30),
        new(AccessScopeTypes.ManagedTeam, HrPermissions.Module, RequiresId: false, Order: 40),
    ];
}

/// <summary>Entities of HR that carry custom fields (D12, D30).</summary>
public sealed class HrCustomFieldEntities : ICustomFieldEntityProvider
{
    public const string Employee = "hr.employee";

    public IEnumerable<CustomFieldEntityType> GetEntityTypes() =>
    [
        new(Employee, "hr.custom_fields.entities.employee"),
    ];
}

/// <summary>Numbered records of HR (P5, D29): the registry number (sicil no) of an employment, per company.</summary>
public sealed class HrNumberSequences : INumberSequenceDefinitionProvider
{
    public const string Employee = "hr.employee";

    /// <summary>A person's change request (D39), numbered per company and year.</summary>
    public const string ProfileChange = "hr.profile-change";

    public IEnumerable<NumberSequenceDefinition> GetSequences() =>
    [
        new(Employee, "{seq:6}", NumberResetPeriod.Never),
        new(ProfileChange, "PDT-{yyyy}-{seq:5}", NumberResetPeriod.Yearly),
    ];
}
