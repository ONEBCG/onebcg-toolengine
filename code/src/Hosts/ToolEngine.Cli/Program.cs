using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ToolEngine.Application.Extensions;
using ToolEngine.Cli.Guards;
using ToolEngine.Cli.Repl;
using ToolEngine.Infrastructure.Extensions;
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
    .ConfigureServices(services =>
    {
        services.AddToolRegistry();
        services.AddToolSamples();
        services.AddToolExecutor();
        services.AddToolApplication();
        services.AddToolInfrastructure(
            opt => opt.UseSqlite("Data Source=toolengine-cli.db"));

        // CLI uses synchronous console prompts instead of async email/webhook channels.
        services.AddTransient<IHumanApprovalGate, ConsoleApprovalGate>();

        services.AddTransient<ReplLoop>();
    })
    .Build();

// Ensure DB exists
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider
                  .GetRequiredService<ToolEngine.Infrastructure.Persistence.AppDbContext>();
    db.Database.EnsureCreated();
}

// StartAsync runs all IHostedService.StartAsync — this is where tool registration happens.
await host.StartAsync();

var repl = host.Services.GetRequiredService<ReplLoop>();
await repl.RunAsync(CancellationToken.None);

await host.StopAsync();
