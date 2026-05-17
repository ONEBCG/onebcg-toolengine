namespace ToolEngine.Core.Domain.Contracts;

using ToolEngine.Core.Domain.Common;

/// <summary>Structured error surfaced in ToolResponse. Wraps domain Error with HTTP-friendly status.</summary>
public sealed record ToolError(
    string Code,
    string Description,
    int    HttpStatusCode = 500,
    IDictionary<string, object?>? Extensions = null)
{
    public static ToolError FromError(Error error, int statusCode = 500) =>
        new(error.Code, error.Description, statusCode);

    public static ToolError Validation(string message) =>
        new("VALIDATION_ERROR", message, 400);

    public static ToolError NotFound(string message) =>
        new("NOT_FOUND", message, 404);

    public static ToolError Internal(string message) =>
        new("INTERNAL_ERROR", message, 500);
}
