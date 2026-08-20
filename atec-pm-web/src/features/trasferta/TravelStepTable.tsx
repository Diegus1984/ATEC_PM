// ── Griglia riga-persona della trasferta: 14 colonne, intestazioni a due piani ──
//
// Questa è una griglia INLINE, quindi valgono le due trappole pagate nel blocco 4:
//  1. il refetch non deve riallineare la riga mentre il fuoco è dentro (guardia su `rowRef`);
//  2. UN SOLO salvataggio per riga, all'uscita dalla riga, con i valori definitivi — non uno
//     per campo, o il secondo e il terzo commit trovano il primo ancora in volo e vengono
//     scartati in silenzio.
// Il modello è `OrderLineRow` in `features/commesse/bva-order.tsx`.
//
// Ore, Vitto, Indennità e Auto NON si digitano qui: sono pulsanti che aprono la loro
// calcolatrice a righe. Giorni, Costi Personale e Costo alloggio sono derivati.

import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { AlertTriangle, Calculator, GripVertical, Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { DateField } from "@/components/shared/date-field"
import { GridScroller } from "@/components/shared/grid-scroller"
import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { MoneyInput } from "@/components/shared/money-input"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
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
import {
  createTravelRow,
  deleteTravelRow,
  reorderTravelRows,
  updateTravelRow,
} from "@/lib/api/travel"
import type {
  TravelPersonLookup,
  TravelCalcKind,
  TravelRowDto,
  TravelStepDto,
  TravelTotalsDto,
} from "@/lib/api/types"
import { formatDateOrDash } from "@/lib/date-iso"
import { euro, fmtHours, parseDecimal } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { plainNum } from "./travel-format"

function numOrNull(text: string): number | null {
  return text.trim() === "" ? null : parseDecimal(text)
}

function textOf(value: number | null | undefined): string {
  return value == null ? "" : String(value)
}

/**
 * Colonne congelate a sinistra (#101): selezione, maniglia e **Nominativo** restano
 * a video mentre le undici colonne dei costi scorrono. Gli scostamenti seguono le
 * larghezze fisse delle prime due celle (`w-10` = 2.5rem, `w-8` = 2rem → 4.5rem).
 *
 * `z-10` e non di più: l'intestazione fissa sta a z-20 (`index.css`) e la barra
 * «Aggrega costi commessa» a z-30 (#100) — una colonna che le scavalcasse le
 * coprirebbe durante lo scorrimento.
 *
 * `bg-inherit`: la riga si tinge (ambra senza ore, rosso da compilare) e la parte
 * congelata deve tingersi con lei, altrimenti resterebbe una striscia bianca in
 * mezzo alla riga colorata. Perché funzioni la riga deve avere un fondo OPACO.
 */
const stickyCell = ["sticky left-0 z-10", "sticky left-10 z-10", "sticky left-18 z-10"] as const
export const stickyBody = stickyCell.map((c) => `${c} bg-inherit`)

/**
 * Riga «da compilare» (#103): nessuno dei campi di spesa previsti è stato toccato.
 * Si guarda `null`, non lo zero: uno 0 scritto apposta è una riga compilata, e dire
 * il contrario manderebbe il PM a ricontrollare una riga già decisa.
 * `lodgingCost` non entra nel conto perché è derivato (notti × prezzo).
 */
export function rigaDaCompilare(row: TravelRowDto): boolean {
  return (
    row.nights == null &&
    row.nightPrice == null &&
    row.mealCost == null &&
    row.allowanceCost == null &&
    row.carCost == null &&
    row.transportCost == null
  )
}

/**
 * Intestazioni a due piani: gruppo sopra, colonne sotto.
 *
 * Dalla #52 il gruppo «Personale» ha 4 colonne e non più 6: **Ore Trasferta e Costi
 * Personale sono stati eliminati**. Quelle ore arrivano dal Timesheet e sono già contate
 * in «Risorse Atec»: ripeterle qui le faceva comparire due volte a chi leggeva la pagina.
 * La prima colonna delle date porta il GIORNO sulle righe che nascono dal Timesheet e
 * l'inizio su quelle scritte a mano, che continuano a lavorare a periodo.
 */
export function TravelTableHead({ selectAll }: { selectAll?: React.ReactNode }) {
  return (
    <TableHeader className="bg-muted/40">
      <TableRow className="hover:bg-transparent">
        {/* Il gruppo «Personale» stava sopra 4 colonne (nominativo + le tre della
            durata). Con la colonna Nominativo congelata (#101) doveva restare
            congelato anche il cappello, e una cella non si può bloccare a metà:
            «Personale» è passato sopra il solo nominativo — che è la persona —
            e le tre colonne di durata hanno preso il loro cappello, «Periodo». */}
        <TableHead className={cn("w-10", stickyCell[0])} aria-label="Seleziona" />
        <TableHead className={cn("w-8", stickyCell[1])} aria-label="Riordina" />
        <TableHead
          className={cn("min-w-44 border-x text-center text-xs font-semibold", stickyCell[2])}
        >
          Personale
        </TableHead>
        <TableHead colSpan={3} className="border-x text-center text-xs font-semibold">
          Periodo
        </TableHead>
        <TableHead colSpan={4} className="border-x text-center text-xs font-semibold">
          Alloggio / Vitto
        </TableHead>
        <TableHead colSpan={3} className="border-x text-center text-xs font-semibold">
          Altri costi
        </TableHead>
        <TableHead className="w-10" aria-label="Azioni" />
      </TableRow>
      <TableRow className="hover:bg-transparent">
        <TableHead className={cn("w-10", stickyCell[0])}>{selectAll}</TableHead>
        <TableHead className={cn("w-8", stickyCell[1])} />
        <TableHead className={cn("min-w-44 text-xs", stickyCell[2])}>Nominativo</TableHead>
        <TableHead className="w-36 text-xs">Data / Inizio</TableHead>
        <TableHead className="w-36 text-xs">Fine Trasferta</TableHead>
        <TableHead className="w-28 text-center text-xs">Giorni trasferta</TableHead>
        <TableHead className="w-16 text-right text-xs">Notti</TableHead>
        <TableHead className="w-28 text-right text-xs">Prezzo</TableHead>
        <TableHead className="w-28 text-right text-xs">Costo</TableHead>
        <TableHead className="w-28 text-right text-xs">Vitto</TableHead>
        <TableHead className="w-28 text-right text-xs">Indennità</TableHead>
        <TableHead className="w-28 text-right text-xs">Auto</TableHead>
        <TableHead className="w-28 text-right text-xs">Treno/aereo</TableHead>
        <TableHead className="w-10" />
      </TableRow>
    </TableHeader>
  )
}

/** Riga Totali: 8 colonne valorizzate, le altre vuote. */
export function TravelTotalsRow({
  label,
  totals,
}: {
  label: string
  totals: TravelTotalsDto
}) {
  // Fondo OPACO (il `<TableFooter>` shadcn nasce `bg-muted/50`): sotto le celle
  // congelate della #101 scorrono le colonne dei costi, e con mezza trasparenza
  // si vedrebbero passare sotto l'etichetta.
  return (
    <TableRow className="bg-muted hover:bg-muted">
      <TableCell className={stickyBody[0]} />
      <TableCell className={stickyBody[1]} />
      {/* L'etichetta si è spostata nella colonna del nominativo (era su quattro
          colonne): è quella congelata, così «Totali» resta leggibile anche quando
          si scorre fino a «Treno/aereo». */}
      <TableCell
        className={cn(stickyBody[2], "text-xs font-semibold uppercase tracking-wide")}
      >
        {label}
      </TableCell>
      <TableCell colSpan={2} />
      <TableCell className="text-center font-semibold tabular-nums">
        {fmtHours(totals.days)}
      </TableCell>
      <TableCell />
      <TableCell />
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
      <TableCell />
    </TableRow>
  )
}

/** Cella-pulsante di una colonna alimentata da una calcolatrice. */
function CalcCell({
  value,
  money,
  onOpen,
  title,
}: {
  value: number | null
  money: boolean
  onOpen: () => void
  title: string
}) {
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
            {money ? euro(value) : plainNum(value)}
          </Button>
        </TooltipTrigger>
        <TooltipContent>{title}</TooltipContent>
      </Tooltip>
    </TableCell>
  )
}

