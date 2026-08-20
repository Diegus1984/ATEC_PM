// ── Trasferta a righe della sezione di preventivo (segnalazione #33) ───────
//
// Sostituisce il riquadro ambra a 7 campi digitati a mano dentro il dialogo risorsa.
// La forma è quella della griglia del modulo Trasferta (`features/trasferta/
// TravelStepTable.tsx`): intestazione a due piani, gruppi «Alloggio / Vitto» e «Altri
// costi», calcolatrici sulle colonne che nascono da un conto. Il gruppo «Personale»
// resta fuori: ore, €/h e K del preventivo stanno già in «Risorse pianificate».
//
// Valgono le due trappole già pagate sulle griglie inline:
//  1. il refetch non riallinea la riga mentre il fuoco è dentro (guardia su `rowRef`);
//  2. UN SOLO salvataggio per riga, all'uscita dalla riga, con i valori definitivi —
//     non uno per campo, o il secondo commit trova il primo ancora in volo e viene
//     scartato in silenzio.
//
// Il K di ricarico della #33 è stato rimosso il 06/08/2026 (#34): tutti gli importi di
// questa tabella sono costi secchi, e il totale di sezione è la loro somma. Non c'è più
// niente da dichiarare in fondo alla tabella perché non c'è più niente di ricaricato.

import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Calculator, Plus, Trash2, Users } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { MoneyInput } from "@/components/shared/money-input"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { TariffOptionsPanel } from "@/features/admin/config-sections/TariffOptionsPanel"
import {
  createCostTravelRow,
  deleteCostTravelRow,
  updateCostTravelRow,
  type CostTravelCalcKind,
  type ProjectCostTravelRow,
} from "@/lib/api/project-costing-travel"
import { fetchTariffOptions } from "@/lib/api/tariffs"
import type { BvaBudgetResourceDto } from "@/lib/api/types"
import { euro, parseDecimal } from "@/lib/format"
import { notifyError } from "@/lib/toast"

import { PreventivoTravelCalcDialog } from "./PreventivoTravelCalcDialog"
import { sumCostTravelRows } from "./preventivo-travel-shared"

/** Tariffe d'anagrafica proposte sul prezzo della notte (novità rispetto alla Trasferta). */
const DAILY_HOTEL = "DAILY_HOTEL"

function numOrNull(text: string): number | null {
  return text.trim() === "" ? null : parseDecimal(text)
}

function textOf(value: number | null | undefined): string {
  return value == null ? "" : String(value)
}

type CalcTarget = {
  rowId: number
  kind: CostTravelCalcKind
  personName: string
  /** Giorni della risorsa collegata: null spegne il controllo di coerenza. */
  expectedDays: number | null
}

/** Cella-pulsante di una colonna alimentata da una calcolatrice. */
function CalcCell({
  value,
  title,
  onOpen,
}: {
  value: number | null
  title: string
  /** Assente in sola lettura: la cella resta un numero, senza pulsante. */
  onOpen?: () => void
}) {
  if (!onOpen) {
    return (
      <TableCell className="text-right text-sm tabular-nums">
        {value == null ? "—" : euro(value)}
      </TableCell>
    )
  }
  return (
    <TableCell className="text-right">
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            variant="ghost"
            size="sm"
            className="h-8 w-full justify-end gap-1 px-2 font-normal tabular-nums"
            onClick={onOpen}
          >
            <Calculator className="size-3.5 shrink-0 text-muted-foreground" />
            {value == null ? "—" : euro(value)}
          </Button>
        </TooltipTrigger>
        <TooltipContent>{title}</TooltipContent>
      </Tooltip>
    </TableCell>
  )
}

