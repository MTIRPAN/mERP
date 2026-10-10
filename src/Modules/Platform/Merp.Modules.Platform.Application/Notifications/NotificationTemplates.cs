using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Notifications;

// E-mail templates of notifications (PLAN P8, D32). Administration needs the Tenant scope; the editor comes with P12.

public sealed record NotificationTypeItem(string Type, string Label);

/// <summary>Notification types of all modules, for template and preference screens.</summary>
public sealed record GetNotificationTypesQuery : IQuery<IReadOnlyList<NotificationTypeItem>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.NotificationTemplates.Read;
}

/// <remarks>Send Version back when updating (optimistic concurrency).</remarks>
public sealed record NotificationTemplateItem(
    Guid Id,
    string? NotificationType,
    Guid? CompanyId,
    string? CompanyName,
    string Culture,
    string Subject,
    string Body,
    uint Version);

/// <summary>The tenant's templates and the built-in layout used where none applies.</summary>
public sealed record NotificationTemplateList(IReadOnlyList<NotificationTemplateItem> Items, string DefaultSubject, string DefaultBody);

public sealed record GetNotificationTemplatesQuery : IQuery<NotificationTemplateList>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.NotificationTemplates.Read;
}

/// <summary>
/// Adds a template. NotificationType null applies to every type, CompanyId null to every company. Subject and body
/// are Liquid with <c>title</c>, <c>body</c>, <c>link</c>, <c>recipient.name</c>, <c>company.name</c>, <c>args.*</c>
/// and <c>texts.open</c>, <c>texts.footer</c>.
/// </summary>
public sealed record CreateNotificationTemplateCommand(string? NotificationType, Guid? CompanyId, string? Culture, string? Subject, string? Body)
    : ICommand<Guid>, IRequirePermission, INotificationTemplateFields
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.NotificationTemplates.Manage;
}

public sealed record UpdateNotificationTemplateCommand(uint Version, string? Subject, string? Body)
    : ICommand, IRequirePermission, INotificationTemplateFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.NotificationTemplates.Manage;
}

/// <summary>Removes a template; the next more general one applies again.</summary>
public sealed record DeleteNotificationTemplateCommand(Guid Id) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.NotificationTemplates.Manage;
}

public interface INotificationTemplateFields
{
    string? Subject { get; }

    string? Body { get; }
}

internal static class NotificationTemplateRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator, ITemplateRenderer renderer)
        where T : INotificationTemplateFields
    {
        validator.RuleFor(c => c.Subject).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(NotificationTemplate.SubjectMaxLength).Custom((value, context) => Check(value, context, renderer));
        validator.RuleFor(c => c.Body).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(NotificationTemplate.BodyMaxLength).Custom((value, context) => Check(value, context, renderer));
    }

    /// <summary>A template that does not parse is refused with its line and the problem.</summary>
    private static void Check<T>(string? template, ValidationContext<T> context, ITemplateRenderer renderer)
    {
        var result = renderer.Validate(template!);
        if (result.IsFailure)
        {
            context.AddFailure(new FluentValidation.Results.ValidationFailure(context.PropertyPath, result.Error.Code)
            {
                ErrorCode = result.Error.Code,
                FormattedMessagePlaceholderValues = result.Error.Args.ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal),
            });
        }
    }
}

internal sealed class CreateNotificationTemplateCommandValidator : AbstractValidator<CreateNotificationTemplateCommand>
{
    public CreateNotificationTemplateCommandValidator(ITemplateRenderer renderer, NotificationTypeRegistry types)
    {
        RuleFor(c => c.Culture).NotEmpty();
        RuleFor(c => c.NotificationType).Must(type => types.Find(type) is not null).WithErrorCode(NotificationErrors.UnknownTypeCode)
            .When(c => c.NotificationType is not null);
        NotificationTemplateRules.AddFieldRules(this, renderer);
    }
}

internal sealed class UpdateNotificationTemplateCommandValidator : AbstractValidator<UpdateNotificationTemplateCommand>
{
    public UpdateNotificationTemplateCommandValidator(ITemplateRenderer renderer) => NotificationTemplateRules.AddFieldRules(this, renderer);
}

