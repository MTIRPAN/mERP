using System.Diagnostics;
using Merp.Application.Abstractions.Localization;
using Merp.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Web.Errors;

/// <summary>
/// Turns every ProblemDetails response into the mERP shape: localized <c>title</c> and <c>detail</c>, a stable
/// <c>code</c>, <c>traceId</c> and, for validation errors, <c>errors</c> (field → messages) plus
/// <c>fieldErrors</c> (field, code, message).
/// </summary>
public static class LocalizedProblemDetails
{
    /// <summary>Internal extension keys set by producers and consumed by <see cref="Customize"/>.</summary>
    public const string CodeKey = "code";
    internal const string ArgsKey = "__args";
    internal const string FieldsKey = "__fields";

    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        problem.Status = status;
        var localizer = context.HttpContext.RequestServices.GetRequiredService<ITextLocalizer>();

        var code = problem.Extensions.TryGetValue(CodeKey, out var value) && value is string text ? text : ProblemDetailsCodes.DefaultErrorCode(status);
        var args = problem.Extensions.TryGetValue(ArgsKey, out var rawArgs) ? rawArgs as IReadOnlyDictionary<string, object?> : null;

        problem.Type ??= $"https://httpstatuses.io/{status}";
        problem.Title = localizer.Get(ProblemDetailsCodes.TitleKey(status));
        problem.Detail = localizer.Get(code, args);
        problem.Extensions[CodeKey] = code;
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        problem.Extensions.Remove(ArgsKey);
        problem.Instance = null;

        if (problem.Extensions.TryGetValue(FieldsKey, out var rawFields) && rawFields is IReadOnlyList<FieldError> fields)
        {
            problem.Extensions.Remove(FieldsKey);
            var localized = fields
                .Select(f => new FieldProblem(f.Field, f.Code, localizer.Get(f.Code, f.Args)))
                .ToList();
            problem.Extensions["errors"] = localized
                .GroupBy(f => f.Field, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(f => f.Message).ToArray(), StringComparer.Ordinal);
            problem.Extensions["fieldErrors"] = localized;
        }
    }

    /// <summary>One invalid field in a validation problem.</summary>
    public sealed record FieldProblem(string Field, string Code, string Message);
}
