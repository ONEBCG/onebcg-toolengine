import type { ToolDescriptor } from '../types'
import { TOOL_TYPE_LABELS } from '../types'

interface Props {
  tools: ToolDescriptor[]
  selected: ToolDescriptor | null
  onSelect: (tool: ToolDescriptor) => void
}

export function ToolList({ tools, selected, onSelect }: Props) {
  return (
    <aside className="tool-list">
      <div className="tool-list-header">Registered Tools</div>
      {tools.length === 0 && (
        <div className="tool-list-empty">No tools registered</div>
      )}
      {tools.map((tool) => {
        const isSelected = selected?.metadata.name === tool.metadata.name
        const typeLabel = TOOL_TYPE_LABELS[tool.metadata.type] ?? 'Unknown'
        return (
          <button
            key={tool.metadata.name + tool.metadata.version}
            className={`tool-item ${isSelected ? 'tool-item--active' : ''}`}
            onClick={() => onSelect(tool)}
          >
            <span className="tool-item-name">{tool.metadata.name}</span>
            <span className="tool-item-meta">
              <span className={`tool-type tool-type--${typeLabel.toLowerCase()}`}>
                {typeLabel}
              </span>
              <span className="tool-version">{tool.metadata.version}</span>
            </span>
            <span className="tool-item-desc">{tool.metadata.description}</span>
          </button>
        )
      })}
    </aside>
  )
}
