using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Scenarios;

/// <summary>
/// Runs the four compliance stages (verify-payee → ppm-check → calculate-wht → kyc-screen)
/// in sequence with output mappings. Demonstrates the core value proposition:
/// payeeId from Stage 1 flows automatically into Stages 2, 3, and 4 without
/// the caller needing to wire it manually.
/// </summary>
public sealed class PaymentComplianceScenario : IScenarioDefinition, IRequiresSetup
{
    public string Name        => "payment.compliance-check";
    public string Version     => "v1";
    public string Description =>
        "Run the four compliance stages (payee verification → contract check → " +
        "WHT calculation → KYC screening) in sequence. " +
        "Output mapping automatically wires payeeId and jurisdiction from Stage 1 " +
        "into Stages 2, 3, and 4. Requires an active seeded payee and PPM.";

    public JsonElement InputSchema => ToJson(new
    {
        type = "object",
        required = new[] { "payerName", "payerJurisdiction", "payerEntityId",
                           "payeeRef", "grossAmount", "currency",
                           "serviceType", "ppmId", "initiatorId" },
        properties = new
        {
            payerName         = new { type = "string",  description = "Legal name of the paying entity." },
            payerJurisdiction = new { type = "string",  description = "ISO 2-letter jurisdiction code (e.g. GB)." },
            payerEntityId     = new { type = "string",  description = "Internal payer entity identifier." },
            payeeRef          = new { type = "string",  description = "Payee name or reference (e.g. Acme Consulting)." },
            grossAmount       = new { type = "number",  description = "Payment amount before WHT." },
            currency          = new { type = "string",  description = "ISO currency code (GBP, USD, EUR)." },
            serviceType       = new { type = "integer", description = "0=ManagementConsulting 1=CloudSaas 2=SoftwareLicense 3=ContractStaffing" },
            ppmId             = new { type = "string",  description = "PPM contract identifier (e.g. PPM-001)." },
            initiatorId       = new { type = "string",  description = "User or system initiating the check." },
        }
    });

