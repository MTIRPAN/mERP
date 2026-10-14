using Merp.Application.Abstractions.Exports;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Merp.Web.Files;

public static class ExportResults
{
    /// <summary>The exported Excel file, or the problem (e.g. too many rows, an invalid sort).</summary>
    public static Results<FileContentHttpResult, ProblemHttpResult> ToFile(this Result<ExportFile> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess
            ? TypedResults.File(result.Value.Content, ExportFile.ContentType, result.Value.FileName)
            : (ProblemHttpResult)result.Error.ToProblem();
    }
}
