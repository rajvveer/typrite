using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Typrite.Api;
using Typrite.Api.Auth;
using Typrite.Api.Endpoints;
using Typrite.Infrastructure.Partners;
using Typrite.Infrastructure.Persistence;
using Typrite.Infrastructure.Storage;
using Typrite.Infrastructure.Weather;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var authOptions = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
var apiOptions = config.GetSection("Api").Get<ApiOptions>() ?? new ApiOptions();
var weatherOptions = config.GetSection("Weather").Get<WeatherOptions>() ?? new WeatherOptions();
var tokens = new TokenIssuer(authOptions, TimeProvider.System);
var releaseSigner = new ReleaseSigner(config.GetSection("Releases").Get<ReleaseOptions>() ?? new ReleaseOptions());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(apiOptions);
builder.Services.AddSingleton(weatherOptions);
builder.Services.AddSingleton(tokens);
builder.Services.AddSingleton(releaseSigner);

builder.Services.AddDbContext<TypriteDbContext>(o => o.UseNpgsql(config.GetConnectionString("Typrite")));
builder.Services.AddSingleton<IBlobStorage>(new FileSystemBlobStorage(config["Storage:Root"] ?? Path.Combine(AppContext.BaseDirectory, "blobs")));
builder.Services.AddSingleton<IAdPartner, MockAdPartner>();

if (weatherOptions.Provider.Equals("mock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IWeatherProvider, MockWeatherProvider>();
}
else
{
    builder.Services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(c =>
    {
        c.BaseAddress = new Uri("https://api.open-meteo.com/");
        c.Timeout = TimeSpan.FromSeconds(5);
    });
}

builder.Services.AddSingleton<WeatherService>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services
    .AddAuthentication(DeviceAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(DeviceAuthenticationHandler.SchemeName, _ => { })
    .AddJwtBearer("Operator", o =>
    {
        o.MapInboundClaims = false;
        if (!string.IsNullOrEmpty(authOptions.OperatorAuthority))
        {
            o.Authority = authOptions.OperatorAuthority;
            o.Audience = authOptions.OperatorAudience;
            o.TokenValidationParameters.RoleClaimType = Claims.Role;
            o.TokenValidationParameters.NameClaimType = "sub";
        }
        else
        {
            o.TokenValidationParameters = tokens.ValidationFor(TokenIssuer.OperatorAudience);
        }
    });

var authz = builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Device, p => p.AddAuthenticationSchemes(DeviceAuthenticationHandler.SchemeName).RequireAuthenticatedUser().RequireClaim(Claims.DeviceId));

void OperatorPolicy(string name, params string[] roles) =>
    authz.AddPolicy(name, p => p.AddAuthenticationSchemes("Operator").RequireAuthenticatedUser().RequireClaim(Claims.AccountId).RequireRole(roles));

OperatorPolicy(Policies.Viewer, "Viewer", "CampaignOperator", "DeviceOperator", "ReleaseManager", "Administrator");
OperatorPolicy(Policies.CampaignOperator, "CampaignOperator", "Administrator");
OperatorPolicy(Policies.DeviceOperator, "DeviceOperator", "Administrator");
OperatorPolicy(Policies.ReleaseManager, "ReleaseManager", "Administrator");
OperatorPolicy(Policies.Administrator, "Administrator");

void PlatformPolicy(string name, params string[] roles) =>
    authz.AddPolicy(name, p => p.AddAuthenticationSchemes("Operator").RequireAuthenticatedUser().RequireRole(roles)
        .RequireClaim(Claims.AccountId, authOptions.PlatformAccountId.ToString()));

PlatformPolicy(Policies.PlatformReleaseManager, "ReleaseManager", "Administrator");
PlatformPolicy(Policies.PlatformViewer, "Viewer", "CampaignOperator", "DeviceOperator", "ReleaseManager", "Administrator");

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("device", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst(Claims.DeviceId)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddOpenApi();
builder.Services.AddAntiforgery();

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TypriteDbContext>();
    await db.Database.MigrateAsync();
    await Seed.ApplyAsync(db, TimeProvider.System, CancellationToken.None);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

if (authOptions.AllowDevAuth)
{
    DevEndpoints.Map(app);
}

DeviceEndpoints.Map(app);
EnrollmentEndpoints.Map(app);
DeliveryEndpoints.Map(app);
EventEndpoints.Map(app);
WeatherEndpoints.Map(app);
AdminEndpoints.Map(app);
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

/// <summary>Entry point marker for WebApplicationFactory.</summary>
public partial class Program;
