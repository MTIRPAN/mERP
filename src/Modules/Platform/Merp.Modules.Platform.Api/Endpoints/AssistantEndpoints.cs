using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Assistant;
using Merp.Web.Errors;
using Merp.Web.Navigation;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>The Ctrl+K assistant (PLAN A12, D65).</summary>
internal static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapPost("/assistant/ask", AskAsync)
            .RequireAuthenticatedUser()
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("AskAssistant")
            .WithSummary("Answer a question in natural language with one read-only action or a page to open (A12, D65)")
            .WithDescription("The model only picks the action or page; the action runs as the caller with their permissions and data scope, and its rows are not sent to the model. Every signed-in user may ask (D22).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Ok<AssistantResponse>, ProblemHttpResult>> AskAsync(
        AskAssistantRequest request,
        IEnumerable<IMenuContributor> menuContributors,
        IPermissionChecker permissionChecker,
        IAiService ai,
        ITextLocalizer texts,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var pages = new List<AssistantPage>();
        await CollectPagesAsync(menuContributors.SelectMany(c => c.GetMenuItems()), null, pages, permissionChecker, ai, texts, cancellationToken);
        var result = await sender.Send(new AskAssistantQuery(request.Text, pages), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    // The menu pages the user may open, as in /me, with their texts in the user's language
    private static async Task CollectPagesAsync(
        IEnumerable<MenuItem> items,
        string? section,
        List<AssistantPage> pages,
        IPermissionChecker permissionChecker,
        IAiService ai,
        ITextLocalizer texts,
        CancellationToken cancellationToken)
    {
        foreach (var item in items.OrderBy(i => i.Order))
        {
            if ((item.AiFeature is not null && !ai.IsEnabled(item.AiFeature))
                || (item.Permission is not null && !await permissionChecker.HasPermissionAsync(item.Permission, cancellationToken)))
            {
                continue;
            }

            var label = texts.Get(item.LabelKey);
            if (item.Path is not null)
            {
                pages.Add(new AssistantPage(item.Key, label, section, item.Path));
            }

            if (item.Children is not null)
            {
                await CollectPagesAsync(item.Children, label, pages, permissionChecker, ai, texts, cancellationToken);
            }
        }
    }
}

/// <param name="Text">The question, at most 300 characters.</param>
public sealed record AskAssistantRequest(string? Text);
