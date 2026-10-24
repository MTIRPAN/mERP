using System.Text.Json;
using Merp.Application.Abstractions.Settings;

namespace Merp.Infrastructure.Settings;

/// <summary>Hosts without the Platform module keep no values: every setting has its default (D35).</summary>
internal sealed class DefaultSettings(SettingDefinitionRegistry registry) : ISettings
{
    public Task<T> GetAsync<T>(string key, Guid? companyId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(SettingValues.Read<T>(registry.Get(key).DefaultValue));
}

/// <summary>Turns stored JSON into the type a module asks for.</summary>
public static class SettingValues
{
    /// <remarks>An empty date setting is null; other settings always have a value.</remarks>
    public static T Read<T>(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null && Nullable.GetUnderlyingType(typeof(T)) is not null
            ? default!
            : value.Deserialize<T>(JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException($"A setting value cannot be read as {typeof(T).Name}.");
}
