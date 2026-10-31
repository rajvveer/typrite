using System.Security.Claims;
using Typrite.Api.Auth;
using AuthClaims = Typrite.Api.Auth.Claims;

namespace Typrite.Api.Endpoints;

public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/auth/config", (AuthOptions options, HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new
            {
                allowDevAuth = options.AllowDevAuth && string.IsNullOrWhiteSpace(options.OperatorAuthority),
                authority = options.OperatorAuthority,
                clientId = options.OperatorClientId,
                scope = options.OperatorScopes,
            });
        }).AllowAnonymous();

        app.MapGet("/api/v1/admin/session", (HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!Guid.TryParse(http.User.FindFirstValue(AuthClaims.AccountId), out var accountId))
            {
                return Results.Forbid();
            }

            // UI capabilities come from server-validated claims, never a user-selected role.
            string[] roles = ["Administrator", "ReleaseManager", "DeviceOperator", "CampaignOperator", "Viewer"];
            return Results.Ok(new
            {
                subject = http.User.FindFirstValue("sub"),
                role = roles.First(http.User.IsInRole),
                accountId,
            });
        }).RequireAuthorization(Policies.Viewer);
    }
}
