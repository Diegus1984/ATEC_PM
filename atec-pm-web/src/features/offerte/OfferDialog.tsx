import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { ChevronDown, Copy } from "lucide-react"

import { CustomerSearchCombobox } from "@/components/shared/customer-search-combobox"
import { useCopyText } from "@/components/shared/copy-text"
import { DateField } from "@/components/shared/date-field"
import { MoneyInput } from "@/components/shared/money-input"
import { Button } from "@/components/ui/button"
import { Collapsible } from "@/components/ui/collapsible"
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
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { fetchCustomers } from "@/lib/api/customers"
import {
  createSalesOffer,
  fetchNextOfferNumber,
  fetchSalesOffer,
  updateSalesOffer,
} from "@/lib/api/sales-offers"
import type {
  SalesOffer,
  SalesOfferSaveRequest,
  SalesOfferSeller,
  SalesOfferType,
} from "@/lib/api/types"
import { formatDateShort, formatDateTimeShort } from "@/lib/date-iso"
import { euro, fmt2, parseDecimal } from "@/lib/format"
import { notifyError, notifyInfo, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { CHANCE_LEVELS, chanceLabel, composeOfferNumber } from "./sales-offer-number"
import { SALES_OFFER_STATUSES } from "./sales-offer-status"

/** Valore-sentinella delle Select: Radix non ammette `value=""`. */
const NESSUNO = "__nessuno__"

interface FormState {
  year: number
  numberText: string
  typeCode: string
  sellerCode: string
  offerDate: string | null
  customerId: number | null
  customerName: string
  contactName: string
  description: string
  amountText: string
  amountMaxText: string
  status: string
  chance: number | null
  orderAmountText: string
  isTimeMaterial: boolean
  advancePctText: string
  advanceAmountText: string
  advanceNotes: string
  postponedYearText: string
  offerPath: string
  calcPath: string
  nextContact: string | null
  notes: string
  tag: string
}

/**
 * 🪤 I campi nullabili arrivano dall'API **assenti**, non a `null`: il server serializza in
 * `WhenWritingNull` e quindi qui valgono `undefined`. Un confronto `=== null` non li vede, e
 * il campo si riempiva di «undefined» (il numero) o di «NaN» (gli importi). Da qui in giù si
 * usa `!= null`, che copre tutti e due.
 */
function statoIniziale(offer: SalesOffer | null, defaultYear: number): FormState {
  if (!offer) {
    const oggi = new Date().toISOString().slice(0, 10)
    return {
      // L'anno di un'offerta nuova è quello della sua data, non quello del filtro della
      // pagina: con il filtro su un anno passato i due nascevano in disaccordo e il
      // salvataggio veniva respinto («La data non appartiene al 2025»). Chi vuole
      // registrare un'offerta arretrata cambia la data, e l'anno la segue (vedi sotto).
      year: Number(oggi.slice(0, 4)) || defaultYear,
      numberText: "",
      typeCode: "",
      sellerCode: "",
      offerDate: oggi,
      customerId: null,
      customerName: "",
      contactName: "",
      description: "",
      amountText: "",
      amountMaxText: "",
      status: "aperta",
      chance: null,
      orderAmountText: "",
      isTimeMaterial: false,
      advancePctText: "",
      advanceAmountText: "",
      advanceNotes: "",
      postponedYearText: "",
      offerPath: "",
      calcPath: "",
      nextContact: null,
      notes: "",
      tag: "",
    }
  }
  return {
    year: offer.year,
    numberText: offer.number != null ? String(offer.number) : "",
    typeCode: offer.typeCode,
    sellerCode: offer.sellerCode,
    offerDate: offer.offerDate ? offer.offerDate.slice(0, 10) : null,
    customerId: offer.customerId,
    customerName: offer.customerName,
    contactName: offer.contactName,
    description: offer.description,
    amountText: offer.amount != null ? fmt2(offer.amount) : "",
    amountMaxText: offer.amountMax != null ? fmt2(offer.amountMax) : "",
    status: offer.status,
    chance: offer.chance ?? null,
    orderAmountText: offer.orderAmount != null ? fmt2(offer.orderAmount) : "",
    isTimeMaterial: offer.isTimeMaterial ?? false,
    advancePctText: offer.advancePct != null ? fmt2(offer.advancePct) : "",
    advanceAmountText: offer.advanceAmount != null ? fmt2(offer.advanceAmount) : "",
    advanceNotes: offer.advanceNotes,
    postponedYearText: offer.postponedYear != null ? String(offer.postponedYear) : "",
    offerPath: offer.offerPath,
    calcPath: offer.calcPath,
    nextContact: offer.nextContact ? offer.nextContact.slice(0, 10) : null,
    notes: offer.notes,
    tag: offer.tag,
  }
}

function numeroOppureNull(text: string): number | null {
  const t = text.trim()
  if (!t) return null
  const n = Number(t)
  return Number.isFinite(n) ? n : null
}

function importoOppureNull(text: string): number | null {
  return text.trim() ? parseDecimal(text) : null
}

/**
 * La scheda offerta: creazione e modifica nello stesso dialogo.
 *
 * <p>`offer.id === 0` significa **duplica**: si parte dai dati di un'altra offerta ma si
 * salva come nuova, e il numero lo assegna il server.</p>
 */
export function OfferDialog({
  open,
  offer,
  types,
  sellers,
  defaultYear,
  onClose,
  onSaved,
}: {
  open: boolean
  offer: SalesOffer | null
  types: SalesOfferType[]
  sellers: SalesOfferSeller[]
  defaultYear: number
  onClose: () => void
  onSaved: () => void
}) {
  const copyText = useCopyText()
  const isNew = !offer || offer.id === 0
  const [form, setForm] = React.useState<FormState>(() =>
    statoIniziale(offer, defaultYear)
  )

  React.useEffect(() => {
    setForm(statoIniziale(offer, defaultYear))
  }, [offer, defaultYear])

  const set = React.useCallback(<K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }))
  }, [])

  const customersQuery = useQuery({
    queryKey: ["customers"],
    queryFn: fetchCustomers,
  })

  /**
   * Contatti e storico modifiche **non stanno nella riga dell'elenco**: l'elenco è paginato
   * e caricarli per cinquanta righe alla volta sarebbe uno spreco. Li porta la scheda, e solo
   * per l'offerta aperta. Si usano SOLO per le due liste in fondo: rifare `form` da qui
   * cancellerebbe quello che l'utente sta scrivendo mentre la richiesta è in volo.
   */
  const dettaglioQuery = useQuery({
    queryKey: ["sales-offer", offer?.id],
    queryFn: () => fetchSalesOffer(offer!.id),
    enabled: !!offer && offer.id > 0,
  })
  const contatti = dettaglioQuery.data?.followups ?? offer?.followups ?? []
  const storico = dettaglioQuery.data?.log ?? offer?.log ?? []

  // Numero proposto: solo in creazione, e solo finché l'utente non ne scrive uno suo.
  const nextNumberQuery = useQuery({
    queryKey: ["sales-offer-next-number", form.year],
    queryFn: () => fetchNextOfferNumber(form.year),
    enabled: isNew,
  })

  const numeroEffettivo =
    numeroOppureNull(form.numberText) ?? (isNew ? nextNumberQuery.data?.number ?? null : null)

  const anteprima = composeOfferNumber(
    form.typeCode,
    numeroEffettivo,
    form.year,
    form.sellerCode
  )

  const saveMutation = useMutation({
    mutationFn: async () => {
      const request: SalesOfferSaveRequest = {
        year: form.year,
        number: numeroOppureNull(form.numberText),
        typeCode: form.typeCode,
        sellerCode: form.sellerCode,
        offerDate: form.offerDate,
        customerId: form.customerId,
        customerName: form.customerName.trim(),
        contactName: form.contactName.trim(),
        description: form.description.trim(),
        notes: form.notes,
        tag: form.tag.trim(),
        amount: importoOppureNull(form.amountText),
        amountMax: importoOppureNull(form.amountMaxText),
        status: form.status,
        chance: form.chance,
        orderAmount: importoOppureNull(form.orderAmountText),
        isTimeMaterial: form.isTimeMaterial,
        advancePct: importoOppureNull(form.advancePctText),
        advanceAmount: importoOppureNull(form.advanceAmountText),
        advanceNotes: form.advanceNotes.trim(),
        postponedYear: numeroOppureNull(form.postponedYearText),
        offerPath: form.offerPath.trim(),
        calcPath: form.calcPath.trim(),
        nextContact: form.nextContact,
        quoteId: offer?.quoteId ?? null,
        projectId: offer?.projectId ?? null,
        rowVersion: isNew ? null : (offer?.rowVersion ?? null),
      }

      if (isNew) return { created: await createSalesOffer(request) }
      await updateSalesOffer(offer!.id, request)
      return { created: null }
    },
    onSuccess: (esito) => {
      if (esito.created?.renumbered) {
        // Il numero proposto era stato preso da un altro venditore fra la proposta e il
        // salvataggio: si dice, invece di far finta di niente.
        notifyInfo(
          `Il numero proposto era già stato preso: assegnato il ${esito.created.composed}`
        )
      } else {
        notifySuccess(isNew ? "Offerta creata" : "Offerta salvata")
      }
      onSaved()
    },
    onError: (err: Error) => notifyError(err),
  })

  /** In produzione si va in HTTP: il percorso NAS si copia, non si apre. */
  const copia = (path: string, cosa: string) => void copyText(path, `Percorso ${cosa} copiato`)

  const forbiceIncoerente =
    form.amountMaxText.trim() !== "" &&
    form.amountText.trim() !== "" &&
    parseDecimal(form.amountMaxText) < parseDecimal(form.amountText)

  const presaSenzaOrdine =
    form.status === "presa" && !form.isTimeMaterial && form.orderAmountText.trim() === ""

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>{isNew ? "Nuova offerta" : "Scheda offerta"}</DialogTitle>
          <DialogDescription className="font-mono text-base font-medium text-foreground">
            {anteprima}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {/* ── Numerazione ─────────────────────────────────────────── */}
          <div className="grid gap-3 sm:grid-cols-4">
            <div className="space-y-1.5">
              <Label htmlFor="offer-year">Anno</Label>
              <Input
                id="offer-year"
                inputMode="numeric"
                value={String(form.year)}
                onChange={(e) => set("year", Number(e.target.value) || form.year)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="offer-number">Numero</Label>
              <Input
                id="offer-number"
                inputMode="numeric"
                placeholder={
                  isNew && nextNumberQuery.data
                    ? String(nextNumberQuery.data.number).padStart(3, "0")
                    : undefined
                }
                value={form.numberText}
                onChange={(e) => set("numberText", e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label>Tipo</Label>
              <Select
                value={form.typeCode || NESSUNO}
                onValueChange={(v) => set("typeCode", v === NESSUNO ? "" : v)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Tipo" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NESSUNO}>—</SelectItem>
                  {types.map((t) => (
                    <SelectItem key={t.code} value={t.code}>
                      {t.code} — {t.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>Venditore</Label>
              <Select
                value={form.sellerCode || NESSUNO}
                onValueChange={(v) => set("sellerCode", v === NESSUNO ? "" : v)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Sigla" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NESSUNO}>—</SelectItem>
                  {sellers.map((s) => (
                    <SelectItem key={s.code} value={s.code}>
                      {s.name ? `${s.code} — ${s.name}` : s.code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          {/* ── Cliente e data ──────────────────────────────────────── */}
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="offer-customer-name">Cliente (come sull'offerta)</Label>
              <Input
                id="offer-customer-name"
                value={form.customerName}
                onChange={(e) => set("customerName", e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label>Cliente in rubrica</Label>
              <CustomerSearchCombobox
                customers={customersQuery.data ?? []}
                value={form.customerId}
                loading={customersQuery.isLoading}
                onValueChange={(id) => {
                  set("customerId", id)
                  // Il nome dell'offerta NON si riscrive: è quello che c'è sul documento.
                  // Si compila solo se è ancora vuoto.
                  if (id && !form.customerName.trim()) {
                    const c = (customersQuery.data ?? []).find((x) => x.id === id)
                    if (c) set("customerName", c.companyName)
                  }
                }}
              />
            </div>
            <div className="space-y-1.5">
              <Label>Data</Label>
              <DateField
                value={form.offerDate}
                onChange={(v) => {
                  // L'anno segue la data: il server pretende che coincidano, e tenerli
                  // allineati a mano è solo un modo per sbagliare. Resta modificabile a
                  // parte per le bozze, che un numero ce l'hanno e una data no.
                  const anno = v ? Number(v.slice(0, 4)) : null
                  setForm((prev) => ({
                    ...prev,
                    offerDate: v,
                    year: anno && anno >= 2000 && anno <= 2100 ? anno : prev.year,
                  }))
                }}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="offer-contact">Referente</Label>
              <Input
                id="offer-contact"
                value={form.contactName}
                onChange={(e) => set("contactName", e.target.value)}
              />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="offer-description">Descrizione</Label>
            <Textarea
              id="offer-description"
              rows={2}
              value={form.description}
              onChange={(e) => set("description", e.target.value)}
            />
          </div>

          {/* ── Importi ─────────────────────────────────────────────── */}
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="space-y-1.5">
              <Label>Importo</Label>
              <MoneyInput
                value={form.amountText}
                onChange={(v) => set("amountText", v)}
              />
            </div>
            <div className="space-y-1.5">
              <Label>…fino a (forbice)</Label>
              <MoneyInput
                value={form.amountMaxText}
                onChange={(v) => set("amountMaxText", v)}
              />
              {forbiceIncoerente ? (
                <p className="text-xs text-destructive">
                  Il «fino a» non può stare sotto l'importo.
                </p>
              ) : null}
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="offer-tag">Tag</Label>
              <Input
                id="offer-tag"
                value={form.tag}
                onChange={(e) => set("tag", e.target.value)}
              />
            </div>
          </div>

          {/* ── Esito ───────────────────────────────────────────────── */}
          <div className="grid gap-3 sm:grid-cols-4">
            <div className="space-y-1.5">
              <Label>Esito</Label>
              <Select value={form.status} onValueChange={(v) => set("status", v)}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {SALES_OFFER_STATUSES.map((s) => (
                    <SelectItem key={s.key} value={s.key}>
                      {s.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>Chance</Label>
              <Select
                value={form.chance == null ? NESSUNO : String(form.chance)}
                onValueChange={(v) => set("chance", v === NESSUNO ? null : Number(v))}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {CHANCE_LEVELS.map((c) => (
                    <SelectItem key={c === null ? NESSUNO : c} value={c === null ? NESSUNO : String(c)}>
                      {chanceLabel(c)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>Importo ordine</Label>
              <MoneyInput
                value={form.orderAmountText}
                onChange={(v) => set("orderAmountText", v)}
                disabled={form.isTimeMaterial}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="offer-tm">A consuntivo</Label>
              <div className="flex h-9 items-center">
                <Switch
                  id="offer-tm"
                  checked={form.isTimeMaterial}
                  onCheckedChange={(v) => {
                    set("isTimeMaterial", v)
                    // A consuntivo l'importo dell'ordine non esiste per definizione.
                    if (v) set("orderAmountText", "")
                  }}
                />
              </div>
            </div>
          </div>

          {presaSenzaOrdine ? (
            <p className="text-xs text-destructive">
              Un'offerta presa vuole l'importo dell'ordine oppure «a consuntivo».
            </p>
          ) : null}

          {/* ── Sezioni secondarie ──────────────────────────────────── */}
          <SezionePiegata titolo="Percorsi sul NAS">
            <div className="grid gap-3">
              <PercorsoNas
                id="offer-path"
                label="Documento di offerta"
                value={form.offerPath}
                onChange={(v) => set("offerPath", v)}
                onCopy={() => copia(form.offerPath, "dell'offerta")}
              />
              <PercorsoNas
                id="calc-path"
                label="Tabella di calcolo"
                value={form.calcPath}
                onChange={(v) => set("calcPath", v)}
                onCopy={() => copia(form.calcPath, "della tabella di calcolo")}
              />
              <p className="text-xs text-muted-foreground">
                Dal browser un file sul NAS non si apre: il pulsante copia il percorso, da
                incollare in Esplora risorse.
              </p>
            </div>
          </SezionePiegata>

          <SezionePiegata titolo="Anticipo e rinvio">
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="space-y-1.5">
                <Label>Anticipo %</Label>
                <MoneyInput
                  value={form.advancePctText}
                  onChange={(v) => set("advancePctText", v)}
                  format={(n) => `${fmt2(n)} %`}
                />
              </div>
              <div className="space-y-1.5">
                <Label>Anticipo €</Label>
                <MoneyInput
                  value={form.advanceAmountText}
                  onChange={(v) => set("advanceAmountText", v)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="offer-postponed">Rinviata all'anno</Label>
                <Input
                  id="offer-postponed"
                  inputMode="numeric"
                  value={form.postponedYearText}
                  onChange={(e) => set("postponedYearText", e.target.value)}
                />
              </div>
              <div className="space-y-1.5 sm:col-span-3">
                <Label htmlFor="offer-advance-notes">Note anticipo</Label>
                <Input
                  id="offer-advance-notes"
                  value={form.advanceNotes}
                  onChange={(e) => set("advanceNotes", e.target.value)}
                />
              </div>
            </div>
          </SezionePiegata>

          <SezionePiegata titolo="Note e prossimo contatto">
            <div className="grid gap-3">
              <div className="space-y-1.5">
                <Label>Prossimo contatto</Label>
                <DateField
                  value={form.nextContact}
                  onChange={(v) => set("nextContact", v)}
                  clearable
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="offer-notes">Note</Label>
                <Textarea
                  id="offer-notes"
                  rows={3}
                  value={form.notes}
                  onChange={(e) => set("notes", e.target.value)}
                />
              </div>
            </div>
          </SezionePiegata>

          {offer && offer.id > 0 && contatti.length > 0 ? (
            <SezionePiegata titolo={`Contatti col referente (${contatti.length})`}>
              <ul className="space-y-2 text-sm">
                {contatti.map((f) => (
                  <li key={f.id} className="rounded-md border px-3 py-2">
                    <div className="flex items-center justify-between text-xs text-muted-foreground">
                      <span className="tabular-nums">{formatDateShort(f.contactDate)}</span>
                      <span>{f.createdByName}</span>
                    </div>
                    <p className="whitespace-pre-wrap">{f.notes}</p>
                  </li>
                ))}
              </ul>
            </SezionePiegata>
          ) : null}

          {offer && offer.id > 0 && storico.length > 0 ? (
            <SezionePiegata titolo={`Storico modifiche (${storico.length})`}>
              <ul className="space-y-1 text-xs">
                {storico.map((l) => (
                  <li key={l.id} className="flex flex-wrap gap-x-2 text-muted-foreground">
                    <span className="tabular-nums">{formatDateTimeShort(l.changedAt)}</span>
                    <span className="font-medium text-foreground">{l.field}</span>
                    <span>
                      {l.oldValue || "—"} → {l.newValue || "—"}
                    </span>
                    <span>{l.changedByName}</span>
                  </li>
                ))}
              </ul>
            </SezionePiegata>
          ) : null}
        </div>

        <DialogFooter className="gap-2 sm:justify-between">
          <span className="text-sm text-muted-foreground">
            {form.amountText.trim()
              ? `Importo ${euro(parseDecimal(form.amountText))}`
              : ""}
          </span>
          <div className="flex gap-2">
            <Button variant="outline" onClick={onClose}>
              Annulla
            </Button>
            <Button
              onClick={() => saveMutation.mutate()}
              disabled={saveMutation.isPending || forbiceIncoerente || presaSenzaOrdine}
            >
              {isNew ? "Crea offerta" : "Salva"}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/**
 * Sezione richiudibile, con l'animazione standard dell'app (`<Collapsible>` + i token
 * `--accordion-*`). Il contenuto resta montato durante la chiusura: un
 * `{open ? … : null}` salterebbe l'animazione.
 */
function SezionePiegata({
  titolo,
  children,
}: {
  titolo: string
  children: React.ReactNode
}) {
  const [open, setOpen] = React.useState(false)
  return (
    <div className="rounded-lg border">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center justify-between px-3 py-2 text-sm font-medium"
      >
        {titolo}
        <ChevronDown
          className={cn("size-4 transition-transform", open && "rotate-180")}
        />
      </button>
      <Collapsible open={open}>
        <div className="px-3 pb-3">{children}</div>
      </Collapsible>
    </div>
  )
}

function PercorsoNas({
  id,
  label,
  value,
  onChange,
  onCopy,
}: {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  onCopy: () => void
}) {
  return (
    <div className="space-y-1.5">
      <Label htmlFor={id}>{label}</Label>
      <div className="flex gap-2">
        <Input
          id={id}
          value={value}
          placeholder="\\NAS-ATEC\Vendite\…"
          onChange={(e) => onChange(e.target.value)}
        />
        <Button
          type="button"
          variant="outline"
          size="icon"
          disabled={!value.trim()}
          onClick={onCopy}
          title="Copia il percorso"
        >
          <Copy />
          <span className="sr-only">Copia</span>
        </Button>
      </div>
    </div>
  )
}
