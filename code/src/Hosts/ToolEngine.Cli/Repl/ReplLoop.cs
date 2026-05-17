namespace ToolEngine.Cli.Repl;

using Spectre.Console;
using ToolEngine.Cli.Commands;
using ToolEngine.Tools.Registry;

public sealed class ReplLoop
{
    private readonly ListToolsCommand  _listCmd;
    private readonly InvokeToolCommand _invokeCmd;

    public ReplLoop(IToolRegistry registry, MediatR.IMediator mediator)
    {
        _listCmd   = new ListToolsCommand(registry);
        _invokeCmd = new InvokeToolCommand(mediator);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[bold]ToolEngine CLI[/] — type [grey]help[/] for commands.");
        AnsiConsole.WriteLine();

        while (!ct.IsCancellationRequested)
        {
            var input = AnsiConsole.Ask<string>("[grey]>[/]").Trim();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            // Split into at most 5 parts: cmd ns name version json
            var parts = input.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);

            switch (parts[0].ToLowerInvariant())
            {
                case "exit":
                case "quit":
                    AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
                    return;

                case "help":
                    PrintHelp();
                    break;

                case "list":
                    _listCmd.Execute();
                    break;

                // invoke <ns> <name> <version> <json-input>
                case "invoke" when parts.Length >= 5:
                    await _invokeCmd.ExecuteAsync(parts[1], parts[2], parts[3], parts[4], ct);
                    break;

                case "invoke":
                    AnsiConsole.MarkupLine(
                        "[yellow]Usage:[/] invoke [grey]<namespace> <name> <version> <json-input>[/]");
                    AnsiConsole.MarkupLine(
                        "[grey]Example:[/] invoke math calculate v1 {\"a\":10,\"b\":5,\"operator\":\"add\"}");
                    break;

                default:
                    AnsiConsole.MarkupLine(
                        $"[red]Unknown command:[/] {parts[0]}. Type [grey]help[/].");
                    break;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static void PrintHelp()
    {
        var table = new Table().HideHeaders().AddColumn("cmd").AddColumn("desc");
        table.AddRow("[bold]list[/]",
                     "List all registered tools with their full names.");
        table.AddRow("[bold]invoke[/] [grey]<ns> <name> <version> <json>[/]",
                     "Invoke a tool. Example: invoke math calculate v1 {\"a\":2,\"b\":3,\"operator\":\"add\"}");
        table.AddRow("[bold]exit[/] / [bold]quit[/]",
                     "Exit the REPL.");
        AnsiConsole.Write(table);
    }
}
