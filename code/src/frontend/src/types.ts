export const TOOL_TYPE_LABELS: Record<number, string> = {
  0: 'Logic',
  1: 'Api',
  2: 'Database',
  3: 'Composite',
}

// Matches the flat ToolSummaryResponse shape returned by GET /tools
export interface ToolDescriptor {
  fullName: string
  namespace: string
  name: string
  version: string
  description: string
  type: number
  isEnabled: boolean
  tenantId: string | null
  inputSchema: Record<string, unknown>
  outputSchema: Record<string, unknown>
}

export interface ToolError {
  code: string
  description: string
  httpStatusCode: number
}

export interface ToolMetrics {
  duration: string
  tokensIn: number
  tokensOut: number
  retryCount: number
}

export interface ToolResponse {
  correlationId: string
  success: boolean
  data: unknown
  error: ToolError | null
  metrics: ToolMetrics
  timestamp: string
}

export interface AgentUsage {
  inputTokens: number
  outputTokens: number
  totalTokens: number
  estimatedCostUsd: number
}

export interface AgentChatResponse {
  reply: string
  toolInvoked: string | null
  toolResult: unknown
  sessionId: string
  usage: AgentUsage
}

// Keys match tool name (e.g. "calculate", "current")
export const EXAMPLE_INPUTS: Record<string, string> = {
  calculate: JSON.stringify(
    { leftOperand: 10, rightOperand: 3, operator: 'add' },
    null, 2
  ),
  current: JSON.stringify({ city: 'London' }, null, 2),
  'user-lookup': JSON.stringify(
    { userId: '11111111-0000-0000-0000-000000000001' },
    null, 2
  ),
  report: JSON.stringify({ city: 'Tokyo' }, null, 2),
}
