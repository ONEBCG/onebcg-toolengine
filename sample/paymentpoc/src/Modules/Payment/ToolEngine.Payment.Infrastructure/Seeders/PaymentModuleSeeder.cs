using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ToolEngine.Infrastructure;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Payment.Infrastructure.Seeders;

/// <summary>
/// Seeds payment domain reference data: payees, PPM contracts, WHT rate stubs.
/// Registered as IModuleSeeder — called by startup after db.Database.MigrateAsync().
/// </summary>
public sealed class PaymentModuleSeeder : IModuleSeeder
{
    private static readonly Guid PayeeId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PayeeId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PayeeId3 = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // ── Payee 1 — ACTIVE (will pass Stage 1) ──────────────────────────────
        if (!await db.Set<PayeeRecord>().AnyAsync(p => p.Id == PayeeId1, ct))
        {
            var payee1 = PayeeRecord.Create(
                id:                PayeeId1,
                legalName:         "Acme Consulting Ltd",
                jurisdiction:      "GB",
                entityType:        EntityType.Corporate,
                taxIdentifier:     "GB123456789",
                registrationNumber: "12345678",
                bankAccountNumber: "12345678",
                iban:              "GB29NWBK60161331926819",
                swiftBic:          "NWBKGB2L",
                routingCode:       null,
                now:               now);

            db.Set<PayeeRecord>().Add(payee1);
            logger.LogInformation("Seeded payee 'Acme Consulting Ltd' (ACTIVE, complete bank details).");
        }

        // ── Payee 2 — ACTIVE (expired PPM — tests Stage 2 contract block) ─────
        if (!await db.Set<PayeeRecord>().AnyAsync(p => p.Id == PayeeId2, ct))
        {
            var payee2 = PayeeRecord.Create(
                id:                PayeeId2,
                legalName:         "Horizon Advisory GmbH",
                jurisdiction:      "DE",
                entityType:        EntityType.Corporate,
                taxIdentifier:     "DE987654321",
                registrationNumber: "HRB-9999",
                bankAccountNumber: "98765432",
                iban:              "DE89370400440532013000",
                swiftBic:          "COBADEFFXXX",
                routingCode:       null,
                now:               now);

            db.Set<PayeeRecord>().Add(payee2);
            logger.LogInformation("Seeded payee 'Horizon Advisory GmbH' (ACTIVE — paired with expired PPM-002).");
        }

        // ── Payee 3 — ACTIVE, name contains "Risq" → KYC stub will BLOCK ─────
        if (!await db.Set<PayeeRecord>().AnyAsync(p => p.Id == PayeeId3, ct))
        {
            var payee3 = PayeeRecord.Create(
                id:                PayeeId3,
                legalName:         "Risq Capital Ltd",
                jurisdiction:      "US",
                entityType:        EntityType.Corporate,
                taxIdentifier:     "US-EIN-88-1234567",
                registrationNumber: "EIN-88-1234567",
                bankAccountNumber: "000123456789",
                iban:              null,
                swiftBic:          "CHASUS33",
                routingCode:       "021000021",
                now:               now);

            db.Set<PayeeRecord>().Add(payee3);
            logger.LogInformation("Seeded payee 'Risq Capital Ltd' (ACTIVE — KYC stub blocks on 'Risq' name).");
        }

        await db.SaveChangesAsync(ct);

        // ── PPM Contract 1 — ACTIVE, broad scope (USD/GBP, consulting, 500k cap) ──
        if (!await db.Set<PpmContract>().AnyAsync(c => c.PpmId == "PPM-001", ct))
        {
            var contract1 = PpmContract.Create(
                ppmId:                 "PPM-001",
                payerEntityId:         "PAYER-ONEBCG-001",
                payeeId:               PayeeId1,
                permittedServiceTypes: "ManagementConsulting,CloudSaas,SoftwareLicense,ContractStaffing",
                approvedCurrencies:    "USD,GBP,EUR",
                maxSingleTransaction:  250_000m,
                aggregateCapAmount:    1_000_000m,
                effectiveFrom:         now.AddYears(-1),
                effectiveTo:           now.AddYears(2),
                contractVersion:       "v1.0",
                documentPath:          null,
                now:                   now);

            db.Set<PpmContract>().Add(contract1);
            logger.LogInformation("Seeded PPM-001 (active, Acme Consulting, broad scope, 1M cap).");
        }

        // ── PPM Contract 3 — ACTIVE, Risq Capital (passes Stages 1-2, KYC blocks at Stage 4) ──
        if (!await db.Set<PpmContract>().AnyAsync(c => c.PpmId == "PPM-003", ct))
        {
            var contract3 = PpmContract.Create(
                ppmId:                 "PPM-003",
                payerEntityId:         "PAYER-ONEBCG-001",
                payeeId:               PayeeId3,
                permittedServiceTypes: "ManagementConsulting,CloudSaas",
                approvedCurrencies:    "USD,GBP",
                maxSingleTransaction:  100_000m,
                aggregateCapAmount:    500_000m,
                effectiveFrom:         now.AddYears(-1),
                effectiveTo:           now.AddYears(2),
                contractVersion:       "v1.0",
                documentPath:          null,
                now:                   now);

            db.Set<PpmContract>().Add(contract3);
            logger.LogInformation("Seeded PPM-003 (active, Risq Capital — KYC blocks at Stage 4).");
        }

        // ── PPM Contract 2 — EXPIRED (will fail Stage 2 for testing) ──────────
        if (!await db.Set<PpmContract>().AnyAsync(c => c.PpmId == "PPM-002", ct))
        {
            var contract2 = PpmContract.Create(
                ppmId:                 "PPM-002",
                payerEntityId:         "PAYER-ONEBCG-001",
                payeeId:               PayeeId2,
                permittedServiceTypes: "ManagementConsulting",
                approvedCurrencies:    "USD",
                maxSingleTransaction:  50_000m,
                aggregateCapAmount:    200_000m,
                effectiveFrom:         now.AddYears(-3),
                effectiveTo:           now.AddYears(-1),  // ← EXPIRED
                contractVersion:       "v1.0",
                documentPath:          null,
                now:                   now);

            db.Set<PpmContract>().Add(contract2);
            logger.LogInformation("Seeded PPM-002 (expired — tests Stage 2 contract block).");
        }

        // ── WHT Rate Entries (stub: all 0% — engine not wired yet) ────────────
        var whtCombinations = new[]
        {
            ("GB", "IN", ServiceType.ManagementConsulting),
            ("GB", "IN", ServiceType.SoftwareLicense),
            ("GB", "IN", ServiceType.CloudSaas),
            ("US", "IN", ServiceType.ManagementConsulting),
            ("US", "SG", ServiceType.SoftwareLicense),
            ("US", "GB", ServiceType.ManagementConsulting),
        };

        foreach (var (payer, payee, svc) in whtCombinations)
        {
            if (!await db.Set<WhtRateEntry>().AnyAsync(
                    w => w.PayerCountry == payer && w.PayeeCountry == payee
                      && w.ServiceCategory == svc, ct))
            {
                var entry = WhtRateEntry.Create(
                    payerCountry:        payer,
                    payeeCountry:        payee,
                    serviceCategory:     svc,
                    treatyArticle:       null,
                    standardRatePct:     0m,   // STUB — replace with real rates
                    reducedTreatyRatePct:0m,
                    conditionsForReduced:null,
                    treatyExists:        false,
                    ruleVersion:         "STUB-v0",
                    now:                 now);

                db.Set<WhtRateEntry>().Add(entry);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("WHT rate stubs seeded (all 0%% — engine expansion pending).");
    }
}
