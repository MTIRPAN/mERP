using ClosedXML.Excel;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Infrastructure.Documents;
using Merp.Infrastructure.Exports;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Merp.BuildingBlocks.UnitTests.Exports;

public sealed class ListExporterTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly RecordingAuditWriter _audit = new();
    private readonly List<Thing> _things = [];
    private readonly List<PageRequest> _pages = [];

    public ListExporterTests() =>
        _sender.Send(Arg.Any<IRequest<Result<PagedResult<Thing>>>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var paging = ((GetThings)call.Arg<IRequest<Result<PagedResult<Thing>>>>()).Paging;
            _pages.Add(paging);
            var items = _things.Skip((paging.PageNumber - 1) * paging.Size).Take(paging.Size).ToList();
            return Task.FromResult(Result.Success(new PagedResult<Thing>(items, paging.PageNumber, paging.Size, _things.Count)));
        });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Export_ReadsEveryPage_AndWritesTypedCellsWithLabels()
    {
        _things.AddRange(Enumerable.Range(1, 450).Select(i => new Thing($"T{i:D3}", i * 1.5m, Kind.Large, i % 2 == 0, new DateOnly(2026, 10, 1),
            new DateTimeOffset(2026, 10, 13, 21, 30, 0, TimeSpan.Zero), ["a", "b"])));

        var result = await Exporter().ExportAsync(List(columns: null), Ct);

        result.IsSuccess.ShouldBeTrue();
        _pages.Select(p => p.PageNumber).ShouldBe([1, 2, 3]);
        _pages.ShouldAllBe(p => p.Size == PageRequest.MaxPageSize && p.Sort == "-code");
        result.Value.FileName.ShouldBe("Şeyler-2026-10-14.xlsx");

        using var workbook = new XLWorkbook(new MemoryStream(result.Value.Content));
        var sheet = workbook.Worksheet(1);
        sheet.Name.ShouldBe("Şeyler");
        Enumerable.Range(1, 7).Select(c => sheet.Cell(1, c).GetString()).ShouldBe(["Kod", "Tutar", "Tür", "Aktif mi", "Tarih", "Zaman", "Etiketler"]);
        sheet.RangeUsed()!.RowCount().ShouldBe(451);
        sheet.Cell(2, 1).GetString().ShouldBe("T001");
        sheet.Cell(2, 2).GetDouble().ShouldBe(1.5);
        sheet.Cell(2, 3).GetString().ShouldBe("Büyük");
        sheet.Cell(2, 4).GetString().ShouldBe("Hayır");
        sheet.Cell(3, 4).GetString().ShouldBe("Evet");
        sheet.Cell(2, 5).GetDateTime().ShouldBe(new DateTime(2026, 10, 1));
        // Moments are written in Europe/Istanbul (UTC+3)
        sheet.Cell(2, 6).GetDateTime().ShouldBe(new DateTime(2026, 10, 14, 0, 30, 0));
        sheet.Cell(2, 7).GetString().ShouldBe("a, b");
        sheet.AutoFilter.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Export_WritesTheChosenColumnsInOrder_SkippingUnknownOnes_AndIsAudited()
    {
        _things.Add(new Thing("T1", 1, Kind.Small, true, null, null, []));

        var result = await Exporter().ExportAsync(List(columns: ["kind", "actions", "code", "kind"]), Ct);

        using var workbook = new XLWorkbook(new MemoryStream(result.Value.Content));
        var sheet = workbook.Worksheet(1);
        sheet.RangeUsed()!.ColumnCount().ShouldBe(2);
        sheet.Cell(1, 1).GetString().ShouldBe("Tür");
        sheet.Cell(2, 1).GetString().ShouldBe("Küçük");
        sheet.Cell(2, 2).GetString().ShouldBe("T1");

        var (type, entries) = _audit.Writes.ShouldHaveSingleItem();
        type.ShouldBe(typeof(Thing));
        var entry = entries.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.Export);
        entry.UserId.ShouldBe(UserId);
        entry.EntityType.ShouldBe(nameof(Thing));
        entry.NewValue.ShouldBe("""{"rows":1,"columns":["kind","code"],"sort":"-code","filters":{"search":"t"}}""");
    }

    [Fact]
    public async Task Export_OfMoreRowsThanAllowed_IsRefused_AndNotAudited()
    {
        _things.AddRange(Enumerable.Range(1, 11).Select(i => new Thing($"T{i}", i, Kind.Small, true, null, null, [])));

        var result = await Exporter(maxRows: 10).ExportAsync(List(columns: null), Ct);

        result.IsFailure.ShouldBeTrue();
        var field = result.Error.Fields.ShouldHaveSingleItem();
        field.Field.ShouldBe(ListExporter.FiltersField);
        field.Code.ShouldBe(ExportRules.TooManyRowsCode);
        field.Args!["max"].ShouldBe(10);
        _audit.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void ParseColumns_SplitsTheQueryParameter()
    {
        ExportRules.ParseColumns(" code, name ,,").ShouldBe(["code", "name"]);
        ExportRules.ParseColumns(" ").ShouldBeNull();
    }

    private ListExporter Exporter(int maxRows = 10_000)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["test.things"] = "Şeyler",
            ["test.code"] = "Kod",
            ["test.amount"] = "Tutar",
            ["test.kind"] = "Tür",
            ["test.kind.Small"] = "Küçük",
            ["test.kind.Large"] = "Büyük",
            ["test.active"] = "Aktif mi",
            ["test.date"] = "Tarih",
            ["test.moment"] = "Zaman",
            ["test.tags"] = "Etiketler",
            ["common.yes"] = "Evet",
            ["common.no"] = "Hayır",
        };
        var localizer = Substitute.For<ITextLocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<string?>())
            .Returns(call => texts.GetValueOrDefault(call.ArgAt<string>(0), call.ArgAt<string>(0)));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);

        return new ListExporter(
            _sender,
            new ClosedXmlSpreadsheet(),
            localizer,
            currentUser,
            _audit,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 22, 0, 0, TimeSpan.Zero)),
            Options.Create(new ExportSettings { MaxRows = maxRows }));
    }

    private static ExportList<Thing> List(IReadOnlyList<string>? columns) => new(
        "test.things",
        typeof(Thing),
        [
            ExportColumn<Thing>.Of("code", "test.code", t => t.Code),
            ExportColumn<Thing>.Of("amount", "test.amount", t => t.Amount),
            ExportColumn<Thing>.Of("kind", "test.kind", t => t.Kind, "test.kind."),
            ExportColumn<Thing>.Of("active", "test.active", t => t.Active),
            ExportColumn<Thing>.Of("date", "test.date", t => t.Date),
            ExportColumn<Thing>.Of("moment", "test.moment", t => t.Moment),
            ExportColumn<Thing>.Of("tags", "test.tags", t => t.Tags),
        ],
        paging => new GetThings(paging),
        "-code",
        columns,
        new Dictionary<string, object?> { ["search"] = "t", ["isActive"] = null });

    public enum Kind
    {
        Small,
        Large,
    }

    public sealed record Thing(string Code, decimal Amount, Kind Kind, bool Active, DateOnly? Date, DateTimeOffset? Moment, IReadOnlyList<string> Tags);

    public sealed record GetThings(PageRequest Paging) : IQuery<PagedResult<Thing>>;

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<(Type Type, IReadOnlyCollection<AuditEntry> Entries)> Writes { get; } = [];

        public Task WriteAsync(Type entityType, IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken)
        {
            Writes.Add((entityType, entries));
            return Task.CompletedTask;
        }
    }
}
