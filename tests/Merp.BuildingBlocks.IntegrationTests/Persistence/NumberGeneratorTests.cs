using Merp.Application.Abstractions.Numbering;
using Merp.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.Persistence;

/// <summary>Document numbers from the module's counters (PLAN P5, D29); the harness pattern is <c>W-{yyyy}-{seq:4}</c>, yearly.</summary>
public sealed class NumberGeneratorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Date = new(2026, 3, 15);

    private WidgetHarness _harness = null!;

    public async ValueTask InitializeAsync() => _harness = await WidgetHarness.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    /// <summary>PLAN P5 acceptance: 50 parallel requests never get the same number.</summary>
    [Fact]
    public async Task FiftyParallelRequests_GetFiftyDifferentNumbers()
    {
        var company = Guid.CreateVersion7();

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => _harness.SendAsync(new NumberWidget(company, Date)))));

        results.ShouldAllBe(r => r.IsSuccess);
        results.Select(r => r.Value).Order(StringComparer.Ordinal)
            .ShouldBe([.. Enumerable.Range(1, 50).Select(n => $"W-2026-{n:0000}")]);
    }

    /// <summary>PLAN P5 acceptance: the counter starts again in a new year.</summary>
    [Fact]
    public async Task NewYear_StartsTheCounterAgain()
    {
        var company = Guid.CreateVersion7();

        (await _harness.SendAsync(new NumberWidget(company, new(2026, 12, 31)))).Value.ShouldBe("W-2026-0001");
        (await _harness.SendAsync(new NumberWidget(company, new(2026, 12, 31)))).Value.ShouldBe("W-2026-0002");
        (await _harness.SendAsync(new NumberWidget(company, new(2027, 1, 1)))).Value.ShouldBe("W-2027-0001");
        // A late document of the old year continues that year's counter
        (await _harness.SendAsync(new NumberWidget(company, new(2026, 12, 30)))).Value.ShouldBe("W-2026-0003");
    }

    [Fact]
    public async Task EachCompany_CountsOnItsOwn()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        (await _harness.SendAsync(new NumberWidget(first, Date))).Value.ShouldBe("W-2026-0001");
        (await _harness.SendAsync(new NumberWidget(second, Date))).Value.ShouldBe("W-2026-0001");
        (await _harness.SendAsync(new NumberWidget(first, Date))).Value.ShouldBe("W-2026-0002");
    }

    [Fact]
    public async Task RolledBackCommand_GivesItsNumberBack()
    {
        var company = Guid.CreateVersion7();
        (await _harness.SendAsync(new NumberWidget(company, Date))).Value.ShouldBe("W-2026-0001");

        await Should.ThrowAsync<InvalidOperationException>(() => _harness.SendAsync(new NumberWidget(company, Date, Fail: true)));

        (await _harness.SendAsync(new NumberWidget(company, Date))).Value.ShouldBe("W-2026-0002");
    }

    [Fact]
    public async Task NumbersOutsideACommand_AreRefused()
    {
        await using var scope = _harness.CreateScope();
        var numbers = scope.ServiceProvider.GetRequiredService<INumberGenerator>();

        await Should.ThrowAsync<InvalidOperationException>(() => numbers.NextAsync(Guid.CreateVersion7(), "widgets.order", Date, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<InvalidOperationException>(() => numbers.NextAsync(Guid.CreateVersion7(), "nomodule.order", Date, TestContext.Current.CancellationToken));
    }
}
