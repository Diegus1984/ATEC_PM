// ── Toolbar e barra filtri del planner risorse (presentazionali) ───────────

import * as React from "react"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import type { LookupItem, ResTipo } from "@/lib/api/types"

import { ALL_TIPI, TIPO_LABELS } from "./planner-geometry"
import { surname } from "./planner-logic"
import type { PlannerUiSettings } from "./use-planner-settings"

type PatchSettings = (patch: Partial<PlannerUiSettings>) => void

export function PlannerToolbar({
  canEdit,
  settings,
  patch,
  periodLabel,
  conflictCount,
  pendingNotify,
  printing,
  onCreate,
  onOpenFerie,
  onNotify,
  onScrollBack,
  onToday,
  onScrollForward,
  onToggleTipo,
  onPrint,
}: {
  canEdit: boolean
  settings: PlannerUiSettings
  patch: PatchSettings
  periodLabel: string
  conflictCount: number
  pendingNotify: number
  printing: boolean
  onCreate: () => void
  onOpenFerie: () => void
  onNotify: () => void
  onScrollBack: () => void
  onToday: () => void
  onScrollForward: () => void
  onToggleTipo: (t: ResTipo) => void
  onPrint: () => void
}) {
  return (
    <div className="no-print flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-card px-3 py-2">
      <div className="flex flex-wrap items-center gap-2">
        {canEdit && (
          <Button size="sm" onClick={onCreate}>
            + Allocazione
          </Button>
        )}
        <Button size="sm" variant="outline" onClick={onOpenFerie}>
          Piano ferie
        </Button>
        {canEdit && pendingNotify > 0 && (
          <button
            type="button"
            className="tb-notify-info no-print"
            title="Clic per scegliere quali modifiche notificare subito"
            onClick={onNotify}
          >
            ⏳ {pendingNotify} {pendingNotify === 1 ? "modifica" : "modifiche"} da notificare
          </button>
        )}
        <div className="flex items-center gap-1">
          <Button size="sm" variant="outline" onClick={onScrollBack}>
            ◀
          </Button>
          <Button size="sm" variant="outline" onClick={onToday}>
            Oggi
          </Button>
          <Button size="sm" variant="outline" onClick={onScrollForward}>
            ▶
          </Button>
        </div>
        <span className="px-1 text-sm font-semibold">{periodLabel}</span>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {conflictCount > 0 && (
          <span className="rounded-md border border-red-200 bg-red-50 px-2 py-1 text-xs font-semibold text-red-600">
            {conflictCount} conflitti
          </span>
        )}
        {/* Legenda / toggle tipo */}
        <div className="legend">
          {ALL_TIPI.map((t) => (
            <span
              key={t}
              className={`legend-chip ${
                settings.tipiVisibili.includes(t) ? "active" : ""
              }`}
              onClick={() => onToggleTipo(t)}
            >
              <span
                className="dot"
                style={{
                  background:
                    t === "OP"
                      ? "#3B82F6"
                      : t === "FLEX"
                        ? "#9CA3AF"
                        : "#EF4444",
                }}
              />
              {TIPO_LABELS[t]}
            </span>
          ))}
        </div>
        {/* Zoom */}
        <Select
          value={String(settings.windowDays)}
          onValueChange={(v) => patch({ windowDays: Number(v) })}
        >
          <SelectTrigger className="h-8 w-[130px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="60">Compatto</SelectItem>
            <SelectItem value="30">Normale</SelectItem>
            <SelectItem value="14">Largo</SelectItem>
          </SelectContent>
        </Select>
        <Button
          size="sm"
          variant="outline"
          title="Stampa il periodo visibile del Gantt"
          disabled={printing}
          onClick={onPrint}
        >
          🖨 Stampa
        </Button>
        <Popover>
          <PopoverTrigger asChild>
            <Button size="sm" variant="outline" className="btn-help" title="Scorciatoie">
              ?
            </Button>
          </PopoverTrigger>
          <PopoverContent className="help-popup w-96" align="end">
            <strong>Scorciatoie</strong>
            <div className="help-section">CALENDARIO</div>
            <div>Scorri orizzontalmente — avanti/indietro nel tempo</div>
            <div>Shift + rotella — scorrimento orizzontale</div>
            <div className="help-section">GANTT · MOUSE</div>
            <div>Trascina barra — sposta le date (Shift = snap settimana)</div>
            <div>Bordi barra — ridimensiona inizio/fine</div>
            <div>Trascina su riga vuota — nuova allocazione (causale selezionata)</div>
            <div>Click — seleziona · Doppio click — modifica · Tasto destro — menu</div>
            <div className="help-section">TASTIERA</div>
            <div>Ctrl + F — cerca · Canc — elimina selezione · Esc — annulla</div>
          </PopoverContent>
        </Popover>
      </div>
    </div>
  )
}

