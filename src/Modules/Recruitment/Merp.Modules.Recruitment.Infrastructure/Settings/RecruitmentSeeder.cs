using Merp.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Recruitment.Infrastructure.Settings;

/// <summary>
/// Seeds the default stage template and the reasons and sources once (PLAN R1, D47). The tenant changes them
/// afterwards, so lists that already have rows are left alone.
/// </summary>
internal sealed partial class RecruitmentSeeder(ILogger<RecruitmentSeeder> logger) : ITenantDataSeeder
{
    private const string Culture = "tr-TR";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<RecruitmentDbContext>();
        if (!await db.StageTemplates.AnyAsync(cancellationToken))
        {
            db.StageTemplates.Add(StageTemplate.Create(LocalizedString.Of(Culture, "Standart işe alım"), DefaultStages(), isDefault: true).Value);
            await db.SaveChangesAsync(cancellationToken);
            LogSeeded(logger, "Stage templates", 1);
        }

        if (!await db.Lookups.AnyAsync(cancellationToken))
        {
            var lookups = Lookups().ToList();
            var orders = new Dictionary<LookupKind, int>();
            foreach (var (kind, code, name) in lookups)
            {
                var order = orders.GetValueOrDefault(kind) + 10;
                orders[kind] = order;
                db.Lookups.Add(RecruitmentLookup.Create(kind, Code.Create(code, RecruitmentLookup.CodeMaxLength).Value, LocalizedString.Of(Culture, name), order));
            }

            await db.SaveChangesAsync(cancellationToken);
            LogSeeded(logger, "Recruitment lists", lookups.Count);
        }
    }

    /// <summary>Applied, screening, two interviews, offer and the final stages.</summary>
    internal static IReadOnlyList<Stage> DefaultStages() =>
    [
        Stage("Başvuru", StageType.Applied, 3),
        Stage("Ön eleme", StageType.Screening, 5),
        Stage("İK mülakatı", StageType.Interview, 7),
        Stage("Yönetici mülakatı", StageType.Interview, 7),
        Stage("Teklif", StageType.Offer, 5),
        Stage("İşe alındı", StageType.Hired, null),
        Stage("Reddedildi", StageType.Rejected, null),
        Stage("Geri çekildi", StageType.Withdrawn, null),
    ];

    private static Stage Stage(string name, StageType type, int? slaDays) => new(Guid.CreateVersion7(), LocalizedString.Of(Culture, name), type, slaDays);

    private static IEnumerable<(LookupKind Kind, string Code, string Name)> Lookups()
    {
        yield return (LookupKind.RejectionReason, "EXPERIENCE", "Deneyim yetersiz");
        yield return (LookupKind.RejectionReason, "SKILLS", "Yetkinlikler uyuşmuyor");
        yield return (LookupKind.RejectionReason, "SALARY", "Ücret beklentisi uyuşmuyor");
        yield return (LookupKind.RejectionReason, "LOCATION", "Konum uygun değil");
        yield return (LookupKind.RejectionReason, "INTERVIEW", "Mülakat sonucu olumsuz");
        yield return (LookupKind.RejectionReason, "POSITION_FILLED", "Pozisyon doldu");
        yield return (LookupKind.RejectionReason, "OTHER", "Diğer");
        yield return (LookupKind.WithdrawalReason, "OTHER_OFFER", "Başka bir teklifi kabul etti");
        yield return (LookupKind.WithdrawalReason, "SALARY", "Ücret teklifi düşük");
        yield return (LookupKind.WithdrawalReason, "NO_LONGER_INTERESTED", "Artık ilgilenmiyor");
        yield return (LookupKind.WithdrawalReason, "OTHER", "Diğer");
        yield return (LookupKind.CandidateSource, "CAREER_PAGE", "Kariyer sayfası");
        yield return (LookupKind.CandidateSource, "LINKEDIN", "LinkedIn");
        yield return (LookupKind.CandidateSource, "JOB_BOARD", "İş ilanı sitesi");
        yield return (LookupKind.CandidateSource, "REFERRAL", "Çalışan referansı");
        yield return (LookupKind.CandidateSource, "AGENCY", "Danışmanlık firması");
        yield return (LookupKind.CandidateSource, "INTERNAL", "İç aday");
        yield return (LookupKind.CandidateSource, "OTHER", "Diğer");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{What} seeded: {Count}")]
    private static partial void LogSeeded(ILogger logger, string what, int count);
}
