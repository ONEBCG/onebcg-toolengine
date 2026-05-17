import type { ToolResponse } from '../types'

interface Props {
  response: ToolResponse | null
}

function formatDuration(duration: string): string {
  // TimeSpan format: "00:00:00.0012345"
  const parts = duration.split('.')
  if (parts.length === 2) {
    const ms = Math.round(parseInt(parts[1].slice(0, 4), 10) / 10)
    return `${ms}ms`
  }
  return duration
}

export function ResponsePanel({ response }: Props) {
  if (!response) {
    return (
      <div className="response-panel response-panel--empty">
        <span>Response will appear here</span>
      </div>
    )
  }

  const { success, data, error, metrics, correlationId, timestamp } = response

  return (
    <div className="response-panel">
      <div className="response-header">
        <span className={`response-status ${success ? 'response-status--ok' : 'response-status--err'}`}>
          {success ? 'Success' : 'Error'}
        </span>
        <span className="response-meta">
          {formatDuration(metrics.duration)} &middot; {new Date(timestamp).toLocaleTimeString()}
        </span>
      </div>

      {success ? (
        <pre className="response-body response-body--ok">
          {JSON.stringify(data, null, 2)}
        </pre>
      ) : (
        <div className="response-error-block">
          <div className="response-error-code">{error?.code}</div>
          <div className="response-error-desc">{error?.description}</div>
          {error?.httpStatusCode && (
            <div className="response-error-status">HTTP {error.httpStatusCode}</div>
          )}
        </div>
      )}

      <div className="response-footer">
        <span>correlation: {correlationId}</span>
      </div>
    </div>
  )
}
