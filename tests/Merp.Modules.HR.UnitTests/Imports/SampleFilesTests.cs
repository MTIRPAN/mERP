using System.Globalization;
using Merp.Modules.HR.Application.Imports;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.UnitTests.Imports;

/// <summary>
/// The sample files of <c>samples/</c> (PLAN H4). After changing <see cref="SampleFiles"/> or an import's columns,
/// write them again with the explicit test: <c>Merp.Modules.HR.UnitTests.exe -method "*WriteTheSampleFiles" -explicit only</c>.
/// </summary>
public sealed class SampleFilesTests
{
    private static readonly string Root = SampleFiles.RepositoryRoot();

    [Fact(Explicit = true)]
    public void WriteTheSampleFiles()
    {
        var folder = Path.Combine(Root, "samples");
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in SampleFiles.Generate(Root))
        {
            File.WriteAllBytes(Path.Combine(folder, name), content);
        }
    }

    [Fact]
    public void CommittedSampleFiles_AreWhatTheGeneratorWrites()
    {
        foreach (var (name, content) in SampleFiles.Generate(Root))
        {
            var committed = File.ReadAllBytes(Path.Combine(Root, "samples", name));
            SampleFiles.Cells(committed).ShouldBe(SampleFiles.Cells(content), $"samples/{name} is stale; write it again with WriteTheSampleFiles.");
        }
    }

    [Fact]
    public void Employees_HaveValidDistinctTckns_AndFitTheSeatsOfTheirPositions()
    {
        var files = SampleFiles.Generate(Root);
        var employees = SampleFiles.Cells(files[SampleFiles.Employees]);
        var positions = SampleFiles.Cells(files[SampleFiles.Positions]);
        int Column(List<List<string>> sheet, string header) => sheet[0].FindIndex(h => h.StartsWith(header, StringComparison.Ordinal));

        var tcknColumn = Column(employees, "T.C. kimlik no");
        var tcknList = employees.Skip(1).Select(r => r[tcknColumn]).ToList();
        tcknList.Count.ShouldBe(SampleFiles.EmployeeCount);
        tcknList.Distinct(StringComparer.Ordinal).Count().ShouldBe(SampleFiles.EmployeeCount);
        tcknList.ShouldAllBe(t => Tckn.Create(t).IsSuccess);

        var norms = positions.Skip(1).ToDictionary(r => r[Column(positions, "Pozisyon kodu")], r => int.Parse(r[Column(positions, "Norm kadro")], CultureInfo.InvariantCulture));
        var held = employees.Skip(1).GroupBy(r => r[Column(employees, "Pozisyon kodu")]).ToDictionary(g => g.Key, g => g.Count());
        held.ShouldAllBe(h => norms.ContainsKey(h.Key) && h.Value <= norms[h.Key]);
        norms.Values.ShouldAllBe(n => n >= 1 && n <= Domain.Organization.Position.MaxNorm);
    }

    [Fact]
    public void ParentsAndManagers_ComeBeforeTheRowsThatNameThem()
    {
        var files = SampleFiles.Generate(Root);
        AssertAbove(SampleFiles.Cells(files[SampleFiles.Units]), "Birim kodu", "Üst birim kodu");
        AssertAbove(SampleFiles.Cells(files[SampleFiles.Positions]), "Pozisyon kodu", "Bağlı olduğu pozisyon kodu");
    }

    [Fact]
    public void ImportKeys_MatchTheSampleFileOrder() =>
        new[] { JobImport.ImportKey, OrgUnitImport.ImportKey, PositionImport.ImportKey, EmployeeImport.ImportKey }
            .ShouldBe(["hr.jobs", "hr.org-units", "hr.positions", "hr.employees"]);

    private static void AssertAbove(List<List<string>> sheet, string codeHeader, string referenceHeader)
    {
        var code = sheet[0].FindIndex(h => h.StartsWith(codeHeader, StringComparison.Ordinal));
        var reference = sheet[0].FindIndex(h => h.StartsWith(referenceHeader, StringComparison.Ordinal));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in sheet.Skip(1))
        {
            var named = reference < row.Count ? row[reference] : string.Empty;
            (named.Length == 0 || seen.Contains(named)).ShouldBeTrue($"{row[code]} names {named} before it");
            seen.Add(row[code]);
        }
    }
}
