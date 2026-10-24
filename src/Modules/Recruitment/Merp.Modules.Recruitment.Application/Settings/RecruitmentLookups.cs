using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Settings;

/// <summary>The choices of the recruitment lists (D47), in their order; a list when <see cref="Kind"/> is given.</summary>
public sealed record GetRecruitmentLookupsQuery(LookupKind? Kind = null, bool IncludeInactive = false)
    : IQuery<IReadOnlyList<RecruitmentLookupItem>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Settings.Read;
}

/// <remarks>Name is in the user's language, Names in each language for the form; Version is sent back when updating.</remarks>
public sealed record RecruitmentLookupItem(
    Guid Id,
    LookupKind Kind,
    string Code,
    string Name,
    IReadOnlyDictionary<string, string> Names,
    int SortOrder,
    bool IsActive,
    uint Version);

/// <summary>Adds a choice to a list; the code is unique within the list. Needs the Tenant scope.</summary>
public sealed record CreateRecruitmentLookupCommand(LookupKind Kind, string? Code, IReadOnlyDictionary<string, string?>? Name, int SortOrder = 0)
    : ICommand<Guid>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Settings.Manage;
}

/// <summary>Renames, reorders or retires a choice; the code and the list never change. Needs the Tenant scope.</summary>
public sealed record UpdateRecruitmentLookupCommand(uint Version, IReadOnlyDictionary<string, string?>? Name, int SortOrder, bool IsActive)
    : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Settings.Manage;
}

internal sealed class CreateRecruitmentLookupCommandValidator : AbstractValidator<CreateRecruitmentLookupCommand>
{
    public CreateRecruitmentLookupCommandValidator(ILocalizationSource localization)
    {
        RuleFor(l => l.Kind).IsInEnum();
        RuleFor(l => l.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, RecruitmentLookup.CodeMaxLength));
        RecruitmentSettingRules.AddNameRules(this, l => l.Name, localization, RecruitmentLookup.NameMaxLength);
        RuleFor(l => l.SortOrder).InclusiveBetween(0, 9999);
    }
}

internal sealed class UpdateRecruitmentLookupCommandValidator : AbstractValidator<UpdateRecruitmentLookupCommand>
{
    public UpdateRecruitmentLookupCommandValidator(ILocalizationSource localization)
    {
        RecruitmentSettingRules.AddNameRules(this, l => l.Name, localization, RecruitmentLookup.NameMaxLength);
        RuleFor(l => l.SortOrder).InclusiveBetween(0, 9999);
    }
}

internal sealed class GetRecruitmentLookupsQueryHandler(IRecruitmentDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetRecruitmentLookupsQuery, IReadOnlyList<RecruitmentLookupItem>>
{
    public async Task<Result<IReadOnlyList<RecruitmentLookupItem>>> Handle(GetRecruitmentLookupsQuery request, CancellationToken cancellationToken)
    {
        var lookups = await db.Lookups.AsNoTracking()
            .Where(l => (request.Kind == null || l.Kind == request.Kind) && (request.IncludeInactive || l.IsActive))
            .Select(l => new { Lookup = l, Version = EF.Property<uint>(l, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        return lookups
            .OrderBy(l => l.Lookup.Kind)
            .ThenBy(l => l.Lookup.SortOrder)
            .ThenBy(l => l.Lookup.Code, StringComparer.Ordinal)
            .Select(l => new RecruitmentLookupItem(
                l.Lookup.Id.Value,
                l.Lookup.Kind,
                l.Lookup.Code,
                l.Lookup.Name.Get(currentUser.Culture, "tr-TR"),
                l.Lookup.Name.Values,
                l.Lookup.SortOrder,
                l.Lookup.IsActive,
                l.Version))
            .ToList();
    }
}

internal sealed class CreateRecruitmentLookupCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<CreateRecruitmentLookupCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRecruitmentLookupCommand request, CancellationToken cancellationToken)
    {
        if (!await RecruitmentSettingRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return RecruitmentSettingsErrors.TenantScopeRequired;
        }

        var code = Code.Create(request.Code, RecruitmentLookup.CodeMaxLength).Value;
        if (await db.Lookups.AnyAsync(l => l.Kind == request.Kind && l.Code == code.Value, cancellationToken))
        {
            return Error.Validation([new FieldError("code", RecruitmentSettingsErrors.DuplicateCodeCode)]);
        }

        var lookup = RecruitmentLookup.Create(request.Kind, code, LocalizedString.Create(request.Name).Value, request.SortOrder);
        db.Lookups.Add(lookup);
        return lookup.Id.Value;
    }
}

internal sealed class UpdateRecruitmentLookupCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<UpdateRecruitmentLookupCommand>
{
    public async Task<Result> Handle(UpdateRecruitmentLookupCommand request, CancellationToken cancellationToken)
    {
        var lookup = await db.Lookups.SingleOrDefaultAsync(l => l.Id == RecruitmentLookupId.From(request.Id), cancellationToken);
        if (lookup is null)
        {
            return CommonErrors.NotFound;
        }

        if (!await RecruitmentSettingRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return RecruitmentSettingsErrors.TenantScopeRequired;
        }

        db.Entry(lookup).ExpectVersion(request.Version);
        lookup.Update(LocalizedString.Create(request.Name).Value, request.SortOrder, request.IsActive);
        return Result.Success();
    }
}
