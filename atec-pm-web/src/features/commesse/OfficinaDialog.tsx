// ── Dialogo nuovo/modifica particolare meccanico ───────────────────────────

import * as React from "react"

import { DateField } from "@/components/shared/date-field"
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
import type {
  DdpDestinationItem,
  DdpStatusItem,
  DdpTreatmentItem,
  OfficinaItemSaveRequest,
} from "@/lib/api/types"

import { DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { DDP_STATUS_TO_ORDER, filterStatusOptions } from "./ddp-constants"
import {
  buildDestinationOptions,
  DDP_DESTINATION_NONE,
} from "./ddp-destination-options"
import type { CodexPriceInfo } from "./officina-shared"

export function OfficinaDialog({
  form,
  statuses,
  transitions,
  destinations,
  treatments,
  saving,
  hasChildren = false,
  codexPriceInfo,
  onCodexPriceInfoChange,
  onClose,
  onChange,
  onSave,
}: {
  form: OfficinaItemSaveRequest | null
  statuses: DdpStatusItem[]
  /** Matrice avanzamenti (v7): stato corrente → stati selezionabili. */
  transitions?: Record<string, string[]>
  destinations: DdpDestinationItem[]
  treatments: DdpTreatmentItem[]
  saving: boolean
  hasChildren?: boolean
  codexPriceInfo?: CodexPriceInfo
  onCodexPriceInfoChange?: (info: CodexPriceInfo) => void
  onClose: () => void
  onChange: (form: OfficinaItemSaveRequest) => void
  onSave: () => void
}) {
  const [strQuantity, setStrQuantity] = React.useState("")
  const [strUnitCost, setStrUnitCost] = React.useState("")
  const [strProduced, setStrProduced] = React.useState("")
  // Stato salvato sulla riga all'apertura: la finestra opzioni si calcola da qui
  // (non dal valore selezionato, che cambia mentre l'utente sceglie).
  const [baseStatus, setBaseStatus] = React.useState("")
  const canEditQuantity =
    !!form &&
    form.itemStatus === DDP_STATUS_TO_ORDER &&
    form.parentOfficinaItemId == null

  const isEditRow = !!form && form.id > 0
  // Riga nuova → finestra di partenza INIZIO (tipo OFFICINA); riga esistente →
  // transizioni ammesse dallo stato salvato (baseStatus).
  const statusOptions = React.useMemo(
    () =>
      filterStatusOptions(statuses, isEditRow ? baseStatus : "", transitions),
    [statuses, isEditRow, baseStatus, transitions]
  )

  const treatmentOptions = React.useMemo(() => {
    const list = [...treatments]
    const current = (form?.treatment ?? "").trim()
    if (current && !list.some((t) => t.name.toUpperCase() === current.toUpperCase())) {
      list.unshift({ id: -1, name: current, sortOrder: 0, isActive: true })
    }
    return list
  }, [treatments, form?.treatment])

  React.useEffect(() => {
    if (form) {
      setStrQuantity(String(form.quantity ?? 0))
      setStrUnitCost(String(form.unitCost ?? 0))
      setStrProduced(String(form.quantityProduced ?? 0))
      setBaseStatus(form.itemStatus ?? "")
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form?.id])

  if (!form) return null
  // In modifica il Codice (101 Codex), la Descrizione e il Richiedente provengono
  // dal Codex e sono read-only, come nella griglia officina del WPF.
  const isEdit = form.id > 0
  const isChild = form.parentOfficinaItemId != null
  const set = (patch: Partial<OfficinaItemSaveRequest>) =>
    onChange({ ...form, ...patch })
  const num = (s: string) => {
    const v = Number(s.replace(",", "."))
    return Number.isFinite(v) ? v : 0
  }
  const field = (
    label: string,
    value: string,
    key: keyof OfficinaItemSaveRequest,
    disabled = false
  ) => (
    <div className="grid gap-1.5">
      <Label className="text-xs text-muted-foreground">{label}</Label>
      <Input
        value={value}
        disabled={disabled}
        onChange={(e) => set({ [key]: e.target.value })}
      />
    </div>
  )

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>
            {form.id > 0 ? "Modifica particolare" : "Nuovo particolare"}
          </DialogTitle>
        </DialogHeader>
        <div className="grid grid-cols-2 gap-3">
          {field("Codice (101 Codex)", form.partNumber, "partNumber", isEdit)}
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Stato</Label>
            <Select
              value={form.itemStatus}
              onValueChange={(v) => set({ itemStatus: v })}
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {statusOptions.map((s) => (
                  <SelectItem key={s.statusKey} value={s.statusKey}>
                    {s.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="col-span-2">
            {field("Descrizione", form.description, "description", isEdit)}
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Quantità</Label>
            <Input
              inputMode="decimal"
              value={strQuantity}
              disabled={!canEditQuantity}
              title={
                isChild
                  ? "Componente di composizione: la quantità segue quella del padre"
                  : canEditQuantity
                    ? undefined
                    : "La quantità è modificabile solo in stato Da Ordinare"
              }
              onChange={(e) => {
                setStrQuantity(e.target.value)
                set({ quantity: num(e.target.value) })
              }}
            />
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Pezzi prodotti</Label>
            <Input
              inputMode="decimal"
              value={strProduced}
              disabled={form.itemStatus === DDP_STATUS_CANCELLED}
              onChange={(e) => {
                setStrProduced(e.target.value)
                set({ quantityProduced: num(e.target.value) })
              }}
            />
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Costo unitario (€)</Label>
            <Input
              inputMode="decimal"
              value={strUnitCost}
              disabled={hasChildren}
              onChange={(e) => {
                setStrUnitCost(e.target.value)
                set({ unitCost: num(e.target.value) })
              }}
            />
          </div>
          {codexPriceInfo?.showCheckbox && (
            <div className="col-span-2 flex items-center gap-2 rounded-lg border border-blue-100 bg-blue-50/50 p-2.5 dark:border-blue-950 dark:bg-blue-950/20">
              <Checkbox
                id="updateCodexPrice"
                checked={codexPriceInfo.checked}
                onCheckedChange={(checked) =>
                  onCodexPriceInfoChange?.({ ...codexPriceInfo, checked: !!checked })
                }
              />
              <Label
                htmlFor="updateCodexPrice"
                className="text-xs font-medium text-blue-900 dark:text-blue-200 cursor-pointer select-none"
              >
                Aggiorna prezzo in Anagrafica Codex (attualmente a 0 €)
              </Label>
            </div>
          )}
          {field("Materiale", form.material, "material")}
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Trattamento</Label>
            <Select
              value={form.treatment || "__none__"}
              onValueChange={(v) => set({ treatment: v === "__none__" ? "" : v })}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="—" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__none__">—</SelectItem>
                {treatmentOptions.map((t) => (
                  <SelectItem key={t.id} value={t.name}>
                    {t.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {field("Fornitore (officina)", form.supplierName, "supplierName")}
          {field("Richiesto da", form.requestedBy, "requestedBy", isEdit)}
          {field("N° Ordine", form.daneaRef, "daneaRef")}
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Necessario per</Label>
            <DateField
              value={form.dateNeeded}
              onChange={(value) => set({ dateNeeded: value })}
            />
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Data ordine</Label>
            <DateField
              value={form.orderDate}
              onChange={(value) => set({ orderDate: value })}
            />
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Descr. destinazione</Label>
            <Select
              value={form.destination || DDP_DESTINATION_NONE}
              onValueChange={(v) => {
                const destination = v === DDP_DESTINATION_NONE ? "" : v
                set({
                  destination,
                  destinationSpec: destination
                    ? form.destination.trim()
                      ? form.destinationSpec
                      : ""
                    : "",
                })
              }}
            >
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={DDP_DESTINATION_NONE}>(nessuna)</SelectItem>
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
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Specifica destinazione</Label>
            <Input
              value={form.destinationSpec}
              disabled={!form.destination.trim()}
              placeholder={
                form.destination.trim()
                  ? "Es. R1, QE1…"
                  : "Selezionare prima la descrizione"
              }
              onChange={(e) => set({ destinationSpec: e.target.value })}
            />
          </div>
          <div className="col-span-2">{field("Note", form.notes, "notes")}</div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>
            Annulla
          </Button>
          <Button onClick={onSave} disabled={saving}>
            {saving ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
