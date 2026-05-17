namespace ToolEngine.Api.Middleware;

using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failure on {Path}: {Errors}",
                ctx.Request.Path,
                string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)));

            ctx.Response.StatusCode  = 400;
            ctx.Response.ContentType = "application/json";

            var problem = new ValidationProblemDetails(
                ex.Errors
                  .GroupBy(e => e.PropertyName)
                  .ToDictionary(
                      g => g.Key,
                      g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = 400,
                Title  = "Validation failed."
            };

            await ctx.Response.WriteAsJsonAsync(problem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);

            ctx.Response.StatusCode  = 500;
            ctx.Response.ContentType = "application/json";

            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "An unexpected error occurred.",
                traceId = ctx.TraceIdentifier
            });
        }
    }
}
