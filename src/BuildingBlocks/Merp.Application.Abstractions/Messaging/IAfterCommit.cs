namespace Merp.Application.Abstractions.Messaging;

/// <summary>
/// Work that must only happen once the command's transaction has committed, e.g. clearing a cache: done earlier, a
/// concurrent request could reload and cache the old data. Actions are dropped when the command fails.
/// </summary>
public interface IAfterCommit
{
    void Enqueue(Func<CancellationToken, Task> action);
}
