using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Numbering;
using Merp.SharedKernel.Numbering;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Numbering;

/// <summary>Clears the tenant's cached number patterns; call it after the command commits.</summary>
public interface INumberingCache
{
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Number sequences of all modules: the defaults and the companies' own patterns (D29).</summary>
public sealed record GetNumberSequencesQuery : IQuery<IReadOnlyList<NumberSequenceItem>>, IRequirePermission
{
    public string Permission => PlatformPermissions.Numbering.Read;
}

/// <remarks>Label is the localized name of the document type; Example is the first number of today's period.</remarks>
public sealed record NumberSequenceItem(
    Guid Id,
    string DocumentType,
    string Label,
    Guid? CompanyId,
    string? CompanyName,
    string Pattern,
    NumberResetPeriod ResetPeriod,
    string Example,
    uint Version);

/// <summary>Gives a company its own pattern for a document type; its numbers keep their own counter anyway.</summary>
public sealed record AddCompanyNumberSequenceCommand(string? DocumentType, Guid? CompanyId, string? Pattern, NumberResetPeriod ResetPeriod)
    : ICommand<Guid>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Numbering.Manage;
}

/// <summary>Changes a pattern and its reset period. The counter goes on; a pattern without the year may repeat old numbers.</summary>
public sealed record UpdateNumberSequenceCommand(uint Version, string? Pattern, NumberResetPeriod ResetPeriod) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Numbering.Manage;
}

/// <summary>Removes a company's own pattern; the default applies again. Defaults cannot be removed.</summary>
public sealed record DeleteNumberSequenceCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => PlatformPermissions.Numbering.Manage;
}

public static class NumberingErrors
{
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.numbering.errors.tenant_scope_required");
    public static readonly Error DefaultRequired = Error.Conflict("platform.numbering.errors.default_required");
    public const string UnknownDocumentTypeCode = "platform.numbering.errors.unknown_document_type";
    public const string UnknownCompanyCode = "platform.numbering.errors.unknown_company";
}

internal sealed class AddCompanyNumberSequenceCommandValidator : AbstractValidator<AddCompanyNumberSequenceCommand>
{
    public AddCompanyNumberSequenceCommandValidator()
    {
        RuleFor(c => c.DocumentType).NotEmpty();
        RuleFor(c => c.CompanyId).NotEmpty();
        RuleFor(c => c.Pattern).NotEmpty();
        RuleFor(c => c.ResetPeriod).IsInEnum();
    }
}

internal sealed class UpdateNumberSequenceCommandValidator : AbstractValidator<UpdateNumberSequenceCommand>
{
    public UpdateNumberSequenceCommandValidator()
    {
        RuleFor(c => c.Pattern).NotEmpty();
        RuleFor(c => c.ResetPeriod).IsInEnum();
    }
}

internal sealed class GetNumberSequencesQueryHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ITextLocalizer localizer,
    TimeProvider timeProvider) : IQueryHandler<GetNumberSequencesQuery, IReadOnlyList<NumberSequenceItem>>
{
    public async Task<Result<IReadOnlyList<NumberSequenceItem>>> Handle(GetNumberSequencesQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return NumberingErrors.TenantScopeRequired;
        }

        var rows = await db.NumberSequences.AsNoTracking()
            .Select(s => new { Sequence = s, Version = EF.Property<uint>(s, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        var companyIds = rows.Select(r => r.Sequence.CompanyId).OfType<Guid>().Select(CompanyId.From).Distinct().ToList();
        var companies = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id.Value, c => c.ShortName, cancellationToken);
        var today = PlatformClock.Today(timeProvider);

        return rows
            .Select(r => new NumberSequenceItem(
                r.Sequence.Id.Value,
                r.Sequence.DocumentType,
                NumberingLabels.Of(localizer, r.Sequence.DocumentType),
                r.Sequence.CompanyId,
                r.Sequence.CompanyId is { } id ? companies.GetValueOrDefault(id) : null,
                r.Sequence.Pattern,
                r.Sequence.ResetPeriod,
                NumberPattern.Format(r.Sequence.Pattern, today, 1),
                r.Version))
            .OrderBy(i => i.DocumentType, StringComparer.Ordinal)
            .ThenBy(i => i.CompanyId is null ? 0 : 1)
            .ThenBy(i => i.CompanyName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true))
            .ToList();
    }
}

