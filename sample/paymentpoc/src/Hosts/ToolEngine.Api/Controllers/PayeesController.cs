using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Route("api/v1/payees")]
[Tags("Master Data")]
[Authorize]
public sealed class PayeesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PayeesController(AppDbContext db) => _db = db;

    /// <summary>List all payee records ordered by legal name.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await _db.Set<PayeeRecord>()
            .OrderBy(p => p.LegalName)
            .Select(p => new
            {
                p.Id, p.LegalName, p.Jurisdiction,
                EntityType    = p.EntityType.ToString(),
                Status        = p.Status.ToString(),
                p.TaxIdentifier, p.RegistrationNumber,
                p.BankAccountNumber, p.Iban, p.SwiftBic, p.RoutingCode,
                p.OnboardedAt, p.KycRefreshedAt, p.CreatedAt, p.UpdatedAt,
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>Get a single payee record by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var payee = await _db.Set<PayeeRecord>().FindAsync([id], ct);
        if (payee is null) return NotFound(new { message = $"Payee {id} not found." });
        return Ok(payee);
    }

    /// <summary>Create a new payee record (status defaults to Active).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePayeeRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (!Enum.TryParse<EntityType>(req.EntityType, ignoreCase: true, out var entityType))
            return BadRequest(new { message = $"Invalid entity type '{req.EntityType}'. Valid values: {string.Join(", ", Enum.GetNames<EntityType>())}" });

        var payee = PayeeRecord.Create(
            id:                 Guid.NewGuid(),
            legalName:          req.LegalName.Trim(),
            jurisdiction:       req.Jurisdiction.Trim().ToUpperInvariant(),
            entityType:         entityType,
            taxIdentifier:      req.TaxIdentifier?.Trim(),
            registrationNumber: req.RegistrationNumber?.Trim(),
            bankAccountNumber:  req.BankAccountNumber?.Trim(),
            iban:               req.Iban?.Trim(),
            swiftBic:           req.SwiftBic?.Trim(),
            routingCode:        req.RoutingCode?.Trim(),
            now:                DateTimeOffset.UtcNow);

        _db.Set<PayeeRecord>().Add(payee);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = payee.Id },
            new { payee.Id, payee.LegalName });
    }
}

public sealed record CreatePayeeRequest(
    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(512)]
    string LegalName,

    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(8)]
    string Jurisdiction,

    /// <summary>Corporate | Individual | Government | Partnership</summary>
    [property: System.ComponentModel.DataAnnotations.Required]
    string EntityType,

    [property: System.ComponentModel.DataAnnotations.MaxLength(128)]
    string? TaxIdentifier,

    [property: System.ComponentModel.DataAnnotations.MaxLength(128)]
    string? RegistrationNumber,

    [property: System.ComponentModel.DataAnnotations.MaxLength(64)]
    string? BankAccountNumber,

    [property: System.ComponentModel.DataAnnotations.MaxLength(64)]
    string? Iban,

    [property: System.ComponentModel.DataAnnotations.MaxLength(16)]
    string? SwiftBic,

    [property: System.ComponentModel.DataAnnotations.MaxLength(64)]
    string? RoutingCode
);
