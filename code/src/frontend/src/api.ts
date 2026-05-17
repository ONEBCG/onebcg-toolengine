import type { ToolDescriptor, ToolResponse } from './types'

const BASE = 'http://localhost:5174'

let _token: string | null = null

export async function fetchDevToken(): Promise<void> {
  const res = await fetch(`${BASE}/dev/token`)
  if (!res.ok) throw new Error('Could not reach API. Is ToolEngine.Api running?')
  const data = (await res.json()) as { token: string }
  _token = data.token
}

export async function fetchTools(): Promise<ToolDescriptor[]> {
  const res = await fetch(`${BASE}/tools`)
  if (!res.ok) throw new Error('Failed to fetch tools')
  return res.json()
}

export async function fetchHealth(): Promise<boolean> {
  try {
    const res = await fetch(`${BASE}/health`)
    return res.ok
  } catch {
    return false
  }
}

export async function invokeTool(
  name: string,
  version: string,
  input: unknown,
): Promise<ToolResponse> {
  const res = await fetch(`${BASE}/tools/${name}/${version}/invoke`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${_token}`,
    },
    body: JSON.stringify(input),
  })
  return res.json()
}