    /// <summary>
    /// Creates a PaymentInstruction and injects the PRID into the input
    /// so all four tool handlers can update the correct record.
    /// </summary>
    public async Task<JsonElement> SetupAsync(
        JsonElement input, IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payerName         = input.GetProperty("payerName").GetString()!;
        var payerJurisdiction = input.GetProperty("payerJurisdiction").GetString()!;
        var payerEntityId     = input.GetProperty("payerEntityId").GetString()!;
        var payeeRef          = input.GetProperty("payeeRef").GetString()!;
        var grossAmount       = input.GetProperty("grossAmount").GetDecimal();
        var currency          = input.GetProperty("currency").GetString()!;
        var serviceType       = ParseServiceType(input.GetProperty("serviceType"));
        var ppmId             = input.GetProperty("ppmId").GetString()!;
        var initiatorId       = input.GetProperty("initiatorId").GetString()!;

        var instruction = PaymentInstruction.Create(
            payerName, payerJurisdiction, payerEntityId,
            payeeRef, grossAmount, currency,
            serviceType, ppmId, initiatorId,
            DateTimeOffset.UtcNow);

        db.Set<PaymentInstruction>().Add(instruction);
        await db.SaveChangesAsync(ct);

        // Rebuild input with prid injected
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input)!;
        dict["prid"] = JsonSerializer.SerializeToElement(instruction.Id);
        return JsonSerializer.SerializeToElement(dict);
    }

    public ToolPlan Build(JsonElement input)
    {
        var prid              = input.GetProperty("prid").GetGuid();
        var payeeRef          = input.GetProperty("payeeRef").GetString()!;
        var ppmId             = input.GetProperty("ppmId").GetString()!;
        var serviceType       = (int)ParseServiceType(input.GetProperty("serviceType"));
        var grossAmount       = input.GetProperty("grossAmount").GetDecimal();
        var currency          = input.GetProperty("currency").GetString()!;
        var payerJurisdiction = input.GetProperty("payerJurisdiction").GetString()!;

        return new ToolPlan(
            PlanId: Guid.NewGuid(),
            Mode:   ExecutionMode.Sequential,
            Steps:
            [
                // Stage 1 — Verify Payee
                new ToolStep(
                    StepId:    "step-1-verify-payee",
                    Namespace: "payment",
                    ToolName:  "verify-payee",
                    Version:   "v1",
                    Input:     ToJson(new { paymentId = prid, payeeRef }),
                    DependsOn: []),

                // Stage 2 — PPM Check
                // verifiedPayeeId mapped from step-1 output (payeeId)
                new ToolStep(
                    StepId:    "step-2-ppm-check",
                    Namespace: "payment",
                    ToolName:  "ppm-check",
                    Version:   "v1",
                    Input:     ToJson(new
                    {
                        paymentId       = prid,
                        ppmId,
                        verifiedPayeeId = Guid.Empty, // placeholder — overridden by mapping
                        serviceType,
                        grossAmount,
                        currency,
                    }),
                    DependsOn: ["step-1-verify-payee"],
                    OutputMappings: new Dictionary<string, string>
                    {
                        ["verifiedPayeeId"] = "step-1-verify-payee.payeeId",
                    }),

                // Stage 3 — Calculate WHT
                // payeeJurisdiction mapped from step-1 output (jurisdiction)
                new ToolStep(
                    StepId:    "step-3-calculate-wht",
                    Namespace: "payment",
                    ToolName:  "calculate-wht",
                    Version:   "v1",
                    Input:     ToJson(new
                    {
                        paymentId         = prid,
                        payerJurisdiction,
                        payeeJurisdiction = "UNKNOWN", // placeholder — overridden by mapping
                        serviceType,
                        grossAmount,
                        currency,
                        taxYear           = DateTimeOffset.UtcNow.Year,
                    }),
                    DependsOn: ["step-1-verify-payee"],
                    OutputMappings: new Dictionary<string, string>
                    {
                        ["payeeJurisdiction"] = "step-1-verify-payee.jurisdiction",
                    }),

                // Stage 4 — KYC Screen
                // payeeId, payeeLegalName, payeeJurisdiction, entityType all mapped from step-1
                new ToolStep(
                    StepId:    "step-4-kyc-screen",
                    Namespace: "payment",
                    ToolName:  "kyc-screen",
                    Version:   "v1",
                    Input:     ToJson(new
                    {
                        paymentId         = prid,
                        payeeId           = Guid.Empty,   // placeholder
                        payeeLegalName    = "PLACEHOLDER", // placeholder
                        payeeJurisdiction = "UNKNOWN",    // placeholder
                        entityType        = "Corporate",  // placeholder
                        taxIdentifier     = (string?)null,
                        paymentAmount     = grossAmount,
                        paymentPurpose    = "ManagementConsulting",
                    }),
                    DependsOn: ["step-1-verify-payee", "step-3-calculate-wht"],
                    OutputMappings: new Dictionary<string, string>
                    {
                        ["payeeId"]           = "step-1-verify-payee.payeeId",
                        ["payeeLegalName"]    = "step-1-verify-payee.legalName",
                        ["payeeJurisdiction"] = "step-1-verify-payee.jurisdiction",
                        ["entityType"]        = "step-1-verify-payee.entityType",
                    }),
            ]);
    }

    /// <summary>
    /// Parses ServiceType from a JsonElement that may be an integer (0) or a string ("ManagementConsulting").
    /// Handles both formats so that plan samples, LLM agents, and direct API callers all work correctly.
    /// </summary>
    private static ServiceType ParseServiceType(JsonElement el) =>
        el.ValueKind == JsonValueKind.Number
            ? (ServiceType)el.GetInt32()
            : Enum.Parse<ServiceType>(el.GetString()!, ignoreCase: true);

    private static JsonElement ToJson(object obj) =>
        JsonSerializer.SerializeToElement(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters           = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
}
