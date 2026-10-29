using Microsoft.EntityFrameworkCore;
using Typrite.Infrastructure.Partners;
using Typrite.Infrastructure.Persistence;
using Typrite.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration.GetSection("Worker").Get<WorkerOptions>() ?? new WorkerOptions());
builder.Services.AddDbContext<TypriteDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Typrite")));
builder.Services.AddSingleton<IAdPartner, MockAdPartner>();
builder.Services.AddHostedService<AggregationHostedService>();
builder.Services.AddHostedService<ReconciliationHostedService>();

var host = builder.Build();
await host.RunAsync();
