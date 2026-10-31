using Npgsql;
using Typrite.Api.Auth;
using Typrite.Api.Endpoints;

namespace Typrite.Api;

internal static class ProductionConfiguration
{
    public static void Validate(IConfiguration config, AuthOptions auth, ApiOptions api, ReleaseOptions releases)
    {
        Require(!string.IsNullOrWhiteSpace(auth.SigningKey), "Auth:SigningKey must be set and retained across deployments.");
        Require(IsHttps(auth.OperatorAuthority), "Auth:OperatorAuthority must be an absolute HTTPS URL.");
        Require(!string.IsNullOrWhiteSpace(auth.OperatorAudience), "Auth:OperatorAudience is required.");
        Require(!string.IsNullOrWhiteSpace(auth.OperatorClientId), "Auth:OperatorClientId is required.");
        Require(auth.OperatorScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("openid", StringComparer.Ordinal),
            "Auth:OperatorScopes must include openid.");
        Require(!string.IsNullOrWhiteSpace(releases.PublisherPrivateKeyPem), "Releases:PublisherPrivateKeyPem must be set and retained across deployments.");
        Require(IsHttps(api.PublicBaseUrl) && !new Uri(api.PublicBaseUrl).IsLoopback,
            "Api:PublicBaseUrl must be the public HTTPS origin, not localhost.");
        var root = config["Storage:Root"];
        Require(!string.IsNullOrWhiteSpace(root) && Path.IsPathFullyQualified(root),
            "Storage:Root must be an absolute path on a persistent writable volume.");
        var connection = config.GetConnectionString("Typrite");
        Require(!string.IsNullOrWhiteSpace(connection), "ConnectionStrings:Typrite is required.");
        var database = new NpgsqlConnectionStringBuilder(connection);
        Require(database.SslMode == SslMode.VerifyFull, "ConnectionStrings:Typrite must use SSL Mode=VerifyFull.");
    }

    private static bool IsHttps(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
