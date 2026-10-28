using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Platform.Hosting;

/// <summary>Turns exceptions into RFC 7807 problem responses with a stable <c>code</c> the UI can switch on.</summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (AppException ex) when (!ctx.Response.HasStarted)
        {
            log.LogInformation("{Code} ({Status}): {Message}", ex.Code, ex.Status, ex.Message);
            await Write(ctx, ex.Status, ex.Code, ex.Message);
        }
        catch (BadHttpRequestException ex) when (!ctx.Response.HasStarted)
        {
            await Write(ctx, 400, "BAD_REQUEST", ex.InnerException?.Message ?? ex.Message);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // client went away — nothing to report
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            log.LogError(ex, "Unhandled exception");
            await Write(ctx, 500, "INTERNAL_ERROR", "Something went wrong on our side. Please try again.");
        }
    }

    private static Task Write(HttpContext ctx, int status, string code, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        var body = new Dictionary<string, object?>
        {
            ["type"] = $"https://claimflow.demo/errors/{code.ToLowerInvariant()}",
            ["title"] = code,
            ["status"] = status,
            ["detail"] = detail,
            ["code"] = code,
            ["correlationId"] = CorrelationContext.Current,
        };
        return ctx.Response.WriteAsJsonAsync(body, JsonDefaults.Options);
    }
}
