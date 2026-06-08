using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Route("api/v1/wht-rates")]
[Tags("Master Data")]
[Authorize]
public sealed class WhtRatesController : ControllerBase
{
    private readonly AppDbContext _db;

    public WhtRatesController(AppDbContext db) => _db = db;

    /// <summary>List all WHT rate entries ordered by country pair and service category.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await _db.Set<WhtRateEntry>()
            .OrderBy(w => w.PayerCountry)
            .ThenBy(w => w.PayeeCountry)
            .ThenBy(w => w.ServiceCategory)
            .Select(w => new
            {
                w.Id, w.PayerCountry, w.PayeeCountry,
                ServiceCategory      = w.ServiceCategory.ToString(),
                w.TreatyArticle, w.StandardRatePct, w.ReducedTreatyRatePct,
                w.ConditionsForReduced, w.TreatyExists, w.RuleVersion,
                w.CreatedAt, w.UpdatedAt,
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>Get a single WHT rate entry by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entry = await _db.Set<WhtRateEntry>().FindAsync([id], ct);
        if (entry is null) return NotFound(new { message = $"WHT rate entry {id} not found." });
        return Ok(entry);
    }

    /// <summary>
    /// Create a new WHT rate entry.
    /// ServiceCategory must match a ServiceType enum value:
    /// SoftwareLicense | CloudSaas | ManagementConsulting | InterestOnLoan | DividendDistribution | ContractStaffing | Other
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWhtRateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (!Enum.TryParse<ServiceType>(req.ServiceCategory, ignoreCase: true, out var serviceCategory))
            return BadRequest(new { message = $"Invalid service category '{req.ServiceCategory}'. Valid values: {string.Join(", ", Enum.GetNames<ServiceType>())}" });

        // Check for duplicate (same payer/payee/service combination)
        var payerUpper = req.PayerCountry.Trim().ToUpperInvariant();
        var payeeUpper = req.PayeeCountry.Trim().ToUpperInvariant();
        var exists = await _db.Set<WhtRateEntry>().AnyAsync(w =>
            w.PayerCountry    == payerUpper &&
            w.PayeeCountry    == payeeUpper &&
            w.ServiceCategory == serviceCategory, ct);

        if (exists)
            return Conflict(new { message = $"A WHT rate for {payerUpper}/{payeeUpper}/{serviceCategory} already exists." });

        var entry = WhtRateEntry.Create(
            payerCountry:         payerUpper,
            payeeCountry:         payeeUpper,
            serviceCategory:      serviceCategory,
            treatyArticle:        req.TreatyArticle?.Trim(),
            standardRatePct:      req.StandardRatePct,
            reducedTreatyRatePct: req.ReducedTreatyRatePct,
            conditionsForReduced: req.ConditionsForReduced?.Trim(),
            treatyExists:         req.TreatyExists,
            ruleVersion:          req.RuleVersion.Trim(),
            now:                  DateTimeOffset.UtcNow);

        _db.Set<WhtRateEntry>().Add(entry);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = entry.Id },
            new { entry.Id, entry.PayerCountry, entry.PayeeCountry, ServiceCategory = entry.ServiceCategory.ToString() });
    }
}

public sealed record CreateWhtRateRequest(
    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(4)]
    string PayerCountry,

    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(4)]
    string PayeeCountry,

    /// <summary>
    /// ServiceType enum: SoftwareLicense | CloudSaas | ManagementConsulting |
    /// InterestOnLoan | DividendDistribution | ContractStaffing | Other
    /// </summary>
    [property: System.ComponentModel.DataAnnotations.Required]
    string ServiceCategory,

    [property: System.ComponentModel.DataAnnotations.MaxLength(256)]
    string? TreatyArticle,

    [property: System.ComponentModel.DataAnnotations.Range(0, 100)]
    decimal StandardRatePct,

    [property: System.ComponentModel.DataAnnotations.Range(0, 100)]
    decimal ReducedTreatyRatePct,

    [property: System.ComponentModel.DataAnnotations.MaxLength(1024)]
    string? ConditionsForReduced,

    bool TreatyExists,

    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(32)]
    string RuleVersion
);
