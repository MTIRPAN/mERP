using System.Linq.Expressions;

namespace Merp.Application.Abstractions.Jobs;

/// <summary>
/// Runs a service method in the background in the caller's tenant, after the current command commits (D35), e.g. a
/// large import. The call and its arguments are stored, so they must be simple values (ids, not entities).
/// </summary>
public interface IBackgroundTasks
{
    void Enqueue<TService>(Expression<Func<TService, Task>> work)
        where TService : notnull;
}
