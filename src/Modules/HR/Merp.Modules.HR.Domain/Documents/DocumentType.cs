using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Domain.Documents;

public readonly record struct DocumentTypeId(Guid Value) : IEntityId<DocumentTypeId>
{
    public static DocumentTypeId From(Guid value) => new(value);
}

/// <summary>
/// A kind of employee document (identity card, diploma, health report, contract, SRC, passport…) of the tenant's
/// list. The common ones are seeded; the tenant adds its own and retires those it no longer uses (D38). The code
/// never changes.
/// </summary>
[Audited]
public sealed class DocumentType : AuditableAggregateRoot<DocumentTypeId>
{
    public const int CodeMaxLength = 30;

    private DocumentType()
    {
    }

    public string Code { get; private set; } = null!;

    public LocalizedString Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public static DocumentType Create(Code code, LocalizedString name)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        return new DocumentType { Id = EntityId.New<DocumentTypeId>(), Code = code.Value, Name = name, IsActive = true };
    }

    public void Update(LocalizedString name, bool isActive)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsActive = isActive;
    }
}
