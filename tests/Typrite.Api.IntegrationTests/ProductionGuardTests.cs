using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Typrite.Api.IntegrationTests;

public sealed class ProductionGuardTests
{
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
