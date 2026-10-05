using System.Globalization;

namespace Merp.BuildingBlocks.UnitTests;

public class CultureTests
{
    [Fact]
    public void Tests_RunUnderTurkishCulture() =>
        // Guards the xunit.runner.json setting: culture-sensitive bugs must reproduce in tests.
        CultureInfo.CurrentCulture.Name.ShouldBe("tr-TR");
}
