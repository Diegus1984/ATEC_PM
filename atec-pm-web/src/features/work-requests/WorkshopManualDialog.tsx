import * as React from "react"
import { Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { DateField } from "@/components/shared/date-field"
import { LookupCombobox } from "@/components/shared/lookup-combobox"
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import type {
  ProjectListItem,
  WorkRequestSaveRequest,
  WorkshopRow,
} from "@/lib/api/types"

/**
 * Riga battuta a mano in «Lavorazioni Officine» (#83): tutti i campi si compilano qui, e la
 * riga non è legata a nessuna distinta. La commessa è **facoltativa** — la segnalazione le
 * vuole ammesse anche senza commessa né Altra Attività.
 */
export function WorkshopManualDialog({
  open,
  onOpenChange,
  projects,
  row,
  defaultType,
  onSave,
  onDelete,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  projects: ProjectListItem[]
  /** Riga da modificare; assente = nuova. */
  row?: WorkshopRow | null
  /** Tipo proposto: quello della vista da cui si apre. */
  defaultType: string
  onSave: (payload: WorkRequestSaveRequest, id?: number) => void
  /** Presente solo su una riga esistente. */
  onDelete?: (id: number) => void
}) {
  const confirm = useConfirm()
  const [projectId, setProjectId] = React.useState<number | null>(null)
  const [partNumber, setPartNumber] = React.useState("")
  const [description, setDescription] = React.useState("")
  const [quantity, setQuantity] = React.useState("1")
  const [quantityProduced, setQuantityProduced] = React.useState("0")
  const [material, setMaterial] = React.useState("")
  const [treatment, setTreatment] = React.useState("")
  const [type, setType] = React.useState(defaultType)
  const [requestDate, setRequestDate] = React.useState<string | null>(null)
  const [destination, setDestination] = React.useState("")
  const [destinationSpec, setDestinationSpec] = React.useState("")
  const [daneaRef, setDaneaRef] = React.useState("")
  const [supplier, setSupplier] = React.useState("")
  const [orderDate, setOrderDate] = React.useState<string | null>(null)
  const [notes, setNotes] = React.useState("")

  // Il dialogo si riempie all'apertura, non a ogni render: mentre è aperto la riga può
  // arrivare aggiornata dal realtime e ributterebbe dentro i valori mentre si scrive.
  React.useEffect(() => {
    if (!open) return
    setProjectId(row?.projectId ?? null)
    setPartNumber(row?.partNumber ?? "")
    setDescription(row?.description ?? "")
    setQuantity(String(row?.quantity ?? 1))
    setQuantityProduced(String(row?.quantityProduced ?? 0))
    setMaterial(row?.material ?? "")
    setTreatment(row?.treatment ?? "")
    setType(row?.workType || defaultType)
    setRequestDate(row?.requestDate || null)
    setDestination(row?.destination ?? "")
    setDestinationSpec(row?.destinationSpec ?? "")
    setDaneaRef(row?.daneaRef ?? "")
    setSupplier(row?.supplierName ?? "")
    setOrderDate(row?.orderDate || null)
    setNotes(row?.notes ?? "")
  }, [open, row, defaultType])

  const numero = (v: string) => {
    const n = Number(String(v).replace(",", "."))
    return Number.isFinite(n) && n > 0 ? n : 0
  }

  function handleSave() {
    onSave(
      {
        projectId: projectId ?? 0,
        requestDate: requestDate ?? "",
        description: description.trim(),
        partNumber: partNumber.trim(),
        quantity: numero(quantity),
        quantityProduced: numero(quantityProduced),
        material: material.trim(),
        treatment: treatment.trim(),
        destination: destination.trim(),
        destinationSpec: destinationSpec.trim(),
        type,
        priority: 2,
        availabilityDate: "",
        notes: notes.trim(),
        isUltraCritical: row?.isUltraCritical ?? false,
        isDelivered: false,
        isStaging: false,
        rfqs: [],
        poSupplier: supplier.trim(),
        poNumber: daneaRef.trim(),
        poDate: orderDate ?? "",
        hasTreatment: treatment.trim().length > 0,
        treatmentDate: "",
        treatmentNotes: "",
        isTreatmentConfirmed: false,
        rowVersion: row?.rowVersion ?? null,
      },
      row?.id
    )
    onOpenChange(false)
  }

  const esterna = type === "External"

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>{row ? "Modifica riga manuale" : "Nuova riga manuale"}</DialogTitle>
          <DialogDescription>
            Riga non collegata a nessuna DDP: tutti i campi si compilano qui. La commessa è
            facoltativa.
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="grid gap-2 sm:col-span-2">
            <Label>Commessa (facoltativa)</Label>
            <LookupCombobox
              options={projects.map((p) => ({
                id: p.id,
                name: `${p.code} — ${p.title}`,
                hint: p.customerName || undefined,
              }))}
              value={projectId}
              onValueChange={setProjectId}
              placeholder="Nessuna commessa"
              searchPlaceholder="Cerca commessa…"
              emptyText="Nessuna commessa trovata"
              noneLabel="— nessuna commessa —"
            />
          </div>

          <div className="grid gap-2">
            <Label>Tipo</Label>
            <Select value={type} onValueChange={setType}>
              <SelectTrigger>
                <SelectValue placeholder="Tipo" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Internal">Interna</SelectItem>
                <SelectItem value="Print3D">Stampa 3D</SelectItem>
                <SelectItem value="External">Esterna</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="grid gap-2">
            <Label>Codice</Label>
            <Input value={partNumber} onChange={(e) => setPartNumber(e.target.value)} />
          </div>

          <div className="grid gap-2 sm:col-span-2">
            <Label>Descrizione</Label>
            <Input value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>

          <div className="grid gap-2">
            <Label>Qtà</Label>
            <Input
              inputMode="decimal"
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
            />
          </div>

          <div className="grid gap-2">
            <Label>Prodotti</Label>
            <Input
              inputMode="decimal"
              value={quantityProduced}
              onChange={(e) => setQuantityProduced(e.target.value)}
            />
          </div>

          <div className="grid gap-2">
            <Label>Materiale</Label>
            <Input value={material} onChange={(e) => setMaterial(e.target.value)} />
          </div>

          <div className="grid gap-2">
            <Label>Trattamento</Label>
            <Input value={treatment} onChange={(e) => setTreatment(e.target.value)} />
          </div>

          <div className="grid gap-2">
            <Label>Data Richiesta</Label>
            <DateField value={requestDate} onChange={setRequestDate} clearable />
          </div>

          <div className="grid gap-2">
            <Label>Destinazione</Label>
            <Input value={destination} onChange={(e) => setDestination(e.target.value)} />
          </div>

          <div className="grid gap-2">
            <Label>Specifica</Label>
            <Input
              value={destinationSpec}
              onChange={(e) => setDestinationSpec(e.target.value)}
            />
          </div>

          {esterna ? (
            <>
              <div className="grid gap-2">
                <Label>Fornitore</Label>
                <Input value={supplier} onChange={(e) => setSupplier(e.target.value)} />
              </div>
              <div className="grid gap-2">
                <Label>Rif. Danea</Label>
                <Input value={daneaRef} onChange={(e) => setDaneaRef(e.target.value)} />
              </div>
              <div className="grid gap-2">
                <Label>Data Ordine</Label>
                <DateField
                  value={orderDate}
                  onChange={setOrderDate}
                  clearable
                  // Un ordine non si emette prima di essere chiesto.
                  disableBefore={requestDate ? new Date(requestDate) : undefined}
                />
              </div>
            </>
          ) : null}

          <div className="grid gap-2 sm:col-span-2">
            <Label>Note</Label>
            <Textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Note di gestione della lavorazione"
            />
          </div>
        </div>

        <DialogFooter className="sm:justify-between">
          {row && onDelete ? (
            <Button
              variant="outline"
              className="text-destructive hover:text-destructive"
              onClick={async () => {
                const ok = await confirm({
                  title: "Eliminare la riga manuale?",
                  description: `«${row.description || row.partNumber || `#${row.id}`}» verrà tolta dalle Lavorazioni. L'operazione non si annulla.`,
                  confirmLabel: "Elimina",
                })
                if (ok) onDelete(row.id)
              }}
            >
              <Trash2 className="size-4" />
              Elimina
            </Button>
          ) : (
            <span />
          )}
          <div className="flex gap-2">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Annulla
          </Button>
          <Button onClick={handleSave} disabled={!description.trim() && !partNumber.trim()}>
            Salva
          </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
