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
/// Demonstrates a Stage 2 block: Horizon Advisory is paired with PPM-002
/// which is expired. The scenario runs Stage 1 (passes) then Stage 2 (blocked).
/// </summary>
public sealed class PaymentExpiredPpmScenario : IScenarioDefinition, IRequiresSetup
{
    public string Name        => "payment.expired-ppm";
    public string Version     => "v1";
    public string Description =>
        "Demonstrates a PPM contract expiry block at Stage 2. " +
        "Horizon Advisory + PPM-002: Stage 1 (verify-payee) passes, " +
        "Stage 2 (ppm-check) is blocked because the contract is expired.";

    public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
    {
        type       = "object",
        properties = new
        {
            initiatorId = new { type = "string", @default = "scenario-demo" },
        }
    });

    // Fixed scenario — input drives nothing, pre-seeded data drives the outcome
    public async Task<JsonElement> SetupAsync(
        JsonElement input, IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instruction = PaymentInstruction.Create(
            "ONE BCG UK Ltd", "GB", "PAYER-ONEBCG-001",
            "Horizon Advisory", 10_000m, "USD",
            ServiceType.ManagementConsulting, "PPM-002",
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
                new ToolStep(
                    StepId:    "step-1-verify-payee",
                    Namespace: "payment",
                    ToolName:  "verify-payee",
                    Version:   "v1",
                    Input:     ToJson(new { paymentId = prid, payeeRef = "Horizon Advisory" }),
                    DependsOn: []),

                new ToolStep(
                    StepId:    "step-2-ppm-check",
                    Namespace: "payment",
                    ToolName:  "ppm-check",
                    Version:   "v1",
                    Input:     ToJson(new
                    {
                        paymentId       = prid,
                        ppmId           = "PPM-002",
                        verifiedPayeeId = Guid.Empty, // mapped from step-1
                        serviceType     = 0,
                        grossAmount     = 10_000m,
                        currency        = "USD",
                    }),
                    DependsOn: ["step-1-verify-payee"],
                    OutputMappings: new Dictionary<string, string>
                    {
                        ["verifiedPayeeId"] = "step-1-verify-payee.payeeId",
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
