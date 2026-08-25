using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ParentalTrack.Api.Common;

/// <summary>
/// The single place error responses are built. Everything here emits RFC7807
/// <c>application/problem+json</c>; no module should hand-roll an error body.
/// </summary>
public static class ApiResults
{
    public static ProblemHttpResult Problem(int status, string title, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: status, title: title);

    public static ProblemHttpResult BadRequest(string title, string? detail = null) =>
        Problem(StatusCodes.Status400BadRequest, title, detail);

    public static ProblemHttpResult NotFound(string title, string? detail = null) =>
        Problem(StatusCodes.Status404NotFound, title, detail);

    public static ProblemHttpResult Unauthorized(string title, string? detail = null) =>
        Problem(StatusCodes.Status401Unauthorized, title, detail);

    public static ProblemHttpResult Forbidden(string title, string? detail = null) =>
        Problem(StatusCodes.Status403Forbidden, title, detail);

    public static Microsoft.AspNetCore.Http.HttpResults.ValidationProblem ValidationProblem(
        IDictionary<string, string[]> errors,
        string? title = null) =>
        TypedResults.ValidationProblem(errors, title: title);
}
