namespace ToolEngine.Cli.Commands;

using Spectre.Console;
using ToolEngine.Tools.Registry;

public sealed class ListToolsCommand
{
    private readonly IToolRegistry _registry;

    public ListToolsCommand(IToolRegistry registry) =>
        _registry = registry;

    public void Execute()
    {
        var tools = _registry.ListAll();

        if (tools.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No tools are registered.[/]");
            return;
        }

        var table = new Table()
            .AddColumn("Full Name")
            .AddColumn("Version")
            .AddColumn("Type")
            .AddColumn("Description");

        foreach (var d in tools)
            table.AddRow(
                d.FullName,                   // "namespace.name" e.g. "math.calculate"
                d.Metadata.Version,
                d.Metadata.Type.ToString(),
                d.Metadata.Description);

        AnsiConsole.Write(table);
    }
}
