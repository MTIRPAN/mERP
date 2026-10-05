using Merp.Application.Abstractions.Messaging;

namespace Merp.Infrastructure.Messaging;

/// <summary>
/// Scoped queue of <see cref="IAfterCommit"/> actions. The unit of work behavior runs it after the outermost
/// command commits and clears it when the command fails.
/// </summary>
public sealed class AfterCommitQueue : IAfterCommit
{
    private readonly List<Func<CancellationToken, Task>> _actions = [];

    public void Enqueue(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var actions = _actions.ToList();
        _actions.Clear();
        foreach (var action in actions)
        {
            await action(cancellationToken);
        }
    }

    internal void Clear() => _actions.Clear();
}
