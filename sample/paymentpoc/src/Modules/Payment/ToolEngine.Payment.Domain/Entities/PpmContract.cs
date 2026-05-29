using ToolEngine.Core.Domain.Common;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Payment.Domain.Entities;

public sealed class PpmContract : Entity<Guid>
{
    public string   PpmId                 { get; private set; } = default!;
    public string   PayerEntityId         { get; private set; } = default!;
    public Guid     PayeeId               { get; private set; }

    // Permitted scope
    public string   PermittedServiceTypes { get; private set; } = default!;  // CSV e.g. "Consulting,SoftwareLicense"
    public string   ApprovedCurrencies    { get; private set; } = default!;  // CSV e.g. "USD,GBP"
    public decimal  MaxSingleTransaction  { get; private set; }
    public decimal  AggregateCapAmount    { get; private set; }
    public decimal  CumulativePaid        { get; private set; }

    // Validity
    public DateTimeOffset EffectiveFrom   { get; private set; }
    public DateTimeOffset EffectiveTo     { get; private set; }
    public bool           IsActive        { get; private set; }

    public string?  ContractDocumentPath  { get; private set; }
    public string   ContractVersion       { get; private set; } = default!;  // immutable versioned store

    private PpmContract() { }

    public static PpmContract Create(
        string ppmId, string payerEntityId, Guid payeeId,
        string permittedServiceTypes, string approvedCurrencies,
        decimal maxSingleTransaction, decimal aggregateCapAmount,
        DateTimeOffset effectiveFrom, DateTimeOffset effectiveTo,
        string contractVersion, string? documentPath, DateTimeOffset now) =>
        new()
        {
            Id                    = Guid.NewGuid(),
            PpmId                 = ppmId,
            PayerEntityId         = payerEntityId,
            PayeeId               = payeeId,
            PermittedServiceTypes = permittedServiceTypes,
            ApprovedCurrencies    = approvedCurrencies,
            MaxSingleTransaction  = maxSingleTransaction,
            AggregateCapAmount    = aggregateCapAmount,
            CumulativePaid        = 0,
            EffectiveFrom         = effectiveFrom,
            EffectiveTo           = effectiveTo,
            IsActive              = true,
            ContractVersion       = contractVersion,
            ContractDocumentPath  = documentPath,
            CreatedAt             = now,
            UpdatedAt             = now,
        };

    // Stage 2 checks
    public bool IsEffective(DateTimeOffset at) =>
        IsActive && at >= EffectiveFrom && at <= EffectiveTo;

    public bool PermitsServiceType(ServiceType serviceType) =>
        PermittedServiceTypes.Split(',', StringSplitOptions.TrimEntries)
            .Contains(serviceType.ToString(), StringComparer.OrdinalIgnoreCase);

    public bool PermitsCurrency(string currency) =>
        ApprovedCurrencies.Split(',', StringSplitOptions.TrimEntries)
            .Contains(currency, StringComparer.OrdinalIgnoreCase);

    public bool IsWithinTransactionLimit(decimal amount)  => amount <= MaxSingleTransaction;
    public bool IsWithinAggregateCapacity(decimal amount) => CumulativePaid + amount <= AggregateCapAmount;
    public decimal RemainingAggregateCapacity             => AggregateCapAmount - CumulativePaid;

    // Called at Stage 7 reconciliation
    public void IncrementCumulativePaid(decimal amount)
    {
        CumulativePaid += amount;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }
}