function TravelRow({
  projectId,
  row,
  people,
  grabbed,
  onGrab,
  dragFrom,
  onDropOn,
  onChanged,
  onOpenCalc,
  selected,
  onToggleSelect,
  daCompilare,
}: {
  projectId: number
  row: TravelRowDto
  people: TravelPersonLookup[]
  grabbed: number | null
  onGrab: (id: number | null) => void
  dragFrom: React.RefObject<number | null>
  onDropOn: (targetId: number) => void
  onChanged: () => void
  onOpenCalc: (rowId: number, kind: TravelCalcKind) => void
  selected: boolean
  onToggleSelect: (rowId: number, checked: boolean) => void
  /**
   * #103: la riga era senza costi all'ultima «Lista aggiornata». È una fotografia
   * congelata dal chiamante, non `rigaDaCompilare(row)` letto qui: se sbiadisse da
   * sola alla prima cifra digitata, il PM perderebbe il segno di dove stava lavorando.
   */
  daCompilare: boolean
}) {
  const confirm = useConfirm()
  const rowRef = React.useRef<HTMLTableRowElement>(null)

  const [employeeId, setEmployeeId] = React.useState<number | null>(row.employeeId)
  const [personName, setPersonName] = React.useState(row.personName)
  const [startDate, setStartDate] = React.useState<string | null>(row.startDate)
  const [endDate, setEndDate] = React.useState<string | null>(row.endDate)
  const [excludeSat, setExcludeSat] = React.useState(row.excludeSat)
  const [excludeSun, setExcludeSun] = React.useState(row.excludeSun)
  const [hourlyRate, setHourlyRate] = React.useState(textOf(row.hourlyRate))
  const [nights, setNights] = React.useState(textOf(row.nights))
  const [nightPrice, setNightPrice] = React.useState(textOf(row.nightPrice))
  const [transportCost, setTransportCost] = React.useState(textOf(row.transportCost))

  // Il server è la verità — MA non mentre il fuoco è dentro questa riga: un refetch che
  // arriva mentre si scrive cancellerebbe sotto le dita quello che si è appena digitato.
  React.useEffect(() => {
    if (rowRef.current?.contains(document.activeElement)) return
    setEmployeeId(row.employeeId)
    setPersonName(row.personName)
    setStartDate(row.startDate)
    setEndDate(row.endDate)
    setExcludeSat(row.excludeSat)
    setExcludeSun(row.excludeSun)
    setHourlyRate(textOf(row.hourlyRate))
    setNights(textOf(row.nights))
    setNightPrice(textOf(row.nightPrice))
    setTransportCost(textOf(row.transportCost))
  }, [
    row.employeeId,
    row.personName,
    row.startDate,
    row.endDate,
    row.excludeSat,
    row.excludeSun,
    row.hourlyRate,
    row.nights,
    row.nightPrice,
    row.transportCost,
    row.rowVersion,
  ])

  const saveMutation = useMutation({
    mutationFn: (patch: Parameters<typeof updateTravelRow>[2]) =>
      updateTravelRow(projectId, row.id, patch),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: () => deleteTravelRow(projectId, row.id),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  /** UN salvataggio per riga, con i valori definitivi. Vedi l'intestazione del file. */
  function commitRow(overrides?: Partial<Record<string, unknown>>) {
    const next = {
      employeeId,
      personName: personName.trim(),
      startDate,
      endDate,
      excludeSat,
      excludeSun,
      hourlyRate: numOrNull(hourlyRate),
      nights: nights.trim() === "" ? null : Math.round(parseDecimal(nights)),
      nightPrice: numOrNull(nightPrice),
      transportCost: numOrNull(transportCost),
      rowVersion: row.rowVersion,
      ...overrides,
    }
    const unchanged =
      next.employeeId === row.employeeId &&
      next.personName === row.personName &&
      next.startDate === row.startDate &&
      next.endDate === row.endDate &&
      next.excludeSat === row.excludeSat &&
      next.excludeSun === row.excludeSun &&
      next.hourlyRate === row.hourlyRate &&
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
   * I controlli che non danno il fuoco (combo, calendario, toggle) non passano dal blur:
   * committano da soli, con il valore appena scelto passato come override — lo stato di
   * React non è ancora aggiornato quando il gestore parte.
   */
  function commitNow(overrides: Partial<Record<string, unknown>>) {
    commitRow(overrides)
  }

  // Il costo orario non si tocca più da qui: con la #52 la colonna «Costi Personale» non
  // esiste più (quelle ore stanno in Risorse Atec). Il valore delle righe vecchie resta
  // scritto sulla riga e viene risalvato com'è, così non lo si cancella per sbaglio.
  function pickPerson(id: number | null) {
    const person = people.find((p) => p.id === id)
    setEmployeeId(id)
    const name = person?.fullName ?? ""
    setPersonName(name)
    commitNow({ employeeId: id, personName: name })
  }

  // Una riga che arriva dal Timesheet non si cancella da qui: tornerebbe al primo
  // salvataggio delle ore. Si toglie togliendo le ore. Fa eccezione la riga rimasta
  // SENZA ore: quella non torna più, e cancellarla è l'unico modo di chiuderla.
  const canDelete = !row.isDerived || row.hoursMissing

  async function handleDelete() {
    const ok = await confirm({
      title: "Elimina persona",
      description: row.hoursMissing
        ? `Eliminare la riga di «${personName.trim()}»? Le ore che l'avevano generata non ci sono più, ma i costi scritti a mano su questa riga verranno persi.`
        : personName.trim()
          ? `Rimuovere «${personName.trim()}» da questo step?`
          : "Rimuovere questa riga dallo step?",
      confirmLabel: "Elimina",
    })
    if (ok) deleteMutation.mutate()
  }

  const busy = saveMutation.isPending || deleteMutation.isPending
  const options = React.useMemo(() => {
    const list = people.map((p) => ({
      id: p.id,
      name: p.fullName,
      hint: p.departmentCode || undefined,
    }))
    // Nome storico non più in anagrafica: resta selezionabile invece di sparire.
    if (row.employeeId != null && !people.some((p) => p.id === row.employeeId)) {
      list.unshift({
        id: row.employeeId,
        name: `${row.personName} (non in anagrafica)`,
        hint: undefined,
      })
    }
    return list
  }, [people, row.employeeId, row.personName])

  return (
    <TableRow
      ref={rowRef}
      onBlur={handleRowBlur}
      draggable={grabbed === row.id}
      className={cn(
        grabbed === row.id && "opacity-70",
        // Fondo pieno di partenza: le tre celle congelate della #101 lo ereditano
        // (`bg-inherit`) e devono essere OPACHE, o sotto ci si vedrebbe scorrere le
        // colonne dei costi. Per lo stesso motivo le due tinte qui sotto non hanno
        // più la mezza trasparenza che avevano prima.
        "bg-background",
        // Riga senza nessun costo compilato (#103): rosso pastello, va guardata.
        // Perde all'ambra: «le ore non ci sono più» è una notizia più grave di
        // «i costi non li ho ancora messi», e una riga può essere tutt'e due.
        daCompilare && "bg-red-50! dark:bg-red-950/40!",
        // Riga rimasta senza le ore che l'avevano generata: si vede a colpo d'occhio,
        // perché va guardata e decisa (tenerla o cancellarla).
        // Il `!` (in CODA, è Tailwind v4) serve davvero: `TableRow` porta già
        // `has-aria-expanded:bg-muted/50`, che senza forzatura vince sul colore e lascia
        // la riga grigia come tutte le altre. Trovato guardando la pagina, non il codice.
        row.hoursMissing && "bg-amber-50! dark:bg-amber-950/40!"
      )}
      onDragStart={(event) => {
        dragFrom.current = row.id
        event.dataTransfer.effectAllowed = "move"
        try {
          event.dataTransfer.setData("text/plain", String(row.id))
        } catch {
          /* alcuni browser rifiutano setData su drag sintetici */
        }
      }}
      onDragEnd={() => {
        dragFrom.current = null
        onGrab(null)
      }}
      onDragOver={(event) => {
        if (dragFrom.current != null) event.preventDefault()
      }}
      onDrop={(event) => {
        event.preventDefault()
        onDropOn(row.id)
      }}
    >
      <TableCell className={cn("w-10", stickyBody[0])}>
        <Checkbox
          checked={selected}
          aria-label={`Seleziona ${personName || "riga"}`}
          onCheckedChange={(value) => onToggleSelect(row.id, value === true)}
        />
      </TableCell>
      <TableCell
        className={cn("w-8 cursor-grab text-muted-foreground", stickyBody[1])}
        onMouseDown={() => onGrab(row.id)}
        onMouseUp={() => onGrab(null)}
        title="Trascina per riordinare"
      >
        <GripVertical className="size-4" />
      </TableCell>

      <TableCell className={stickyBody[2]}>
        {row.isDerived ? (
          // Riga nata dal Timesheet: nominativo e giorno li decide l'imputazione delle ore,
          // non si toccano da qui. Si cambiano cambiando le ore.
          <div className="flex items-center gap-1.5">
            {/* #103: il nome in grassetto nero è il secondo segnale, oltre al rosso
                della riga — si legge anche a colpo d'occhio scorrendo l'elenco. */}
            <span className={cn("text-sm", daCompilare && "font-bold text-foreground")}>
              {personName}
            </span>
            {row.hoursMissing ? (
              <Tooltip>
                <TooltipTrigger asChild>
                  <AlertTriangle className="size-3.5 shrink-0 text-amber-600" />
                </TooltipTrigger>
                <TooltipContent>
                  Le ore che avevano generato questa riga non ci sono più (cancellate o
                  spostate). La riga è rimasta con i suoi costi: cancellala tu se non serve.
                </TooltipContent>
              </Tooltip>
            ) : (
              <Tooltip>
                <TooltipTrigger asChild>
                  <span className="rounded border px-1 text-[9px] uppercase leading-4 text-muted-foreground">
                    ts
                  </span>
                </TooltipTrigger>
                <TooltipContent>
                  Riga creata dalle ore del Timesheet su questa fase
                </TooltipContent>
              </Tooltip>
            )}
          </div>
        ) : employeeId == null && personName.trim() !== "" ? (
          // Nome scritto senza dipendente collegato (import o storico): si legge com'è.
          <span className={cn("text-sm", daCompilare && "font-bold text-foreground")}>
            {personName}
          </span>
        ) : (
          <LookupCombobox
            options={options}
            value={employeeId}
            onValueChange={pickPerson}
            placeholder="— seleziona —"
            searchPlaceholder="Cerca persona…"
            emptyText="Nessun nominativo"
            disabled={busy}
            className={cn(
              "h-8 border-transparent bg-transparent shadow-none hover:border-input focus-visible:border-input focus-visible:bg-background",
              daCompilare && "font-bold text-foreground"
            )}
          />
        )}
      </TableCell>

      <TableCell>
        {row.isDerived ? (
          // Il giorno arriva dal Timesheet: si sposta spostando le ore, non di qui.
          <span className="text-sm tabular-nums">
            {formatDateOrDash(row.workDate)}
          </span>
        ) : (
          <DateField
            value={startDate}
            size="sm"
            showWeekday={false}
            segmented
            flat
            disabled={busy}
            onChange={(value) => {
              setStartDate(value)
              commitNow({ startDate: value })
            }}
          />
        )}
      </TableCell>
      <TableCell>
        {row.isDerived ? (
          <span className="text-sm text-muted-foreground">—</span>
        ) : (
          <DateField
            value={endDate}
            size="sm"
            showWeekday={false}
            segmented
            flat
            disabled={busy}
            // Regola di progetto sulle coppie di date: la fine non precede l'inizio.
            disableBefore={startDate ? new Date(startDate) : undefined}
            onChange={(value) => {
              setEndDate(value)
              commitNow({ endDate: value })
            }}
          />
        )}
      </TableCell>

      <TableCell className="text-center">
        <div className="flex flex-col items-center gap-1">
          <span className="text-sm font-medium tabular-nums">
            {row.days == null ? "—" : fmtHours(row.days)}
          </span>
          {/*
            Sulle righe derivate il giorno è uno solo, quindi escludere sabato o domenica
            non vuol dire niente: i due interruttori restano alle righe a periodo.
            E se la persona quel giorno ha lavorato su due fasi di cantiere, il giorno
            vale sulla prima riga (1, o 0,5 fino a 4 ore — #98) e 0 sulle altre — o
            l'indennità si conterebbe doppia.
          */}
          <div className={cn("flex gap-0.5", row.isDerived && "hidden")}>
            {(["sat", "sun"] as const).map((day) => {
              const off = day === "sat" ? excludeSat : excludeSun
              return (
                <Tooltip key={day}>
                  <TooltipTrigger asChild>
                    <button
                      type="button"
                      disabled={busy}
                      aria-pressed={off}
                      className={cn(
                        "rounded border px-1 text-[9px] uppercase leading-4 transition-colors",
                        off
                          ? "border-destructive/40 text-destructive line-through"
                          : "border-border text-muted-foreground hover:bg-muted"
                      )}
                      onClick={() => {
                        const next = !off
                        if (day === "sat") {
                          setExcludeSat(next)
                          commitNow({ excludeSat: next })
                        } else {
                          setExcludeSun(next)
                          commitNow({ excludeSun: next })
                        }
                      }}
                    >
                      {day === "sat" ? "sab" : "dom"}
                    </button>
                  </TooltipTrigger>
                  <TooltipContent>
                    {off
                      ? `${day === "sat" ? "Sabati" : "Domeniche"} esclusi dai giorni: clicca per includerli`
                      : `${day === "sat" ? "Sabati" : "Domeniche"} inclusi nel conteggio: clicca per escluderli`}
                  </TooltipContent>
                </Tooltip>
              )
            })}
          </div>
        </div>
      </TableCell>

      <TableCell>
        <Input
          value={nights}
          placeholder="0"
          inputMode="numeric"
          disabled={busy}
          className="h-8 w-14 border-transparent bg-transparent text-right tabular-nums shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus-visible:border-input focus-visible:bg-background"
          onChange={(e) => setNights(e.target.value.replace(/\D/g, "").slice(0, 4))}
        />
      </TableCell>
      <TableCell>
        <MoneyInput
          value={nightPrice}
          placeholder="0,00 €"
          disabled={busy}
          className="h-8 w-24 border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus-visible:border-input focus-visible:bg-background"
          onChange={setNightPrice}
        />
      </TableCell>
      <TableCell className="text-right text-sm tabular-nums">
        {row.lodgingCost == null ? "—" : euro(row.lodgingCost)}
      </TableCell>

      <CalcCell
        value={row.mealCost}
        money
        title="Calcolo della diaria (Giorni × Diaria)"
        onOpen={() => onOpenCalc(row.id, "vitto")}
      />
      <CalcCell
        value={row.allowanceCost}
        money
        title="Calcolo dell'indennità (Giorni × Indennità)"
        onOpen={() => onOpenCalc(row.id, "indennita")}
      />
      <CalcCell
        value={row.carCost}
        money
        title="Calcolo auto (Km × Rimborso × Numero Tratte)"
        onOpen={() => onOpenCalc(row.id, "auto")}
      />

      <TableCell>
        <MoneyInput
          value={transportCost}
          placeholder="0,00 €"
          disabled={busy}
          className="h-8 w-24 border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus-visible:border-input focus-visible:bg-background"
          onChange={setTransportCost}
        />
      </TableCell>

      <TableCell className="w-10">
        <Tooltip>
          <TooltipTrigger asChild>
            <span>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Elimina persona"
                disabled={busy || !canDelete}
                onClick={() => void handleDelete()}
              >
                <Trash2 className="text-destructive" />
              </Button>
            </span>
          </TooltipTrigger>
          <TooltipContent>
            {canDelete
              ? "Elimina persona"
              : "Riga creata dal Timesheet: si toglie togliendo le ore"}
          </TooltipContent>
        </Tooltip>
      </TableCell>
    </TableRow>
  )
}

export function TravelStepTable({
  projectId,
  step,
  people,
  onChanged,
  onOpenCalc,
  selectedIds,
  onToggleSelect,
  onToggleSelectAll,
  idsDaCompilare,
}: {
  projectId: number
  step: TravelStepDto
  people: TravelPersonLookup[]
  onChanged: () => void
  onOpenCalc: (rowId: number, kind: TravelCalcKind) => void
  selectedIds: Set<number>
  onToggleSelect: (rowId: number, checked: boolean) => void
  onToggleSelectAll: (rowIds: number[], checked: boolean) => void
  /** #103: righe senza costi all'ultima «Lista aggiornata». Le congela la testata. */
  idsDaCompilare: Set<number>
}) {
  const [grabbed, setGrabbed] = React.useState<number | null>(null)
  const dragFrom = React.useRef<number | null>(null)

  const addMutation = useMutation({
    mutationFn: () => createTravelRow(projectId, step.id),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  const reorderMutation = useMutation({
    mutationFn: (ids: number[]) => reorderTravelRows(projectId, step.id, ids),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  function handleDropOn(targetId: number) {
    const from = dragFrom.current
    dragFrom.current = null
    setGrabbed(null)
    if (from == null || from === targetId) return
    const ids = step.rows.map((r) => r.id)
    const fromIndex = ids.indexOf(from)
    const toIndex = ids.indexOf(targetId)
    if (fromIndex < 0 || toIndex < 0) return
    ids.splice(toIndex, 0, ids.splice(fromIndex, 1)[0])
    reorderMutation.mutate(ids)
  }

  const rowIds = step.rows.map((r) => r.id)
  const selectedInStep = rowIds.filter((id) => selectedIds.has(id)).length
  const allSelected =
    rowIds.length > 0 && selectedInStep === rowIds.length
  const someSelected = selectedInStep > 0 && !allSelected

  return (
    <div className="space-y-2">
      <GridScroller className="rounded-lg border">
        <Table className="w-max min-w-full">
          <TravelTableHead
            selectAll={
              rowIds.length > 0 ? (
                <Checkbox
                  checked={allSelected ? true : someSelected ? "indeterminate" : false}
                  aria-label="Seleziona tutte le righe dello step"
                  onCheckedChange={(value) =>
                    onToggleSelectAll(rowIds, value === true)
                  }
                />
              ) : null
            }
          />
          <TableBody>
            {step.rows.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={14} className="py-6 text-center text-sm text-muted-foreground">
                  Nessuna persona in questo step. Aggiungine una dal pulsante qui sotto.
                </TableCell>
              </TableRow>
            ) : (
              step.rows.map((row) => (
                <TravelRow
                  key={row.id}
                  projectId={projectId}
                  row={row}
                  people={people}
                  grabbed={grabbed}
                  onGrab={setGrabbed}
                  dragFrom={dragFrom}
                  onDropOn={handleDropOn}
                  onChanged={onChanged}
                  onOpenCalc={onOpenCalc}
                  selected={selectedIds.has(row.id)}
                  onToggleSelect={onToggleSelect}
                  daCompilare={idsDaCompilare.has(row.id)}
                />
              ))
            )}
          </TableBody>
          <TableFooter>
            <TravelTotalsRow label="Totali" totals={step.totals} />
          </TableFooter>
        </Table>
      </GridScroller>

      <Button
        variant="outline"
        size="sm"
        className="h-7 text-xs"
        disabled={addMutation.isPending}
        onClick={() => addMutation.mutate()}
      >
        <Plus className="size-3.5 mr-1" />
        Aggiungi persona
      </Button>
    </div>
  )
}
