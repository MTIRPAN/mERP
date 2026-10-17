using System.Net.Http.Json;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Merp.Testing.Data;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.IntegrationTests.Employees;

/// <summary>A company with a unit, a job and two positions, and helpers to hire into them.</summary>
public sealed record EmployeeSetup(Guid CompanyId, Guid UnitId, Guid JobId, Guid PositionA, Guid PositionB)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<EmployeeSetup> CreateAsync(HttpClient admin, Guid? companyId = null)
    {
        var company = companyId ?? await HrFactory.CreateCompanyAsync(admin);
        var unit = await HrFactory.CreateUnitAsync(admin, company);
        var job = await HrFactory.CreateJobAsync(admin, "Satış Uzmanı");
        return new EmployeeSetup(
            company,
            unit,
            job,
            await HrFactory.CreatePositionAsync(admin, unit, job, norm: 2),
            await HrFactory.CreatePositionAsync(admin, unit, job));
    }

    public object HireBody(string? tckn = null, DateOnly? hireDate = null, Guid? positionId = null, object? extra = null) =>
        Merge(
            new Dictionary<string, object?>
            {
                ["tckn"] = tckn ?? TurkishData.Tckn(),
                ["firstName"] = "Ayşe",
                ["lastName"] = "Yılmaz",
                ["companyId"] = CompanyId,
                ["hireDate"] = (hireDate ?? new DateOnly(2026, 1, 1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["employmentType"] = "Indefinite",
                ["positionId"] = positionId ?? PositionA,
            },
            extra);

    public async Task<Guid> HireAsync(HttpClient admin, DateOnly? hireDate = null, object? extra = null) =>
        await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync("/api/hr/employees", HireBody(hireDate: hireDate, extra: extra), Ct));

    public static Dictionary<string, object?> Merge(Dictionary<string, object?> body, object? extra)
    {
        if (extra is not null)
        {
            foreach (var property in extra.GetType().GetProperties())
            {
                body[property.Name] = property.GetValue(extra);
            }
        }

        return body;
    }

    /// <summary>The person with employments and assignments, as stored.</summary>
    public static Task<Employee> LoadAsync(HrFactory factory, Guid employeeId) =>
        factory.QueryAsync(db => db.Employees.AsNoTracking()
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleAsync(e => e.Id == EmployeeId.From(employeeId), Ct));

    public static Task<uint> VersionAsync(HrFactory factory, Guid employeeId) =>
        factory.QueryAsync(db => db.Employees.AsNoTracking()
            .Where(e => e.Id == EmployeeId.From(employeeId))
            .Select(e => EF.Property<uint>(e, "Version"))
            .SingleAsync(Ct));

    /// <summary>Taken seats of a position, as the positions list shows them.</summary>
    public static async Task<int> FilledAsync(HttpClient admin, Guid companyId, Guid positionId)
    {
        var page = (await admin.GetFromJsonAsync<Merp.Application.Abstractions.Paging.PagedResult<Application.Organization.PositionListItem>>(
            $"/api/hr/positions?companyId={companyId}&pageSize=200",
            Ct))!;
        return page.Items.Single(p => p.Id == positionId).Filled;
    }
}
