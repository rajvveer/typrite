using System.Net.Http.Json;
using Typrite.Contracts;

namespace Typrite.Api.IntegrationTests;

public sealed class OperatorSessionTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Session_returns_the_authenticated_operators_role_and_account()
    {
        var account = await fixture.CreateAccountAsync("session-test");
        using var client = await fixture.OperatorAsync("Viewer", account, "read-only-operator");
        using var response = await client.GetAsync("/api/v1/admin/session");
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(ContractsJson.Options);
        Assert.Equal("Viewer", session!.Role);
        Assert.Equal(account, session.AccountId);
        Assert.Equal("read-only-operator", session.Subject);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    private sealed record SessionResponse(string Subject, string Role, Guid AccountId);
}