internal sealed class GetNotificationTypesQueryHandler(NotificationTypeRegistry types, ITextLocalizer localizer)
    : IQueryHandler<GetNotificationTypesQuery, IReadOnlyList<NotificationTypeItem>>
{
    public Task<Result<IReadOnlyList<NotificationTypeItem>>> Handle(GetNotificationTypesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationTypeItem> items = [.. types.All.Select(t => new NotificationTypeItem(t.Type, localizer.Get(t.LabelKey))).OrderBy(t => t.Type, StringComparer.Ordinal)];
        return Task.FromResult(Result.Success(items));
    }
}

internal sealed class GetNotificationTemplatesQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes)
    : IQueryHandler<GetNotificationTemplatesQuery, NotificationTemplateList>
{
    public async Task<Result<NotificationTemplateList>> Handle(GetNotificationTemplatesQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return NotificationErrors.TenantScopeRequired;
        }

        var rows = await db.NotificationTemplates.AsNoTracking()
            .Select(t => new { Template = t, Version = EF.Property<uint>(t, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        var companyIds = rows.Select(r => r.Template.CompanyId).OfType<Guid>().Select(CompanyId.From).Distinct().ToList();
        var companies = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id.Value, c => c.ShortName, cancellationToken);

        var items = rows
            .Select(r => new NotificationTemplateItem(
                r.Template.Id.Value,
                r.Template.NotificationType,
                r.Template.CompanyId,
                r.Template.CompanyId is { } id ? companies.GetValueOrDefault(id) : null,
                r.Template.Culture,
                r.Template.Subject,
                r.Template.Body,
                r.Version))
            .OrderBy(t => t.NotificationType ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.CompanyName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.Culture, StringComparer.Ordinal)
            .ToList();
        return new NotificationTemplateList(items, EmailComposer.DefaultSubject, EmailComposer.DefaultBody);
    }
}

internal sealed class CreateNotificationTemplateCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, ILocalizationSource localization)
    : ICommandHandler<CreateNotificationTemplateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateNotificationTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return NotificationErrors.TenantScopeRequired;
        }

        var errors = new List<FieldError>();
        var culture = (await localization.GetAsync(cancellationToken)).Find(request.Culture);
        if (culture is null)
        {
            errors.Add(new FieldError("culture", CommonErrors.InvalidCulture.Code));
        }

        if (request.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == CompanyId.From(companyId), cancellationToken))
        {
            errors.Add(new FieldError("companyId", CommonErrors.NotFound.Code));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var exists = await db.NotificationTemplates.AnyAsync(
            t => t.NotificationType == request.NotificationType && t.CompanyId == request.CompanyId && t.Culture == culture!.Code,
            cancellationToken);
        if (exists)
        {
            return Error.Validation([OrganizationErrors.Duplicate("notificationType")]);
        }

        var template = NotificationTemplate.Create(request.NotificationType, request.CompanyId, culture!.Code, request.Subject!, request.Body!);
        db.NotificationTemplates.Add(template);
        return template.Id.Value;
    }
}

internal sealed class UpdateNotificationTemplateCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<UpdateNotificationTemplateCommand>
{
    public async Task<Result> Handle(UpdateNotificationTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return NotificationErrors.TenantScopeRequired;
        }

        var template = await db.NotificationTemplates.SingleOrDefaultAsync(t => t.Id == NotificationTemplateId.From(request.Id), cancellationToken);
        if (template is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(template).ExpectVersion(request.Version);
        template.Update(request.Subject!, request.Body!);
        return Result.Success();
    }
}

internal sealed class DeleteNotificationTemplateCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<DeleteNotificationTemplateCommand>
{
    public async Task<Result> Handle(DeleteNotificationTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return NotificationErrors.TenantScopeRequired;
        }

        var template = await db.NotificationTemplates.SingleOrDefaultAsync(t => t.Id == NotificationTemplateId.From(request.Id), cancellationToken);
        if (template is null)
        {
            return CommonErrors.NotFound;
        }

        db.NotificationTemplates.Remove(template);
        return Result.Success();
    }
}
