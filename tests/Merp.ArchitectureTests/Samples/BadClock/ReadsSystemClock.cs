namespace Merp.ArchitectureTests.Samples.BadClock;

/// <summary>Deliberate violation: reads the system clock instead of TimeProvider (D10). See RuleSelfTests.</summary>
public static class ReadsSystemClock
{
    public static DateTimeOffset Now() => DateTimeOffset.UtcNow;

    public static DateTime Today() => DateTime.Today;
}
