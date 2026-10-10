using FluentValidation;
using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Audit;

/// <summary>
/// The audit trail of all modules, newest first (D13). Reading it needs <c>platform.audit.read</c> with the Tenant
/// scope, because entries carry no company (D27).
/// </summary>
/// <remarks>
/// Entries of the modules are merged in memory, so a page may lie at most <see cref="MaxDepth"/> entries deep;
/// narrow the period or the record to look further back.
/// </remarks>
public sealed record SearchAuditQuery(
    PageRequest Paging,
    string? Module = null,
    string? EntityType = null,
    string? EntityId = null,
    Guid? UserId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IQuery<PagedResult<AuditItem>>, IRequirePermission
{
    public const int MaxDepth = 10_000;

    public string Permission => PlatformPermissions.Audit.Read;
}

/// <remarks>
/// EntityLabel and FieldLabel are localized names, or the technical names when no text exists. UserName is the
/// display name of the user; null for system changes.
/// </remarks>
public sealed record AuditItem(
    Guid Id,
    string Module,
    DateTimeOffset OccurredAt,
    Guid? UserId,
    string? UserName,
    string EntityType,
    string EntityLabel,
    string EntityId,
    string Action,
    string? Field,
    string? FieldLabel,
    string? OldValue,
    string? NewValue,
    string? CorrelationId);

/// <summary>Audited entity types per module, for the search filter.</summary>
public sealed record GetAuditEntityTypesQuery : IQuery<IReadOnlyList<AuditEntityType>>, IRequirePermission
{
    public string Permission => PlatformPermissions.Audit.Read;
}

public sealed record AuditEntityType(string Module, string EntityType, string Label);

public static class AuditErrors
{
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.audit.errors.tenant_scope_required");
    public const string TooDeepCode = "platform.audit.errors.too_deep";
}

internal sealed class SearchAuditQueryValidator : AbstractValidator<SearchAuditQuery>
{
    public SearchAuditQueryValidator()
    {
        RuleFor(q => q.To)
            .Must((q, to) => q.From is null || to is null || q.From <= to)
            .WithErrorCode(CommonErrors.InvalidDateRange.Code)
            .OverridePropertyName("to");
        RuleFor(q => q.Paging)
            .Must(p => p.Skip + p.Size <= SearchAuditQuery.MaxDepth)
            .WithErrorCode(AuditErrors.TooDeepCode)
            .OverridePropertyName("page");
        RuleFor(q => q.EntityType).MaximumLength(200);
        RuleFor(q => q.EntityId).MaximumLength(200);
    }
}

internal sealed class SearchAuditQueryHandler(
    IEnumerable<IAuditReader> readers,
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ITextLocalizer localizer) : IQueryHandler<SearchAuditQuery, PagedResult<AuditItem>>
{
    public async Task<Result<PagedResult<AuditItem>>> Handle(SearchAuditQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return AuditErrors.TenantScopeRequired;
        }

        var filter = new AuditFilter(Blank(request.EntityType), Blank(request.EntityId), request.UserId, request.From, request.To);
        var take = request.Paging.Skip + request.Paging.Size;
        var records = new List<AuditRecord>();
        var total = 0;

        // DbContexts are not thread safe and readers may share one, so the modules are read one after the other
        foreach (var reader in readers.Where(r => Blank(request.Module) is not { } module || string.Equals(r.Module, module, StringComparison.Ordinal)))
        {
            total += await reader.CountAsync(filter, cancellationToken);
            records.AddRange(await reader.ReadAsync(filter, take, cancellationToken));
        }

        var page = records
            .OrderByDescending(r => r.OccurredAt)
            .ThenByDescending(r => r.Id)
            .Skip(request.Paging.Skip)
            .Take(request.Paging.Size)
            .ToList();

        var userIds = page.Select(r => r.UserId).OfType<Guid>().Distinct().Select(UserId.From).ToList();
        var names = (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id.Value, u => u.DisplayName);

        var labels = new AuditLabels(localizer);
        return new PagedResult<AuditItem>(
            [.. page.Select(r => new AuditItem(
                r.Id,
                r.Module,
                r.OccurredAt,
                r.UserId,
                r.UserId is { } id ? names.GetValueOrDefault(id) : null,
                r.EntityType,
                labels.Entity(r.Module, r.EntityType),
                r.EntityId,
                r.Action,
                r.Field,
                r.Field is null ? null : labels.Field(r.Module, r.EntityType, r.Field),
                r.OldValue,
                r.NewValue,
                r.CorrelationId))],
            request.Paging.PageNumber,
            request.Paging.Size,
            total);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class GetAuditEntityTypesQueryHandler(IEnumerable<IAuditReader> readers, IAccessScopeProvider scopes, ITextLocalizer localizer)
    : IQueryHandler<GetAuditEntityTypesQuery, IReadOnlyList<AuditEntityType>>
{
    public async Task<Result<IReadOnlyList<AuditEntityType>>> Handle(GetAuditEntityTypesQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return AuditErrors.TenantScopeRequired;
        }

        var labels = new AuditLabels(localizer);
        return readers
            .SelectMany(r => r.EntityTypes.Select(type => new AuditEntityType(r.Module, type, labels.Entity(r.Module, type))))
            .OrderBy(t => t.Module, StringComparer.Ordinal)
            .ThenBy(t => t.Label, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true))
            .ToList();
    }
}

/// <summary>
/// Labels of entity types and fields: <c>{module}.entities.{Type}</c> and <c>{module}.entities.{Type}.{Field}</c>
/// (e.g. <c>platform.entities.Company.Address.City</c>). Tables every module has (<c>StoredFile</c>, D31) fall back to
/// <c>common.entities.{Type}</c>. Without a text the technical name is shown.
/// </summary>
internal sealed class AuditLabels(ITextLocalizer localizer)
{
    private const string Common = "common";

    public string Entity(string module, string entityType) =>
        Text($"{module}.entities.{entityType}", Text($"{Common}.entities.{entityType}", entityType));

    /// <remarks>The key of an entity (<c>Id</c>) has one shared text.</remarks>
    public string Field(string module, string entityType, string field) =>
        Text(
            $"{module}.entities.{entityType}.{field}",
            Text($"{Common}.entities.{entityType}.{field}", string.Equals(field, "Id", StringComparison.Ordinal) ? Text(IdKey, field) : field));

    private const string IdKey = "platform.audit.fields.id";

    private string Text(string key, string fallback)
    {
        var text = localizer.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? fallback : text;
    }
}
