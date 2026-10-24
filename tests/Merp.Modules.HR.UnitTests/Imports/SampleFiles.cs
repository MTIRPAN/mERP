using System.Globalization;
using System.Text;
using System.Text.Json;
using Bogus;
using ClosedXML.Excel;
using Merp.Application.Abstractions.Imports;
using Merp.Modules.HR.Application.Imports;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;

namespace Merp.Modules.HR.UnitTests.Imports;

/// <summary>
/// The sample files of <c>samples/</c> (PLAN H4): the job catalog, the units, the positions and 2,000 employees of the
/// company <see cref="CompanyCode"/>, fictitious and generated with Bogus from a fixed seed, so the same files come out
/// every time. Headers and choices are the templates' Turkish texts, read from HR's <c>tr-TR.json</c>.
/// </summary>
internal static class SampleFiles
{
    public const string CompanyCode = "DEMO";
    public const int EmployeeCount = 2000;
    public const string Jobs = "1-unvanlar.xlsx";
    public const string Units = "2-birimler.xlsx";
    public const string Positions = "3-pozisyonlar.xlsx";
    public const string Employees = "4-personel.xlsx";

    private const int Seed = 20261024;

    private static readonly string[] Cities =
        ["İstanbul", "Ankara", "İzmir", "Bursa", "Kocaeli", "Konya", "Adana", "Antalya", "Kayseri", "Eskişehir", "Samsun", "Trabzon", "Gaziantep", "Sakarya", "Denizli"];

    /// <summary>Hire dates end here, so no one is hired in the future whenever the files are loaded.</summary>
    private static readonly DateOnly LastHireDate = new(2026, 9, 30);

    // (code, name, family, level); the first is the general manager
    private static readonly (string Code, string Name, string Family, string Level)[] JobCatalog =
    [
        ("GM", "Genel Müdür", "Yönetim", "Üst Yönetici"),
        ("DIR", "Direktör", "Yönetim", "Üst Yönetici"),
        ("MDR", "Müdür", "Yönetim", "Müdür"),
        ("SEF", "Şef", "Yönetim", "Şef"),
        ("TL", "Takım Lideri", "Yönetim", "Şef"),
        ("MUH-UZM", "Muhasebe Uzmanı", "Finans", "Uzman"),
        ("FIN-ANL", "Finansal Analist", "Finans", "Uzman"),
        ("IK-UZM", "İnsan Kaynakları Uzmanı", "İnsan Kaynakları", "Uzman"),
        ("BORDRO", "Bordro Uzmanı", "İnsan Kaynakları", "Uzman"),
        ("SAT-TEM", "Satış Temsilcisi", "Satış", "Uzman"),
        ("MUS-TEM", "Müşteri Temsilcisi", "Satış", "Uzman"),
        ("PAZ-UZM", "Pazarlama Uzmanı", "Pazarlama", "Uzman"),
        ("YAZ-GEL", "Yazılım Geliştirici", "Bilgi Teknolojileri", "Uzman"),
        ("SIS-UZM", "Sistem Uzmanı", "Bilgi Teknolojileri", "Uzman"),
        ("DEST-UZM", "Destek Uzmanı", "Bilgi Teknolojileri", "Uzman Yardımcısı"),
        ("URT-OPR", "Üretim Operatörü", "Üretim", "Operatör"),
        ("BAKIM", "Bakım Teknisyeni", "Üretim", "Teknisyen"),
        ("KALITE", "Kalite Kontrol Uzmanı", "Üretim", "Uzman"),
        ("DEPO", "Depo Görevlisi", "Lojistik", "Operatör"),
        ("SEVK", "Sevkiyat Uzmanı", "Lojistik", "Uzman"),
        ("SATIN", "Satın Alma Uzmanı", "Satın Alma", "Uzman"),
        ("IDARI", "İdari İşler Uzmanı", "İdari İşler", "Uzman"),
        ("ASIST", "Yönetici Asistanı", "İdari İşler", "Uzman Yardımcısı"),
        ("STJ", "Stajyer", "Genel", "Stajyer"),
    ];

