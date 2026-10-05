using System.Diagnostics;

namespace Merp.Infrastructure.Messaging;

public static class MediatorTelemetry
{
    public const string ActivitySourceName = "Merp.Mediator";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
