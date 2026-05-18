namespace ToolEngine.Cli.Repl;

using Spectre.Console;
using ToolEngine.Cli.Commands;
using ToolEngine.Llm.Commands;
using ToolEngine.Tools.Registry;

public sealed class ReplLoop
{
    private readonly ListToolsCommand  _listCmd;
    private readonly InvokeToolCommand _invokeCmd;
    private readonly MediatR.IMediator _mediator;

    // Multi-turn chat state — null when not in chat mode
    private string? _chatSessionId;

    public ReplLoop(IToolRegistry registry, MediatR.IMediator mediator)
    {
        _listCmd   = new ListToolsCommand(registry);
        _invokeCmd = new InvokeToolCommand(mediator);
        _mediator  = mediator;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[bold]ToolEngine CLI[/] — type [grey]help[/] for commands.");
        AnsiConsole.WriteLine();

        while (!ct.IsCancellationRequested)
        {
            var prompt = _chatSessionId is not null ? "[cyan]chat>[/]" : "[grey]>[/]";
            var input  = AnsiConsole.Ask<string>(prompt).Trim();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            // Split into at most 5 parts: cmd arg1 arg2 arg3 rest
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

                // ask <natural language text> — single-turn LLM agent invocation
                case "ask" when parts.Length >= 2:
                    await AskAsync(string.Join(' ', parts[1..]), sessionId: null, ct);
                    break;

                case "ask":
                    AnsiConsole.MarkupLine("[yellow]Usage:[/] ask [grey]<natural language text>[/]");
                    AnsiConsole.MarkupLine("[grey]Example:[/] ask what is 25 times 48?");
                    break;

                // chat — enter multi-turn session mode
                case "chat" when parts.Length == 1:
                    _chatSessionId = Guid.NewGuid().ToString();
                    AnsiConsole.MarkupLine("[cyan]Chat mode started.[/] Type [grey]chat end[/] to exit.");
                    break;

                // chat end — exit chat mode
                case "chat" when parts.Length >= 2 && parts[1].Equals("end", StringComparison.OrdinalIgnoreCase):
                    _chatSessionId = null;
                    AnsiConsole.MarkupLine("[cyan]Chat session ended.[/]");
                    break;

                default:
                    // In chat mode: treat the whole line as a message
                    if (_chatSessionId is not null)
                        await AskAsync(input, sessionId: _chatSessionId, ct);
                    else
                        AnsiConsole.MarkupLine(
                            $"[red]Unknown command:[/] {parts[0]}. Type [grey]help[/].");
                    break;
            }

            AnsiConsole.WriteLine();
        }
    }

    // Mirror the API's length limit so CLI and API behave identically
    private const int MaxTextLength = 4_000;

    private async Task AskAsync(string text, string? sessionId, CancellationToken ct)
    {
        if (text.Length > MaxTextLength)
        {
            AnsiConsole.MarkupLine(
                $"[red]Input too long.[/] Maximum {MaxTextLength} characters — received {text.Length}.");
            return;
        }

        var command = new AgentChatCommand(
            Guid.NewGuid(),
            "onebcg-default-tenant",   // CLI default tenant
            "cli-user",
            text,
            sessionId);

        await AnsiConsole.Status()
            .StartAsync("Thinking...", async _ =>
            {
                var response = await _mediator.Send(command, ct);

                if (!response.Success)
                {
                    // Scope boundary — conversational refusal, not a system error.
                    // Display in cyan so the user understands what the agent can help with.
                    if (response.IsOutOfScope)
                    {
                        AnsiConsole.MarkupLine(
                            $"[cyan]Out of scope:[/] {Markup.Escape(response.Reply ?? "This request is outside the scope of available tools.")}");
                        return;
                    }

                    if (response.PendingInvocationId.HasValue)
                    {
                        AnsiConsole.MarkupLine(
                            $"[yellow]Approval required.[/] Invocation ID: {response.PendingInvocationId}");
                        return;
                    }

                    AnsiConsole.MarkupLine(
                        $"[red]Error:[/] {Markup.Escape(response.ErrorMessage ?? "Unknown error")}");
                    return;
                }

                if (response.ToolInvoked is not null)
                {
                    AnsiConsole.MarkupLine($"[grey]Tool selected:[/] [bold]{Markup.Escape(response.ToolInvoked)}[/]");
                    if (response.ToolResult.HasValue)
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(
                            response.ToolResult.Value,
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        AnsiConsole.MarkupLine("[grey]Tool result:[/]");
                        AnsiConsole.WriteLine(json);
                    }
                }

                if (!string.IsNullOrWhiteSpace(response.Reply))
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Reply)}[/]");
                }

                AnsiConsole.MarkupLine(
                    $"[grey]Tokens: {response.Usage.TotalTokens} | Cost: ${response.Usage.EstimatedCostUsd:F4} | Session: {response.SessionId[..8]}...[/]");
            });
    }

    private static void PrintHelp()
    {
        var table = new Table().HideHeaders().AddColumn("cmd").AddColumn("desc");
        table.AddRow("[bold]list[/]",                                         "List all registered tools.");
        table.AddRow("[bold]invoke[/] [grey]<ns> <name> <version> <json>[/]", "Invoke a tool directly with JSON input.");
        table.AddRow("[bold]ask[/] [grey]<text>[/]",                          "Single-turn: LLM selects and invokes the right tool.");
        table.AddRow("[bold]chat[/]",                                         "Enter multi-turn chat session.");
        table.AddRow("[bold]chat end[/]",                                     "End the current chat session.");
        table.AddRow("[bold]exit[/] / [bold]quit[/]",                         "Exit the REPL.");
        AnsiConsole.Write(table);
    }
}
