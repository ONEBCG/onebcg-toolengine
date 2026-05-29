using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Scenarios;

/// <summary>
/// Demonstrates a Stage 4 KYC block. Risq Capital triggers a CONFIRMED_MATCH
/// in the KYC screening stub. Stages 1-3 pass; Stage 4 is blocked.
/// </summary>
public sealed class PaymentKycBlockScenario : IScenarioDefinition, IRequiresSetup
{
    public string Name        => "payment.kyc-block";
    public string Version     => "v1";
    public string Description =>
        "Demonstrates a KYC screening block at Stage 4. " +
        "Risq Capital + PPM-003: Stages 1-3 pass, Stage 4 is blocked (CONFIRMED_MATCH).";

    public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { initiatorId = new { type = "string", @default = "scenario-demo" } }
    });

    public async Task<JsonElement> SetupAsync(
        JsonElement input, IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instruction = PaymentInstruction.Create(
            "ONE BCG UK Ltd", "GB", "PAYER-ONEBCG-001",
            "Risq Capital", 1_000m, "USD",
            ServiceType.ManagementConsulting, "PPM-003",
            "scenario-demo", DateTimeOffset.UtcNow);

        db.Set<PaymentInstruction>().Add(instruction);
        await db.SaveChangesAsync(ct);

        return JsonSerializer.SerializeToElement(new { prid = instruction.Id });
    }

    public ToolPlan Build(JsonElement input)
    {
        var prid = input.GetProperty("prid").GetGuid();

        return new ToolPlan(
            PlanId: Guid.NewGuid(),
            Mode:   ExecutionMode.Sequential,
            Steps:
            [
                new ToolStep("step-1-verify-payee", "payment", "verify-payee", "v1",
                    ToJson(new { paymentId = prid, payeeRef = "Risq Capital" }), []),

                new ToolStep("step-2-ppm-check", "payment", "ppm-check", "v1",
                    ToJson(new { paymentId = prid, ppmId = "PPM-003",
                                 verifiedPayeeId = Guid.Empty, serviceType = 0,
                                 grossAmount = 1_000m, currency = "USD" }),
                    ["step-1-verify-payee"],
                    new Dictionary<string, string>
                    {
                        ["verifiedPayeeId"] = "step-1-verify-payee.payeeId",
                    }),

                new ToolStep("step-3-calculate-wht", "payment", "calculate-wht", "v1",
                    ToJson(new { paymentId = prid, payerJurisdiction = "GB",
                                 payeeJurisdiction = "US", serviceType = 0,
                                 grossAmount = 1_000m, currency = "USD",
                                 taxYear = DateTimeOffset.UtcNow.Year }),
                    ["step-1-verify-payee"],
                    new Dictionary<string, string>
                    {
                        ["payeeJurisdiction"] = "step-1-verify-payee.jurisdiction",
                    }),

                new ToolStep("step-4-kyc-screen", "payment", "kyc-screen", "v1",
                    ToJson(new { paymentId = prid, payeeId = Guid.Empty,
                                 payeeLegalName = "PLACEHOLDER",
                                 payeeJurisdiction = "UNKNOWN", entityType = "Corporate",
                                 taxIdentifier = (string?)null,
                                 paymentAmount = 1_000m, paymentPurpose = "ManagementConsulting" }),
                    ["step-1-verify-payee", "step-3-calculate-wht"],
                    new Dictionary<string, string>
                    {
                        ["payeeId"]           = "step-1-verify-payee.payeeId",
                        ["payeeLegalName"]    = "step-1-verify-payee.legalName",
                        ["payeeJurisdiction"] = "step-1-verify-payee.jurisdiction",
                        ["entityType"]        = "step-1-verify-payee.entityType",
                    }),
            ]);
    }

    private static JsonElement ToJson(object obj) =>
        JsonSerializer.SerializeToElement(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters           = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
}