internal static class NumberingLabels
{
    /// <summary><c>recruitment.offer</c> → text of <c>recruitment.numbering.offer</c>, or the type itself.</summary>
    public static string Of(ITextLocalizer localizer, string documentType)
    {
        var dot = documentType.IndexOf('.', StringComparison.Ordinal);
        var key = dot < 0 ? documentType : $"{documentType[..dot]}.numbering.{documentType[(dot + 1)..]}";
        var text = localizer.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? documentType : text;
    }
}

internal sealed class AddCompanyNumberSequenceCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    INumberingCache cache,
    IAfterCommit afterCommit) : ICommandHandler<AddCompanyNumberSequenceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddCompanyNumberSequenceCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return NumberingErrors.TenantScopeRequired;
        }

        var documentType = request.DocumentType!.Trim();
        var companyId = request.CompanyId!.Value;
        if (!await db.NumberSequences.AnyAsync(s => s.DocumentType == documentType && s.CompanyId == null, cancellationToken))
        {
            return Error.Validation([new FieldError("documentType", NumberingErrors.UnknownDocumentTypeCode)]);
        }

        if (!await db.Companies.AnyAsync(c => c.Id == CompanyId.From(companyId), cancellationToken))
        {
            return Error.Validation([new FieldError("companyId", NumberingErrors.UnknownCompanyCode)]);
        }

        if (await db.NumberSequences.AnyAsync(s => s.DocumentType == documentType && s.CompanyId == companyId, cancellationToken))
        {
            return Error.Validation([new FieldError("companyId", CommonErrors.Duplicate.Code)]);
        }

        var sequence = NumberSequence.Create(documentType, companyId, request.Pattern!.Trim(), request.ResetPeriod);
        if (sequence.IsFailure)
        {
            return sequence.Error;
        }

        db.NumberSequences.Add(sequence.Value);
        afterCommit.Enqueue(cache.InvalidateAsync);
        return sequence.Value.Id.Value;
    }
}

internal sealed class UpdateNumberSequenceCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    INumberingCache cache,
    IAfterCommit afterCommit) : ICommandHandler<UpdateNumberSequenceCommand>
{
    public async Task<Result> Handle(UpdateNumberSequenceCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return NumberingErrors.TenantScopeRequired;
        }

        var sequence = await db.NumberSequences.SingleOrDefaultAsync(s => s.Id == NumberSequenceId.From(request.Id), cancellationToken);
        if (sequence is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(sequence).ExpectVersion(request.Version);
        var changed = sequence.Change(request.Pattern!.Trim(), request.ResetPeriod);
        if (changed.IsSuccess)
        {
            afterCommit.Enqueue(cache.InvalidateAsync);
        }

        return changed;
    }
}

internal sealed class DeleteNumberSequenceCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    INumberingCache cache,
    IAfterCommit afterCommit) : ICommandHandler<DeleteNumberSequenceCommand>
{
    public async Task<Result> Handle(DeleteNumberSequenceCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return NumberingErrors.TenantScopeRequired;
        }

        var sequence = await db.NumberSequences.SingleOrDefaultAsync(s => s.Id == NumberSequenceId.From(request.Id), cancellationToken);
        if (sequence is null)
        {
            return CommonErrors.NotFound;
        }

        if (sequence.IsDefault)
        {
            return NumberingErrors.DefaultRequired;
        }

        db.NumberSequences.Remove(sequence);
        afterCommit.Enqueue(cache.InvalidateAsync);
        return Result.Success();
    }
}
