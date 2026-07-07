import * as React from "react"

// Persistenza delle preferenze UI del planner in localStorage (port di
// PlannerUiSettings/PlannerSettingsService del Blazor). Best-effort: gli errori di
// storage vengono ignorati, la pagina resta usabile.

const STORAGE_KEY = "atec.pm.planner.ui"

export type NameColMode = "full" | "badge" | "surname"

export interface PlannerUiSettings {
  /** Giorni visibili (zoom): 14 (largo) / 30 (normale) / 60 (compatto). */
  windowDays: number
  search: string
  mineOnly: boolean
  conflictsOnly: boolean
  occupiedOnly: boolean
  /** Tipi visibili, sottoinsieme di OP/FLEX/FERIE; vuoto = tutti. */
  tipiVisibili: string[]
  /** Filtro "In lista" attivo (pool ristretto di risorse). */
  resourceFilterActive: boolean
  selectedResourceIds: number[]
  /** Risorse spente nel pannello laterale (interruttori Gantt). */
  ganttOffIds: number[]
  nameColMode: NameColMode
  resSideCollapsed: boolean
}

export const DEFAULT_SETTINGS: PlannerUiSettings = {
  windowDays: 30,
  search: "",
  mineOnly: false,
  conflictsOnly: false,
  occupiedOnly: false,
  tipiVisibili: ["OP", "FLEX", "FERIE"],
  resourceFilterActive: false,
  selectedResourceIds: [],
  ganttOffIds: [],
  nameColMode: "full",
  resSideCollapsed: false,
}

function load(): PlannerUiSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return { ...DEFAULT_SETTINGS }
    const parsed = JSON.parse(raw) as Partial<PlannerUiSettings>
    return { ...DEFAULT_SETTINGS, ...parsed }
  } catch {
    return { ...DEFAULT_SETTINGS }
  }
}

/**
 * Stato delle preferenze planner con persistenza automatica (debounce 400 ms).
 * Ritorna `[settings, patch]`: `patch` applica un aggiornamento parziale.
 */
export function usePlannerSettings(): [
  PlannerUiSettings,
  (patch: Partial<PlannerUiSettings>) => void,
] {
  const [settings, setSettings] = React.useState<PlannerUiSettings>(load)
  const timer = React.useRef<ReturnType<typeof setTimeout> | null>(null)

  React.useEffect(() => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => {
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(settings))
      } catch {
        /* best-effort */
      }
    }, 400)
    return () => {
      if (timer.current) clearTimeout(timer.current)
    }
  }, [settings])

  const patch = React.useCallback((p: Partial<PlannerUiSettings>) => {
    setSettings((prev) => ({ ...prev, ...p }))
  }, [])

  return [settings, patch]
}
