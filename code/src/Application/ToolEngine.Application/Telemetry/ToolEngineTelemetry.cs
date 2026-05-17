namespace ToolEngine.Application.Telemetry;

using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// Centralised telemetry for the ToolEngine application layer.
///
/// ActivitySource — W3C TraceContext-compatible distributed tracing.
/// Spans are automatically propagated via the 'traceparent' / 'tracestate' HTTP headers
/// when OpenTelemetry.Instrumentation.AspNetCore is configured.
///
/// Meter — OTel metrics instruments.
/// All instruments follow OTel semantic conventions:
///   - Histogram: milliseconds (ms), unit "ms"
///   - Counter: total count, unit "{invocation}"
///   - UpDownCounter: current count, unit "{approval}"
///
/// The host registers the ActivitySource and Meter name with the OTel SDK.
/// No NuGet dependencies required here — ActivitySource and Meter are BCL types (.NET 8).
/// </summary>
public static class ToolEngineTelemetry
{
    public const string ServiceName    = "ToolEngine";
    public const string ServiceVersion = "2026.1.0";

    // ── Tracing ───────────────────────────────────────────────────────────────

    public static readonly ActivitySource ActivitySource =
        new(ServiceName, ServiceVersion);

    // ── Metrics ───────────────────────────────────────────────────────────────

    private static readonly Meter Meter = new(ServiceName, ServiceVersion);

    /// <summary>
    /// Duration of tool invocations in milliseconds.
    /// Tags: tool.fullName, tenant.id, invocation.status ("succeeded"|"failed"|"suspended")
    /// </summary>
    public static readonly Histogram<double> ToolInvocationDuration =
        Meter.CreateHistogram<double>(
            "tool.invocation.duration",
            unit:        "ms",
            description: "Duration of tool invocations.");

    /// <summary>
    /// Total tool invocations.
    /// Tags: tool.fullName, tenant.id, invocation.status
    /// </summary>
    public static readonly Counter<long> ToolInvocationCount =
        Meter.CreateCounter<long>(
            "tool.invocation.count",
            unit:        "{invocation}",
            description: "Total number of tool invocations.");

    /// <summary>
    /// Approvals currently in Pending status (gauge).
    /// Tags: tenant.id, channel, risk
    /// </summary>
    public static readonly UpDownCounter<long> PendingApprovalCount =
        Meter.CreateUpDownCounter<long>(
            "tool.approval.pending.count",
            unit:        "{approval}",
            description: "Number of approvals currently pending.");

    /// <summary>
    /// Duration from approval creation to decision in milliseconds.
    /// Tags: tenant.id, channel, risk, decision ("approved"|"denied"|"expired")
    /// </summary>
    public static readonly Histogram<double> ApprovalWaitDuration =
        Meter.CreateHistogram<double>(
            "tool.approval.wait.duration",
            unit:        "ms",
            description: "Duration from approval creation to decision.");

    /// <summary>
    /// Number of times the agent loop detector opened the circuit.
    /// Tags: tool.fullName, tenant.id
    /// </summary>
    public static readonly Counter<long> LoopDetectionTriggers =
        Meter.CreateCounter<long>(
            "tool.loop.detection.triggers",
            unit:        "{trigger}",
            description: "Number of times the loop detection circuit opened.");

    /// <summary>
    /// Number of times the daily budget cap was hit.
    /// Tags: tenant.id
    /// </summary>
    public static readonly Counter<long> DailyBudgetExceeded =
        Meter.CreateCounter<long>(
            "tool.daily.budget.exceeded",
            unit:        "{event}",
            description: "Number of times a tenant hit the daily tool-call budget cap.");
}
