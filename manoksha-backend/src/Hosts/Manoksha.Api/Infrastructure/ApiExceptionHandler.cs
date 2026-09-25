using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Api.Infrastructure;

/// <summary>Maps exceptions to RFC 7807 problem details with a stable machine-readable "code".</summary>
internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            BusinessRuleException b => (b.StatusCode, b.Code, b.Message),
            DbUpdateConcurrencyException => (409, ErrorCodes.ConcurrencyConflict, "This record was changed by someone else. Reload and try again."),
            BadHttpRequestException b => (b.StatusCode, "BAD_REQUEST", "The request could not be read."),
            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred."),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {Code}: {Message}", code, exception.Message);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = httpContext.TraceIdentifier;
        if (exception is BusinessRuleException { Details.Count: > 0 } withDetails)
        {
            foreach (var (key, value) in withDetails.Details)
            {
                problem.Extensions[key] = value;
            }
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }
}
