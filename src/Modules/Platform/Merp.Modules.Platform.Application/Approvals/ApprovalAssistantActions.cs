using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Application.Approvals;

/// <summary>The assistant's "what waits for my approval" (D65): the caller's open tasks, the most urgent first.</summary>
internal sealed class MyApprovalTasksAssistantAction(ISender sender) : IAssistantAction
{
    public AssistantActionDefinition Definition { get; } = new(
        "platform.my_approval_tasks",
        "The approval tasks waiting for the user's decision (leave requests, hiring requests, offers and other documents), the most urgent first.",
        null,
        []);

    public async Task<Result<AssistantAnswer>> RunAsync(AssistantArguments arguments, CancellationToken cancellationToken)
    {
        var tasks = await sender.Send(new GetMyApprovalTasksQuery(new PageRequest(1, AssistantAnswer.MaxItems)), cancellationToken);
        return tasks.IsSuccess
            ? new AssistantAnswer(
                tasks.Value.TotalCount,
                [.. tasks.Value.Items.Select(t => new AssistantItem(t.Title, $"{t.RequesterName} · {t.StepName}", $"/approvals/requests/{t.RequestId}"))],
                "/approvals")
            : tasks.Error;
    }
}
