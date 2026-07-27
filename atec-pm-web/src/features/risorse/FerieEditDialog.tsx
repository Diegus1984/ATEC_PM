import * as React from "react"

import { ApiError } from "@/lib/api/client"
import {
  createAssignments,
  deleteAssignment,
  updateAssignment,
} from "@/lib/api/resource-planner"
import type { ResAssignmentDto } from "@/lib/api/types"
import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DateField } from "@/components/shared/date-field"
import { toDateOnly, dateToIso } from "@/lib/date-iso"

export interface FerieEditDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Allocazione FERIE esistente (edit) o null (nuova). */
  existing: ResAssignmentDto | null
  employeeId: number
  employeeName: string
  presetStart?: string | null
  presetEnd?: string | null
  connRef: React.MutableRefObject<string | null>
  onSaved: (message: string) => void
}

export function FerieEditDialog({
  open,
  onOpenChange,
  existing,
  employeeId,
  employeeName,
  presetStart,
  presetEnd,
  connRef,
  onSaved,
}: FerieEditDialogProps) {
  const confirm = useConfirm()
  const isNew = existing == null
  const [start, setStart] = React.useState<string | null>(null)
  const [end, setEnd] = React.useState<string | null>(null)
  const [descrizione, setDescrizione] = React.useState("")
  const [busy, setBusy] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)
  const endTouched = React.useRef(false)

  React.useEffect(() => {
    if (!open) return
    endTouched.current = false
    setError(null)
    if (existing) {
      setStart(toDateOnly(existing.dataInizio))
      setEnd(toDateOnly(existing.dataFine))
      setDescrizione(existing.descrizione ?? "")
    } else {
      const s = toDateOnly(presetStart) ?? dateToIso(new Date())
      setStart(s)
      setEnd(toDateOnly(presetEnd) ?? s)
      setDescrizione("")
    }
  }, [open, existing, presetStart, presetEnd])

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

  async function handleSave() {
    setError(null)
    if (!start || !end) {
      setError("Indica le date di inizio e fine.")
      return
    }
    if (end < start) {
      setError("La data fine non può precedere la data inizio.")
      return
    }
    setBusy(true)
    try {
      if (existing) {
        await updateAssignment(
          existing.id,
          {
            employeeId,
            tipo: "FERIE",
            dataInizio: start,
            dataFine: end,
            descrizione: descrizione.trim() ? descrizione.trim() : null,
            expectedUpdatedAt: existing.updatedAt,
          },
          connRef.current
        )
        onSaved("Ferie aggiornate")
      } else {
        await createAssignments(
          {
            employeeIds: [employeeId],
            tipo: "FERIE",
            dataInizio: start,
            dataFine: end,
            descrizione: descrizione.trim() ? descrizione.trim() : null,
          },
          connRef.current
        )
        onSaved("Ferie create")
      }
      onOpenChange(false)
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setError(
          "Queste ferie sono state modificate da un altro utente nel frattempo. Chiudi il dialogo e riprova."
        )
      } else {
        setError(e instanceof ApiError ? e.message : "Errore durante il salvataggio.")
      }
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    if (!existing) return
    const ok = await confirm({
      title: "Eliminare questo periodo di ferie?",
      description: employeeName,
    })
    if (!ok) return
    setBusy(true)
    try {
      await deleteAssignment(existing.id, connRef.current)
      onSaved("Ferie eliminate")
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Errore eliminazione.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>{isNew ? "Nuove ferie" : "Modifica ferie"}</DialogTitle>
        </DialogHeader>

        <p className="text-sm text-muted-foreground">
          {employeeName} · ferie / permesso
        </p>

        <div className="grid gap-4 py-1">
          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-1.5">
              <Label>Data inizio</Label>
              <DateField
                value={start}
                onChange={handleStartChange}
                clearable={false}
                disableBefore={isNew ? new Date(dateToIso(new Date())) : undefined}
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

        <DialogFooter className="sm:justify-between">
          {!isNew ? (
            <Button
              variant="outline"
              className="text-destructive"
              onClick={handleDelete}
              disabled={busy}
            >
              Elimina
            </Button>
          ) : (
            <span />
          )}
          <div className="flex gap-2">
            <Button
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={busy}
            >
              Annulla
            </Button>
            <Button onClick={handleSave} disabled={busy}>
              {busy ? "Salvataggio…" : "Salva"}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
