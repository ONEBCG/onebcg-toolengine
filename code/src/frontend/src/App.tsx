import { useEffect, useState } from 'react'
import type { ToolDescriptor, ToolResponse } from './types'
import { fetchDevToken, fetchTools, fetchHealth } from './api'
import { ToolList } from './components/ToolList'
import { ToolInvoker } from './components/ToolInvoker'
import { ResponsePanel } from './components/ResponsePanel'

type AppState = 'loading' | 'ready' | 'error'

export default function App() {
  const [state, setState] = useState<AppState>('loading')
  const [errorMsg, setErrorMsg] = useState('')
  const [tools, setTools] = useState<ToolDescriptor[]>([])
  const [selected, setSelected] = useState<ToolDescriptor | null>(null)
  const [response, setResponse] = useState<ToolResponse | null>(null)
  const [healthy, setHealthy] = useState<boolean | null>(null)

  useEffect(() => {
    async function init() {
      try {
        await fetchDevToken()
        const [toolList, health] = await Promise.all([fetchTools(), fetchHealth()])
        setTools(toolList)
        setSelected(toolList[0] ?? null)
        setHealthy(health)
        setState('ready')
      } catch (e) {
        setErrorMsg(e instanceof Error ? e.message : 'Unknown error')
        setState('error')
      }
    }
    init()
  }, [])

  function handleSelectTool(tool: ToolDescriptor) {
    setSelected(tool)
    setResponse(null)
  }

  if (state === 'loading') {
    return (
      <div className="app-loading">
        <div className="spinner" />
        <span>Connecting to ToolEngine API…</span>
      </div>
    )
  }

  if (state === 'error') {
    return (
      <div className="app-error">
        <h2>Cannot reach API</h2>
        <p>{errorMsg}</p>
        <p className="app-error-hint">
          Run: <code>dotnet run --project src/Hosts/ToolEngine.Api</code>
        </p>
      </div>
    )
  }

  return (
    <div className="app">
      <header className="app-header">
        <div className="app-header-left">
          <span className="app-brand">ONE BCG</span>
          <span className="app-title">ToolEngine</span>
        </div>
        <div className="app-header-right">
          <span className={`health-badge ${healthy ? 'health-badge--ok' : 'health-badge--err'}`}>
            {healthy ? '● API healthy' : '● API unhealthy'}
          </span>
          <span className="header-tenant">tenant: acme-corp</span>
        </div>
      </header>

      <div className="app-body">
        <ToolList
          tools={tools}
          selected={selected}
          onSelect={handleSelectTool}
        />

        <main className="app-main">
          {selected ? (
            <>
              <ToolInvoker
                tool={selected}
                onResponse={setResponse}
              />
              <ResponsePanel response={response} />
            </>
          ) : (
            <div className="app-empty">Select a tool from the sidebar.</div>
          )}
        </main>
      </div>
    </div>
  )
}
