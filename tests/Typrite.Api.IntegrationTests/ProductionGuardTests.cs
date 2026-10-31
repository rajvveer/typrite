using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace Typrite.Api.IntegrationTests;

public sealed class ProductionGuardTests
{
    private static readonly string ReleaseKey = CreateReleaseKey();

    private static string CreateReleaseKey()
    {
        using var key = RSA.Create(2048);
        return key.ExportPkcs8PrivateKeyPem();
    }

    private sealed class ProductionHost(string? overrideKey = null, string? overrideValue = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Typrite", "Host=database.example;Database=typrite;Username=test;SSL Mode=VerifyFull");
            builder.UseSetting("Auth:AllowDevAuth", "false");
            builder.UseSetting("Auth:SigningKey", new string('x', 64));
            builder.UseSetting("Auth:OperatorAuthority", "https://identity.example");
            builder.UseSetting("Auth:OperatorAudience", "typrite-api");
            builder.UseSetting("Auth:OperatorClientId", "typrite-admin");
            builder.UseSetting("Releases:PublisherPrivateKeyPem", ReleaseKey);
            builder.UseSetting("Api:PublicBaseUrl", "https://typrite.example");
            builder.UseSetting("Storage:Root", Path.Combine(Path.GetTempPath(), "typrite-production-test"));
            builder.UseSetting("Database:MigrateOnStartup", "false");
            if (overrideKey is not null) builder.UseSetting(overrideKey, overrideValue);
        }
    }

    [Theory]
    [InlineData("Auth:SigningKey", "")]
    [InlineData("Auth:OperatorAuthority", "http://identity.example")]
    [InlineData("Auth:OperatorAudience", "")]
    [InlineData("Auth:OperatorClientId", "")]
    [InlineData("Auth:OperatorScopes", "profile")]
    [InlineData("Releases:PublisherPrivateKeyPem", "")]
    [InlineData("Api:PublicBaseUrl", "https://localhost:7210")]
    [InlineData("Api:PublicBaseUrl", "http://typrite.example")]
    [InlineData("Storage:Root", "")]
    [InlineData("Storage:Root", "./ephemeral-blobs")]
    [InlineData("ConnectionStrings:Typrite", "")]
    [InlineData("ConnectionStrings:Typrite", "Host=database.example;SSL Mode=Disable")]
    public void Incomplete_production_configuration_is_rejected(string key, string value)
    {
        using var factory = new ProductionHost(key, value);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_exposes_only_public_auth_settings_and_disables_dev_endpoints()
    {
        using var factory = new ProductionHost();
        using var client = factory.CreateClient();
        var config = await client.GetStringAsync("/api/v1/auth/config");
        Assert.Contains("\"allowDevAuth\":false", config, StringComparison.Ordinal);
        Assert.DoesNotContain("SigningKey", config, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", config, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/admin/dev-login", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/devices/dev-register", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/session")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    private sealed class ProductionWithDevAuth : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Typrite", ApiFixture.ConnectionString);
            builder.UseSetting("Auth:AllowDevAuth", "true");
            builder.UseSetting("Database:MigrateOnStartup", "false");
        }
    }

    [Fact]
    public void Dev_auth_cannot_be_enabled_in_production()
    {
        using var factory = new ProductionWithDevAuth();

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }
}
