using Merp.Application.Abstractions.Auditing;

namespace Merp.Infrastructure.Persistence.Auditing;

/// <inheritdoc cref="IAuditSource"/>
internal sealed class AuditSource : IAuditSource
{
    public string? Current { get; private set; }

    public void Set(string? source)
    {
        if (source is { Length: > IAuditSource.MaxLength })
        {
            throw new ArgumentException($"An audit source has at most {IAuditSource.MaxLength} characters.", nameof(source));
        }

        Current = string.IsNullOrWhiteSpace(source) ? null : source;
    }
}
