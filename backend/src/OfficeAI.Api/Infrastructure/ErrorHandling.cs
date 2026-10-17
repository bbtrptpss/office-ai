using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OfficeAI.Domain.Common;

namespace OfficeAI.Api.Infrastructure;

/// <summary>Maps business exceptions to RFC 7807 problem responses. Unexpected errors return 500 without internals.</summary>
public class AppExceptionHandler(IProblemDetailsService problems, ILogger<AppExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, code, title) = ex switch
        {
            NotFoundException => (404, "not_found", ex.Message),
            ConcurrencyException => (409, "concurrency_conflict", ex.Message),
            ForbiddenException => (403, "forbidden", ex.Message),
            DomainException d => (d.Code.EndsWith("cycle") || d.Code.StartsWith("task.") || d.Code.StartsWith("agent.") || d.Code.StartsWith("approval.") || d.Code.StartsWith("workflow.") ? 409 : 400, d.Code, d.Message),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (409, "duplicate", "A record with the same unique value already exists."),
            BadHttpRequestException => (400, "bad_request", "The request body is invalid."),
            _ => (500, "server_error", "An unexpected error occurred."),
        };
        if (status == 500) log.LogError(ex, "Unhandled exception");

        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code, ["correlationId"] = ctx.Items[CorrelationIdMiddleware.ItemKey] },
            },
        });
    }
}
