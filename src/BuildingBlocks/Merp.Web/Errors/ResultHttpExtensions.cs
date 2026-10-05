using Merp.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Merp.Web.Errors;

/// <summary>Maps application results to HTTP results; errors become localized ProblemDetails.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        (result ?? throw new ArgumentNullException(nameof(result))).IsSuccess ? Results.NoContent() : result.Error.ToProblem();

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        (result ?? throw new ArgumentNullException(nameof(result))).IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();

    /// <summary>201 Created with a location built from the created value (usually the new id).</summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, Func<T, string> location) =>
        (result ?? throw new ArgumentNullException(nameof(result))).IsSuccess
            ? Results.Created(location(result.Value), result.Value)
            : result.Error.ToProblem();

    /// <summary>Typed 200 or problem, so OpenAPI describes the success body.</summary>
    public static Results<Ok<T>, ProblemHttpResult> ToOkOrProblem<T>(this Result<T> result) =>
        (result ?? throw new ArgumentNullException(nameof(result))).IsSuccess
            ? TypedResults.Ok(result.Value)
            : (ProblemHttpResult)result.Error.ToProblem();

    /// <summary>Typed 204 or problem.</summary>
    public static Results<NoContent, ProblemHttpResult> ToNoContentOrProblem(this Result result) =>
        (result ?? throw new ArgumentNullException(nameof(result))).IsSuccess
            ? TypedResults.NoContent()
            : (ProblemHttpResult)result.Error.ToProblem();

    /// <summary>Typed 201 with the new id and its location, or problem.</summary>
    public static Results<Created<CreatedResponse>, ProblemHttpResult> ToCreatedOrProblem(this Result<Guid> result, string collectionPath) =>
        (result ?? throw new ArgumentNullException(nameof(result))).IsSuccess
            ? TypedResults.Created($"{collectionPath}/{result.Value}", new CreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var status = StatusCode(error.Type);
        var problem = new ProblemDetails { Status = status };
        problem.Extensions[LocalizedProblemDetails.CodeKey] = error.Code;
        if (error.Args.Count > 0)
        {
            problem.Extensions[LocalizedProblemDetails.ArgsKey] = error.Args;
        }

        if (error.Fields.Count > 0)
        {
            problem.Extensions[LocalizedProblemDetails.FieldsKey] = error.Fields;
        }

        return Results.Problem(problem);
    }

    public static int StatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
}
