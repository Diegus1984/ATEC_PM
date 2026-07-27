// ── Pannello laterale «Risorse»: interruttori Gantt per riga ───────────────

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import type { LookupItem } from "@/lib/api/types"

export function PlannerSidePanel({
  collapsed,
  onToggleCollapsed,
  resources,
  ganttOn,
  onToggleGanttOff,
  ganttOnCount,
  poolCount,
  myEmployeeId,
  search,
  setSearch,
  onShowAll,
  onHideAll,
}: {
  collapsed: boolean
  onToggleCollapsed: () => void
  resources: LookupItem[]
  ganttOn: (id: number) => boolean
  onToggleGanttOff: (id: number) => void
  ganttOnCount: number
  poolCount: number
  myEmployeeId: number
  search: string
  setSearch: (value: string) => void
  onShowAll: () => void
  onHideAll: () => void
}) {
  return (
    <div className={`res-side no-print ${collapsed ? "collapsed" : ""}`}>
      <div className="res-side-head">
        <button
          type="button"
          className="res-side-toggle"
          title={collapsed ? "Mostra il pannello risorse" : "Nascondi il pannello risorse"}
          aria-label={collapsed ? "Mostra il pannello risorse" : "Nascondi il pannello risorse"}
          onClick={onToggleCollapsed}
        >
          <svg
            className="g2-burger"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={2}
            strokeLinecap="round"
            aria-hidden="true"
          >
            <line x1="4" y1="7" x2="20" y2="7" />
            <line x1="4" y1="12" x2="20" y2="12" />
            <line x1="4" y1="17" x2="20" y2="17" />
          </svg>
        </button>
        <div className="res-side-title">Risorse</div>
        <div className="res-side-counter">
          {ganttOnCount} / {poolCount} in vista
        </div>
        <input
          className="res-side-search"
          placeholder="Cerca nominativo…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </div>
      <div className="res-side-actions">
        <Button size="sm" variant="outline" onClick={onShowAll}>
          Tutte
        </Button>
        <Button size="sm" variant="outline" onClick={onHideAll}>
          Nessuna
        </Button>
      </div>
      <div className="res-side-list">
        {resources.map((r) => (
          <label
            key={r.id}
            className={`res-person ${r.id === myEmployeeId ? "sel" : ""}`}
          >
            <Checkbox
              checked={ganttOn(r.id)}
              onCheckedChange={() => onToggleGanttOff(r.id)}
            />
            <span className="res-person-name">{r.name}</span>
          </label>
        ))}
        {poolCount === 0 && (
          <div className="px-2.5 py-2 text-xs text-muted-foreground">
            Nessuna risorsa in lista. Usa «In lista» in alto per sceglierle.
          </div>
        )}
      </div>
    </div>
  )
}
