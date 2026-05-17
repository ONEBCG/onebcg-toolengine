namespace ToolEngine.Cli.Guards;

using Spectre.Console;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// CLI-side IHumanApprovalGate. Presents a colour-coded Spectre.Console prompt.
/// Low risk: auto-approved silently. Medium/High/Critical: explicit Y/N prompt.
///
/// Synchronous by design — CLI execution is single-threaded and blocking the
/// terminal is the correct behaviour for human-in-the-loop controls.
/// </summary>
public sealed class ConsoleApprovalGate : IHumanApprovalGate
{
    public Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalContext   context,
        string            reason,
        ApprovalRisk      risk,
        object?           inputSummary,
        CancellationToken ct = default)
    {
        if (risk == ApprovalRisk.Low)
            return Task.FromResult(ApprovalDecision.Allow("system-auto"));

        var riskColor = risk switch
        {
            ApprovalRisk.Critical => "red",
            ApprovalRisk.High     => "darkorange3",
            ApprovalRisk.Medium   => "yellow",
            _                     => "white"
        };

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold {riskColor}]⚠  Approval Required — {risk} Risk[/]");
        AnsiConsole.MarkupLine(
            $"  [dim]Tool   :[/] [cyan]{Markup.Escape(context.ToolFullName)}[/]");
        AnsiConsole.MarkupLine(
            $"  [dim]Reason :[/] {Markup.Escape(reason)}");

        if (inputSummary is not null)
            AnsiConsole.MarkupLine(
                $"  [dim]Input  :[/] {Markup.Escape(inputSummary.ToString() ?? string.Empty)}");

        AnsiConsole.WriteLine();

        var approved = AnsiConsole.Confirm(
            $"[bold]Allow execution of '{Markup.Escape(context.ToolFullName)}'?[/]",
            defaultValue: false);

        var decision = approved
            ? ApprovalDecision.Allow("cli-user")
            : ApprovalDecision.Deny("cli-user", "Denied at CLI prompt.");

        AnsiConsole.MarkupLine(approved
            ? "[green]Approved.[/]"
            : "[red]Denied.[/]");
        AnsiConsole.WriteLine();

        return Task.FromResult(decision);
    }
}
