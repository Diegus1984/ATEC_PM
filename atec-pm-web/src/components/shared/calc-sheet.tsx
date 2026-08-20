// ── Finestra di calcolo a righe (blocco 5 del piano V32) ───────────────────
//
// Una sola finestra riusata da più modalità: oggi «Lavorazioni Officine» a preventivo,
// domani le 4 calcolatrici della Trasferta. Quello che porta rispetto a un campo libero
// è il DETTAGLIO: come si è arrivati a quel totale resta scritto e si riapre.
//
// Perché è un dialogo con una Conferma sola, e non una griglia con commit su blur:
// nel blocco 4 le griglie inline hanno perso dati due volte in silenzio (il refetch che
// riallinea la riga mentre ci stai scrivendo dentro, e i salvataggi di fila che si
// scartano a vicenda). Qui si lavora su una copia locale — nessun refetch la tocca — e
// alla Conferma parte UNA PUT con i valori definitivi.

import * as React from "react"
import {
  Check,
  GripVertical,
  Lock,
  Plus,
  Trash2,
  TriangleAlert,
  Unlock,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import { MoneyInput } from "@/components/shared/money-input"
import { Badge } from "@/components/ui/badge"
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
import type { ProjectCalcRowDto } from "@/lib/api/types"
import { euro, parseDecimal } from "@/lib/format"
import { cn } from "@/lib/utils"

/** Ricarico di VENDITA (D4): moltiplicatori, non percentuali. */
export const MIN_MARKUP = 1
export const MAX_MARKUP = 9.999

/** Colonne opzionali di una sezione, oltre a Descrizione (sempre presente). */
export type CalcSheetColumn =
  | "quantity"
  | "unitCost"
  | "multiplier"
  | "amount"
  | "markup"
  | "sale"

export type CalcSheetSection = {
  /** Valore scritto in `groupKey` sulle righe della sezione. */
  key: string
  heading: string
  /** Etichetta della riga di totale della sezione. */
  totalLabel: string
  columns: CalcSheetColumn[]
  /** Intestazioni personalizzate (es. `unitCost` = «Costo orario» invece di «Costo»). */
  labels?: Partial<Record<CalcSheetColumn, string>>
  addLabel?: string
  /**
   * Come si leggono valori e totali. `money` (default) sono importi in euro;
   * `plain` sono numeri puri — la calcolatrice delle Ore somma ore, non soldi,
   * e mostrarle come «6,00 €» sarebbe sbagliato prima ancora che brutto.
   */
  valueKind?: "money" | "plain"
  /** Descrizione facoltativa: le calcolatrici di trasferta hanno solo numeri. */
  hideDescription?: boolean
  /** Valori proponibili per il costo unitario (anagrafica tariffe). */
  unitCostOptions?: {
    values: number[]
    /** Nome della tariffa (#87): con più tariffe, l'importo da solo non dice quale sia. */
    labelOf?: (value: number) => string | undefined
    manageLabel?: string
    onManage?: () => void
  }
}

/**
 * Controllo di coerenza mostrato in fondo alla finestra: confronta la somma di una colonna
 * con un valore atteso. Informa, non blocca — nel prototipo è la banda verde/gialla
 * «giorni del calcolo vs giorni trasferta».
 */
export type CalcSheetCheck = {
  column: "quantity"
  expected: number | null
  /** Es. «Giorni del calcolo». */
  subject: string
  /** Es. «i Giorni trasferta della riga». */
  reference: string
}

/** Riga della copia di lavoro: i campi restano testo finché non si conferma. */
type WorkRow = {
  uid: string
  groupKey: string
  description: string
  quantity: string
  unitCost: string
  multiplier: string
  amount: string
  amountPinned: boolean
  markup: string
  linkedSource: string
}

let uidCounter = 0
function nextUid(): string {
  uidCounter += 1
  return `calc-${uidCounter}`
}

function numOrNull(text: string): number | null {
  return text.trim() === "" ? null : parseDecimal(text)
}

function textOf(value: number | null | undefined): string {
  return value == null ? "" : String(value)
}

/**
 * Il ricarico si scrive sempre come in D4: tre decimali e virgola («1,450»).
 * Dal server torna come numero, quindi `String(1.45)` darebbe «1.45» — che in un
 * campo che parla di moltiplicatori si legge male ed è proprio il punto della D4.
 */
function markupText(value: number): string {
  return value.toFixed(3).replace(".", ",")
}

/**
 * Importo dal calcolo — stessa formula del server (`ProjectCalcRowDto.ComputedAmount`).
 * Senza quantità il costo unitario vale come importo della riga: è quello che rende
 * esprimibile una lavorazione a forfait senza inventarsi delle ore.
 */
function computedAmount(
  quantity: number | null,
  unitCost: number | null,
  multiplier: number | null
): number | null {
  if (unitCost == null) return null
  const base = quantity == null ? unitCost : quantity * unitCost
  return base * (multiplier ?? 1)
}

function rowAmount(row: WorkRow): number | null {
  if (row.amountPinned) return numOrNull(row.amount)
  return computedAmount(
    numOrNull(row.quantity),
    numOrNull(row.unitCost),
    numOrNull(row.multiplier)
  )
}

/** Numero puro all'italiana, senza decimali forzati (colonne e totali non monetari). */
function plainNumber(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return "—"
  return String(Math.round(value * 1000) / 1000).replace(".", ",")
}

function rowSale(row: WorkRow): number | null {
  const amount = rowAmount(row)
  if (amount == null) return null
  const markup = numOrNull(row.markup)
  return amount * (markup ?? 1)
}

function isEmptyRow(row: WorkRow): boolean {
  return (
    row.description.trim() === "" &&
    row.quantity.trim() === "" &&
    row.unitCost.trim() === "" &&
    row.multiplier.trim() === "" &&
    row.amount.trim() === "" &&
    row.linkedSource.trim() === ""
  )
}

function emptyRow(groupKey: string, defaultMarkup: number): WorkRow {
  return {
    uid: nextUid(),
    groupKey,
    description: "",
    quantity: "",
    unitCost: "",
    multiplier: "",
    amount: "",
    amountPinned: false,
    markup: markupText(defaultMarkup),
    linkedSource: "",
  }
}

/** Totale: «—» solo se NESSUNA riga ha un importo (0,00 € vorrebbe dire «vale zero»). */
function sumOrNull(
  rows: WorkRow[],
  pick: (row: WorkRow) => number | null
): number | null {
  let sum = 0
  let any = false
  for (const row of rows) {
    const value = pick(row)
    if (value == null) continue
    sum += value
    any = true
  }
  return any ? sum : null
}

export function CalcSheetDialog({
  open,
  title,
  intro,
  sections,
  rows,
  grandTotalLabel,
  defaultMarkup = 1.45,
  check,
  saving = false,
  onClose,
  onConfirm,
}: {
  open: boolean
  title: string
  /** Sottotitolo: quale voce e quale lato del Riepilogo si sta calcolando. */
  intro?: string
  sections: CalcSheetSection[]
  /** Righe già salvate. La finestra ne fa una copia: finché non confermi non tocca niente. */
  rows: ProjectCalcRowDto[]
  grandTotalLabel: string
  defaultMarkup?: number
  /** Controllo di coerenza informativo mostrato in fondo (non blocca la Conferma). */
  check?: CalcSheetCheck
  saving?: boolean
  onClose: () => void
  onConfirm: (rows: ProjectCalcRowDto[]) => void
}) {
  const confirm = useConfirm()
  const [work, setWork] = React.useState<WorkRow[]>([])
  const [grabbed, setGrabbed] = React.useState<string | null>(null)
  const dragFrom = React.useRef<string | null>(null)

  // La copia di lavoro nasce all'APERTURA e da lì è indipendente: un refetch che arriva
  // mentre l'utente sta scrivendo non deve riallineare niente (trappola n.1 del blocco 4).
  const wasOpen = React.useRef(false)
  React.useEffect(() => {
    if (open && !wasOpen.current) {
      const copy: WorkRow[] = rows.map((row) => ({
        uid: nextUid(),
        groupKey: row.groupKey,
        description: row.description,
        quantity: textOf(row.quantity),
        unitCost: textOf(row.unitCost),
        multiplier: textOf(row.multiplier),
        amount: textOf(row.amount),
        amountPinned: row.amountPinned,
        markup: markupText(row.markupValue),
        linkedSource: row.linkedSource,
      }))
      // Resta sempre almeno una riga vuota per sezione: si scrive senza dover
      // prima premere «Aggiungi».
      for (const section of sections) {
        if (!copy.some((r) => r.groupKey === section.key)) {
          copy.push(emptyRow(section.key, defaultMarkup))
        }
      }
      setWork(copy)
      setGrabbed(null)
    }
    wasOpen.current = open
  }, [open, rows, sections, defaultMarkup])

  function patchRow(uid: string, patch: Partial<WorkRow>) {
    setWork((prev) =>
      prev.map((row) => (row.uid === uid ? { ...row, ...patch } : row))
    )
  }

  function addRow(groupKey: string) {
    setWork((prev) => {
      const next = [...prev]
      // In coda alla sua sezione, non in fondo a tutto.
      let insertAt = next.length
      for (let i = next.length - 1; i >= 0; i -= 1) {
        if (next[i].groupKey === groupKey) {
          insertAt = i + 1
          break
        }
      }
      next.splice(insertAt, 0, emptyRow(groupKey, defaultMarkup))
      return next
    })
  }

  async function removeRow(row: WorkRow) {
    if (!isEmptyRow(row)) {
      const ok = await confirm({
        title: "Elimina riga",
        description: row.description.trim()
          ? `Rimuovere la riga «${row.description.trim()}» dal calcolo?`
          : "Rimuovere questa riga dal calcolo?",
        confirmLabel: "Elimina",
      })
      if (!ok) return
    }
    setWork((prev) => {
      const next = prev.filter((r) => r.uid !== row.uid)
      if (!next.some((r) => r.groupKey === row.groupKey)) {
        next.push(emptyRow(row.groupKey, defaultMarkup))
      }
      return next
    })
  }

  /** Riordino: si trascina solo dentro la propria sezione. */
  function moveRow(fromUid: string, toUid: string) {
    if (fromUid === toUid) return
    setWork((prev) => {
      const from = prev.findIndex((r) => r.uid === fromUid)
      const to = prev.findIndex((r) => r.uid === toUid)
      if (from < 0 || to < 0) return prev
      if (prev[from].groupKey !== prev[to].groupKey) return prev
      const next = [...prev]
      const [moved] = next.splice(from, 1)
      next.splice(to, 0, moved)
      return next
    })
  }

  const kept = work.filter((row) => !isEmptyRow(row))
  const grandTotal = sumOrNull(kept, rowAmount)
  const grandSale = sumOrNull(kept, rowSale)

  // Un foglio è omogeneo: basta la prima sezione a dire come si leggono i totali.
  const money = (sections[0]?.valueKind ?? "money") === "money"
  const showSale = sections.some((s) => s.columns.includes("sale"))

  // Il ricarico si controlla solo dove esiste: le calcolatrici di trasferta non lo hanno
  // e imporre 1,000–9,999 su un campo che non si vede bloccherebbe la Conferma per nulla.
  const markupError =
    sections.some((s) => s.columns.includes("markup")) &&
    kept.some((row) => {
      const markup = numOrNull(row.markup)
      return markup == null || markup < MIN_MARKUP || markup > MAX_MARKUP
    })

  const checkSum = check
    ? kept.reduce((sum, row) => sum + (numOrNull(row.quantity) ?? 0), 0)
    : 0
  const checkOk = check?.expected != null && Math.abs(checkSum - check.expected) < 1e-9

  function handleConfirm() {
    if (markupError || saving) return
    onConfirm(
      kept.map((row, index) => ({
        id: 0,
        groupKey: row.groupKey,
        description: row.description.trim(),
        quantity: numOrNull(row.quantity),
        unitCost: numOrNull(row.unitCost),
        multiplier: numOrNull(row.multiplier),
        amount: rowAmount(row),
        amountPinned: row.amountPinned,
        markupValue: numOrNull(row.markup) ?? defaultMarkup,
        linkedSource: row.linkedSource,
        sortOrder: index + 1,
      }))
    )
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-5xl">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {intro ? <DialogDescription>{intro}</DialogDescription> : null}
        </DialogHeader>

        <div className="space-y-4">
          {sections.map((section) => (
            <CalcSection
              key={section.key}
              section={section}
              rows={work.filter((row) => row.groupKey === section.key)}
              grabbed={grabbed}
              onGrab={setGrabbed}
              dragFrom={dragFrom}
              onMove={moveRow}
              onPatch={patchRow}
              onAdd={() => addRow(section.key)}
              onRemove={removeRow}
            />
          ))}

          {/* Con una sezione sola il totale generale ripeterebbe parola per parola quello
              della sezione: si tiene solo il piede della tabella. */}
          {sections.length > 1 ? (
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-muted/40 px-3 py-2">
              <span className="text-sm font-semibold uppercase tracking-wide">
                {grandTotalLabel}
              </span>
              <span className="text-sm tabular-nums">
                <span className="font-semibold">
                  {money ? euro(grandTotal) : plainNumber(grandTotal)}
                </span>
                {showSale ? (
                  <span className="ml-3 text-xs text-muted-foreground">
                    vendita {euro(grandSale)}
                  </span>
                ) : null}
              </span>
            </div>
          ) : null}

          {check && check.expected != null ? (
            <div
              className={cn(
                "flex items-start gap-2 rounded-lg border px-3 py-2 text-xs",
                checkOk
                  ? "border-emerald-500/40 bg-emerald-50/60 text-emerald-700 dark:bg-emerald-950/30"
                  : "border-amber-500/50 bg-amber-50/60 text-amber-800 dark:bg-amber-950/30"
              )}
            >
              {checkOk ? (
                <Check className="mt-px size-3.5 shrink-0" />
              ) : (
                <TriangleAlert className="mt-px size-3.5 shrink-0" />
              )}
              <span>
                {checkOk ? (
                  <>
                    {check.subject} ({plainNumber(checkSum)}) coerenti con{" "}
                    {check.reference}.
                  </>
                ) : (
                  <>
                    Anomalia: {check.subject.toLowerCase()} ({plainNumber(checkSum)}) non
                    coincidono con {check.reference} ({plainNumber(check.expected)}).
                  </>
                )}
              </span>
            </div>
          ) : null}

          {markupError ? (
            <p className="text-sm text-destructive">
              {/* Virgola decimale: scritto «1.000» un messaggio che parla di
                  moltiplicatori si legge «mille» ed è peggio del problema. */}
              Il ricarico è un moltiplicatore tra{" "}
              {MIN_MARKUP.toFixed(3).replace(".", ",")} e{" "}
              {MAX_MARKUP.toFixed(3).replace(".", ",")} (1,450 = +45%), non una
              percentuale: con «45» venderesti a 45 volte il costo.
            </p>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>
            Annulla
          </Button>
          <Button onClick={handleConfirm} disabled={saving || markupError}>
            {saving ? "Salvataggio…" : "Conferma"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function CalcSection({
  section,
  rows,
  grabbed,
  onGrab,
  dragFrom,
  onMove,
  onPatch,
  onAdd,
  onRemove,
}: {
  section: CalcSheetSection
  rows: WorkRow[]
  grabbed: string | null
  onGrab: (uid: string | null) => void
  dragFrom: React.RefObject<string | null>
  onMove: (fromUid: string, toUid: string) => void
  onPatch: (uid: string, patch: Partial<WorkRow>) => void
  onAdd: () => void
  onRemove: (row: WorkRow) => void
}) {
  const has = (column: CalcSheetColumn) => section.columns.includes(column)
  const label = (column: CalcSheetColumn, fallback: string) =>
    section.labels?.[column] ?? fallback

  const money = (section.valueKind ?? "money") === "money"
  const showValue = (n: number | null) => (money ? euro(n) : plainNumber(n))
  const valuePlaceholder = money ? "0,00 €" : "0"

  const kept = rows.filter((row) => !isEmptyRow(row))
  const total = sumOrNull(kept, rowAmount)
  const sale = sumOrNull(kept, rowSale)

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        {/* Con una sezione sola (le calcolatrici di trasferta) l'intestazione ripeterebbe
            il titolo della finestra: si passa heading="" e sparisce. */}
        <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
          {section.heading}
        </p>
        <Button variant="outline" size="sm" className="h-7 text-xs" onClick={onAdd}>
          <Plus className="size-3.5 mr-1" />
          {section.addLabel ?? "Aggiungi riga"}
        </Button>
      </div>

      <GridScroller className="rounded-lg border">
        <Table>
          <TableHeader className="bg-muted/40">
            <TableRow className="hover:bg-transparent">
              <TableHead className="w-8" aria-label="Riordina" />
              {!section.hideDescription && (
                <TableHead className="text-xs">Descrizione</TableHead>
              )}
              {has("quantity") && (
                <TableHead className="w-24 text-right text-xs">
                  {label("quantity", "Q.tà")}
                </TableHead>
              )}
              {has("unitCost") && (
                <TableHead className="w-40 text-right text-xs">
                  {label("unitCost", "Costo")}
                </TableHead>
              )}
              {has("multiplier") && (
                <TableHead className="w-28 text-right text-xs">
                  {label("multiplier", "×")}
                </TableHead>
              )}
              {has("amount") && (
                <TableHead className="w-44 text-right text-xs">
                  {label("amount", "Importo")}
                </TableHead>
              )}
              {has("markup") && (
                <TableHead className="w-28 text-right text-xs">
                  {label("markup", "Ricarico")}
                  <span className="block text-[9px] font-normal normal-case text-muted-foreground">
                    1,450 = +45%
                  </span>
                </TableHead>
              )}
              {has("sale") && (
                <TableHead className="w-32 text-right text-xs">
                  {label("sale", "Vendita")}
                </TableHead>
              )}
              <TableHead className="w-10" aria-label="Azioni" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((row) => {
              const linked = row.linkedSource.trim() !== ""
              const amount = rowAmount(row)
              return (
                <TableRow
                  key={row.uid}
                  draggable={grabbed === row.uid}
                  className={cn(grabbed === row.uid && "opacity-70")}
                  onDragStart={(event) => {
                    dragFrom.current = row.uid
                    event.dataTransfer.effectAllowed = "move"
                    try {
                      event.dataTransfer.setData("text/plain", row.uid)
                    } catch {
                      /* alcuni browser rifiutano setData su drag sintetici */
                    }
                  }}
                  onDragEnd={() => {
                    dragFrom.current = null
                    onGrab(null)
                  }}
                  onDragOver={(event) => {
                    if (dragFrom.current) event.preventDefault()
                  }}
                  onDrop={(event) => {
                    event.preventDefault()
                    if (dragFrom.current) onMove(dragFrom.current, row.uid)
                    dragFrom.current = null
                    onGrab(null)
                  }}
                >
                  <TableCell
                    className="w-8 cursor-grab text-muted-foreground"
                    onMouseDown={() => onGrab(row.uid)}
                    onMouseUp={() => onGrab(null)}
                    title="Trascina per riordinare"
                  >
                    <GripVertical className="size-4" />
                  </TableCell>

                  {!section.hideDescription && (
                    <TableCell>
                      <div className="flex items-center gap-1.5">
                        {linked ? (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <Badge variant="outline" className="shrink-0 text-[10px]">
                                AUTO
                              </Badge>
                            </TooltipTrigger>
                            <TooltipContent>
                              Riga generata da {row.linkedSource}: si aggiorna da sola.
                              Puoi solo forzarne l'importo.
                            </TooltipContent>
                          </Tooltip>
                        ) : null}
                        <Input
                          value={row.description}
                          placeholder="Descrizione"
                          readOnly={linked}
                          className={cn(
                            "h-8 bg-background field-sizing-content min-w-40",
                            linked && "text-muted-foreground"
                          )}
                          onChange={(e) =>
                            onPatch(row.uid, { description: e.target.value })
                          }
                          onKeyDown={(e) => {
                            if (e.key === "Enter") {
                              e.preventDefault()
                              onAdd()
                            }
                          }}
                        />
                      </div>
                    </TableCell>
                  )}

                  {has("quantity") && (
                    <TableCell>
                      <Input
                        value={row.quantity}
                        placeholder="0"
                        inputMode="decimal"
                        readOnly={linked}
                        className="h-8 w-20 bg-background text-right tabular-nums"
                        onChange={(e) =>
                          onPatch(row.uid, { quantity: e.target.value })
                        }
                        onKeyDown={(e) => {
                          if (e.key === "Enter") {
                            e.preventDefault()
                            onAdd()
                          }
                        }}
                      />
                    </TableCell>
                  )}

                  {has("unitCost") && (
                    <TableCell>
                      <div className="flex items-center justify-end gap-1">
                        {money ? (
                          <MoneyInput
                            value={row.unitCost}
                            placeholder={valuePlaceholder}
                            disabled={linked}
                            className="h-8 w-28 bg-background"
                            onChange={(value) => onPatch(row.uid, { unitCost: value })}
                            onKeyDown={(e) => {
                              if (e.key === "Enter") {
                                e.preventDefault()
                                onAdd()
                              }
                            }}
                          />
                        ) : (
                          <Input
                            value={row.unitCost}
                            placeholder={valuePlaceholder}
                            inputMode="decimal"
                            readOnly={linked}
                            className="h-8 w-28 bg-background text-right tabular-nums"
                            onChange={(e) =>
                              onPatch(row.uid, { unitCost: e.target.value })
                            }
                            onKeyDown={(e) => {
                              if (e.key === "Enter") {
                                e.preventDefault()
                                onAdd()
                              }
                            }}
                          />
                        )}
                        {section.unitCostOptions && !linked ? (
                          <UnitCostPicker
                            options={section.unitCostOptions}
                            onPick={(value) =>
                              onPatch(row.uid, { unitCost: String(value) })
                            }
                          />
                        ) : null}
                      </div>
                    </TableCell>
                  )}

                  {has("multiplier") && (
                    <TableCell>
                      <Input
                        value={row.multiplier}
                        placeholder="1"
                        inputMode="decimal"
                        readOnly={linked}
                        className="h-8 w-20 bg-background text-right tabular-nums"
                        onChange={(e) =>
                          onPatch(row.uid, { multiplier: e.target.value })
                        }
                        onKeyDown={(e) => {
                          if (e.key === "Enter") {
                            e.preventDefault()
                            onAdd()
                          }
                        }}
                      />
                    </TableCell>
                  )}

                  {has("amount") && (
                    <TableCell>
                      <div className="flex items-center justify-end gap-1">
                        {/* Stesso comportamento del prototipo in tutte e due le
                            modalità: scrivere qui congela l'importo, svuotare torna
                            al calcolo. Cambia solo come si legge. */}
                        {money ? (
                          <MoneyInput
                            value={
                              row.amountPinned ? row.amount : amount == null ? "" : String(amount)
                            }
                            placeholder={valuePlaceholder}
                            className={cn(
                              "h-8 w-28 bg-background",
                              !row.amountPinned && "text-muted-foreground"
                            )}
                            onChange={(value) =>
                              onPatch(row.uid, {
                                amount: value,
                                amountPinned: value.trim() !== "",
                              })
                            }
                            onKeyDown={(e) => {
                              if (e.key === "Enter") {
                                e.preventDefault()
                                onAdd()
                              }
                            }}
                          />
                        ) : (
                          <Input
                            value={
                              row.amountPinned ? row.amount : amount == null ? "" : String(amount)
                            }
                            placeholder={valuePlaceholder}
                            inputMode="decimal"
                            className={cn(
                              "h-8 w-28 bg-background text-right tabular-nums",
                              !row.amountPinned && "text-muted-foreground"
                            )}
                            onChange={(e) =>
                              onPatch(row.uid, {
                                amount: e.target.value,
                                amountPinned: e.target.value.trim() !== "",
                              })
                            }
                            onKeyDown={(e) => {
                              if (e.key === "Enter") {
                                e.preventDefault()
                                onAdd()
                              }
                            }}
                          />
                        )}
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              aria-label={
                                row.amountPinned
                                  ? "Torna all'importo calcolato"
                                  : "Importo calcolato"
                              }
                              className={
                                row.amountPinned ? "text-amber-600" : "text-muted-foreground"
                              }
                              onClick={() =>
                                onPatch(row.uid, {
                                  amountPinned: !row.amountPinned,
                                  amount: row.amountPinned
                                    ? ""
                                    : amount == null
                                      ? ""
                                      : String(amount),
                                })
                              }
                            >
                              {row.amountPinned ? <Lock /> : <Unlock />}
                            </Button>
                          </TooltipTrigger>
                          <TooltipContent>
                            {row.amountPinned
                              ? "Importo forzato a mano: clicca per tornare al calcolo"
                              : "Importo calcolato: clicca per forzarlo a mano"}
                          </TooltipContent>
                        </Tooltip>
                      </div>
                    </TableCell>
                  )}

                  {has("markup") && (
                    <TableCell>
                      <Input
                        value={row.markup}
                        placeholder="1,450"
                        inputMode="decimal"
                        className="h-8 w-20 bg-background text-right tabular-nums"
                        onChange={(e) => onPatch(row.uid, { markup: e.target.value })}
                        onKeyDown={(e) => {
                          if (e.key === "Enter") {
                            e.preventDefault()
                            onAdd()
                          }
                        }}
                      />
                    </TableCell>
                  )}

                  {has("sale") && (
                    <TableCell className="text-right tabular-nums text-muted-foreground">
                      {euro(rowSale(row))}
                    </TableCell>
                  )}

                  <TableCell className="w-10">
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label="Elimina riga"
                          onClick={() => void onRemove(row)}
                        >
                          <Trash2 className="text-destructive" />
                        </Button>
                      </TooltipTrigger>
                      <TooltipContent>Elimina riga</TooltipContent>
                    </Tooltip>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
          <TableFooter>
            {/* Totali in una fascia, non incolonnati: la colonna che porta il costo
                cambia da sezione a sezione (Costo nelle esterne, Importo nelle interne)
                e allinearli sotto una sola di esse sarebbe fuorviante. */}
            <TableRow className="hover:bg-transparent">
              <TableCell
                colSpan={2 + section.columns.length + (section.hideDescription ? 0 : 1)}
                className="py-1.5"
              >
                <div className="flex flex-wrap items-center gap-4 text-xs">
                  <span className="mr-auto font-semibold uppercase tracking-wide">
                    {section.totalLabel}
                  </span>
                  {has("sale") ? (
                    <span className="tabular-nums text-muted-foreground">
                      vendita {euro(sale)}
                    </span>
                  ) : null}
                  <span className="font-semibold tabular-nums">{showValue(total)}</span>
                </div>
              </TableCell>
            </TableRow>
          </TableFooter>
        </Table>
      </GridScroller>
    </div>
  )
}

/** Tendina dei valori d'anagrafica per il costo unitario (tariffe orarie, diarie, €/km…). */
function UnitCostPicker({
  options,
  onPick,
}: {
  options: NonNullable<CalcSheetSection["unitCostOptions"]>
  onPick: (value: number) => void
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label="Scegli una tariffa"
          className="shrink-0"
        >
          €
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-44">
        <DropdownMenuLabel className="text-xs">Tariffe in anagrafica</DropdownMenuLabel>
        {options.values.length === 0 ? (
          <DropdownMenuItem disabled>Nessuna tariffa</DropdownMenuItem>
        ) : (
          options.values.map((value) => {
            const label = options.labelOf?.(value)
            return (
              <DropdownMenuItem
                key={value}
                className="tabular-nums"
                onSelect={() => onPick(value)}
              >
                {label ? `${label} — ` : ""}
                {euro(value)}
              </DropdownMenuItem>
            )
          })
        )}
        {options.onManage ? (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuItem onSelect={() => options.onManage?.()}>
              {options.manageLabel ?? "Gestisci tariffe…"}
            </DropdownMenuItem>
          </>
        ) : null}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
