import * as React from "react"
import { useMutation } from "@tanstack/react-query"

import { ApiError } from "@/lib/api/client"
import { DateField } from "@/components/shared/date-field"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
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
import { Textarea } from "@/components/ui/textarea"
import { updateDdpRow } from "@/lib/api/project-ddp"
import type {
  DdpDestinationItem,
  DdpRowItem,
  DdpStatusItem,
} from "@/lib/api/types"

import {
  buildDestinationOptions,
  DDP_DESTINATION_NONE,
} from "./ddp-destination-options"

const EDITABLE = {
  quantity: "1",
  itemStatus: "",
  daneaRef: "",
  dateNeeded: null as string | null,
  destination: "",
  destinationSpec: "",
  notes: "",
}

type EditableState = typeof EDITABLE

function parseDecimal(value: string): number {
  const n = Number(value.replace(",", "."))
  return Number.isFinite(n) ? n : 0
}

function toDateOnly(value: string | null): string | null {
  return value ? value.slice(0, 10) : null
}

/**
 * Modifica di una riga DDP commerciale esistente. Le nuove righe si inseriscono
 * dal picker Catalogo (CatalogPickerDialog); qui il server persiste solo
 * quantità, stato, rif. Danea, data, destinazione e note — codice, descrizione,
 * UM, costo, fornitore e produttore restano in sola lettura.
 */
export function DdpRowDialog({
  open,
  projectId,
  target,
  statuses,
  destinations,
  onClose,
  onSaved,
  onConflict,
}: {
  open: boolean
  projectId: number
  target: DdpRowItem | null
  statuses: DdpStatusItem[]
  destinations: DdpDestinationItem[]
  onClose: () => void
  onSaved: () => Promise<void>
  /** Conflitto di concorrenza (409): il parent ricarica le righe per avere un token fresco. */
  onConflict?: () => void
}) {
  const editRow = target

  const defaultStatus =
    statuses.find((s) => s.statusKey === "DO")?.statusKey ??
    statuses[0]?.statusKey ??
    "DO"

  const [form, setForm] = React.useState<EditableState>(EDITABLE)
  const [error, setError] = React.useState<string | null>(null)

  function set<K extends keyof EditableState>(key: K, value: EditableState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  React.useEffect(() => {
    if (!open || !editRow) {
      return
    }
    setError(null)
    setForm({
      quantity: String(editRow.quantity ?? 0),
      itemStatus: editRow.itemStatus || defaultStatus,
      daneaRef: editRow.daneaRef ?? "",
      dateNeeded: toDateOnly(editRow.dateNeeded),
      destination: editRow.destination ?? "",
      destinationSpec: editRow.destinationSpec ?? "",
      notes: editRow.notes ?? "",
    })
    // defaultStatus dipende da `statuses`, ma ricarichiamo solo al cambio target/apertura.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, target])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!editRow) {
        return
      }
      const quantity = parseDecimal(form.quantity)
      if (!(quantity > 0)) {
        throw new Error("La quantità deve essere maggiore di zero.")
      }
      await updateDdpRow(projectId, editRow.id, {
        id: editRow.id,
        projectId,
        catalogItemId: editRow.catalogItemId ?? null,
        partNumber: editRow.partNumber,
        description: editRow.description,
        unit: editRow.unit,
        quantity,
        unitCost: editRow.unitCost,
        supplierId: null,
        manufacturer: editRow.manufacturer,
        itemStatus: form.itemStatus || defaultStatus,
        requestedBy: editRow.requestedBy,
        daneaRef: form.daneaRef.trim(),
        dateNeeded: form.dateNeeded,
        destination: form.destination.trim(),
        destinationSpec: form.destination.trim()
          ? form.destinationSpec.trim()
          : "",
        notes: form.notes.trim(),
        ddpType: "COMMERCIAL",
        expectedUpdatedAt: editRow.updatedAt ?? null,
      })
    },
    onSuccess: async () => {
      await onSaved()
    },
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        setError(
          "La riga è stata modificata da un altro utente. Chiudi e riapri la riga per ripartire dai dati aggiornati."
        )
        // Ricarica le righe nel parent così che, riaprendo, il token sia fresco.
        onConflict?.()
        return
      }
      setError(err.message)
    },
  })

  if (!editRow) {
    return null
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Modifica riga DDP</DialogTitle>
          <DialogDescription>
            Si aggiornano quantità, stato, rif. Danea, data, destinazione e note;
            gli altri campi provengono dal catalogo.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid grid-cols-[2fr_1fr] gap-4">
            <div className="grid gap-2">
              <Label>Part Number</Label>
              <Input value={editRow.partNumber} disabled />
            </div>
            <div className="grid gap-2">
              <Label>UdM</Label>
              <Input value={editRow.unit} disabled />
            </div>
          </div>

          <div className="grid gap-2">
            <Label>Descrizione</Label>
            <Input value={editRow.description} disabled />
          </div>

          <div className="grid grid-cols-3 gap-4">
            <div className="grid gap-2">
              <Label>Quantità</Label>
              <Input
                inputMode="decimal"
                autoFocus
                value={form.quantity}
                onChange={(event) => set("quantity", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Costo unit. (€)</Label>
              <Input value={String(editRow.unitCost ?? 0)} disabled />
            </div>
            <div className="grid gap-2">
              <Label>Stato</Label>
              <Select
                value={form.itemStatus || defaultStatus}
                onValueChange={(value) => set("itemStatus", value)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {statuses.map((status) => (
                    <SelectItem key={status.statusKey} value={status.statusKey}>
                      {status.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Fornitore</Label>
              <div className="flex h-9 items-center rounded-md border bg-muted px-3 text-sm text-muted-foreground">
                {editRow.supplierName || "(nessuno)"}
              </div>
            </div>
            <div className="grid gap-2">
              <Label>Produttore</Label>
              <Input value={editRow.manufacturer} disabled />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Descr. destinazione</Label>
              <Select
                value={form.destination || DDP_DESTINATION_NONE}
                onValueChange={(value) => {
                  const destination =
                    value === DDP_DESTINATION_NONE ? "" : value
                  set("destination", destination)
                  if (!destination) {
                    set("destinationSpec", "")
                  } else if (!form.destination.trim()) {
                    set("destinationSpec", "")
                  }
                }}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={DDP_DESTINATION_NONE}>
                    (nessuna)
                  </SelectItem>
                  {buildDestinationOptions(destinations, form.destination).map(
                    (name) => (
                      <SelectItem key={name} value={name}>
                        {name}
                      </SelectItem>
                    )
                  )}
                </SelectContent>
              </Select>
            </div>
            <div className="grid gap-2">
              <Label>Specifica destinazione</Label>
              <Input
                value={form.destinationSpec}
                disabled={!form.destination.trim()}
                placeholder={
                  form.destination.trim()
                    ? "Es. R1, QE1…"
                    : "Selezionare prima la descrizione"
                }
                onChange={(event) =>
                  set("destinationSpec", event.target.value)
                }
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Data necessità</Label>
              <DateField
                value={form.dateNeeded}
                onChange={(value) => set("dateNeeded", value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Rif. Danea</Label>
              <Input
                value={form.daneaRef}
                onChange={(event) => set("daneaRef", event.target.value)}
              />
            </div>
          </div>

          <div className="grid gap-2">
            <Label>Richiesto da</Label>
            <Input value={editRow.requestedBy} disabled />
          </div>

          <div className="grid gap-2">
            <Label>Note</Label>
            <Textarea
              value={form.notes}
              rows={2}
              onChange={(event) => set("notes", event.target.value)}
            />
          </div>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending}
          >
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
