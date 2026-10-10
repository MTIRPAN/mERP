using Merp.Modules.Platform.Domain.Calendars;
using Merp.Modules.Platform.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Merp.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class HolidayCalendarConfiguration : IEntityTypeConfiguration<HolidayCalendar>
{
    public void Configure(EntityTypeBuilder<HolidayCalendar> builder)
    {
        builder.ToTable("holiday_calendars");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(HolidayCalendar.CodeMaxLength);
        builder.HasMany(c => c.Holidays).WithOne().HasForeignKey(h => h.CalendarId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Holidays).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(c => c.Code).IsUnique();
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("holidays");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Duration).HasConversion<string>().HasMaxLength(10);
        builder.Ignore(h => h.Fraction);
        builder.HasIndex(h => new { h.CalendarId, h.Date }).IsUnique();
    }
}

internal sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> builder)
    {
        builder.ToTable("work_schedules");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Code).HasMaxLength(WorkSchedule.CodeMaxLength);
        builder.Property(s => s.Days).HasJson();
        builder.Ignore(s => s.WorkingDaysPerWeek);
        builder.HasOne<Company>().WithMany().HasForeignKey(s => s.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HolidayCalendar>().WithMany().HasForeignKey(s => s.HolidayCalendarId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.Code).IsUnique();

        // One default per company and one for the tenant is kept by WorkScheduleMadeDefault. A unique index cannot
        // enforce it: the new default and the old one change in the same save, and the order of the updates is not fixed.
        builder.HasIndex(s => new { s.CompanyId, s.IsDefault });
    }
}
