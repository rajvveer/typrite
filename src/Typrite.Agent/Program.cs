using Typrite.Agent;
using Typrite.Agent.Identity;

var builder = Host.CreateApplicationBuilder(args);
var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();

builder.Services.AddWindowsService(o => o.ServiceName = "Typrite.Agent");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<DeviceApiClient>(c =>
{
    c.BaseAddress = new Uri(options.ApiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<AgentService>();

await builder.Build().RunAsync();
