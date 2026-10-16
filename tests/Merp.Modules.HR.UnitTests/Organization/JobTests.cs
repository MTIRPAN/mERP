using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.UnitTests.Organization;

public class JobTests
{
    [Fact]
    public void Create_IsActive_WithTheCodeInUpperCase()
    {
        var job = Job.Create(Code.Create("sat-mdr").Value, new JobProfile(" Satış Müdürü ", "Satış", "M2"));

        job.Code.ShouldBe("SAT-MDR");
        job.Name.ShouldBe("Satış Müdürü");
        job.Family.ShouldBe("Satış");
        job.Level.ShouldBe("M2");
        job.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Update_ChangesDetails_ButNotTheCode()
    {
        var job = Job.Create(Code.Create("SAT-MDR").Value, new JobProfile("Satış Müdürü", null, null));

        job.Update(new JobProfile("Bölge Satış Müdürü", "Satış", " "));
        job.Deactivate();

        job.Code.ShouldBe("SAT-MDR");
        job.Name.ShouldBe("Bölge Satış Müdürü");
        job.Level.ShouldBeNull();
        job.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Create_WithoutName_Throws() =>
        Should.Throw<ArgumentException>(() => Job.Create(Code.Create("X").Value, new JobProfile(" ", null, null)));
}
