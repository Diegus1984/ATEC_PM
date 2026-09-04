// ── Dialogo nuovo/modifica particolare meccanico ───────────────────────────

import * as React from "react"
import { useQuery } from "@tanstack/react-query"

import { MoneyInput } from "@/components/shared/money-input"
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
import { formatDateOrDash } from "@/lib/date-iso"
import { fetchTariffOptions } from "@/lib/api/tariffs"
import type {
  DdpDestinationItem,
  DdpStatusItem,
  DdpTreatmentItem,
  OfficinaItemSaveRequest,
} from "@/lib/api/types"
import { euro } from "@/lib/format"

import { DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { DDP_STATUS_TO_ORDER, filterStatusOptions } from "./ddp-constants"
import {
  buildDestinationOptions,
  DDP_DESTINATION_NONE,
} from "./ddp-destination-options"
import type { CodexPriceInfo } from "./officina-shared"

/**
 * Segnalazione #82: Data Richiesta e Data ordine si gestiscono in griglia.
 * I campi restano nel dialogo ma nascosti — basta rimettere a `true` se
 * domani si vuole di nuovo la doppia modifica.
 */
const SHOW_DIALOG_DATES = false

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
  const [strWorkHours, setStrWorkHours] = React.useState("")

  // Tariffe orarie delle officine interne (anagrafica tariffe, tipo HOURLY_RATE).
  // Dalla segnalazione #87 sono PIÙ D'UNA e hanno un nome — meccanica 50 €/h,
  // carpenteria 35, stampa 3D 20 — e quale usare lo dice chi compila la riga.
  const hourlyRateQuery = useQuery({
    queryKey: ["tariff-options", "HOURLY_RATE"],
    queryFn: () => fetchTariffOptions("HOURLY_RATE"),
    staleTime: 0,
  })
  const tariffe = React.useMemo(() => hourlyRateQuery.data ?? [], [hourlyRateQuery.data])

  /**
   * Tariffa con cui si calcola il costo: quella scritta sulla riga, altrimenti — e solo se
   * in anagrafica ce n'è UNA SOLA — quell'unica. Con più tariffe non se ne sceglie una a
   * caso: 5 ore × 20 € e 5 ore × 50 € sono due costi entrambi plausibili, e la differenza
   * la vedrebbe solo il Bilancio a fine commessa.
   */
  const hourlyRate = React.useMemo(() => {
    if (form?.hourlyRate != null) return form.hourlyRate
    return tariffe.length === 1 ? tariffe[0].value : null
  }, [form?.hourlyRate, tariffe])
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
      setStrWorkHours(form.workHours == null ? "" : String(form.workHours))
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
  /**
   * Ore × costo orario → costo unitario (#54, #87). Il costo si riscrive solo quando ci
   * sono ENTRAMBI: senza tariffa scelta o senza ore resta quello che c'è, che può essere
   * stato battuto a mano.
   */
  const ricalcola = (
    hours: number | null,
    rate: number | null,
    extra: Partial<OfficinaItemSaveRequest> = {}
  ) => {
    const patch: Partial<OfficinaItemSaveRequest> = { workHours: hours, ...extra }
    if (hours != null && rate != null) {
      const cost = hours * rate
      patch.unitCost = cost
      // La tariffa usata resta scritta sulla riga anche quando non è stata scelta a mano
      // (anagrafica con una sola tariffa): è quella che spiega il costo.
      patch.hourlyRate = rate
      setStrUnitCost(String(cost))
    }
    set(patch)
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
          {/*
            Ore di lavorazione (segnalazione #54): sta PRIMA del costo unitario perché è
            quello che si compila per primo. Scrivendo le ore il costo unitario si calcola
            da solo (ore × tariffa oraria scelta) e resta comunque correggibile a mano: il
            calcolo propone, non impone. Svuotare il campo non azzera il costo.
            #87: la tariffa è una scelta esplicita, perché ora ce n'è una per lavorazione.
          */}
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Ore lavorazione</Label>
            <Input
              inputMode="decimal"
              value={strWorkHours}
              disabled={hasChildren}
              title={
                hourlyRate == null
                  ? "Scegli il costo orario: senza, il costo unitario resta manuale"
                  : "Ore × costo orario = costo unitario"
              }
              onChange={(e) => {
                const text = e.target.value
                setStrWorkHours(text)
                const hours = text.trim() === "" ? null : num(text)
                ricalcola(hours, hourlyRate)
              }}
            />
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Costo orario</Label>
            <Select
              value={hourlyRate == null ? "" : String(hourlyRate)}
              disabled={hasChildren || tariffe.length === 0}
              onValueChange={(v) => {
                const rate = num(v)
                const hours = strWorkHours.trim() === "" ? null : num(strWorkHours)
                ricalcola(hours, rate, { hourlyRate: rate })
              }}
            >
              <SelectTrigger className="w-full">
                <SelectValue
                  placeholder={
                    tariffe.length === 0
                      ? "Nessuna tariffa in anagrafica"
                      : "Scegli il costo orario"
                  }
                />
              </SelectTrigger>
              <SelectContent>
                {tariffe.map((t) => (
                  <SelectItem key={t.id} value={String(t.value)}>
                    {t.label ? `${t.label} — ` : ""}
                    {euro(t.value)}/h
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Costo unitario (€)</Label>
            <MoneyInput
              value={strUnitCost}
              disabled={hasChildren}
              onChange={(value) => {
                setStrUnitCost(value)
                set({ unitCost: num(value) })
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
          {/* «Inserito da» (#61): si compila da solo alla nascita della riga col nome di
              chi è collegato, e resta correggibile anche dopo — come nell'Excel. Prima
              si chiamava «Richiesto da» ed era bloccato in modifica. */}
          {field("Inserito da", form.requestedBy, "requestedBy")}
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Data inserimento</Label>
            <Input value={formatDateOrDash(form?.createdAt)} disabled className="h-8 text-xs" />
          </div>
          {/* «Creata da»: l'autore vero registrato dal server, che resta anche se
              «Inserito da» viene corretto a mano. Non si scrive. */}
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Creata da</Label>
            <Input value={form?.createdByName || "—"} disabled className="h-8 text-xs" />
          </div>
          {/* Stessi nomi della griglia (segnalazione #58): la finestra e la colonna che
              scrivono lo stesso dato non possono chiamarlo in due modi. */}
          {field("Rif. Danea", form.daneaRef, "daneaRef")}
          {SHOW_DIALOG_DATES ? (
            <>
              <div className="grid gap-1.5">
                <Label className="text-xs text-muted-foreground">Data Richiesta</Label>
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
            </>
          ) : null}
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
