using Merp.Application.Abstractions.Imports;
using Merp.Application.Abstractions.Localization;
using Merp.Modules.Platform.Application.Imports;
using NSubstitute;

namespace Merp.Modules.Platform.UnitTests.Imports;

public sealed class ImportReaderTests
{
    private static readonly ImportColumn[] Columns =
    [
        new("code", "test.code", Required: true, MaxLength: 5),
        new("amount", "test.amount", ImportColumnType.Number),
        new("count", "test.count", ImportColumnType.WholeNumber),
        new("start", "test.start", ImportColumnType.Date),
        new("active", "test.active", ImportColumnType.Boolean),
        new("kind", "test.kind", ImportColumnType.Choice, Choices: [new("Small", "test.kind.small"), new("Large", "test.kind.large")]),
    ];

    private static readonly string?[] Headers = ["Kod *", "Tutar", "Adet", "Başlangıç", "Aktif", "Tür"];

    private readonly ImportReader _reader = new(Localizer());

    [Fact]
    public void Read_ParsesEachTypeInBothTheCellAndTheTypedForm()
    {
        var sheet = _reader.Read(
        [
            Headers,
            ["A1", "1234.5", "3", "2026-10-13", "true", "Small"],
            ["a2", "1.234,5", "4", "13.10.2026", "Evet", "büyük"],
            [null, null, null, null, null, null],
            ["A3", "-7", null, "1/2/2026", "hayır", "large"],
        ], Columns, "tr-TR");

        sheet.Errors.ShouldBeEmpty();
        sheet.Rows.Select(r => r.Number).ShouldBe([2, 3, 5]);
        sheet.Rows[0].AsDecimal("amount").ShouldBe(1234.5m);
        sheet.Rows[1].AsDecimal("amount").ShouldBe(1234.5m);
        sheet.Rows[2].AsDecimal("amount").ShouldBe(-7m);
        sheet.Rows[0].AsDate("start").ShouldBe(new DateOnly(2026, 10, 13));
        sheet.Rows[1].AsDate("start").ShouldBe(new DateOnly(2026, 10, 13));
        sheet.Rows[2].AsDate("start").ShouldBe(new DateOnly(2026, 2, 1));
        sheet.Rows.Select(r => r.AsBoolean("active")).ShouldBe([true, true, false]);
        sheet.Rows.Select(r => r.Text("kind")).ShouldBe(["Small", "Large", "Large"]);
        sheet.Rows[2].Text("count").ShouldBeNull();
    }

    [Fact]
    public void Read_ReportsCellErrorsWithRowAndColumn_AndLeavesThoseRowsOut()
    {
        var sheet = _reader.Read(
        [
            Headers,
            [null, "abc", "1,5", "31.02.2026", "belki", "Huge"],
            ["TOOLONG", null, null, null, null, null],
            ["OK", null, null, null, null, null],
        ], Columns, "tr-TR");

        sheet.Rows.ShouldHaveSingleItem().Number.ShouldBe(4);
        sheet.Errors.Select(e => (e.Row, e.Column, e.Message)).ShouldBe(
        [
            (2, "code", "platform.imports.errors.required"),
            (2, "amount", "platform.imports.errors.invalid_number"),
            (2, "count", "platform.imports.errors.invalid_whole_number"),
            (2, "start", "platform.imports.errors.invalid_date"),
            (2, "active", "platform.imports.errors.invalid_boolean"),
            (2, "kind", "platform.imports.errors.invalid_choice"),
            (3, "code", "common.errors.max_length"),
        ]);
    }

    [Fact]
    public void Read_MatchesHeadersByLabelOrKey_AndReportsAMissingRequiredColumn()
    {
        var byKey = _reader.Read([["CODE", "kind"], ["A1", "Small"]], Columns, "en-US");
        byKey.Errors.ShouldBeEmpty();
        byKey.Rows.ShouldHaveSingleItem().Text("code").ShouldBe("A1");

        var missing = _reader.Read([["Tutar"], ["5"]], Columns, "tr-TR");
        missing.Rows.ShouldBeEmpty();
        missing.Errors.ShouldHaveSingleItem().ShouldBe(new ImportError(1, "code", "platform.imports.errors.missing_column"));

        _reader.Read([], Columns, "tr-TR").Errors.ShouldHaveSingleItem().Message.ShouldBe("platform.imports.errors.empty");
    }

    [Fact]
    public void Read_UsesTheUsersMapping_WhenTheHeadersAreNotTheTemplates()
    {
        // D62: the user mapped the columns of their own file; unmapped ones are not read by their header either
        var mapping = new Dictionary<string, int> { ["code"] = 2, ["amount"] = 0 };
        var sheet = _reader.Read([["Toplam", "Kod", "Şube no"], ["12,5", "X", "A1"]], Columns, "tr-TR", mapping);

        sheet.Errors.ShouldBeEmpty();
        var row = sheet.Rows.ShouldHaveSingleItem();
        row.Text("code").ShouldBe("A1");
        row.AsDecimal("amount").ShouldBe(12.5m);
        _reader.Positions(["Toplam", "Kod", "Şube no"], Columns, "tr-TR", mapping).ShouldBe(mapping);
        _reader.Positions(["Toplam", "Kod *"], Columns, "tr-TR").ShouldBe(new Dictionary<string, int> { ["code"] = 1 });

        // A required column left unmapped is missing
        _reader.Read([["Toplam"], ["5"]], Columns, "tr-TR", new Dictionary<string, int> { ["amount"] = 0 })
            .Errors.ShouldHaveSingleItem().Column.ShouldBe("code");
    }

    [Theory]
    [InlineData(new[] { "12345678901", "10987654321" }, "11-digit number")]
    [InlineData(new[] { "3", "15" }, "whole number")]
    [InlineData(new[] { "12,5", "7.25" }, "decimal number")]
    [InlineData(new[] { "2026-10-13", "13.10.2026" }, "date")]
    [InlineData(new[] { "Evet", "hayır" }, "yes/no")]
    [InlineData(new[] { "a@ornek.test", "b@ornek.test" }, "e-mail address")]
    [InlineData(new[] { "Gebze", "Kadıköy" }, "text up to 7 characters")]
    [InlineData(new string[0], "empty")]
    public void Hints_DescribeTheValues_WithoutRepeatingThem(string[] values, string hint) =>
        ImportHints.Describe(values).ShouldBe(hint);

    /// <summary>Turkish labels for the test columns; messages come back as their keys.</summary>
    private static ITextLocalizer Localizer()
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["test.code"] = "Kod",
            ["test.amount"] = "Tutar",
            ["test.count"] = "Adet",
            ["test.start"] = "Başlangıç",
            ["test.active"] = "Aktif",
            ["test.kind"] = "Tür",
            ["test.kind.small"] = "Küçük",
            ["test.kind.large"] = "Büyük",
        };
        var localizer = Substitute.For<ITextLocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(2) == "tr-TR" && texts.TryGetValue(call.ArgAt<string>(0), out var text) ? text : call.ArgAt<string>(0));
        return localizer;
    }
}
