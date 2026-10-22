namespace Merp.Application.Abstractions.Auditing;

/// <summary>
/// What made the changes of the current command when it is not the user editing directly (D39), e.g. an approved
/// change request: <c>hr.profile-change:PDT-2026-00012</c>. Every audit entry written while it is in use carries it,
/// so the audit trail shows where a change came from.
/// </summary>
public interface IAuditSource
{
    const int MaxLength = 100;

    /// <summary>The source in use, if any.</summary>
    string? Current { get; }

    /// <summary>Uses <paramref name="source"/> until the returned scope is disposed.</summary>
    IDisposable Use(string source);
}
