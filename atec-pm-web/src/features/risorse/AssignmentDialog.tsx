import * as React from "react"
import { Check } from "lucide-react"

import { ApiError } from "@/lib/api/client"
import {
  createAssignments,
  updateAssignment,
} from "@/lib/api/resource-planner"
import type { LookupItem, ResAssignmentDto, ResTipo } from "@/lib/api/types"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { DateField } from "@/components/shared/date-field"
import { toDateOnly } from "@/lib/date-iso"

const NONE = "0"

function todayIso(): string {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, "0")
  const day = String(d.getDate()).padStart(2, "0")
  return `${y}-${m}-${day}`
}

export interface AssignmentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Allocazione esistente (edit) o null (create). */
  existing: ResAssignmentDto | null
  /** Risorsa preimpostata (single mode anche in create: drag su riga / "+"). */
  presetEmployeeId?: number | null
  presetEmployeeName?: string | null
  presetStart?: string | null
  presetEnd?: string | null
  presetTipo?: ResTipo | null
  resources: LookupItem[]
  projects: LookupItem[]
  connRef: React.MutableRefObject<string | null>
  onSaved: (message: string) => void
}

export function AssignmentDialog({
  open,
  onOpenChange,
  existing,
  presetEmployeeId,
  presetEmployeeName,
  presetStart,
  presetEnd,
  presetTipo,
  resources,
  projects,
  connRef,
  onSaved,
}: AssignmentDialogProps) {
  const isEdit = existing != null
  const singleMode = isEdit || (!!presetEmployeeId && presetEmployeeId > 0)

  const [tipo, setTipo] = React.useState<ResTipo>("OP")
  const [start, setStart] = React.useState<string | null>(null)
  const [end, setEnd] = React.useState<string | null>(null)
  const [projectId, setProjectId] = React.useState<string>(NONE)
  const [descrizione, setDescrizione] = React.useState("")
  const [selectedIds, setSelectedIds] = React.useState<Set<number>>(new Set())
  // In modifica la risorsa si può cambiare (riassegnare l'attività a un'altra persona).
  const [editEmployeeId, setEditEmployeeId] = React.useState<number | null>(null)
  const [resSearch, setResSearch] = React.useState("")
  const [busy, setBusy] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)
  const endTouched = React.useRef(false)

  // Inizializza i campi all'apertura dal record esistente o dai preset.
  React.useEffect(() => {
    if (!open) return
    endTouched.current = false
    setError(null)
    setResSearch("")
    if (existing) {
      setTipo(existing.tipo)
      setStart(toDateOnly(existing.dataInizio))
      setEnd(toDateOnly(existing.dataFine))
      setProjectId(existing.projectId ? String(existing.projectId) : NONE)
      setDescrizione(existing.descrizione ?? "")
      setSelectedIds(new Set([existing.employeeId]))
      setEditEmployeeId(existing.employeeId)
    } else {
      setTipo(presetTipo ?? "OP")
      setStart(toDateOnly(presetStart) ?? todayIso())
      setEnd(toDateOnly(presetEnd) ?? toDateOnly(presetStart) ?? todayIso())
      setProjectId(NONE)
      setDescrizione("")
      setSelectedIds(
        presetEmployeeId && presetEmployeeId > 0
          ? new Set([presetEmployeeId])
          : new Set()
      )
      setEditEmployeeId(null)
    }
  }, [open, existing, presetEmployeeId, presetStart, presetEnd, presetTipo])

  const isFerie = tipo === "FERIE"

  // La data fine segue l'inizio finché l'utente non la tocca manualmente.
  function handleStartChange(value: string | null) {
    setStart(value)
    if (!endTouched.current && value) {
      setEnd((prev) => (prev && prev >= value ? prev : value))
    }
  }

  function handleEndChange(value: string | null) {
    endTouched.current = true
    setEnd(value)
  }

  function toggleResource(id: number) {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const filteredResources = React.useMemo(() => {
    const q = resSearch.trim().toLowerCase()
    if (!q) return resources
    return resources.filter((r) => r.name.toLowerCase().includes(q))
  }, [resources, resSearch])

  // Lista del picker di riassegnazione (edit): la persona attuale sempre in cima, anche
  // se il filtro non la matcha, così la selezione resta visibile mentre si cerca.
  const editPickerResources = React.useMemo(() => {
    if (!existing) return []
    const current: LookupItem = resources.find(
      (r) => r.id === existing.employeeId
    ) ?? { id: existing.employeeId, name: existing.employeeName }
    return [
      current,
      ...filteredResources.filter((r) => r.id !== existing.employeeId),
    ]
  }, [existing, resources, filteredResources])

  const allFilteredSelected =
    filteredResources.length > 0 &&
    filteredResources.every((r) => selectedIds.has(r.id))

  function toggleAll() {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (allFilteredSelected) {
        for (const r of filteredResources) next.delete(r.id)
      } else {
        for (const r of filteredResources) next.add(r.id)
      }
      return next
    })
  }

  async function handleSubmit() {
    setError(null)
    if (!start || !end) {
      setError("Indica le date di inizio e fine.")
      return
    }
    if (end < start) {
      setError("La data fine non può precedere la data inizio.")
      return
    }
    const ids = singleMode
      ? [existing?.employeeId ?? presetEmployeeId ?? 0]
      : Array.from(selectedIds)
    if (ids.length === 0 || ids.some((x) => x <= 0)) {
      setError("Seleziona almeno una risorsa.")
      return
    }

    const projId = isFerie || projectId === NONE ? null : Number(projectId)
    const desc = descrizione.trim() ? descrizione.trim() : null

    if (existing && (!editEmployeeId || editEmployeeId <= 0)) {
      setError("Seleziona una risorsa.")
      return
    }

    setBusy(true)
    try {
      if (existing) {
        await updateAssignment(
          existing.id,
          {
            employeeId: editEmployeeId ?? existing.employeeId,
            tipo,
            dataInizio: start,
            dataFine: end,
            projectId: projId,
            descrizione: desc,
            expectedUpdatedAt: existing.updatedAt,
          },
          connRef.current
        )
        onSaved("Allocazione aggiornata")
      } else {
        const n = await createAssignments(
          {
            employeeIds: ids,
            tipo,
            dataInizio: start,
            dataFine: end,
            projectId: projId,
            descrizione: desc,
          },
          connRef.current
        )
        onSaved(n > 1 ? `${n} allocazioni create` : "Allocazione creata")
      }
      onOpenChange(false)
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setError(
          "Questa allocazione è stata modificata da un altro utente nel frattempo. Chiudi il dialogo e riprova."
        )
      } else {
        setError(
          e instanceof ApiError ? e.message : "Errore durante il salvataggio."
        )
      }
    } finally {
      setBusy(false)
    }
  }

  const singleName =
    existing?.employeeName ??
    presetEmployeeName ??
    resources.find((r) => r.id === presetEmployeeId)?.name ??
    "—"

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>
            {isEdit ? "Modifica allocazione" : "Nuova allocazione"}
          </DialogTitle>
        </DialogHeader>

        <div className="grid gap-4 py-1">
          {/* Risorsa/e */}
          {isEdit ? (
            /* In modifica la risorsa si può cambiare: scelta singola con ricerca,
               la persona attuale resta fissata in cima. */
            <div className="grid gap-1.5">
              <Label>Risorsa</Label>
              <Input
                placeholder="Cerca risorsa…"
                value={resSearch}
                onChange={(e) => setResSearch(e.target.value)}
              />
              <div className="max-h-40 overflow-y-auto rounded-md border">
                {editPickerResources.map((r) => {
                  const isSelected = editEmployeeId === r.id
                  return (
                    <button
                      type="button"
                      key={r.id}
                      onClick={() => setEditEmployeeId(r.id)}
                      className={`flex w-full cursor-pointer items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-muted/50 ${
                        isSelected ? "bg-muted/60 font-medium" : ""
                      }`}
                    >
                      <Check
                        className={`h-4 w-4 shrink-0 ${
                          isSelected ? "opacity-100" : "opacity-0"
                        }`}
                      />
                      <span>
                        {r.name}
                        {r.id === existing?.employeeId && (
                          <span className="ml-1.5 text-xs text-muted-foreground">
                            (attuale)
                          </span>
                        )}
                      </span>
                    </button>
                  )
                })}
              </div>
            </div>
          ) : singleMode ? (
            <div className="grid gap-1.5">
              <Label>Risorsa</Label>
              <div className="rounded-md border bg-muted/40 px-3 py-2 text-sm">
                {singleName}
              </div>
            </div>
          ) : (
            <div className="grid gap-1.5">
              <div className="flex items-center justify-between">
                <Label>Risorse</Label>
                <button
                  type="button"
                  className="text-xs text-muted-foreground hover:text-foreground"
                  onClick={toggleAll}
                >
                  {allFilteredSelected ? "Deseleziona tutti" : "Seleziona tutti"}
                </button>
              </div>
              <Input
                placeholder="Cerca risorsa…"
                value={resSearch}
                onChange={(e) => setResSearch(e.target.value)}
              />
              <div className="max-h-48 overflow-y-auto rounded-md border">
                {filteredResources.map((r) => (
                  <label
                    key={r.id}
                    className="flex cursor-pointer items-center gap-2 px-3 py-1.5 text-sm hover:bg-muted/50"
                  >
                    <Checkbox
                      checked={selectedIds.has(r.id)}
                      onCheckedChange={() => toggleResource(r.id)}
                    />
                    <span>{r.name}</span>
                  </label>
                ))}
                {filteredResources.length === 0 && (
                  <div className="px-3 py-2 text-sm text-muted-foreground">
                    Nessuna risorsa
                  </div>
                )}
              </div>
              <p className="text-xs text-muted-foreground">
                {selectedIds.size} selezionate
              </p>
            </div>
          )}

          {/* Tipo */}
          <div className="grid gap-1.5">
            <Label>Tipo</Label>
            <Select value={tipo} onValueChange={(v) => setTipo(v as ResTipo)}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="OP">Operativo</SelectItem>
                <SelectItem value="FLEX">Flessibile</SelectItem>
                <SelectItem value="FERIE">Ferie</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {/* Date */}
          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-1.5">
              <Label>Data inizio</Label>
              <DateField
                value={start}
                onChange={handleStartChange}
                clearable={false}
                disableBefore={isEdit ? undefined : new Date(todayIso())}
              />
            </div>
            <div className="grid gap-1.5">
              <Label>Data fine</Label>
              <DateField
                value={end}
                onChange={handleEndChange}
                clearable={false}
                disabled={!start}
                disableBefore={start ? new Date(start) : undefined}
              />
            </div>
          </div>

          {/* Commessa (solo non-FERIE): segue la filiera offerta accettata → conversione in
              commessa, quindi la lista commesse ATTIVE è già il "cosa ci sto lavorando". */}
          {!isFerie && (
            <div className="grid gap-1.5">
              <Label>Commessa</Label>
              <Select value={projectId} onValueChange={setProjectId}>
                <SelectTrigger>
                  <SelectValue placeholder="— nessuna —" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>— nessuna —</SelectItem>
                  {projects.map((p) => (
                    <SelectItem key={p.id} value={String(p.id)}>
                      {p.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {/* Descrizione */}
          <div className="grid gap-1.5">
            <Label>Descrizione</Label>
            <Input
              value={descrizione}
              onChange={(e) => setDescrizione(e.target.value)}
              placeholder="Facoltativa"
            />
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={busy}
          >
            Annulla
          </Button>
          <Button onClick={handleSubmit} disabled={busy}>
            {busy ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
