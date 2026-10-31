using Merp.SharedKernel.Notifications;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

/// <summary>The <c>{name}</c> placeholders of a message a user wrote (D58).</summary>
public sealed class MessagePlaceholdersTests
{
    [Fact]
    public void Names_AreListedOnce_InOrder()
    {
        MessagePlaceholders.Names("Sayın {candidate}, {position} için {date} {time}. {candidate}").ShouldBe(["candidate", "position", "date", "time"]);
        MessagePlaceholders.Names("Süslü { serbest } ve {} ile {yarım").ShouldBeEmpty();
    }

    [Fact]
    public void Fill_ReplacesTheKnownPlaceholders_AndLeavesTheRest()
    {
        var values = new Dictionary<string, string> { ["candidate"] = "Ayşe Yılmaz", ["position"] = "Satış Uzmanı" };

        MessagePlaceholders.Fill("Sayın {candidate}, {position} başvurunuz {unknown} { candidate }", values)
            .ShouldBe("Sayın Ayşe Yılmaz, Satış Uzmanı başvurunuz {unknown} { candidate }");
    }
}
