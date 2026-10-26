using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Application.Careers;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Merp.Modules.Recruitment.Api.Endpoints;

/// <summary>
/// The career page and applications (PLAN R3b, D49): anonymous under <c>/api/public/{tenantCode}/careers</c> (D20,
/// limited per IP), and from self service under <c>/api/recruitment/me</c>.
/// </summary>
internal static class CareerEndpoints
{
    /// <summary>The application form field that carries the application as JSON; the CV is the <c>cv</c> file.</summary>
    private const string ApplicationField = "application";

    public static void MapPublicCareerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var careers = endpoints.MapGroup("/api/public/{tenantCode}/careers")
            .WithTags("careers")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.PublicPolicy);

        careers.MapGet("", async Task<Results<Ok<CareerPage>, ProblemHttpResult>> ([FromRoute] string tenantCode, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetCareerPageQuery(), ct)))
            .WithName("GetCareerPage")
            .WithSummary("The open public openings of every company of the tenant, with the companies")
            .ProducesProblem(StatusCodes.Status404NotFound);

        careers.MapGet("/openings/{id:guid}", async Task<Results<Ok<CareerOpeningDetails>, ProblemHttpResult>> ([FromRoute] string tenantCode, Guid id, string? culture, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetCareerOpeningQuery(id, culture), ct)))
            .WithName("GetCareerOpening")
            .WithSummary("An open public opening with its form and the KVKK texts to accept")
            .ProducesProblem(StatusCodes.Status404NotFound);

        careers.MapGet("/companies/{companyId:guid}/logo", LogoAsync)
            .WithName("GetCareerCompanyLogo")
            .WithSummary("The logo of a company with an open public opening")
            .ProducesProblem(StatusCodes.Status404NotFound);

        careers.MapPost("/openings/{id:guid}/applications", ApplyAsync)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.RequestLimit(CareerCatalog.CvRules.MaxBytes)))
            // Anonymous: no cookie authenticates the request, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("SubmitCareerApplication")
            .WithSummary("Apply with a multipart form: the application as JSON in 'application', the CV in 'cv' (PDF or DOCX, at most 5 MB)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public static void MapSelfServiceCareerEndpoints(this IEndpointRouteBuilder group)
    {
        var me = group.MapGroup("/me");

        me.MapGet("/openings", async Task<Results<Ok<IReadOnlyList<CareerOpeningItem>>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyOpeningsQuery(), ct)))
            .RequirePermission(RecruitmentPermissions.Self.Apply)
            .WithName("ListMyOpenings")
            .WithSummary("Internal and public openings that take applications, for an employee");

        me.MapGet("/openings/{id:guid}", async Task<Results<Ok<CareerOpeningDetails>, ProblemHttpResult>> (Guid id, string? culture, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyOpeningQuery(id, culture), ct)))
            .RequirePermission(RecruitmentPermissions.Self.Apply)
            .WithName("GetMyOpening")
            .WithSummary("An opening an employee may apply to, with its form and the KVKK texts")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("/openings/{id:guid}/applications", ApplyMineAsync)
            .RequirePermission(RecruitmentPermissions.Self.Apply)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.RequestLimit(CareerCatalog.CvRules.MaxBytes)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("SubmitMyApplication")
            .WithSummary("An employee applies as an internal candidate: the application as JSON in 'application', the CV in 'cv'")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/applications", async Task<Results<Ok<IReadOnlyList<MyApplicationItem>>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyApplicationsQuery(), ct)))
            .RequirePermission(RecruitmentPermissions.Self.Apply)
            .WithName("ListMyApplications")
            .WithSummary("The employee's applications, newest first");
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> LogoAsync(
        [FromRoute] string tenantCode,
        Guid companyId,
        ISender sender,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCareerLogoQuery(companyId), cancellationToken);
        if (result.IsFailure)
        {
            return (ProblemHttpResult)result.Error.ToProblem();
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = "public, max-age=3600";
        return TypedResults.Stream(result.Value.Content, result.Value.ContentType);
    }

    /// <param name="tenantCode">The tenant of the career page.</param>
    /// <param name="id">The opening.</param>
    /// <param name="application">The application as JSON (<see cref="SubmitApplicationCommand"/> without the CV).</param>
    /// <param name="cv">The CV.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="json">JSON options of the API.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Accepted, ProblemHttpResult>> ApplyAsync(
        [FromRoute] string tenantCode,
        Guid id,
        [FromForm(Name = ApplicationField)] string application,
        IFormFile? cv,
        ISender sender,
        IOptions<JsonOptions> json,
        CancellationToken cancellationToken)
    {
        if (Read<SubmitApplicationCommand>(application, json.Value) is not { } command)
        {
            return InvalidApplication();
        }

        var result = await sender.Send(command with { OpeningId = id, Cv = cv.ToUpload() }, cancellationToken);
        return result.IsSuccess ? TypedResults.Accepted((string?)null) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="id">The opening.</param>
    /// <param name="application">The application as JSON (<see cref="SubmitMyApplicationCommand"/> without the CV).</param>
    /// <param name="cv">The CV.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="json">JSON options of the API.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Accepted, ProblemHttpResult>> ApplyMineAsync(
        Guid id,
        [FromForm(Name = ApplicationField)] string application,
        IFormFile? cv,
        ISender sender,
        IOptions<JsonOptions> json,
        CancellationToken cancellationToken)
    {
        if (Read<SubmitMyApplicationCommand>(application, json.Value) is not { } command)
        {
            return InvalidApplication();
        }

        var result = await sender.Send(command with { OpeningId = id, Cv = cv.ToUpload() }, cancellationToken);
        return result.IsSuccess ? TypedResults.Accepted((string?)null) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static T? Read<T>(string application, JsonOptions json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(application, json.SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProblemHttpResult InvalidApplication() =>
        (ProblemHttpResult)Error.Validation([new FieldError(ApplicationField, CommonErrors.ValidationCode)]).ToProblem();

    private static Results<Ok<T>, ProblemHttpResult> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
}
