using Merp.Infrastructure.Persistence;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Infrastructure.Persistence;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.HR.Infrastructure.Leave;

/// <summary>
/// Seeds the leave types of Turkish law and the legal annual leave policy once (docs/domain/izin-kurallari.md §2–3,
/// D41). The tenant changes them afterwards, so lists that already have rows are left alone.
/// </summary>
internal sealed partial class LeaveSeeder(ILogger<LeaveSeeder> logger) : ITenantDataSeeder
{
    public const string LegalPolicyCode = "KANUNI";
    private const string Culture = "tr-TR";

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<HrDbContext>();
        if (!await db.LeaveTypes.AnyAsync(cancellationToken))
        {
            var types = Types().ToList();
            foreach (var type in types)
            {
                db.LeaveTypes.Add(LeaveType.Create(
                    Code.Create(type.Code, LeaveType.CodeMaxLength).Value,
                    LocalizedString.Of(Culture, type.Name),
                    null,
                    type.Profile,
                    type.Versions).Value);
            }

            await db.SaveChangesAsync(cancellationToken);
            LogSeeded(logger, "Leave types", types.Count);
        }

        if (!await db.LeavePolicies.AnyAsync(cancellationToken))
        {
            db.LeavePolicies.Add(LeavePolicy.Create(
                Code.Create(LegalPolicyCode, LeavePolicy.CodeMaxLength).Value,
                LocalizedString.Of(Culture, "Kanuni yıllık izin (4857)"),
                null,
                isDefault: true,
                [new LeavePolicyVersion(DateOnly.MinValue, LegalRules())]).Value);
            await db.SaveChangesAsync(cancellationToken);
            LogSeeded(logger, "Leave policies", 1);
        }
    }

    /// <summary>The annual leave of 4857 md. 53–56 (docs/domain/izin-kurallari.md §2).</summary>
    internal static AnnualLeaveRules LegalRules() => new(
        MinServiceYearsForEntitlement: 1,
        Tiers: [new EntitlementTier(1, 14), new EntitlementTier(5, 20), new EntitlementTier(15, 26)],
        AgeMinimums: [new AgeMinimum(MaxAge: 18, MinAge: null, MinDays: 20), new AgeMinimum(MaxAge: null, MinAge: 50, MinDays: 20)],
        ExtraDayRules: [new ExtraDayRule(new PolicyCriterion(CriterionKind.CustomField, ["true"], "underground"), 4)],
        AccrualMethod: AccrualMethod.AnniversaryLumpSum,
        TenDayBlockRule: TenDayBlockRule.Warn,
        AllowTravelDays: true,
        MaxTravelDays: 4,
        CarryOverLimitDays: null,
        UseGroupSeniority: false,
        UnpaidLeaveShiftsAccrualDate: false,
        AdvanceLimitDays: 0,
        AllowBeforeFirstEntitlement: false,
        AllowHalfDay: true,
        AllowHourly: false,
        Rounding: LeaveRounding.None,
        TransferBalanceOnGroupTransfer: true,
        MinNoticeDays: null,
        MaxDaysPerRequest: null,
        AllowPastDatesForHr: true);

    /// <summary>The leave types of docs/domain/izin-kurallari.md §3.</summary>
    internal static IEnumerable<(string Code, string Name, LeaveTypeProfile Profile, IReadOnlyList<LeaveTypeVersion> Versions)> Types()
    {
        const string Annual = "4857 md. 53–56";
        const string Excuse = "4857 Ek md. 2";
        const string Maternity = "4857 md. 74";
        var always = DateOnly.MinValue;
        var change = LegalMinimums.ParentalLeaveChange;

        LeaveTypeProfile Profile(LeaveCategory category, LeavePayer payer, LeaveUnit unit, DurationBasis basis, bool document, string? legalBasis,
            string? deductsFrom = null, Gender? gender = null, bool approval = true) =>
            new(category, payer, deductsFrom, unit, basis, document, approval, gender, AllowPastDates: true, legalBasis);

        yield return ("YILLIK", "Yıllık ücretli izin",
            Profile(LeaveCategory.Annual, LeavePayer.Employer, LeaveUnit.HalfDay, DurationBasis.WorkingDays, false, Annual, deductsFrom: "YILLIK"),
            [new LeaveTypeVersion(always)]);
        yield return ("EVLILIK", "Evlilik izni",
            Profile(LeaveCategory.Excuse, LeavePayer.Employer, LeaveUnit.Day, DurationBasis.CalendarDays, true, Excuse),
            [new LeaveTypeVersion(always, FixedDays: 3)]);
        yield return ("EVLAT_EDINME", "Evlat edinme izni",
            Profile(LeaveCategory.Excuse, LeavePayer.Employer, LeaveUnit.Day, DurationBasis.CalendarDays, true, Excuse),
            [new LeaveTypeVersion(always, FixedDays: 3)]);
        yield return ("VEFAT", "Vefat izni",
            Profile(LeaveCategory.Excuse, LeavePayer.Employer, LeaveUnit.Day, DurationBasis.CalendarDays, true, Excuse),
            [new LeaveTypeVersion(always, FixedDays: 3)]);
        yield return ("BABALIK", "Babalık izni",
            Profile(LeaveCategory.Excuse, LeavePayer.Employer, LeaveUnit.Day, DurationBasis.CalendarDays, true, Excuse, gender: Gender.Male),
            [new LeaveTypeVersion(always, FixedDays: 5), new LeaveTypeVersion(change, FixedDays: 10)]);
        yield return ("ENGELLI_COCUK", "Engelli veya süreğen hastalığı olan çocuğun tedavisi",
            Profile(LeaveCategory.Excuse, LeavePayer.Employer, LeaveUnit.Day, DurationBasis.WorkingDays, true, Excuse),
            [new LeaveTypeVersion(always, MaxDaysPerYear: 10)]);
        yield return ("DOGUM", "Doğum izni",
            Profile(LeaveCategory.Maternity, LeavePayer.Sgk, LeaveUnit.Day, DurationBasis.CalendarDays, true, Maternity, gender: Gender.Female),
            [
                new LeaveTypeVersion(always, FixedDays: 112, DaysBeforeEvent: 56, MultipleBirthExtraDays: 14),
                new LeaveTypeVersion(change, FixedDays: 168, DaysBeforeEvent: 56, MultipleBirthExtraDays: 14),
            ]);
        yield return ("DOGUM_SONRASI_UCRETSIZ", "Doğum sonrası ücretsiz izin",
            Profile(LeaveCategory.Unpaid, LeavePayer.None, LeaveUnit.Day, DurationBasis.CalendarDays, false, Maternity),
            [new LeaveTypeVersion(always, MaxDaysPerRequest: 180)]);
        yield return ("SUT", "Süt izni",
            Profile(LeaveCategory.Arrangement, LeavePayer.Employer, LeaveUnit.Hour, DurationBasis.Hours, false, Maternity, gender: Gender.Female, approval: false),
            [new LeaveTypeVersion(always, DailyHours: 1.5m)]);
        yield return ("RAPOR", "İstirahat raporu",
            Profile(LeaveCategory.Sick, LeavePayer.Sgk, LeaveUnit.Day, DurationBasis.CalendarDays, true, "5510"),
            [new LeaveTypeVersion(always)]);
        yield return ("UCRETSIZ", "Ücretsiz izin",
            Profile(LeaveCategory.Unpaid, LeavePayer.None, LeaveUnit.Day, DurationBasis.WorkingDays, false, null),
            [new LeaveTypeVersion(always)]);
        yield return ("IDARI", "İdari izin",
            Profile(LeaveCategory.Administrative, LeavePayer.Employer, LeaveUnit.Day, DurationBasis.WorkingDays, false, null),
            [new LeaveTypeVersion(always)]);
        yield return ("YOL", "Yol izni",
            Profile(LeaveCategory.Travel, LeavePayer.None, LeaveUnit.Day, DurationBasis.CalendarDays, true, "4857 md. 56"),
            [new LeaveTypeVersion(always, MaxDaysPerRequest: 4)]);
        yield return ("SAATLIK", "Saatlik mazeret izni",
            Profile(LeaveCategory.Excuse, LeavePayer.Employer, LeaveUnit.Hour, DurationBasis.Hours, false, null),
            [new LeaveTypeVersion(always)]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{What} seeded: {Count}")]
    private static partial void LogSeeded(ILogger logger, string what, int count);
}
