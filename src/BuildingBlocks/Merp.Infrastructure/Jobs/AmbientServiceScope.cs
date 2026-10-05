namespace Merp.Infrastructure.Jobs;

/// <summary>
/// Flows the current DI scope to Hangfire's client filters, which are global singletons and cannot resolve scoped
/// services themselves (same idea as <c>IHttpContextAccessor</c>). Set per request by the web host and per job by
/// <see cref="TenantAwareJobActivator"/>.
/// </summary>
public static class AmbientServiceScope
{
    private static readonly AsyncLocal<IServiceProvider?> Scope = new();

    public static IServiceProvider? Current
    {
        get => Scope.Value;
        set => Scope.Value = value;
    }
}
