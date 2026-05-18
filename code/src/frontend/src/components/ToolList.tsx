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
        const isSelected = selected?.name === tool.name
        const typeLabel = TOOL_TYPE_LABELS[tool.type] ?? 'Unknown'
        return (
          <button
            key={tool.name + tool.version}
            className={`tool-item ${isSelected ? 'tool-item--active' : ''}`}
            onClick={() => onSelect(tool)}
          >
            <span className="tool-item-name">{tool.name}</span>
            <span className="tool-item-meta">
              <span className={`tool-type tool-type--${typeLabel.toLowerCase()}`}>
                {typeLabel}
              </span>
              <span className="tool-version">{tool.version}</span>
            </span>
            <span className="tool-item-desc">{tool.description}</span>
          </button>
        )
      })}
    </aside>
  )
}
