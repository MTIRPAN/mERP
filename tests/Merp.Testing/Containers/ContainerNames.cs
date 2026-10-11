namespace Merp.Testing.Containers;

/// <summary>
/// Names of test containers. With <see cref="PrefixVariable"/> set (e.g. <c>merp-sdk-7</c>) a run's containers are
/// numbered, <c>merp-sdk-7-postgres-1</c>, <c>-2</c>..., so they are easy to find and remove together; otherwise they
/// get <c>merp-test-{kind}-{random}</c>.
/// </summary>
public static class ContainerNames
{
    public const string PrefixVariable = "MERP_TEST_CONTAINER_PREFIX";

    public static string Next(string kind)
    {
        if (Environment.GetEnvironmentVariable(PrefixVariable) is not { Length: > 0 } prefix)
        {
            return $"merp-test-{kind}-{Guid.NewGuid().ToString("N")[..8]}";
        }

        // Test assemblies run in parallel processes: a marker file claims each number
        for (var n = 1; ; n++)
        {
            var name = $"{prefix}-{kind}-{n}";
            try
            {
                using var marker = new FileStream(Path.Combine(Path.GetTempPath(), name), FileMode.CreateNew);
                return name;
            }
            catch (IOException)
            {
            }
        }
    }
}