export function PlannerFilters({
  settings,
  patch,
  myEmployeeId,
  resources,
  resourceFilterLabel,
  poolPopoverOpen,
  setPoolPopoverOpen,
  onToggleResourcePick,
  onSetAllResourcePicks,
  resSearch,
  setResSearch,
  searchInputRef,
}: {
  settings: PlannerUiSettings
  patch: PatchSettings
  myEmployeeId: number
  resources: LookupItem[]
  resourceFilterLabel: string
  poolPopoverOpen: boolean
  setPoolPopoverOpen: (open: boolean) => void
  onToggleResourcePick: (id: number) => void
  onSetAllResourcePicks: (on: boolean) => void
  resSearch: string
  setResSearch: (value: string) => void
  searchInputRef: React.RefObject<HTMLInputElement | null>
}) {
  return (
    <div className="no-print mt-2 flex flex-wrap items-center gap-3 rounded-lg border bg-card px-3 py-2 text-sm">
      {myEmployeeId > 0 && (
        <label className="flex cursor-pointer items-center gap-2 text-sm">
          <Switch
            size="sm"
            checked={settings.mineOnly}
            onCheckedChange={(checked) => patch({ mineOnly: checked })}
          />
          Solo mie attività
        </label>
      )}
      <label className="flex cursor-pointer items-center gap-2 text-sm">
        <Switch
          size="sm"
          checked={settings.conflictsOnly}
          onCheckedChange={(checked) => patch({ conflictsOnly: checked })}
        />
        Solo conflitti
      </label>
      <label className="flex cursor-pointer items-center gap-2 text-sm">
        <Switch
          size="sm"
          checked={settings.occupiedOnly}
          onCheckedChange={(checked) => patch({ occupiedOnly: checked })}
        />
        Solo occupate
      </label>

      <span className="filter-lbl">In lista:</span>
      <Popover open={poolPopoverOpen} onOpenChange={setPoolPopoverOpen}>
        <PopoverTrigger asChild>
          <Button size="sm" variant="outline">
            {resourceFilterLabel} ▾
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-80" align="start">
          <div className="mb-2 flex gap-2">
            <Button
              size="sm"
              variant="outline"
              onClick={() => onSetAllResourcePicks(true)}
            >
              Tutte
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => onSetAllResourcePicks(false)}
            >
              Nessuna
            </Button>
          </div>
          <div className="grid max-h-72 grid-cols-2 gap-1 overflow-y-auto">
            {[...resources]
              .sort(
                (a, b) =>
                  surname(a.name).localeCompare(surname(b.name)) ||
                  a.name.localeCompare(b.name)
              )
              .map((r) => (
                <label
                  key={r.id}
                  className="flex cursor-pointer items-center gap-1.5 text-xs"
                >
                  <Checkbox
                    checked={
                      !settings.resourceFilterActive ||
                      settings.selectedResourceIds.includes(r.id)
                    }
                    onCheckedChange={() => onToggleResourcePick(r.id)}
                  />
                  <span className="truncate">{r.name}</span>
                </label>
              ))}
          </div>
        </PopoverContent>
      </Popover>

      <input
        ref={searchInputRef}
        className="ml-auto h-8 w-[230px] rounded-md border px-2 text-sm"
        placeholder="Cerca risorsa o attività…"
        value={resSearch}
        onChange={(e) => setResSearch(e.target.value)}
      />
    </div>
  )
}
