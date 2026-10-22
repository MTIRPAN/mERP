using Merp.Application.Abstractions.Auditing;

namespace Merp.Infrastructure.Persistence.Auditing;

/// <inheritdoc cref="IAuditSource"/>
internal sealed class AuditSource : IAuditSource
{
    public string? Current { get; private set; }

    public IDisposable Use(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (source.Length > IAuditSource.MaxLength)
        {
            throw new ArgumentException($"An audit source has at most {IAuditSource.MaxLength} characters.", nameof(source));
        }

        var previous = Current;
        Current = source;
        return new Restore(this, previous);
    }

    private sealed class Restore(AuditSource owner, string? previous) : IDisposable
    {
        public void Dispose() => owner.Current = previous;
    }
}