    // Directorates with their departments, and each department's staff jobs
    private static readonly (string Code, string Name, (string Code, string Name, string[] Jobs)[] Departments)[] Directorates =
    [
        ("FIN", "Finans Direktörlüğü",
        [
            ("MUH", "Muhasebe", ["MUH-UZM"]),
            ("FPL", "Finansal Planlama", ["FIN-ANL"]),
            ("HAZ", "Hazine", ["FIN-ANL", "MUH-UZM"]),
        ]),
        ("IKD", "İnsan Kaynakları Direktörlüğü",
        [
            ("ISE", "İşe Alım", ["IK-UZM"]),
            ("BRD", "Bordro ve Özlük", ["BORDRO", "IK-UZM"]),
            ("EGT", "Eğitim ve Gelişim", ["IK-UZM"]),
        ]),
        ("SAT", "Satış ve Pazarlama Direktörlüğü",
        [
            ("KUR", "Kurumsal Satış", ["SAT-TEM"]),
            ("BAY", "Bayi Satış", ["SAT-TEM", "MUS-TEM"]),
            ("PAZ", "Pazarlama", ["PAZ-UZM"]),
            ("MHZ", "Müşteri Hizmetleri", ["MUS-TEM"]),
        ]),
        ("URT", "Üretim Direktörlüğü",
        [
            ("MON", "Montaj", ["URT-OPR"]),
            ("ISL", "Talaşlı İmalat", ["URT-OPR", "BAKIM"]),
            ("BKM", "Bakım Onarım", ["BAKIM"]),
            ("KAL", "Kalite Güvence", ["KALITE"]),
        ]),
        ("BTD", "Bilgi Teknolojileri Direktörlüğü",
        [
            ("YZL", "Yazılım Geliştirme", ["YAZ-GEL"]),
            ("ALT", "Altyapı ve Sistem", ["SIS-UZM"]),
            ("DST", "Kullanıcı Destek", ["DEST-UZM"]),
        ]),
        ("LOJ", "Lojistik ve Satın Alma Direktörlüğü",
        [
            ("DEP", "Depo", ["DEPO"]),
            ("SVK", "Sevkiyat", ["SEVK", "DEPO"]),
            ("STN", "Satın Alma", ["SATIN"]),
        ]),
        ("IDR", "İdari İşler Direktörlüğü",
        [
            ("IDI", "İdari İşler", ["IDARI", "ASIST"]),
        ]),
    ];

