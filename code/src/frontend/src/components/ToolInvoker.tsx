import { useState, useEffect } from 'react'
import type { ToolDescriptor, ToolResponse } from '../types'
import { EXAMPLE_INPUTS } from '../types'
import { invokeTool } from '../api'

interface Props {
  tool: ToolDescriptor
  onResponse: (response: ToolResponse) => void
}

export function ToolInvoker({ tool, onResponse }: Props) {
  const [input, setInput] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const example = EXAMPLE_INPUTS[tool.metadata.name]
    setInput(example ?? '{}')
    setError(null)
  }, [tool])

  async function handleInvoke() {
    let parsed: unknown
    try {
      parsed = JSON.parse(input)
    } catch {
      setError('Invalid JSON — check your input.')
      return
    }

    setError(null)
    setLoading(true)
    try {
      const response = await invokeTool(
        tool.metadata.namespace,
        tool.metadata.name,
        tool.metadata.version,
        parsed,
      )
      onResponse(response)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Request failed')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="invoker">
      <div className="invoker-header">
        <div>
          <h2 className="invoker-title">{tool.metadata.name}</h2>
          <p className="invoker-desc">{tool.metadata.description}</p>
        </div>
        <span className="invoker-version">{tool.metadata.version}</span>
      </div>

      <label className="invoker-label">JSON Input</label>
      <textarea
        className="invoker-textarea"
        value={input}
        onChange={(e) => setInput(e.target.value)}
        spellCheck={false}
        rows={10}
      />

      {error && <div className="invoker-error">{error}</div>}

      <button
        className="invoke-btn"
        onClick={handleInvoke}
        disabled={loading}
      >
        {loading ? 'Invoking…' : 'Invoke'}
      </button>
    </div>
  )
}
