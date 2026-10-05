namespace Merp.Infrastructure.AI;

/// <summary>Configuration section <c>Ai</c> (D24). Tenant level settings move to the settings screen with PLAN P11.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>All features are off when <see cref="AiProvider.None"/>.</summary>
    public AiProvider Provider { get; set; } = AiProvider.None;

    public string Model { get; set; } = "claude-opus-5-5";

    public int MaxOutputTokens { get; set; } = 16000;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum retries for rate limits and transient provider errors (the SDK backs off between attempts).</summary>
    public int MaxRetries { get; set; } = 2;

    public AnthropicOptions Anthropic { get; set; } = new();

    /// <summary>List prices in USD per million tokens, used to estimate cost and enforce budgets.</summary>
    public AiPricingOptions Pricing { get; set; } = new();

    /// <summary>Settings for tenants without an entry in <see cref="Tenants"/>.</summary>
    public AiTenantOptions Defaults { get; set; } = new();

    /// <summary>Per tenant settings keyed by tenant code; unset values fall back to <see cref="Defaults"/>.</summary>
    public Dictionary<string, AiTenantOptions> Tenants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AiTenantSettings ForTenant(string? tenantCode)
    {
        var tenant = tenantCode is not null && Tenants.TryGetValue(tenantCode, out var options) ? options : null;
        return new AiTenantSettings(
            tenant?.Enabled ?? Defaults.Enabled ?? false,
            tenant?.Features ?? Defaults.Features ?? [],
            tenant?.MonthlyBudgetUsd ?? Defaults.MonthlyBudgetUsd ?? 0m);
    }
}

public enum AiProvider
{
    None,
    Anthropic,
}

public sealed class AnthropicOptions
{
    /// <summary>API key; set it with user secrets or the <c>Ai__Anthropic__ApiKey</c> environment variable, never in source.</summary>
    public string? ApiKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.anthropic.com");

    /// <summary>Where inference runs: empty (provider default), <c>global</c> or <c>us</c>.</summary>
    public string? InferenceGeo { get; set; }

    /// <summary>
    /// When the model declines for policy reasons, let the provider retry on its recommended fallback model in the
    /// same call (<c>fallbacks: "default"</c>).
    /// </summary>
    public bool ServerSideFallback { get; set; } = true;
}

public sealed class AiPricingOptions
{
    public decimal InputPerMillionTokens { get; set; } = 4m;

    public decimal OutputPerMillionTokens { get; set; } = 20m;

    public decimal CacheReadPerMillionTokens { get; set; } = 0.2m;

    public decimal CacheWritePerMillionTokens { get; set; } = 5m;

    public decimal Cost(long input, long output, long cacheRead, long cacheWrite) =>
        ((input * InputPerMillionTokens)
            + (output * OutputPerMillionTokens)
            + (cacheRead * CacheReadPerMillionTokens)
            + (cacheWrite * CacheWritePerMillionTokens)) / 1_000_000m;
}

public sealed class AiTenantOptions
{
    public bool? Enabled { get; set; }

    /// <summary>Enabled feature keys; <c>*</c> enables all, <c>recruitment.*</c> all features of a module.</summary>
    public string[]? Features { get; set; }

    /// <summary>Monthly spending limit in USD; 0 means no limit.</summary>
    public decimal? MonthlyBudgetUsd { get; set; }
}

public sealed record AiTenantSettings(bool Enabled, IReadOnlyList<string> Features, decimal MonthlyBudgetUsd)
{
    public bool Allows(string feature) =>
        Enabled && Features.Any(pattern =>
            string.Equals(pattern, "*", StringComparison.Ordinal)
            || string.Equals(pattern, feature, StringComparison.OrdinalIgnoreCase)
            || (pattern.EndsWith(".*", StringComparison.Ordinal) && feature.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)));
}
