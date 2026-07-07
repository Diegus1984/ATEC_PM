import * as React from "react"

import { ApiError } from "@/lib/api/client"
import { fetchSelectivePreview, sendSelected } from "@/lib/api/digest"
import type { PlanChangeLine, SelectivePerson } from "@/lib/api/types"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"

const KIND_LABEL: Record<string, string> = {
  new: "Nuova",
  changed: "Modificata",
  deleted: "Cancellata",
}
const KIND_COLOR: Record<string, string> = {
  new: "text-green-700",
  changed: "text-amber-700",
  deleted: "text-red-700",
}

export interface NotifyNowDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  emailConfigurata: boolean
  onSent: (message: string) => void
}

export function NotifyNowDialog({
  open,
  onOpenChange,
  emailConfigurata,
  onSent,
}: NotifyNowDialogProps) {
  const [people, setPeople] = React.useState<SelectivePerson[]>([])
  const [selected, setSelected] = React.useState<Set<number>>(new Set())
  const [loading, setLoading] = React.useState(true)
  const [busy, setBusy] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setError(null)
    setLoading(true)
    void fetchSelectivePreview()
      .then((preview) => {
        setPeople(preview.dipendenti)
        setSelected(new Set())
      })
      .catch((e) => setError(e instanceof ApiError ? e.message : "Errore di caricamento"))
      .finally(() => setLoading(false))
  }, [open])

  const allLines = React.useMemo(
    () => people.flatMap((p) => p.righe),
    [people]
  )

  function toggleLine(id: number) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function selectAll(on: boolean) {
    setSelected(on ? new Set(allLines.map((l) => l.assignmentId)) : new Set())
  }

  async function handleSend() {
    if (selected.size === 0) return
    setBusy(true)
    setError(null)
    try {
      const result = await sendSelected({ assignmentIds: Array.from(selected) })
      onSent(result.message || "Notifiche inviate")
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Errore durante l'invio.")
    } finally {
      setBusy(false)
    }
  }

  function renderLine(l: PlanChangeLine) {
    return (
      <label
        key={l.assignmentId}
        className="flex cursor-pointer items-start gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-muted/50"
      >
        <Checkbox
          checked={selected.has(l.assignmentId)}
          onCheckedChange={() => toggleLine(l.assignmentId)}
          className="mt-0.5"
        />
        <span>
          <span className={`font-medium ${KIND_COLOR[l.kind] ?? ""}`}>
            {KIND_LABEL[l.kind] ?? l.kind}
          </span>
          {": "}
          {l.attivita} — {l.periodo}
          {l.autoreNome && (
            <span className="text-muted-foreground"> (da {l.autoreNome})</span>
          )}
        </span>
      </label>
    )
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Notifica subito</DialogTitle>
        </DialogHeader>

        {!emailConfigurata && (
          <p className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            L'invio email non è ancora configurato (vedi Gestione avanzata → Digest email).
          </p>
        )}

        <p className="text-sm text-muted-foreground">
          Spunta le modifiche <strong>urgenti</strong> da inviare adesso. Le altre partiranno
          dal digest automatico.
        </p>

        <div className="flex gap-2">
          <Button size="sm" variant="outline" onClick={() => selectAll(true)}>
            Seleziona tutte
          </Button>
          <Button size="sm" variant="outline" onClick={() => selectAll(false)}>
            Deseleziona tutte
          </Button>
        </div>

        <div className="max-h-80 overflow-y-auto rounded-md border">
          {loading ? (
            <p className="p-3 text-sm text-muted-foreground">Caricamento…</p>
          ) : people.length === 0 ? (
            <p className="p-3 text-sm text-muted-foreground">
              Nessuna modifica da notificare.
            </p>
          ) : (
            people.map((p) => (
              <div key={p.employeeId} className="border-b last:border-b-0">
                <div className="flex items-center gap-2 bg-muted/40 px-2 py-1.5 text-sm font-medium">
                  {p.employeeName}
                  {!p.hasEmail && (
                    <span className="text-xs text-red-600">⚠ senza email</span>
                  )}
                </div>
                {p.righe.map(renderLine)}
              </div>
            ))
          )}
        </div>

        {error && <p className="text-sm text-destructive">{error}</p>}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            Annulla
          </Button>
          <Button onClick={() => void handleSend()} disabled={busy || selected.size === 0}>
            {busy ? "Invio…" : `Invia selezionate (${selected.size})`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
