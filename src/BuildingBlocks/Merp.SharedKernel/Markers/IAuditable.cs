namespace Merp.SharedKernel.Markers;

/// <summary>Creation and modification stamps, set by the EF interceptor (never by domain code).</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset? ModifiedAt { get; }

    Guid? ModifiedBy { get; }
}
