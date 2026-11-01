using System.Globalization;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Domain.Leave;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>The assistant's "how much leave do I have" (D65): the balances of the user's main employment.</summary>
internal sealed class MyLeaveBalancesAssistantAction(ISender sender, ITextLocalizer texts, ICurrentUser currentUser) : IAssistantAction
{
    public AssistantActionDefinition Definition { get; } = new(
        "hr.my_leave_balances",
        "The user's own leave balances: for each leave type the days available, used and waiting for approval.",
        HrPermissions.Self.LeaveRead,
        []);

    public async Task<Result<AssistantAnswer>> RunAsync(AssistantArguments arguments, CancellationToken cancellationToken)
    {
        var balances = await sender.Send(new GetMyLeaveBalancesQuery(), cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        var culture = CultureInfo.GetCultureInfo(currentUser.Culture);
        string Number(decimal value) => value.ToString("0.##", culture);
        return new AssistantAnswer(
            balances.Value.Count,
            [.. balances.Value.Take(AssistantAnswer.MaxItems).Select(b => new AssistantItem(
                b.LeaveTypeName,
                texts.Get("hr.assistant.leave_balance", new Dictionary<string, object?>
                {
                    ["available"] = Number(b.Available),
                    ["used"] = Number(b.Used),
                    ["pending"] = Number(b.Pending),
                }),
                "/hr/me/leave"))],
            "/hr/me/leave");
    }
}

/// <summary>The assistant's "my leave requests" (D65), optionally of some states, newest first.</summary>
internal sealed class MyLeaveRequestsAssistantAction(ISender sender, ITextLocalizer texts, ICurrentUser currentUser) : IAssistantAction
{
    public AssistantActionDefinition Definition { get; } = new(
        "hr.my_leave_requests",
        "The user's own leave requests, newest first.",
        HrPermissions.Self.LeaveRead,
        [
            new(
                "status",
                AssistantParameterType.Choice,
                "Only requests in these states: Draft (not sent), Pending (waiting for approval), Returned (sent back for correction), Approved, Rejected, Cancelled, CancellationPending (cancellation waiting for approval).",
                Enum.GetNames<LeaveRequestStatus>()),
        ]);

    public async Task<Result<AssistantAnswer>> RunAsync(AssistantArguments arguments, CancellationToken cancellationToken)
    {
        var statuses = arguments.Choices<LeaveRequestStatus>("status");
        var requests = await sender.Send(
            new GetMyLeaveRequestsQuery(new PageRequest(1, AssistantAnswer.MaxItems), statuses.Count > 0 ? statuses : null),
            cancellationToken);
        if (requests.IsFailure)
        {
            return requests.Error;
        }

        var culture = CultureInfo.GetCultureInfo(currentUser.Culture);
        return new AssistantAnswer(
            requests.Value.TotalCount,
            [.. requests.Value.Items.Select(r => new AssistantItem(
                $"{r.LeaveTypeName} · {r.Number}",
                $"{AssistantDates.Range(r.StartDate, r.EndDate, culture)} · {texts.Get($"web.hr.leave_requests.status.{r.Status}")}",
                $"/hr/me/leave?request={r.Id}"))],
            "/hr/me/leave");
    }
}

internal static class AssistantDates
{
    /// <summary>"6 Kasım 2026" or "6 Kasım 2026 – 9 Kasım 2026" in the user's culture.</summary>
    public static string Range(DateOnly start, DateOnly end, CultureInfo culture) =>
        start == end
            ? start.ToString("d MMMM yyyy", culture)
            : $"{start.ToString("d MMMM yyyy", culture)} – {end.ToString("d MMMM yyyy", culture)}";
}
