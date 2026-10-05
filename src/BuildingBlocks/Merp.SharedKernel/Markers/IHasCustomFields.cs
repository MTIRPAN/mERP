using Merp.SharedKernel.ValueObjects;

namespace Merp.SharedKernel.Markers;

/// <summary>Entity that carries tenant-defined custom field values (D12).</summary>
public interface IHasCustomFields
{
    CustomFieldValues CustomFields { get; }
}
