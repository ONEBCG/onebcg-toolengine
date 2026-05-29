namespace ToolEngine.Core.Domain.Enums;

public enum ExecutionMode  { Sequential, Parallel, Dag }
public enum ToolType       { Logic, Api, Database, Composite }
public enum ToolStatus     { Pending, Running, Succeeded, Failed, Suspended }
public enum ApprovalStatus { Pending, Approved, Denied, Expired }
public enum ApprovalChannel{ Dashboard, EmailMagicLink, EmailOtp, Webhook }
public enum ApprovalRisk   { Low, Medium, High, Critical }

// H4 — NIST AI Agent Identity & Authorization (Feb 2026)
public enum CallerType     { Human, AiAgent, SystemService }

public enum ScenarioStatus { Running, Suspended, Completed, Failed }
