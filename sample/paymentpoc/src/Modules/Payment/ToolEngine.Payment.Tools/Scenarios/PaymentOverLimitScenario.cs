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
/// Demonstrates a Stage 2 block due to transaction limit. GBP 300,000 against
/// PPM-001 which has a 250,000 single-transaction cap. Stage 1 passes; Stage 2 blocks.
/// </summary>
public sealed class PaymentOverLimitScenario : IScenarioDefinition, IRequiresSetup
{
    public string Name        => "payment.over-limit";
    public string Version     => "v1";
    public string Description =>
        "Demonstrates a transaction limit block at Stage 2. " +
        "Acme Consulting + PPM-001 + GBP 300,000: Stage 1 (verify-payee) passes, " +
        "Stage 2 (ppm-check) blocks because the amount exceeds the 250,000 single-transaction cap.";

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
            "Acme Consulting", 300_000m, "GBP",
            ServiceType.ManagementConsulting, "PPM-001",
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
                    ToJson(new { paymentId = prid, payeeRef = "Acme Consulting" }), []),

                new ToolStep("step-2-ppm-check", "payment", "ppm-check", "v1",
                    ToJson(new { paymentId = prid, ppmId = "PPM-001",
                                 verifiedPayeeId = Guid.Empty, serviceType = 0,
                                 grossAmount = 300_000m, currency = "GBP" }),
                    ["step-1-verify-payee"],
                    new Dictionary<string, string>
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