/** Tendina delle tariffe d'anagrafica accanto al prezzo della notte. */
function TariffPicker({
  values,
  onPick,
  onManage,
  disabled,
}: {
  values: number[]
  onPick: (value: number) => void
  onManage: () => void
  disabled?: boolean
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label="Scegli una tariffa alloggio"
          className="shrink-0"
          disabled={disabled}
        >
          €
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-44">
        <DropdownMenuLabel className="text-xs">
          Tariffe alloggio in anagrafica
        </DropdownMenuLabel>
        {values.length === 0 ? (
          <DropdownMenuItem disabled>Nessuna tariffa</DropdownMenuItem>
        ) : (
          values.map((value) => (
            <DropdownMenuItem
              key={value}
              className="tabular-nums"
              onSelect={() => onPick(value)}
            >
              {euro(value)}
            </DropdownMenuItem>
          ))
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={onManage}>Gestisci tariffe…</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function TravelRowLine({
  projectId,
  row,
  resources,
  hotelTariffs,
  canEdit,
  onManageTariffs,
  onChanged,
  onOpenCalc,
}: {
  projectId: number
  row: ProjectCostTravelRow
  resources: BvaBudgetResourceDto[]
  hotelTariffs: number[]
  canEdit: boolean
  onManageTariffs: () => void
  onChanged: () => void
  onOpenCalc: (target: CalcTarget) => void
}) {
  const confirm = useConfirm()
  const rowRef = React.useRef<HTMLTableRowElement>(null)

  const [resourceId, setResourceId] = React.useState<number | null>(row.resourceId)
  const [personName, setPersonName] = React.useState(row.personName)
  const [nights, setNights] = React.useState(textOf(row.nights))
  const [nightPrice, setNightPrice] = React.useState(textOf(row.nightPrice))
  const [transportCost, setTransportCost] = React.useState(textOf(row.transportCost))
  // Nominativo scritto a mano invece che scelto fra le risorse pianificate: una riga
  // può valere per una voce che in preventivo non è una persona (es. «trasferta extra»).
  const [manual, setManual] = React.useState(
    row.resourceId == null && row.personName.trim() !== ""
  )

  // Il server è la verità — MA non mentre il fuoco è dentro questa riga: un refetch che
  // arriva mentre si scrive cancellerebbe sotto le dita quello che si è appena digitato.
  React.useEffect(() => {
    if (rowRef.current?.contains(document.activeElement)) return
    setResourceId(row.resourceId)
    setPersonName(row.personName)
    setNights(textOf(row.nights))
    setNightPrice(textOf(row.nightPrice))
    setTransportCost(textOf(row.transportCost))
  }, [
    row.resourceId,
    row.personName,
    row.nights,
    row.nightPrice,
    row.transportCost,
    row.rowVersion,
  ])

  const saveMutation = useMutation({
    mutationFn: (patch: Parameters<typeof updateCostTravelRow>[2]) =>
      updateCostTravelRow(projectId, row.id, patch),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: () => deleteCostTravelRow(projectId, row.id),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  /** UN salvataggio per riga, con i valori definitivi. Vedi l'intestazione del file. */
  function commitRow(overrides?: Partial<Parameters<typeof updateCostTravelRow>[2]>) {
    if (!canEdit) return
    const next = {
      resourceId,
      personName: personName.trim(),
      nights: nights.trim() === "" ? null : Math.round(parseDecimal(nights)),
      nightPrice: numOrNull(nightPrice),
      transportCost: numOrNull(transportCost),
      sortOrder: row.sortOrder,
      rowVersion: row.rowVersion,
      ...overrides,
    }
    const unchanged =
      next.resourceId === row.resourceId &&
      next.personName === row.personName &&
      next.nights === row.nights &&
      next.nightPrice === row.nightPrice &&
      next.transportCost === row.transportCost
    if (unchanged) return
    saveMutation.mutate(next)
  }

  /** Il fuoco è uscito davvero dalla riga (non è passato da un campo all'altro)? */
  function handleRowBlur(event: React.FocusEvent<HTMLTableRowElement>) {
    const next = event.relatedTarget as Node | null
    if (next && rowRef.current?.contains(next)) return
    commitRow()
  }

  /**
   * Combo e tendine non passano dal blur: committano da sole, con il valore appena
   * scelto passato come override — lo stato di React non è ancora aggiornato quando
   * il gestore parte.
   */
  function pickResource(id: number | null) {
    if (id == null) {
      // «Manuale (testo libero)»: si passa al campo di testo, il nome lo scrive l'utente.
      setResourceId(null)
      setManual(true)
      commitRow({ resourceId: null })
      return
    }
    const resource = resources.find((r) => r.resourceId === id)
    const name = resource?.resourceName ?? ""
    setResourceId(id)
    setPersonName(name)
    setManual(false)
    commitRow({ resourceId: id, personName: name })
  }

  async function handleDelete() {
    const ok = await confirm({
      title: "Elimina riga trasferta",
      description: personName.trim()
        ? `Rimuovere «${personName.trim()}» dalla trasferta di questa sezione?`
        : "Rimuovere questa riga dalla trasferta di questa sezione?",
      confirmLabel: "Elimina",
    })
    if (ok) deleteMutation.mutate()
  }

  const busy = saveMutation.isPending || deleteMutation.isPending
  const disabled = !canEdit || busy

  const options = React.useMemo(() => {
    const list = resources.map((r) => ({
      id: r.resourceId,
      name: r.resourceName,
    }))
    // Risorsa collegata e poi eliminata dal preventivo: resta selezionabile invece
    // di sparire dalla tendina lasciando la cella apparentemente vuota.
    if (row.resourceId != null && !resources.some((r) => r.resourceId === row.resourceId)) {
      list.unshift({
        id: row.resourceId,
        name: `${row.personName} (non più in preventivo)`,
      })
    }
    return list
  }, [resources, row.resourceId, row.personName])

  const expectedDays =
    resources.find((r) => r.resourceId === resourceId)?.workDays ?? null

  function openCalc(kind: CostTravelCalcKind) {
    onOpenCalc({
      rowId: row.id,
      kind,
      personName: personName.trim(),
      expectedDays,
    })
  }

  return (
    <TableRow ref={rowRef} onBlur={handleRowBlur}>
      <TableCell>
        {manual ? (
          <div className="flex items-center gap-1">
            <Input
              value={personName}
              placeholder="Nominativo / voce"
              disabled={disabled}
              className="h-8 border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus:border-input focus:bg-background focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
              onChange={(e) => setPersonName(e.target.value)}
            />
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label="Scegli fra le risorse pianificate"
                  disabled={disabled}
                  onClick={() => setManual(false)}
                >
                  <Users className="size-3.5 text-muted-foreground" />
                </Button>
              </TooltipTrigger>
              <TooltipContent>Scegli fra le risorse pianificate</TooltipContent>
            </Tooltip>
          </div>
        ) : (
          <LookupCombobox
            options={options}
            value={resourceId}
            onValueChange={pickResource}
            placeholder="— seleziona —"
            noneLabel="Manuale (testo libero)"
            searchPlaceholder="Cerca risorsa…"
            emptyText="Nessuna risorsa pianificata"
            disabled={disabled}
            className="h-8 border-transparent bg-transparent shadow-none hover:border-input data-[state=open]:border-input data-[state=open]:bg-background"
          />
        )}
      </TableCell>

      <TableCell>
        <Input
          value={nights}
          placeholder="0"
          inputMode="numeric"
          disabled={disabled}
          className="h-8 w-14 text-right tabular-nums border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus:border-input focus:bg-background focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
          onChange={(e) => setNights(e.target.value.replace(/\D/g, "").slice(0, 4))}
        />
      </TableCell>

      <TableCell>
        <div className="flex items-center gap-1">
          <MoneyInput
            value={nightPrice}
            placeholder="0,00 €"
            disabled={disabled}
            className="h-8 w-24 border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus:border-input focus:bg-background focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
            onChange={setNightPrice}
          />
          {canEdit ? (
            <TariffPicker
              values={hotelTariffs}
              disabled={busy}
              onManage={onManageTariffs}
              onPick={(value) => {
                setNightPrice(String(value))
                commitRow({ nightPrice: value })
              }}
            />
          ) : null}
        </div>
      </TableCell>

      {/* Derivata dal server (Notti × Prezzo): «—» finché mancano i due fattori. */}
      <TableCell className="text-right text-sm tabular-nums">
        {row.lodgingCost == null ? "—" : euro(row.lodgingCost)}
      </TableCell>

      <CalcCell
        value={row.mealCost}
        title="Calcolo della diaria (Giorni × Diaria)"
        onOpen={canEdit ? () => openCalc("vitto") : undefined}
      />
      <CalcCell
        value={row.allowanceCost}
        title="Calcolo dell'indennità (Giorni × Indennità) — non prende il K"
        onOpen={canEdit ? () => openCalc("indennita") : undefined}
      />
      <CalcCell
        value={row.carCost}
        title="Calcolo auto (Km × Rimborso × Numero Tratte)"
        onOpen={canEdit ? () => openCalc("auto") : undefined}
      />

      <TableCell>
        <MoneyInput
          value={transportCost}
          placeholder="0,00 €"
          disabled={disabled}
          className="h-8 w-24 border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus:border-input focus:bg-background focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
          onChange={setTransportCost}
        />
      </TableCell>

      {canEdit ? (
        <TableCell className="w-10">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Elimina riga trasferta"
                disabled={busy}
                onClick={() => void handleDelete()}
              >
                <Trash2 className="text-destructive" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Elimina riga</TooltipContent>
          </Tooltip>
        </TableCell>
      ) : null}
    </TableRow>
  )
}

export function PreventivoTravelTable({
  projectId,
  sectionId,
  rows,
  resources,
  canEdit,
  onChanged,
}: {
  projectId: number
  sectionId: number
  rows: ProjectCostTravelRow[]
  /** Risorse pianificate della sezione: alimentano la tendina del nominativo. */
  resources: BvaBudgetResourceDto[]
  canEdit: boolean
  onChanged: () => void
}) {
  const [calc, setCalc] = React.useState<CalcTarget | null>(null)
  const [tariffsOpen, setTariffsOpen] = React.useState(false)

  const tariffsQuery = useQuery({
    queryKey: ["tariff-options", DAILY_HOTEL],
    queryFn: () => fetchTariffOptions(DAILY_HOTEL),
    enabled: canEdit,
  })

  const addMutation = useMutation({
    mutationFn: () =>
      createCostTravelRow(projectId, sectionId, {
        resourceId: null,
        personName: "",
        nights: null,
        nightPrice: null,
        transportCost: null,
        sortOrder: rows.length + 1,
        rowVersion: null,
      }),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  const totals = React.useMemo(() => sumCostTravelRows(rows), [rows])
  const columns = canEdit ? 9 : 8

  // Bilancio in sola lettura (commessa nata da un'offerta) e nessuna riga: una griglia
  // vuota che non si può nemmeno riempire è solo rumore.
  if (!canEdit && rows.length === 0) return null

  return (
    <div className="space-y-2">
      <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        Trasferta (sezione da cliente)
      </p>

      <GridScroller className="rounded-md border">
        <Table className="w-max min-w-full">
          {/* Intestazione a due piani, la stessa forma della Gestione Trasferta. */}
          <TableHeader className="bg-muted/40">
            <TableRow className="hover:bg-transparent">
              <TableHead className="min-w-44" aria-label="Nominativo" />
              <TableHead colSpan={4} className="border-x text-center text-xs font-semibold">
                Alloggio / Vitto
              </TableHead>
              <TableHead colSpan={3} className="border-x text-center text-xs font-semibold">
                Altri costi
              </TableHead>
              {canEdit ? <TableHead className="w-10" aria-label="Azioni" /> : null}
            </TableRow>
            <TableRow className="hover:bg-transparent">
              <TableHead className="min-w-44 text-xs">Nominativo</TableHead>
              <TableHead className="w-16 text-right text-xs">Notti</TableHead>
              <TableHead className="w-36 text-right text-xs">Prezzo</TableHead>
              <TableHead className="w-28 text-right text-xs">Costo</TableHead>
              <TableHead className="w-28 text-right text-xs">Vitto</TableHead>
              <TableHead className="w-28 text-right text-xs">Indennità</TableHead>
              <TableHead className="w-28 text-right text-xs">Auto</TableHead>
              <TableHead className="w-28 text-right text-xs">Treno/aereo</TableHead>
              {canEdit ? <TableHead className="w-10" /> : null}
            </TableRow>
          </TableHeader>

          <TableBody>
            {rows.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell
                  colSpan={columns}
                  className="py-4 text-center text-xs text-muted-foreground"
                >
                  Nessuna trasferta preventivata in questa sezione.
                </TableCell>
              </TableRow>
            ) : (
              rows.map((row) => (
                <TravelRowLine
                  key={row.id}
                  projectId={projectId}
                  row={row}
                  resources={resources}
                  hotelTariffs={(tariffsQuery.data ?? []).map((t) => t.value)}
                  canEdit={canEdit}
                  onManageTariffs={() => setTariffsOpen(true)}
                  onChanged={onChanged}
                  onOpenCalc={setCalc}
                />
              ))
            )}
          </TableBody>

          <TableFooter>
            {/* Notti e Prezzo restano vuote: sommare notti e prezzo/notte non vuol
                dire niente (è già la scelta della griglia di Trasferta). */}
            <TableRow className="hover:bg-transparent">
              <TableCell colSpan={3} className="text-xs font-semibold uppercase tracking-wide">
                Totali
              </TableCell>
              <TableCell className="text-right font-semibold tabular-nums">
                {euro(totals.lodgingCost)}
              </TableCell>
              <TableCell className="text-right font-semibold tabular-nums">
                {euro(totals.mealCost)}
              </TableCell>
              <TableCell className="text-right font-semibold tabular-nums">
                {euro(totals.allowanceCost)}
              </TableCell>
              <TableCell className="text-right font-semibold tabular-nums">
                {euro(totals.carCost)}
              </TableCell>
              <TableCell className="text-right font-semibold tabular-nums">
                {euro(totals.transportCost)}
              </TableCell>
              {canEdit ? <TableCell /> : null}
            </TableRow>
            {/* Totale di sezione: somma secca. Prima qui c'era la riga che dichiarava il
                ricarico («spese × K, indennità no»); col K rimosso (#34) sarebbe rumore. */}
            <TableRow className="hover:bg-transparent">
              <TableCell colSpan={columns} className="text-xs tabular-nums">
                <span className="text-muted-foreground">Spese</span>{" "}
                <span className="font-medium">{euro(totals.markableCost)}</span>
                <span className="mx-2 text-muted-foreground">·</span>
                <span className="text-muted-foreground">Indennità</span>{" "}
                <span className="font-medium">{euro(totals.allowanceCost)}</span>
                <span className="mx-2 text-muted-foreground">=</span>
                <span className="font-semibold">
                  {euro(totals.markableCost + totals.allowanceCost)}
                </span>
              </TableCell>
            </TableRow>
          </TableFooter>
        </Table>
      </GridScroller>

      {canEdit ? (
        <Button
          variant="outline"
          size="sm"
          className="h-7 text-xs"
          disabled={addMutation.isPending}
          onClick={() => addMutation.mutate()}
        >
          <Plus className="size-3.5 mr-1" />
          Aggiungi riga trasferta
        </Button>
      ) : null}

      <PreventivoTravelCalcDialog
        projectId={projectId}
        rowId={calc?.rowId ?? null}
        personName={calc?.personName ?? ""}
        kind={calc?.kind ?? "vitto"}
        expectedDays={calc?.expectedDays ?? null}
        onClose={() => setCalc(null)}
        onSaved={() => {
          setCalc(null)
          onChanged()
        }}
      />

      <Dialog open={tariffsOpen} onOpenChange={(next) => !next && setTariffsOpen(false)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Tariffe alloggio</DialogTitle>
            <DialogDescription>
              Prezzo a notte proposto sul campo «Prezzo»
            </DialogDescription>
          </DialogHeader>
          <TariffOptionsPanel types={[DAILY_HOTEL]} />
          <DialogFooter>
            <Button onClick={() => setTariffsOpen(false)}>Chiudi</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
