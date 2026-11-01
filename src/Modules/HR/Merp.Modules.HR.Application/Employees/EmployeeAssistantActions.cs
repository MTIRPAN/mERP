using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>
/// The assistant's employee search (D65): active people by name or registry number, within the user's data scope like
/// the employee list.
/// </summary>
internal sealed class EmployeeSearchAssistantAction(ISender sender) : IAssistantAction
{
    public AssistantActionDefinition Definition { get; } = new(
        "hr.employees",
        "Find active employees the user may see, by name or registry number; without a search, how many there are.",
        HrPermissions.Employees.Read,
        [new("search", AssistantParameterType.Text, "Part of the person's name or the registry number, as the user wrote it.")]);

    public async Task<Result<AssistantAnswer>> RunAsync(AssistantArguments arguments, CancellationToken cancellationToken)
    {
        var search = arguments.Text("search");
        var employees = await sender.Send(
            new GetEmployeesQuery(new PageRequest(1, AssistantAnswer.MaxItems, GetEmployeesQuery.DefaultSort), Search: search),
            cancellationToken);
        return employees.IsSuccess
            ? new AssistantAnswer(
                employees.Value.TotalCount,
                [.. employees.Value.Items.Select(e => new AssistantItem(
                    e.FullName,
                    string.Join(" · ", new[] { e.JobName, e.OrgUnitName, e.CompanyName }.Where(p => !string.IsNullOrWhiteSpace(p))),
                    $"/hr/employees/{e.Id}"))],
                search is null ? "/hr/employees" : $"/hr/employees?search={Uri.EscapeDataString(search)}")
            : employees.Error;
    }
}
