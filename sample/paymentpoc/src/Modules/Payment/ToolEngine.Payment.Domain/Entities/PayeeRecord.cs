using ToolEngine.Core.Domain.Common;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Payment.Domain.Entities;

public sealed class PayeeRecord : Entity<Guid>
{
    public string      LegalName          { get; private set; } = default!;
    public string      Jurisdiction       { get; private set; } = default!;
    public EntityType  EntityType         { get; private set; }
    public PayeeStatus Status             { get; private set; }
    public string?     TaxIdentifier      { get; private set; }
    public string?     RegistrationNumber { get; private set; }

    // Bank details — must be complete before payment can proceed (Stage 1 barrier)
    public string?     BankAccountNumber  { get; private set; }
    public string?     Iban               { get; private set; }
    public string?     SwiftBic           { get; private set; }
    public string?     RoutingCode        { get; private set; }

    public DateTimeOffset OnboardedAt     { get; private set; }
    public DateTimeOffset? KycRefreshedAt { get; private set; }

    private PayeeRecord() { }

    public static PayeeRecord Create(
        Guid id, string legalName, string jurisdiction, EntityType entityType,
        string? taxIdentifier, string? registrationNumber,
        string? bankAccountNumber, string? iban, string? swiftBic, string? routingCode,
        DateTimeOffset now) =>
        new()
        {
            Id                 = id,
            LegalName          = legalName,
            Jurisdiction       = jurisdiction,
            EntityType         = entityType,
            Status             = PayeeStatus.Active,
            TaxIdentifier      = taxIdentifier,
            RegistrationNumber = registrationNumber,
            BankAccountNumber  = bankAccountNumber,
            Iban               = iban,
            SwiftBic           = swiftBic,
            RoutingCode        = routingCode,
            OnboardedAt        = now,
            CreatedAt          = now,
            UpdatedAt          = now,
        };

    // Stage 1 barrier: all bank details must be present
    public bool HasCompleteBankDetails() =>
        (!string.IsNullOrWhiteSpace(Iban) || !string.IsNullOrWhiteSpace(BankAccountNumber))
     && !string.IsNullOrWhiteSpace(SwiftBic);

    public void Suspend(string reason) { Status = PayeeStatus.Suspended; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Activate()             { Status = PayeeStatus.Active;    UpdatedAt = DateTimeOffset.UtcNow; }
}