    /// <summary>The four files by name, as the template would hold them: one sheet, a header row and the data.</summary>
    public static IReadOnlyDictionary<string, byte[]> Generate(string repositoryRoot)
    {
        var texts = Texts(repositoryRoot);
        var data = Build();
        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Jobs] = Workbook(texts, Columns(new JobImport(null!, null!, null!, null!)), "hr.imports.jobs.name", data.Jobs),
            [Units] = Workbook(texts, Columns(new OrgUnitImport(null!, null!, null!, null!, null!)), "hr.imports.org_units.name", data.Units),
            [Positions] = Workbook(texts, Columns(new PositionImport(null!, null!, null!, null!, null!)), "hr.imports.positions.name", data.Positions),
            [Employees] = Workbook(texts, Columns(new EmployeeImport(null!, null!, null!, null!, null!, null!)), "hr.imports.employees.name", data.Employees),
        };
    }

    /// <summary>The cells of a sheet, for comparing files by content (a workbook's bytes carry its creation time).</summary>
    public static List<List<string>> Cells(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook);
        using var book = new XLWorkbook(stream);
        return [.. book.Worksheet(1).RowsUsed().Select(r => r.Cells(1, r.LastCellUsed().Address.ColumnNumber).Select(c => c.GetString()).ToList())];
    }

    /// <summary>The folder holding <c>Merp.slnx</c>.</summary>
    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Merp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Merp.slnx was not found above the test output.");
    }

    private static IReadOnlyList<ImportColumn> Columns(IImportDefinition definition) => definition.Columns;

    private static Dictionary<string, string> Texts(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "src", "Modules", "HR", "Merp.Modules.HR.Api", "Localization", "tr-TR.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    private static byte[] Workbook(Dictionary<string, string> texts, IReadOnlyList<ImportColumn> columns, string nameKey, List<Dictionary<string, string?>> rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(texts[nameKey]);
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = texts[columns[c].HeaderKey] + (columns[c].Required ? " *" : string.Empty);
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                if (rows[r].GetValueOrDefault(columns[c].Key) is { } value)
                {
                    // Choices show their label, as picked from the template's list
                    var choice = columns[c].Choices?.SingleOrDefault(o => o.Value == value);
                    sheet.Cell(r + 2, c + 1).SetValue(choice is null ? value : texts[choice.LabelKey]);
                }
            }
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static (List<Dictionary<string, string?>> Jobs, List<Dictionary<string, string?>> Units, List<Dictionary<string, string?>> Positions, List<Dictionary<string, string?>> Employees) Build()
    {
        var faker = new Faker("tr") { Random = new Randomizer(Seed) };
        var jobs = JobCatalog.Select(j => Row(
            (JobImport.Columns.Code, j.Code),
            (JobImport.Columns.Name, j.Name),
            (JobImport.Columns.Family, j.Family),
            (JobImport.Columns.Level, j.Level))).ToList();

        var units = new List<Dictionary<string, string?>>();
        var positions = new List<Dictionary<string, string?>>();

        // Seats to fill: (position code, seats, job code, is a manager)
        var seats = new List<(string Position, int Seats, string Job, bool Manager)>();
        void Unit(string code, string name, OrgUnitType type, string? parent, string managerJob, string? reportsTo)
        {
            units.Add(Row(
                (OrgUnitImport.Columns.Company, CompanyCode),
                (OrgUnitImport.Columns.Code, code),
                (OrgUnitImport.Columns.Name, name),
                (OrgUnitImport.Columns.Type, type.ToString()),
                (OrgUnitImport.Columns.Parent, parent),
                (OrgUnitImport.Columns.CostCenter, "MM-" + code)));
            positions.Add(Position(code + "-Y", code, managerJob, reportsTo, 1, manager: true));
            seats.Add((code + "-Y", 1, managerJob, true));
        }

        Unit("GMD", "Genel Müdürlük", OrgUnitType.Directorate, null, "GM", null);
        positions.Add(Position("GMD-AS", "GMD", "ASIST", null, 2, manager: false));
        seats.Add(("GMD-AS", 2, "ASIST", false));
        foreach (var (directorate, directorateName, departments) in Directorates)
        {
            Unit(directorate, directorateName, OrgUnitType.Directorate, "GMD", "DIR", "GMD-Y");
            foreach (var (department, departmentName, staffJobs) in departments)
            {
                var departmentCode = directorate + "-" + department;
                Unit(departmentCode, departmentName, OrgUnitType.Department, directorate, "MDR", directorate + "-Y");
                for (var team = 1; team <= 2; team++)
                {
                    var teamCode = departmentCode + "-" + team.ToString(CultureInfo.InvariantCulture);
                    Unit(teamCode, $"{departmentName} {team}. Ekip", OrgUnitType.Team, departmentCode, "TL", departmentCode + "-Y");
                    foreach (var job in staffJobs)
                    {
                        var code = $"{teamCode}-{job}";
                        positions.Add(Position(code, teamCode, job, null, 0, manager: false));
                        seats.Add((code, 0, job, false));
                    }
                }

                var intern = departmentCode + "-STJ";
                positions.Add(Position(intern, departmentCode, "STJ", null, 2, manager: false));
                seats.Add((intern, 2, "STJ", false));
            }
        }

        // Staff seats share what is left of the employees, unevenly as in a real company
        var staff = seats.Select((seat, index) => (seat, index)).Where(x => x.seat.Seats == 0).Select(x => x.index).ToList();
        var weights = staff.Select(_ => faker.Random.Int(1, 6)).ToList();
        var total = EmployeeCount - seats.Sum(seat => seat.Seats);
        var shares = weights.Select(w => Math.Max(1, total * w / weights.Sum())).ToList();
        for (var (k, diff) = (0, total - shares.Sum()); diff != 0; k = (k + 1) % shares.Count)
        {
            if (diff > 0)
            {
                shares[k]++;
                diff--;
            }
            else if (shares[k] > 1)
            {
                shares[k]--;
                diff++;
            }
        }

        for (var k = 0; k < staff.Count; k++)
        {
            seats[staff[k]] = seats[staff[k]] with { Seats = shares[k] };
        }

        foreach (var (code, count, _, _) in seats)
        {
            positions.Single(p => p[PositionImport.Columns.Code] == code)[PositionImport.Columns.Norm] = count.ToString(CultureInfo.InvariantCulture);
        }

        var employees = new List<Dictionary<string, string?>>();
        var tcknList = new HashSet<string>(StringComparer.Ordinal);
        var emails = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (position, count, job, manager) in seats)
        {
            for (var i = 0; i < count; i++)
            {
                employees.Add(Employee(faker, position, job, manager, tcknList, emails));
            }
        }

        return (jobs, units, positions, employees);
    }

    private static Dictionary<string, string?> Position(string code, string unit, string job, string? reportsTo, int norm, bool manager) =>
        Row(
            (PositionImport.Columns.Company, CompanyCode),
            (PositionImport.Columns.Code, code),
            (PositionImport.Columns.OrgUnit, unit),
            (PositionImport.Columns.Job, job),
            (PositionImport.Columns.ReportsTo, reportsTo),
            (PositionImport.Columns.Norm, norm.ToString(CultureInfo.InvariantCulture)),
            (PositionImport.Columns.IsManager, manager ? "Evet" : "Hayır"),
            (PositionImport.Columns.ManagesUnit, manager ? "Evet" : "Hayır"));

    private static Dictionary<string, string?> Employee(Faker faker, string position, string job, bool manager, HashSet<string> tcknList, HashSet<string> emails)
    {
        var female = faker.Random.Bool();
        var gender = female ? Bogus.DataSets.Name.Gender.Female : Bogus.DataSets.Name.Gender.Male;
        var firstName = faker.Name.FirstName(gender);
        var lastName = faker.Name.LastName(gender);
        var intern = job == "STJ";
        // Managers are older, interns young
        var (bornFrom, bornTo) = intern ? (2000, 2005) : manager ? (1966, 1986) : (1966, 2002);
        var birthDate = DateOnly.FromDateTime(faker.Date.Between(new DateTime(bornFrom, 1, 1), new DateTime(bornTo, 12, 31)));
        var earliestHire = birthDate.AddYears(intern ? 20 : 22);
        var hireFrom = new[] { earliestHire, new DateOnly(2006, 1, 1), manager ? new DateOnly(2006, 1, 1) : new DateOnly(2008, 1, 1) }.Max();
        var hireDate = intern
            ? LastHireDate.AddDays(-faker.Random.Int(0, 240))
            : DateOnly.FromDateTime(faker.Date.Between(hireFrom.ToDateTime(TimeOnly.MinValue), LastHireDate.ToDateTime(TimeOnly.MinValue)));

        var type = intern ? EmploymentType.Intern : faker.Random.WeightedRandom(
            [EmploymentType.Indefinite, EmploymentType.FixedTerm, EmploymentType.PartTime],
            [0.86f, 0.09f, 0.05f]);
        if (manager)
        {
            type = EmploymentType.Indefinite;
        }

        string tckn;
        do
        {
            tckn = Tckn(faker);
        }
        while (!tcknList.Add(tckn));

        var local = $"{Ascii(firstName)}.{Ascii(lastName)}";
        var email = $"{local}@demo.example";
        for (var n = 2; !emails.Add(email); n++)
        {
            email = $"{local}{n}@demo.example";
        }

        return Row(
            (EmployeeImport.Columns.Company, CompanyCode),
            (EmployeeImport.Columns.Tckn, tckn),
            (EmployeeImport.Columns.FirstName, firstName),
            (EmployeeImport.Columns.LastName, lastName),
            (EmployeeImport.Columns.BirthDate, Date(birthDate)),
            (EmployeeImport.Columns.BirthPlace, faker.PickRandom(Cities)),
            (EmployeeImport.Columns.Gender, (female ? Gender.Female : Gender.Male).ToString()),
            (EmployeeImport.Columns.Nationality, "TR"),
            (EmployeeImport.Columns.MaritalStatus, faker.Random.Bool(0.6f) ? nameof(MaritalStatus.Married) : nameof(MaritalStatus.Unmarried)),
            (EmployeeImport.Columns.MilitaryStatus, female
                ? nameof(MilitaryStatus.NotApplicable)
                : faker.Random.WeightedRandom([MilitaryStatus.Completed, MilitaryStatus.Exempt, MilitaryStatus.Postponed], [0.8f, 0.1f, 0.1f]).ToString()),
            (EmployeeImport.Columns.Email, email),
            (EmployeeImport.Columns.Phone, "05" + faker.Random.Int(30, 59).ToString(CultureInfo.InvariantCulture) + faker.Random.ReplaceNumbers(" ### ## ##")),
            (EmployeeImport.Columns.HireDate, Date(hireDate)),
            (EmployeeImport.Columns.EmploymentType, type.ToString()),
            (EmployeeImport.Columns.ContractEndDate, type is EmploymentType.FixedTerm or EmploymentType.Intern ? Date(hireDate.AddMonths(intern ? 6 : 12)) : null),
            (EmployeeImport.Columns.WeeklyHours, type == EmploymentType.PartTime ? faker.PickRandom("20", "24", "30") : null),
            (EmployeeImport.Columns.ProbationEndDate, Date(hireDate.AddMonths(2))),
            (EmployeeImport.Columns.Position, position));
    }

    private static Dictionary<string, string?> Row(params (string Key, string? Value)[] cells) =>
        cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>A valid TCKN: 11 digits, the first not 0, with both check digits.</summary>
    private static string Tckn(Faker faker)
    {
        var digits = new int[11];
        digits[0] = faker.Random.Int(1, 9);
        for (var i = 1; i < 9; i++)
        {
            digits[i] = faker.Random.Int(0, 9);
        }

        var odd = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var even = digits[1] + digits[3] + digits[5] + digits[7];
        digits[9] = (((odd * 7) - even) % 10 + 10) % 10;
        digits[10] = digits.Take(10).Sum() % 10;
        return string.Concat(digits);
    }

    /// <summary>The letters of a name for an e-mail address: lower case, Turkish letters folded, others dropped.</summary>
    private static string Ascii(string name)
    {
        var folded = new StringBuilder(name.Length);
        foreach (var c in name.ToLower(CultureInfo.GetCultureInfo("tr-TR")))
        {
            folded.Append(c switch
            {
                'ı' => 'i',
                'ş' => 's',
                'ğ' => 'g',
                'ü' => 'u',
                'ö' => 'o',
                'ç' => 'c',
                _ when char.IsAsciiLetterLower(c) => c,
                _ => '\0',
            });
        }

        return folded.ToString().Replace("\0", string.Empty, StringComparison.Ordinal);
    }
}
