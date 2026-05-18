import { useState } from 'react'
import type { AgentChatResponse } from '../types'
import { agentChat } from '../api'

interface Message {
  role: 'user' | 'agent'
  text: string
  toolInvoked?: string | null
  toolResult?: unknown
  usage?: AgentChatResponse['usage']
}

export function AgentChat() {
  const [input, setInput] = useState('')
  const [messages, setMessages] = useState<Message[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sessionId, setSessionId] = useState<string | undefined>(undefined)

  async function handleSend() {
    const text = input.trim()
    if (!text) return

    setInput('')
    setError(null)
    setMessages(prev => [...prev, { role: 'user', text }])
    setLoading(true)

    try {
      const res = await agentChat(text, sessionId)
      setSessionId(res.sessionId)
      setMessages(prev => [
        ...prev,
        {
          role: 'agent',
          text: res.reply,
          toolInvoked: res.toolInvoked,
          toolResult: res.toolResult,
          usage: res.usage,
        },
      ])
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Agent request failed')
    } finally {
      setLoading(false)
    }
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
      handleSend()
    }
  }

  function handleReset() {
    setMessages([])
    setSessionId(undefined)
    setError(null)
  }

  return (
    <div className="agent-chat">
      <div className="agent-chat-header">
        <div>
          <h2 className="agent-chat-title">Agent Chat</h2>
          <p className="agent-chat-desc">
            Type a natural-language request. The LLM selects and invokes the right tool automatically.
          </p>
        </div>
        {messages.length > 0 && (
          <button className="agent-reset-btn" onClick={handleReset}>
            New session
          </button>
        )}
      </div>

      <div className="agent-messages">
        {messages.length === 0 && (
          <div className="agent-empty">
            <div className="agent-empty-examples">
              <p className="agent-empty-label">Try asking:</p>
              <button className="agent-example-chip" onClick={() => setInput('What is the weather in Tokyo right now?')}>
                What is the weather in Tokyo right now?
              </button>
              <button className="agent-example-chip" onClick={() => setInput('Calculate 144 divided by 12')}>
                Calculate 144 divided by 12
              </button>
              <button className="agent-example-chip" onClick={() => setInput('Convert 25 degrees Celsius to Fahrenheit')}>
                Convert 25°C to Fahrenheit
              </button>
            </div>
          </div>
        )}

        {messages.map((msg, i) => (
          <div key={i} className={`agent-message agent-message--${msg.role}`}>
            <div className="agent-message-role">{msg.role === 'user' ? 'You' : 'Agent'}</div>
            <div className="agent-message-text">{msg.text}</div>

            {msg.role === 'agent' && msg.toolInvoked && (
              <div className="agent-tool-badge">
                Tool invoked: <span>{msg.toolInvoked}</span>
              </div>
            )}

            {msg.role === 'agent' && msg.toolResult !== null && msg.toolResult !== undefined && (
              <details className="agent-tool-result">
                <summary>Raw tool result</summary>
                <pre>{JSON.stringify(msg.toolResult, null, 2)}</pre>
              </details>
            )}

            {msg.role === 'agent' && msg.usage && (
              <div className="agent-usage">
                {msg.usage.inputTokens} in · {msg.usage.outputTokens} out · ~${msg.usage.estimatedCostUsd.toFixed(4)}
              </div>
            )}
          </div>
        ))}

        {loading && (
          <div className="agent-message agent-message--agent agent-message--loading">
            <div className="agent-message-role">Agent</div>
            <div className="agent-thinking">
              <span /><span /><span />
            </div>
          </div>
        )}
      </div>

      {error && <div className="agent-error">{error}</div>}

      <div className="agent-input-row">
        <textarea
          className="agent-textarea"
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Ask anything — e.g. 'What's the weather in Paris?' or 'Add 128 and 256'"
          rows={3}
          disabled={loading}
        />
        <button
          className="agent-send-btn"
          onClick={handleSend}
          disabled={loading || !input.trim()}
        >
          {loading ? '…' : 'Send'}
        </button>
      </div>
      <div className="agent-input-hint">Ctrl+Enter to send</div>

      {sessionId && (
        <div className="agent-session-id">session: {sessionId}</div>
      )}
    </div>
  )
}
