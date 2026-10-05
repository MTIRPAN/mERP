using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Roles;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Roles;

/// <summary>Creates a custom role. Needs <c>platform.roles.manage</c> with the Tenant scope.</summary>
/// <remarks>Name holds a text per culture, e.g. <c>{"tr-TR": "Bordro Uzmanı"}</c>.</remarks>
public sealed record CreateRoleCommand(
    string? Code,
    IReadOnlyDictionary<string, string?>? Name,
    string? Description,
    IReadOnlyList<string>? Permissions) : ICommand<Guid>, IRequirePermission, IRoleFields
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Roles.Manage;
}

/// <summary>Renames a role and replaces its permissions (the permission matrix). The code never changes.</summary>
/// <remarks>Version is the one the client read; a concurrent change ends in 409.</remarks>
public sealed record UpdateRoleCommand(
    uint Version,
    IReadOnlyDictionary<string, string?>? Name,
    string? Description,
    IReadOnlyList<string>? Permissions) : ICommand, IRequirePermission, IRoleFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Roles.Manage;
}

/// <summary>Deletes a custom role that nobody holds. System roles cannot be deleted.</summary>
public sealed record DeleteRoleCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => PlatformPermissions.Roles.Manage;
}

/// <summary>Resets a system role's name and permissions to the defaults ("varsayılana dön").</summary>
public sealed record ResetRoleCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => PlatformPermissions.Roles.Manage;
}

public interface IRoleFields
{
    IReadOnlyDictionary<string, string?>? Name { get; }

    string? Description { get; }

    IReadOnlyList<string>? Permissions { get; }
}

internal static class RoleRules
{
    public const string UnknownPermissionCode = "platform.roles.errors.unknown_permission";
    public static readonly Error SystemRole = Error.Conflict("platform.roles.errors.system_role");
    public static readonly Error NotASystemRole = Error.Conflict("platform.roles.errors.not_a_system_role");
    public static readonly Error InUse = Error.Conflict("platform.roles.errors.in_use");

    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IRoleFields
    {
        validator.RuleFor(r => r.Name).Custom((name, context) =>
        {
            var result = LocalizedString.Create(name);
            if (result.IsFailure)
            {
                context.AddFailure(new FluentValidation.Results.ValidationFailure("Name", result.Error.Code) { ErrorCode = result.Error.Code });
            }
            else if (result.Value.Values.Values.Any(text => text.Length > 100))
            {
                context.AddFailure(new FluentValidation.Results.ValidationFailure("Name", "common.errors.max_length")
                {
                    ErrorCode = "common.errors.max_length",
                    FormattedMessagePlaceholderValues = new Dictionary<string, object> { ["MaxLength"] = 100 },
                });
            }
        });
        validator.RuleFor(r => r.Description).MaximumLength(Role.DescriptionMaxLength);
        validator.RuleFor(r => r.Permissions).NotNull();
    }

    /// <summary>Unknown codes are reported on the <c>permissions</c> field, with the first one as argument.</summary>
    public static async Task<Result<IReadOnlyList<string>>> CheckPermissionsAsync(IPlatformDbContext db, IReadOnlyList<string> requested, CancellationToken cancellationToken)
    {
        var codes = requested.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToList();
        var known = await db.Permissions.Where(p => codes.Contains(p.Code)).Select(p => p.Code).ToListAsync(cancellationToken);
        var unknown = codes.Except(known, StringComparer.Ordinal).FirstOrDefault();
        return unknown is null
            ? codes
            : Error.Validation([new FieldError("permissions", UnknownPermissionCode, new Dictionary<string, object?> { ["permission"] = unknown })]);
    }
}

internal sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(r => r.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, Role.CodeMaxLength));
        RoleRules.AddFieldRules(this);
    }
}

internal sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator() => RoleRules.AddFieldRules(this);
}

internal sealed class CreateRoleCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes) : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var code = Code.Create(request.Code, Role.CodeMaxLength).Value;
        if (await db.Roles.AnyAsync(r => r.Code == code.Value, cancellationToken))
        {
            return Error.Validation([new FieldError("code", CommonErrors.Duplicate.Code)]);
        }

        var permissions = await RoleRules.CheckPermissionsAsync(db, request.Permissions!, cancellationToken);
        if (permissions.IsFailure)
        {
            return permissions.Error;
        }

        var role = Role.Create(code, LocalizedString.Create(request.Name).Value, request.Description, permissions.Value);
        db.Roles.Add(role);
        return role.Id.Value;
    }
}

internal sealed class UpdateRoleCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, IAccessCache accessCache, IAfterCommit afterCommit)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var id = RoleId.From(request.Id);
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return CommonErrors.NotFound;
        }

        var permissions = await RoleRules.CheckPermissionsAsync(db, request.Permissions!, cancellationToken);
        if (permissions.IsFailure)
        {
            return permissions.Error;
        }

        db.Entry(role).ExpectVersion(request.Version);
        role.Rename(LocalizedString.Create(request.Name).Value, request.Description);
        if (role.SetPermissions(permissions.Value))
        {
            afterCommit.Enqueue(accessCache.InvalidateAllAsync);
        }

        return Result.Success();
    }
}

internal sealed class DeleteRoleCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes) : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var id = RoleId.From(request.Id);
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return CommonErrors.NotFound;
        }

        if (role.IsSystem)
        {
            return RoleRules.SystemRole;
        }

        if (await db.Users.AnyAsync(u => u.Assignments.Any(a => a.RoleId == id), cancellationToken))
        {
            return RoleRules.InUse;
        }

        // Master data: the soft delete interceptor keeps the row (D9)
        db.Roles.Remove(role);
        return Result.Success();
    }
}

internal sealed class ResetRoleCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ITextLocalizer localizer,
    IAccessCache accessCache,
    IAfterCommit afterCommit) : ICommandHandler<ResetRoleCommand>
{
    public async Task<Result> Handle(ResetRoleCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return TenantAdministration.TenantScopeRequired;
        }

        var id = RoleId.From(request.Id);
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return CommonErrors.NotFound;
        }

        if (!role.IsSystem || SystemRoles.Find(role.Code) is not { } definition)
        {
            return RoleRules.NotASystemRole;
        }

        var catalog = await db.Permissions.Select(p => p.Code).ToListAsync(cancellationToken);
        role.Rename(SystemRoleNames.Default(definition, localizer), null);
        role.SetPermissions(definition.DefaultPermissions(catalog));
        afterCommit.Enqueue(accessCache.InvalidateAllAsync);
        return Result.Success();
    }
}

/// <summary>Default names of system roles from the localization texts (<c>roles.{code}</c>) of the active cultures.</summary>
public static class SystemRoleNames
{
    public static LocalizedString Default(SystemRoleDefinition definition, ITextLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(localizer);
        return LocalizedString.Of("tr-TR", localizer.Get(definition.NameKey, culture: "tr-TR"));
    }
}
