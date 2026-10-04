using System.Globalization;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Security;
using Merp.Web.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Merp.Web.Hosting;

/// <summary>
/// Binds the per-request context: the culture chosen by request localization and the active company from the
/// <c>X-Company-Id</c> header (D4). The header narrows data, it never widens permissions.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public const string CompanyHeader = "X-Company-Id";

    public async Task InvokeAsync(HttpContext context, CurrentUser currentUser, CompanyContext companyContext, IProblemDetailsService problemDetails)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(problemDetails);

        currentUser.SetCulture(CultureInfo.CurrentUICulture.Name);

        if (context.Request.Headers.TryGetValue(CompanyHeader, out var header) && !string.IsNullOrWhiteSpace(header))
        {
            if (!Guid.TryParse(header, out var companyId) || companyId == Guid.Empty)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Extensions = { [LocalizedProblemDetails.CodeKey] = "common.errors.invalid_company_header" },
                    },
                });
                return;
            }

            companyContext.Set(companyId);
        }

        await next(context);
    }
}
