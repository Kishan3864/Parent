using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParentalTrack.Api.Common;
using ParentalTrack.Api.Security;

namespace ParentalTrack.Api.Modules.Auth;

/// <summary>
/// Minimal API surface for <c>/api/v1/auth</c>. Endpoints only validate input and translate an
/// <see cref="AuthResult"/> into a status code; all logic lives in <see cref="AuthService"/>.
/// </summary>
internal static class AuthEndpoints
{
    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        // Shares the "login" window (10/min/IP): registration is anonymous, costs a full PBKDF2
        // hash per attempt and answers 409 for an address that already has an account, so without
        // a limiter it is both an account-enumeration oracle and a CPU amplifier.
        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AuthConstants.LoginRateLimit)
            .WithName("AuthRegister")
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AuthConstants.LoginRateLimit)
            .WithName("AuthLogin")
            .Produces<AuthResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithName("AuthRefresh")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithName("AuthLogout")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", MeAsync)
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithName("AuthMe")
            .Produces<ParentDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest? request,
        AuthService auth,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ApiResults.BadRequest("Missing request body", "A JSON body with email, password and displayName is required.");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!request.Email.IsValidEmail())
        {
            errors["email"] = ["A valid email address is required."];
        }

        var (passwordOk, passwordError) = request.Password.ValidatePassword();
        if (!passwordOk && passwordError is not null)
        {
            errors["password"] = [passwordError];
        }

        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName) || displayName.Length > 128)
        {
            errors["displayName"] = ["Display name is required and must be at most 128 characters."];
        }

        if (errors.Count > 0)
        {
            return ApiResults.ValidationProblem(errors);
        }

        var result = await auth.RegisterAsync(request.Email!.Trim(), request.Password!, displayName!, ct);

        if (result.Response is not null)
        {
            return TypedResults.Created("/api/v1/auth/me", result.Response);
        }

        return result.Error switch
        {
            AuthError.RegistrationDisabled => ApiResults.Forbidden(
                "Registration disabled",
                "Self-service registration is turned off on this server."),
            AuthError.DuplicateEmail => ApiResults.Problem(
                StatusCodes.Status409Conflict,
                "Email already registered",
                "An account already exists for that email address."),
            _ => ApiResults.Problem(StatusCodes.Status500InternalServerError, "Registration failed"),
        };
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest? request,
        AuthService auth,
        CancellationToken ct)
    {
        var email = request?.Email;
        var password = request?.Password;

        // A malformed request is reported as such: it depends on the payload alone and leaks nothing
        // about which accounts exist.
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(email))
            {
                errors["email"] = ["Email is required."];
            }

            if (string.IsNullOrEmpty(password))
            {
                errors["password"] = ["Password is required."];
            }

            return ApiResults.ValidationProblem(errors);
        }

        var result = await auth.LoginAsync(email, password, ct);

        return result.Response is not null
            ? TypedResults.Ok(result.Response)
            : InvalidCredentials();
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest? request,
        AuthService auth,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return InvalidRefreshToken();
        }

        var result = await auth.RefreshAsync(request.RefreshToken, ct);

        return result.Response is not null
            ? TypedResults.Ok(result.Response)
            : InvalidRefreshToken();
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest? request,
        AuthService auth,
        CancellationToken ct)
    {
        await auth.LogoutAsync(request?.RefreshToken, ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> MeAsync(
        ClaimsPrincipal user,
        AuthService auth,
        CancellationToken ct)
    {
        // "sub" is the claim a parent token carries; the mapped name is accepted as a fallback in case
        // the bearer handler is ever configured with inbound claim mapping on.
        if (!user.TryGetGuid(AuthConstants.SubjectClaim, out var parentId)
            && !user.TryGetGuid(ClaimTypes.NameIdentifier, out parentId))
        {
            return ApiResults.Unauthorized("Invalid token", "The token does not identify a parent.");
        }

        var parent = await auth.GetParentAsync(parentId, ct);

        return parent is not null
            ? TypedResults.Ok(parent)
            : ApiResults.NotFound("Parent not found");
    }

    /// <summary>One response body for "no such email" and "wrong password" — no user enumeration.</summary>
    private static IResult InvalidCredentials() =>
        ApiResults.Unauthorized("Invalid credentials", "The email address or password is incorrect.");

    private static IResult InvalidRefreshToken() =>
        ApiResults.Unauthorized("Invalid refresh token", "The refresh token is unknown, expired or already used.");
}
