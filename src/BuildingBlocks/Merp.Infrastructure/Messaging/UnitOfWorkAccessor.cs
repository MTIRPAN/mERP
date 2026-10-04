namespace Merp.Infrastructure.Messaging;

/// <summary>Tracks the units of work that are open in the current scope (innermost last).</summary>
public sealed class UnitOfWorkAccessor
{
    private readonly Stack<IUnitOfWork> _active = new();

    public IUnitOfWork? Current => _active.TryPeek(out var unitOfWork) ? unitOfWork : null;

    public bool IsActive(IUnitOfWork unitOfWork) => _active.Contains(unitOfWork);

    internal void Push(IUnitOfWork unitOfWork) => _active.Push(unitOfWork);

    internal void Pop(IUnitOfWork unitOfWork)
    {
        if (!_active.TryPeek(out var current) || !ReferenceEquals(current, unitOfWork))
        {
            throw new InvalidOperationException("Units of work must be closed in the reverse order they were opened.");
        }

        _active.Pop();
    }
}
