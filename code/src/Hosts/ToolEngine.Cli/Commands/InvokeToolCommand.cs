namespace ToolEngine.Cli.Commands;

using System.Text.Json;
using MediatR;
using Spectre.Console;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Enums;

public sealed class InvokeToolCommand
{
    private readonly IMediator _mediator;

    public InvokeToolCommand(IMediator mediator) =>
        _mediator = mediator;

    /// <summary>
    /// Invokes a tool by namespace + name + version with a JSON input payload.
    /// Called from ReplLoop: invoke &lt;ns&gt; &lt;name&gt; &lt;version&gt; &lt;json-input&gt;
    /// </summary>
    public async Task ExecuteAsync(
        string            ns,
        string            name,
        string            version,
        string            jsonInput,
        CancellationToken ct)
    {
        JsonElement input;
        try
        {
            input = JsonSerializer.Deserialize<JsonElement>(jsonInput);
        }
        catch
        {
            AnsiConsole.MarkupLine("[red]Invalid JSON input.[/]");
            return;
        }

        var command = new ExecuteToolCommand<JsonElement, JsonElement>(
            Guid.NewGuid(), "onebcg-default-tenant", "cli-user",
            ToolName:      name,
            ToolVersion:   version,
            Input:         input,
            ToolType:      ToolType.Logic,
            ToolNamespace: ns);

        await AnsiConsole.Status()
            .StartAsync($"Invoking [bold]{ns}.{name}[/]...", async _ =>
            {
                var response = await _mediator.Send(command, ct);

                if (response.Success)
                {
                    var json = JsonSerializer.Serialize(
                        response.Data,
                        new JsonSerializerOptions { WriteIndented = true });

                    AnsiConsole.MarkupLine("[green]Success[/]");
                    AnsiConsole.WriteLine(json);
                    AnsiConsole.MarkupLine(
                        $"[grey]Duration: {response.Metrics.Duration.TotalMilliseconds:F0}ms[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"[red]Error [[{Markup.Escape(response.Error!.Code)}]]:[/] " +
                        Markup.Escape(response.Error.Description));
                }
            });
    }
}
