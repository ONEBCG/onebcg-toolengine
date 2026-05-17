namespace ToolEngine.Core.Domain.Entities;

using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Durable state for a suspended tool invocation awaiting human approval.
/// Created by AsyncApprovalGate when a tool tagged [RequiresApproval] is invoked
/// and the risk tier is Medium or above.
///
/// Lifecycle:  Pending → Approved (→ tool executes, Result stored)
///             Pending → Denied
///             Pending → Expired (background sweep or on next poll)
/// </summary>
public sealed class PendingApproval : AggregateRoot<Guid>
{
    private PendingApproval() { } // EF Core

    private PendingApproval(
        Guid                correlationId,
        string              tenantId,
        string              userId,
        string              toolNamespace,
        string              toolName,
        string              toolVersion,
        string              serializedInput,
        ApprovalChannelType channel,
        ApprovalRisk        risk,
        string              approvalReason,
        string?             approverEmail,
        DateTimeOffset      expiresAt)
        : base(Guid.NewGuid())
    {
        CorrelationId   = correlationId;
        TenantId        = tenantId;
        UserId          = userId;
        ToolNamespace   = toolNamespace;
        ToolName        = toolName;
        ToolVersion     = toolVersion;
        SerializedInput = serializedInput;
        Channel         = channel;
        Risk            = risk;
        ApprovalReason  = approvalReason;
        ApproverEmail   = approverEmail;
        Status          = ApprovalStatus.Pending;
        ApprovalToken   = Guid.NewGuid().ToString("N"); // URL-safe opaque token
        ExpiresAt       = expiresAt;
        CreatedAt       = DateTimeOffset.UtcNow;
    }

    public Guid                CorrelationId   { get; private set; }
    public string              TenantId        { get; private set; } = default!;
    public string              UserId          { get; private set; } = default!;
    public string              ToolNamespace   { get; private set; } = default!;
    public string              ToolName        { get; private set; } = default!;
    public string              ToolVersion     { get; private set; } = default!;
    public string              ToolFullName    => $"{ToolNamespace}.{ToolName}";
    public string              SerializedInput { get; private set; } = default!;
    public ApprovalChannelType Channel         { get; private set; }
    public ApprovalRisk        Risk            { get; private set; }
    public string              ApprovalReason  { get; private set; } = default!;
    public string?             ApproverEmail   { get; private set; }
    public ApprovalStatus      Status          { get; private set; }
    // Opaque token included in magic-link URLs and webhook payloads.
    public string              ApprovalToken   { get; private set; } = default!;
    // PBKDF2 hash of the OTP — only set for EmailOtp channel.
    public string?             OtpHash         { get; private set; }
    public DateTimeOffset      ExpiresAt       { get; private set; }
    public DateTimeOffset      CreatedAt       { get; private set; }
    public DateTimeOffset?     DecidedAt       { get; private set; }
    public string?             DecidedByUserId { get; private set; }
    // JSON-serialized ToolResponse<JsonElement> written after approved execution.
    public string?             SerializedResult { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt && Status == ApprovalStatus.Pending;

    public static PendingApproval Create(
        Guid                correlationId,
        string              tenantId,
        string              userId,
        string              toolNamespace,
        string              toolName,
        string              toolVersion,
        string              serializedInput,
        ApprovalChannelType channel,
        ApprovalRisk        risk,
        string              approvalReason,
        string?             approverEmail,
        int                 timeoutMinutes = 60) =>
        new(correlationId, tenantId, userId, toolNamespace, toolName, toolVersion,
            serializedInput, channel, risk, approvalReason, approverEmail,
            DateTimeOffset.UtcNow.AddMinutes(timeoutMinutes));

    public Result Approve(string decidedByUserId)
    {
        if (Status != ApprovalStatus.Pending)
            return Result.Failure(Error.Conflict($"Approval {Id} is not in Pending status."));
        if (IsExpired)
        {
            Status = ApprovalStatus.Expired;
            return Result.Failure(Error.Conflict($"Approval {Id} has expired."));
        }
        Status          = ApprovalStatus.Approved;
        DecidedAt       = DateTimeOffset.UtcNow;
        DecidedByUserId = decidedByUserId;
        return Result.Success();
    }

    public Result Deny(string decidedByUserId, string? reason = null)
    {
        if (Status != ApprovalStatus.Pending)
            return Result.Failure(Error.Conflict($"Approval {Id} is not in Pending status."));
        Status          = ApprovalStatus.Denied;
        DecidedAt       = DateTimeOffset.UtcNow;
        DecidedByUserId = decidedByUserId;
        return Result.Success();
    }

    public void Expire()
    {
        if (Status == ApprovalStatus.Pending)
            Status = ApprovalStatus.Expired;
    }

    public void SetOtpHash(string otpHash) => OtpHash = otpHash;

    public void SetResult(string serializedResult) => SerializedResult = serializedResult;
}
