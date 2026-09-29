using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Minos.Domain;
using Minos.Application;
using Minos.Application.Accounts;

namespace Minos.Api;

internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProblemDetails details = exception switch
        {
            DomainValidationException validation => new ValidationProblemDetails(new Dictionary<string, string[]> { [validation.Property] = [validation.Message] })
                { Status = 400, Title = "Validation failed." },
            AccountAuthenticationException => new ProblemDetails { Status = 401, Title = "Authentication failed." },
            AccountRegistrationException => new ProblemDetails { Status = 400, Title = "Unable to register with those details." },
            ExternalLoginException => new ProblemDetails { Status = 409, Title = "Unable to complete external sign-in. Sign in to your existing account to link the provider." },
            AccessDeniedException => new ProblemDetails { Status = 403, Title = "Access denied." },
            ResourceNotFoundException => new ProblemDetails { Status = 404, Title = "Resource not found." },
            ConcurrencyConflictException => new ProblemDetails { Status = 409, Title = "The resource changed. Reload and try again." },
            ArtifactValidationException => new ProblemDetails { Status = 400, Title = "Invalid test package." },
            AdmissionLimitExceededException => new ProblemDetails { Status = 429, Title = "Too many active submissions." },
            ArgumentOutOfRangeException => new ProblemDetails { Status = 400, Title = "Invalid request." },
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
