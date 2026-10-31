namespace Merp.Infrastructure.AI;

/// <summary>Configuration section <c>Ai</c> (D24). Tenant level settings move to the settings screen with PLAN P11.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>All features are off when <see cref="AiProvider.None"/>.</summary>
    public AiProvider Provider { get; set; } = AiProvider.None;

    /// <summary>Model for new calls. Claude Sonnet 5.5 by default (D24); Claude Opus 5.5 for the hardest work.</summary>
    public string Model { get; set; } = "claude-sonnet-5-5";

    public int MaxOutputTokens { get; set; } = 16000;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum retries for rate limits and transient provider errors (the SDK backs off between attempts).</summary>
    public int MaxRetries { get; set; } = 2;

    public AnthropicOptions Anthropic { get; set; } = new();

    /// <summary>The share of the list price a batch request costs (D59); the provider bills batches at half price.</summary>
    public decimal BatchPriceFactor { get; set; } = 0.5m;

    /// <summary>
    /// List prices in USD per million tokens by model id, used to estimate cost and enforce budgets. Entries here
    /// replace the built-in prices (<see cref="AiPricingOptions.Defaults"/>).
    /// </summary>
    public Dictionary<string, AiPricingOptions> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Settings for tenants without an entry in <see cref="Tenants"/>.</summary>
    public AiTenantOptions Defaults { get; set; } = new();

    /// <summary>Per tenant settings keyed by tenant code; unset values fall back to <see cref="Defaults"/>.</summary>
    public Dictionary<string, AiTenantOptions> Tenants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Prices of <paramref name="model"/>: configured, else built-in. An unknown model is priced like the most
    /// expensive built-in model, so budgets are never underestimated.
    /// </summary>
    public AiPricingOptions PricingFor(string model) =>
        Pricing.TryGetValue(model, out var configured) ? configured
        : AiPricingOptions.Defaults.TryGetValue(model, out var known) ? known
        : AiPricingOptions.MostExpensive;

    public AiTenantSettings ForTenant(string? tenantCode)
    {
        var tenant = tenantCode is not null && Tenants.TryGetValue(tenantCode, out var options) ? options : null;
        return new AiTenantSettings(
            tenant?.Enabled ?? Defaults.Enabled ?? false,
            tenant?.Features ?? Defaults.Features ?? [],
            tenant?.MonthlyBudgetUsd ?? Defaults.MonthlyBudgetUsd ?? 0m,
            tenant?.TransferBasis ?? Defaults.TransferBasis,
            tenant?.TransferReference ?? Defaults.TransferReference);
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
    /// <summary>List prices of the models the product is set up for (docs/domain/yapay-zeka.md §6).</summary>
    public static IReadOnlyDictionary<string, AiPricingOptions> Defaults { get; } =
        new Dictionary<string, AiPricingOptions>(StringComparer.OrdinalIgnoreCase)
        {
            // Claude Sonnet 5 serves server-side fallbacks of Claude Sonnet 5.5 at the same prices.
            ["claude-sonnet-5-5"] = new() { InputPerMillionTokens = 2m, OutputPerMillionTokens = 10m, CacheReadPerMillionTokens = 0.2m, CacheWritePerMillionTokens = 2.5m },
            ["claude-sonnet-5"] = new() { InputPerMillionTokens = 2m, OutputPerMillionTokens = 10m, CacheReadPerMillionTokens = 0.2m, CacheWritePerMillionTokens = 2.5m },
            ["claude-opus-5-5"] = new() { InputPerMillionTokens = 4m, OutputPerMillionTokens = 20m, CacheReadPerMillionTokens = 0.2m, CacheWritePerMillionTokens = 5m },
        };

    internal static AiPricingOptions MostExpensive { get; } = Defaults.Values.MaxBy(p => p.OutputPerMillionTokens)!;

    public decimal InputPerMillionTokens { get; set; }

    public decimal OutputPerMillionTokens { get; set; }

    public decimal CacheReadPerMillionTokens { get; set; }

    /// <summary>Price of writing the 5-minute prompt cache.</summary>
    public decimal CacheWritePerMillionTokens { get; set; }

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

    /// <summary>
    /// Legal basis for transferring personal data to the AI provider abroad (KVKK md. 9). AI stays off for the
    /// tenant until it is set (docs/domain/yapay-zeka.md §10).
    /// </summary>
    public AiTransferBasis? TransferBasis { get; set; }

    /// <summary>Evidence of the basis, e.g. the date and registered e-mail (KEP) reference of the KVKK notification.</summary>
    public string? TransferReference { get; set; }
}

/// <summary>
/// Bases KVKK md. 9 accepts for regular transfers abroad. Explicit consent is deliberately missing: since
/// 1 September 2024 it only covers occasional (arızi) transfers, and AI features transfer data regularly.
/// </summary>
public enum AiTransferBasis
{
    /// <summary>Standard contract approved by the KVKK Board, notified to the Authority within five business days of signing.</summary>
    StandardContract,

    /// <summary>Binding corporate rules approved by the Board.</summary>
    BindingCorporateRules,

    /// <summary>An adequacy decision of the Board covers the provider's country and sector.</summary>
    AdequacyDecision,
}

public sealed record AiTenantSettings(
    bool Enabled,
    IReadOnlyList<string> Features,
    decimal MonthlyBudgetUsd,
    AiTransferBasis? TransferBasis = null,
    string? TransferReference = null)
{
    public bool Allows(string feature) =>
        Enabled && TransferBasis is not null && Features.Any(pattern =>
            string.Equals(pattern, "*", StringComparison.Ordinal)
            || string.Equals(pattern, feature, StringComparison.OrdinalIgnoreCase)
            || (pattern.EndsWith(".*", StringComparison.Ordinal) && feature.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)));
}
