using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Messaging;

/// <summary>Maps each module's requests to the unit of work of that module.</summary>
public sealed class UnitOfWorkRegistry
{
    private readonly List<(ModuleScope Scope, Type UnitOfWorkType)> _registrations = [];

    public void Register(Assembly requestAssembly, Type unitOfWorkType, string? namespacePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(requestAssembly);
        ArgumentNullException.ThrowIfNull(unitOfWorkType);
        if (!typeof(IUnitOfWork).IsAssignableFrom(unitOfWorkType))
        {
            throw new ArgumentException($"{unitOfWorkType.FullName} does not implement {nameof(IUnitOfWork)}.", nameof(unitOfWorkType));
        }

        var scope = new ModuleScope(requestAssembly, namespacePrefix);
        if (_registrations.Any(r => r.Scope == scope))
        {
            throw new InvalidOperationException($"A unit of work is already registered for {requestAssembly.GetName().Name} {namespacePrefix}.");
        }

        _registrations.Add((scope, unitOfWorkType));
    }

    internal IUnitOfWork Resolve(Type requestType, IServiceProvider serviceProvider)
    {
        var match = _registrations.Where(r => r.Scope.Contains(requestType)).OrderByDescending(r => r.Scope.Specificity).FirstOrDefault();
        return match.UnitOfWorkType is not null
            ? (IUnitOfWork)serviceProvider.GetRequiredService(match.UnitOfWorkType)
            : throw new InvalidOperationException(
                $"No unit of work is registered for the module of {requestType.FullName}. "
                + "Call AddModuleUnitOfWork<TUnitOfWork>(applicationAssembly) in the module registration.");
    }
}
