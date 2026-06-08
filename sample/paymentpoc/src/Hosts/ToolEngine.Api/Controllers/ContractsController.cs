using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Route("api/v1/contracts")]
[Tags("Master Data")]
[Authorize]
public sealed class ContractsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ContractsController(AppDbContext db) => _db = db;

    /// <summary>List all PPM contracts ordered by PPM ID.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await _db.Set<PpmContract>()
            .OrderBy(c => c.PpmId)
            .Select(c => new
            {
                c.Id, c.PpmId, c.PayerEntityId, c.PayeeId,
                c.PermittedServiceTypes, c.ApprovedCurrencies,
                c.MaxSingleTransaction, c.AggregateCapAmount, c.CumulativePaid,
                c.EffectiveFrom, c.EffectiveTo, c.IsActive,
                c.ContractVersion, c.ContractDocumentPath,
                c.CreatedAt, c.UpdatedAt,
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>Get a single PPM contract by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var contract = await _db.Set<PpmContract>().FindAsync([id], ct);
        if (contract is null) return NotFound(new { message = $"Contract {id} not found." });
        return Ok(contract);
    }

    /// <summary>Create a new PPM contract (IsActive defaults to true).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateContractRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (req.EffectiveTo <= req.EffectiveFrom)
            return BadRequest(new { message = "EffectiveTo must be after EffectiveFrom." });

        var from = new DateTimeOffset(req.EffectiveFrom, TimeOnly.MinValue, TimeSpan.Zero);
        var to   = new DateTimeOffset(req.EffectiveTo,   TimeOnly.MinValue, TimeSpan.Zero);
        var now  = DateTimeOffset.UtcNow;

        var contract = PpmContract.Create(
            ppmId:                 req.PpmId.Trim(),
            payerEntityId:         req.PayerEntityId.Trim(),
            payeeId:               req.PayeeId,
            permittedServiceTypes: req.PermittedServiceTypes.Trim(),
            approvedCurrencies:    req.ApprovedCurrencies.Trim().ToUpperInvariant(),
            maxSingleTransaction:  req.MaxSingleTransaction,
            aggregateCapAmount:    req.AggregateCapAmount,
            effectiveFrom:         from,
            effectiveTo:           to,
            contractVersion:       req.ContractVersion.Trim(),
            documentPath:          req.ContractDocumentPath?.Trim(),
            now:                   now);

        _db.Set<PpmContract>().Add(contract);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = contract.Id },
            new { contract.Id, contract.PpmId });
    }
}

public sealed record CreateContractRequest(
    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(128)]
    string PpmId,

    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(256)]
    string PayerEntityId,

    [property: System.ComponentModel.DataAnnotations.Required]
    Guid PayeeId,

    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(1024)]
    string PermittedServiceTypes,

    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(128)]
    string ApprovedCurrencies,

    [property: System.ComponentModel.DataAnnotations.Range(0.01, double.MaxValue)]
    decimal MaxSingleTransaction,

    [property: System.ComponentModel.DataAnnotations.Range(0.01, double.MaxValue)]
    decimal AggregateCapAmount,

    [property: System.ComponentModel.DataAnnotations.Required]
    DateOnly EffectiveFrom,

    [property: System.ComponentModel.DataAnnotations.Required]
    DateOnly EffectiveTo,

    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(64)]
    string ContractVersion,

    [property: System.ComponentModel.DataAnnotations.MaxLength(2048)]
    string? ContractDocumentPath
);
