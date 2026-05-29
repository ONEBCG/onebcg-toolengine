using MediatR;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;

namespace ToolEngine.Payment.Application.Queries;

// ── GetPaymentStatus ──────────────────────────────────────────────────────────

public sealed record GetPaymentStatusQuery(Guid Prid)
    : IRequest<PaymentStatusDto?>;

public sealed record PaymentStatusDto(
    Guid    Prid,
    string  Status,
    int     CurrentStage,
    string  PayerName,
    string  PayeeRef,
    decimal GrossAmount,
    string  Currency,
    decimal? WhtAmount,
    decimal? NetPayableAmount,
    string?  KycResult,
    string?  ApprovalTier,
    Guid?    PendingApprovalId,
    string?  BankTransactionId,
    string?  BlockReason,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? SettledAt);

public sealed class GetPaymentStatusQueryHandler
    : IRequestHandler<GetPaymentStatusQuery, PaymentStatusDto?>
{
    private readonly AppDbContext _db;

    public GetPaymentStatusQueryHandler(AppDbContext db) => _db = db;

    public async Task<PaymentStatusDto?> Handle(
        GetPaymentStatusQuery query, CancellationToken ct)
    {
        var p = await _db.Set<PaymentInstruction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Prid, ct);

        if (p is null) return null;

        return new PaymentStatusDto(
            Prid:              p.Id,
            Status:            p.Status.ToString(),
            CurrentStage:      p.CurrentStage,
            PayerName:         p.PayerName,
            PayeeRef:          p.PayeeRef,
            GrossAmount:       p.GrossAmount,
            Currency:          p.Currency,
            WhtAmount:         p.WhtAmount,
            NetPayableAmount:  p.NetPayableAmount,
            KycResult:         p.KycResult?.ToString(),
            ApprovalTier:      p.ApprovalTier,
            PendingApprovalId: p.PendingApprovalId,
            BankTransactionId: p.BankTransactionId,
            BlockReason:       p.BlockReason,
            InitiatedAt:       p.InitiatedAt,
            SettledAt:         p.SettledAt);
    }
}

// ── GetPaymentAuditTrail ──────────────────────────────────────────────────────

public sealed record GetPaymentAuditTrailQuery(Guid Prid)
    : IRequest<IReadOnlyList<PaymentAuditLogDto>>;

public sealed record PaymentAuditLogDto(
    int     Stage,
    string  StageName,
    string  Outcome,
    string? Details,
    string? ActorId,
    DateTimeOffset EnteredAt);

public sealed class GetPaymentAuditTrailQueryHandler
    : IRequestHandler<GetPaymentAuditTrailQuery, IReadOnlyList<PaymentAuditLogDto>>
{
    private readonly AppDbContext _db;

    public GetPaymentAuditTrailQueryHandler(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PaymentAuditLogDto>> Handle(
        GetPaymentAuditTrailQuery query, CancellationToken ct)
    {
        var exists = await _db.Set<PaymentInstruction>()
            .AsNoTracking()
            .AnyAsync(p => p.Id == query.Prid, ct);

        if (!exists) return [];

        return await _db.Set<PaymentAuditLog>()
            .AsNoTracking()
            .Where(l => l.PaymentId == query.Prid)
            .OrderBy(l => l.Stage)
            .Select(l => new PaymentAuditLogDto(
                l.Stage, l.StageName, l.Outcome,
                l.Details, l.ActorId, l.EnteredAt))
            .ToListAsync(ct);
    }
}
