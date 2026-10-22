using Merp.Application.Abstractions.Auditing;
using Merp.Infrastructure.Persistence.Auditing;

namespace Merp.BuildingBlocks.UnitTests.Auditing;

/// <summary>The source of the changes a command writes when it is not a direct edit (D39).</summary>
public class AuditSourceTests
{
    [Fact]
    public void Set_HoldsUntilChanged_AndBlankStopsUsingOne()
    {
        var source = new AuditSource();
        source.Current.ShouldBeNull();

        source.Set("hr.profile-change:PDT-2026-00012");
        source.Current.ShouldBe("hr.profile-change:PDT-2026-00012");

        source.Set(" ");
        source.Current.ShouldBeNull();
    }

    [Fact]
    public void Set_RejectsALongerSourceThanTheColumn() =>
        Should.Throw<ArgumentException>(() => new AuditSource().Set(new string('x', IAuditSource.MaxLength + 1)));
}
