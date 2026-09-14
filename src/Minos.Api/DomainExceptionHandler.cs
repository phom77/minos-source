using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Minos.Domain;
using Minos.Application;

namespace Minos.Api;

internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProblemDetails details = exception switch
        {
            DomainValidationException validation => new ValidationProblemDetails(new Dictionary<string, string[]> { [validation.Property] = [validation.Message] })
                { Status = 400, Title = "Problem draft is invalid." },
            AccessDeniedException => new ProblemDetails { Status = 403, Title = "Access denied." },
            BadHttpRequestException badRequest => new ProblemDetails { Status = badRequest.StatusCode, Title = "Invalid request." },
            _ => new ProblemDetails { Status = 500, Title = "An unexpected error occurred." }
        };
        if (details.Status == 500)
            logger.LogError("Request failed. TraceId: {TraceId}; ErrorType: {ErrorType}", context.TraceIdentifier, exception.GetType().Name);
        context.Response.StatusCode = details.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = details
        });
    }
}
