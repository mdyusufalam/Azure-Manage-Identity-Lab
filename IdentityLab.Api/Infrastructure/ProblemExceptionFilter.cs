using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IdentityLab.Api.Infrastructure;

/// <summary>
/// Turns a <see cref="ConfigurationMissingException"/> (raised lazily when an
/// Azure client is first resolved without its endpoint configured) into a
/// 503 ProblemDetails response. Everything else is left for the default
/// handling / developer exception page.
/// </summary>
public sealed class ProblemExceptionFilter(ILogger<ProblemExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ConfigurationMissingException ex)
        {
            return;
        }

        logger.LogError(ex,
            "An Azure client could not be created: configuration key '{ConfigKey}' is missing.", ex.ConfigKey);

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Azure service not configured",
            Detail = ex.Message,
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
        context.ExceptionHandled = true;
    }
}
