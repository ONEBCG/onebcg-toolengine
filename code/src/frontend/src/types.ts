export const TOOL_TYPE_LABELS: Record<number, string> = {
  0: 'Logic',
  1: 'Api',
  2: 'Database',
  3: 'Composite',
}

export interface ToolMetadata {
  name: string
  version: string
  description: string
  type: number
  tenantId: string | null
  isEnabled: boolean
}

export interface ToolDescriptor {
  metadata: ToolMetadata
  tenantId: string | null
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

export const EXAMPLE_INPUTS: Record<string, string> = {
  calculator: JSON.stringify(
    { leftOperand: 10, rightOperand: 3, operator: 'add' },
    null, 2
  ),
  weather: JSON.stringify({ city: 'London' }, null, 2),
  'user-lookup': JSON.stringify(
    { userId: '11111111-0000-0000-0000-000000000001' },
    null, 2
  ),
  'weather-report': JSON.stringify({ city: 'Tokyo' }, null, 2),
}
