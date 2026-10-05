namespace Merp.Application.Abstractions.AI;

/// <summary>An AI feature a module offers. Tenants enable features one by one (D24).</summary>
/// <param name="Key">Feature key, <c>{module}.{feature}</c>.</param>
public sealed record AiFeatureDefinition(string Key);

/// <summary>Implemented by modules to declare their AI features, e.g. for the web shell and settings screens.</summary>
public interface IAiFeatureDefinitionProvider
{
    IEnumerable<AiFeatureDefinition> GetFeatures();
}
