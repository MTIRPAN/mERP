using Merp.Infrastructure.Persistence;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Infrastructure.Persistence;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.HR.Infrastructure.Employees;

/// <summary>
/// Seeds the SGK leaving codes (işten ayrılış kodları) once (D38). The list stays editable, so codes SGK adds or
/// renames later are maintained by the tenant; a tenant that already has codes is left alone.
/// </summary>
internal sealed partial class HrReferenceDataSeeder(ILogger<HrReferenceDataSeeder> logger) : ITenantDataSeeder
{
    private const string Culture = "tr-TR";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<HrDbContext>();
        if (await db.SgkExitCodes.AnyAsync(cancellationToken))
        {
            return;
        }

        var codes = SgkExitCodes().ToList();
        foreach (var (code, name) in codes)
        {
            db.SgkExitCodes.Add(SgkExitCode.Create(Code.Create(code, SgkExitCode.CodeMaxLength).Value, LocalizedString.Of(Culture, name)));
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, codes.Count);
    }

    /// <summary>The leaving codes of SGK's employment declaration in common use.</summary>
    internal static IEnumerable<(string Code, string Name)> SgkExitCodes()
    {
        yield return ("01", "Deneme süreli iş sözleşmesinin işverence feshi");
        yield return ("02", "Deneme süreli iş sözleşmesinin işçi tarafından feshi");
        yield return ("03", "Belirsiz süreli iş sözleşmesinin işçi tarafından feshi (istifa)");
        yield return ("04", "Belirsiz süreli iş sözleşmesinin işveren tarafından haklı sebep bildirilmeden feshi");
        yield return ("05", "Belirli süreli iş sözleşmesinin sona ermesi");
        yield return ("08", "Emeklilik (yaşlılık) veya toptan ödeme nedeniyle");
        yield return ("09", "Malulen emeklilik nedeniyle");
        yield return ("10", "Ölüm");
        yield return ("11", "İş kazası sonucu ölüm");
        yield return ("12", "Askerlik");
        yield return ("13", "Kadın işçinin evlenmesi");
        yield return ("14", "Emeklilik için yaş dışında diğer şartların tamamlanması");
        yield return ("15", "Toplu işçi çıkarma");
        yield return ("16", "Sözleşme sona ermeden sigortalının aynı işverene ait diğer işyerine nakli");
        yield return ("17", "İşyerinin kapanması");
        yield return ("18", "İşin sona ermesi");
        yield return ("19", "Mevsim bitimi");
        yield return ("20", "Kampanya bitimi");
        yield return ("21", "Statü değişikliği");
        yield return ("22", "Diğer nedenler");
        yield return ("23", "İşçi tarafından zorunlu nedenle fesih");
        yield return ("24", "İşçi tarafından sağlık nedeniyle fesih");
        yield return ("25", "İşçi tarafından işverenin ahlak ve iyi niyet kurallarına aykırı davranışı nedeniyle fesih");
        yield return ("26", "Disiplin kurulu kararı ile fesih");
        yield return ("27", "İşveren tarafından zorunlu nedenlerle ve tutukluluk nedeniyle fesih");
        yield return ("28", "İşveren tarafından sağlık nedeniyle fesih");
        yield return ("29", "İşveren tarafından işçinin ahlak ve iyi niyet kurallarına aykırı davranışı nedeniyle fesih");
        yield return ("30", "Vize süresinin bitimi");
        yield return ("33", "Gazeteci tarafından sözleşmenin feshi");
        yield return ("34", "İşyerinin devri, işin veya işyerinin niteliğinin değişmesi nedeniyle fesih");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SGK leaving codes seeded: {Count}")]
    private static partial void LogSeeded(ILogger logger, int count);
}
