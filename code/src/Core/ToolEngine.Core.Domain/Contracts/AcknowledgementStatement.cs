namespace ToolEngine.Core.Domain.Contracts;

using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Structured acknowledgement payload required by EU AI Act Article 14 for High and
/// Critical risk tools. Serialised as JSON and stored on PendingApproval.AcknowledgementJson.
///
/// Article 14 mandates that human operators exercise meaningful oversight over high-risk
/// AI systems. This record documents that an operator was informed of the risk tier,
/// the specific tool action, and the regulatory basis before granting approval.
///
/// The JSON representation is verifiable during a compliance audit and is not subject
/// to anonymisation (GDPR Recital 26 — personal data needed for legal accountability).
/// </summary>
/// <param name="RegulatoryBasis">Regulatory or policy basis. E.g. "EU AI Act Article 14 §4".</param>
/// <param name="RiskLevel">Risk classification applied at the time of the approval request.</param>
/// <param name="ToolFullName">Full name of the tool requiring oversight. E.g. "payments.charge-card".</param>
/// <param name="OperatorStatement">Human-readable statement the approver implicitly accepts. Stored verbatim.</param>
/// <param name="IssuedAt">UTC timestamp when this acknowledgement was generated.</param>
public sealed record AcknowledgementStatement(
    string         RegulatoryBasis,
    ApprovalRisk   RiskLevel,
    string         ToolFullName,
    string         OperatorStatement,
    DateTimeOffset IssuedAt);
