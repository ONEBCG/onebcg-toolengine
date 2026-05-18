using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ToolEngine.Application.Extensions;
using ToolEngine.Cli.Guards;
using ToolEngine.Cli.Repl;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Infrastructure.Extensions;
using ToolEngine.Llm.Extensions;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Executor.Extensions;
using ToolEngine.Tools.Registry.Extensions;
using ToolEngine.Tools.Samples.Extensions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .MinimumLevel.Warning()
    .CreateLogger();

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()
    .ConfigureServices((context, services) =>
    {
        services.AddToolRegistry();
        services.AddToolSamples();
        services.AddToolExecutor();
        services.AddToolApplication();
        services.AddToolLlm(context.Configuration);
        services.AddToolInfrastructure(
            opt => opt.UseSqlite("Data Source=toolengine-cli.db"));

        // CLI uses synchronous console prompts instead of async email/webhook channels.
        services.AddTransient<IHumanApprovalGate, ConsoleApprovalGate>();

        services.AddTransient<ReplLoop>();
    })
    .Build();

// Drop and recreate schema on every startup — prevents stale-schema errors when
// entities are added (e.g. OutboxMessages). CLI data is ephemeral; seed after create.
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider
                  .GetRequiredService<ToolEngine.Infrastructure.Persistence.AppDbContext>();
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();

    var clock     = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
    var devTenant = Tenant.Create("onebcg-default-tenant", "ONE BCG Default Tenant", "cli-seed", clock).Value;
    devTenant.AllowNamespace("*");
    db.Set<Tenant>().Add(devTenant);
    await db.SaveChangesAsync();
}

// StartAsync runs all IHostedService.StartAsync — this is where tool registration happens.
await host.StartAsync();

var repl = host.Services.GetRequiredService<ReplLoop>();
await repl.RunAsync(CancellationToken.None);

await host.StopAsync();
