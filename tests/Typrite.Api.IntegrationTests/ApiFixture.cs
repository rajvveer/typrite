using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Typrite.Contracts;

namespace Typrite.Api.IntegrationTests;

/// <summary>One API host per test class against the local PostgreSQL test database; migrations run at startup.</summary>
public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("TYPRITE_TEST_PG") ?? "Host=localhost;Database=typrite_test;Username=thenoah";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Typrite", ConnectionString);
        builder.UseSetting("Storage:Root", Path.Combine(Path.GetTempPath(), "typrite-test-blobs"));
        builder.UseSetting("Weather:Provider", "mock");
        builder.UseSetting("Auth:AllowDevAuth", "true");
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }

    /// <summary>Test classes run in parallel; migrations must not.</summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (HostGate)
        {
            return base.CreateHost(builder);
        }
    }

    private static readonly object HostGate = new();

    public async Task<(Guid DeviceId, HttpClient Client)> RegisterDeviceAsync(Guid? accountId = null)
    {
        var deviceId = Guid.NewGuid();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var anon = CreateClient();
        var res = await anon.PostAsJsonAsync("api/v1/devices/dev-register", new { deviceId, secret, appVersion = "0.1.0", accountId }, ContractsJson.Options);
        res.EnsureSuccessStatusCode();
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return (deviceId, client);
    }

    public async Task<HttpClient> OperatorAsync(string role, Guid? accountId = null, string? subject = null)
    {
        var anon = CreateClient();
        var res = await anon.PostAsJsonAsync("api/v1/admin/dev-login", new { subject = subject ?? $"op-{Guid.NewGuid():N}", role, accountId }, ContractsJson.Options);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<DevLoginResponse>(ContractsJson.Options);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    public async Task<Guid> CreateAccountAsync(string name)
    {
        var admin = await OperatorAsync("Administrator");
        var res = await admin.PostAsJsonAsync("api/v1/admin/accounts", new { name }, ContractsJson.Options);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IdResponse>(ContractsJson.Options))!.Id;
    }

    public sealed record DevLoginResponse(string Token, Guid AccountId);

    public sealed record IdResponse(Guid Id);
}
