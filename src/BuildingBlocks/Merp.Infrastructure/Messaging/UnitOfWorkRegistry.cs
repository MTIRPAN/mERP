using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Messaging;

/// <summary>Maps each module's application assembly to the unit of work of that module.</summary>
public sealed class UnitOfWorkRegistry
{
    private readonly Dictionary<Assembly, Type> _unitOfWorkTypes = [];

    public void Register(Assembly requestAssembly, Type unitOfWorkType)
    {
        ArgumentNullException.ThrowIfNull(requestAssembly);
        ArgumentNullException.ThrowIfNull(unitOfWorkType);
        if (!typeof(IUnitOfWork).IsAssignableFrom(unitOfWorkType))
        {
            throw new ArgumentException($"{unitOfWorkType.FullName} does not implement {nameof(IUnitOfWork)}.", nameof(unitOfWorkType));
        }

        if (!_unitOfWorkTypes.TryAdd(requestAssembly, unitOfWorkType))
        {
            throw new InvalidOperationException($"A unit of work is already registered for {requestAssembly.GetName().Name}.");
        }
    }

    internal IUnitOfWork Resolve(Type requestType, IServiceProvider serviceProvider) =>
        _unitOfWorkTypes.TryGetValue(requestType.Assembly, out var unitOfWorkType)
            ? (IUnitOfWork)serviceProvider.GetRequiredService(unitOfWorkType)
            : throw new InvalidOperationException(
                $"No unit of work is registered for the module of {requestType.FullName}. "
                + "Call AddModuleUnitOfWork<TUnitOfWork>(applicationAssembly) in the module registration.");
}
