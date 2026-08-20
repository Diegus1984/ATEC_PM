// ── Bilancio commessa: «Ordine Commessa» e «Riepilogo Costi» ───────────────
// Le etichette sono quelle del prototipo Gestione_Commesse_V32, alla lettera.
//
// Due regole del prototipo che vanno riprodotte o i numeri non tornano:
//  1. un totale è «—» solo se TUTTI i suoi addendi mancano (0,00 € significa
//     «vale zero», ≠ «non compilato»);
//  2. Margine di Sicurezza e Redditività si calcolano appena UNO dei due termini
//     esiste, trattando l'altro come 0.
// La Redditività di ENTRAMBE le sezioni si misura sul Totale Ordine, mai sul
// Totale Costi di Vendita: la vendita entra solo nel Margine di Sicurezza.

import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { Calculator, ChevronRight, Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import { MoneyInput } from "@/components/shared/money-input"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
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
  createOrderLine,
  deleteOrderLine,
  updateOrderLine,
} from "@/lib/api/project-bva"
import type {
  BvaCostLineDto,
  BvaEconomicSummary,
  ProjectOrderLineDto,
} from "@/lib/api/types"
import { euro, parseDecimal, percent } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

/** Rosso sui valori negativi (nel dettaglio commessa non c'è nessuna soglia: solo il segno). */
function signClass(value: number | null | undefined): string {
  return value != null && value < 0 ? "text-destructive" : ""
}

/** Numero da campo testo: vuoto → null (≠ 0, che significherebbe «vale zero»). */
function amountFromText(text: string): number | null {
  return text.trim() === "" ? null : parseDecimal(text)
}

function amountToText(amount: number | null): string {
  return amount == null ? "" : String(amount)
}

// ═══════════════════════════════════════════════════════════════
// ORDINE COMMESSA
// ═══════════════════════════════════════════════════════════════

/** Una riga della tabella, con stato locale e un solo commit all'uscita dalla riga. */
function OrderLineRow({
  projectId,
  line,
  canDelete,
  onChanged,
}: {
  projectId: number
  line: ProjectOrderLineDto
  canDelete: boolean
  onChanged: () => void
}) {
  const confirm = useConfirm()
  const rowRef = React.useRef<HTMLTableRowElement>(null)
  const [orderRef, setOrderRef] = React.useState(line.orderRef)
  const [position, setPosition] = React.useState(line.orderPosition)
  const [amount, setAmount] = React.useState(amountToText(line.amount))

  // Il server è la verità: quando la riga torna dal refetch (o da un altro utente via
  // SignalR) i campi si riallineano — MA non mentre il fuoco è dentro questa riga.
  // Senza questa guardia un aggiornamento che arriva mentre si sta scrivendo cancella
  // sotto le dita quello che l'utente ha appena digitato (visto a runtime: la Posizione
  // spariva). Appena il fuoco esce, il commit parte e il refetch riallinea tutto.
  React.useEffect(() => {
    if (rowRef.current?.contains(document.activeElement)) return
    setOrderRef(line.orderRef)
    setPosition(line.orderPosition)
    setAmount(amountToText(line.amount))
  }, [line.orderRef, line.orderPosition, line.amount, line.rowVersion])

  const saveMutation = useMutation({
    mutationFn: (patch: {
      orderRef: string
      orderPosition: string
      amount: number | null
    }) =>
      updateOrderLine(projectId, line.id, { ...patch, rowVersion: line.rowVersion }),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: () => deleteOrderLine(projectId, line.id),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  const insertMutation = useMutation({
    mutationFn: () =>
      createOrderLine(projectId, {
        orderRef: "",
        orderPosition: "",
        amount: null,
        afterLineId: line.id,
      }),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  /**
   * UN salvataggio per riga, quando il fuoco esce dalla riga — non uno per campo.
   * Con il commit per campo tre modifiche di fila producevano tre PUT in fila: il
   * secondo e il terzo trovavano il primo ancora in volo e venivano scartati in
   * silenzio (verificato a runtime: si salvava solo il primo campo). Qui invece
   * quando l'utente lascia la riga i tre valori sono quelli definitivi, la PUT è una
   * sola e porta il `rowVersion` aggiornato dall'ultimo refetch.
   */
  function commitRow() {
    const next = {
      orderRef: orderRef.trim(),
      orderPosition: position,
      amount: amountFromText(amount),
    }
    const unchanged =
      next.orderRef === line.orderRef &&
      next.orderPosition === line.orderPosition &&
      next.amount === line.amount
    if (unchanged) return
    saveMutation.mutate(next)
  }

  /** Il fuoco è uscito davvero dalla riga (non è passato da un campo all'altro)? */
  function handleRowBlur(event: React.FocusEvent<HTMLTableRowElement>) {
    const next = event.relatedTarget as Node | null
    if (next && rowRef.current?.contains(next)) return
    commitRow()
  }

  async function handleDelete() {
    const label = [line.orderRef, line.orderPosition].filter(Boolean).join(" · ")
    const ok = await confirm({
      title: "Elimina riga d'ordine",
      description: label
        ? `Rimuovere la riga «${label}»?`
        : "Rimuovere questa riga d'ordine?",
      confirmLabel: "Elimina",
    })
    if (ok) deleteMutation.mutate()
  }

  const busy = saveMutation.isPending || deleteMutation.isPending

  return (
    <TableRow ref={rowRef} onBlur={handleRowBlur}>
      <TableCell>
        <Input
          value={orderRef}
          placeholder="Numero ordine"
          inputMode="numeric"
          disabled={busy}
          className="h-8 field-sizing-content min-w-32 border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus:border-input focus:bg-background focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
          onChange={(e) => setOrderRef(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") e.currentTarget.blur()
          }}
        />
      </TableCell>
      <TableCell>
        <Input
          value={position}
          placeholder="00000"
          inputMode="numeric"
          disabled={busy}
          className="h-8 w-24 text-right tabular-nums border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus:border-input focus:bg-background focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
          // Stessa normalizzazione del server: solo cifre, massimo 5.
          onChange={(e) =>
            setPosition(e.target.value.replace(/\D/g, "").slice(0, 5))
          }
          onKeyDown={(e) => {
            if (e.key === "Enter") e.currentTarget.blur()
          }}
        />
      </TableCell>
      <TableCell>
        <MoneyInput
          value={amount}
          placeholder="0,00 €"
          disabled={busy}
          className="h-8 w-40 text-right tabular-nums border-transparent bg-transparent shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus:border-input focus:bg-background focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
          onChange={setAmount}
        />
      </TableCell>
      <TableCell className="w-24">
        <div className="flex items-center justify-end gap-1">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Inserisci riga sotto"
                disabled={busy || insertMutation.isPending}
                onClick={() => insertMutation.mutate()}
              >
                <Plus />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Inserisci riga sotto</TooltipContent>
          </Tooltip>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Elimina riga"
                disabled={busy || !canDelete}
                onClick={() => void handleDelete()}
              >
                <Trash2 className="text-destructive" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              {canDelete
                ? "Elimina riga"
                : "L'ordine deve avere almeno una riga"}
            </TooltipContent>
          </Tooltip>
        </div>
      </TableCell>
    </TableRow>
  )
}

export function OrderLinesBlock({
  projectId,
  lines,
  economic,
  onChanged,
}: {
  projectId: number
  lines: ProjectOrderLineDto[]
  economic: BvaEconomicSummary | null
  onChanged: () => void
}) {
  // Segnalazione #34: il «Totale Costi di Vendita» non si digita più, lo calcola il server.
  // Sparisce quindi lo stato locale del campo e la sua mutation.
  const [explainOpen, setExplainOpen] = React.useState(false)

  const addMutation = useMutation({
    mutationFn: () =>
      createOrderLine(projectId, {
        orderRef: "",
        orderPosition: "",
        amount: null,
      }),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  // «—» se nessuna riga ha un importo: 0,00 € direbbe «l'ordine vale zero».
  const orderTotal = lines.some((l) => l.amount != null)
    ? lines.reduce((sum, l) => sum + (l.amount ?? 0), 0)
    : null

  return (
    <GridScroller className="rounded-lg border">
      <Table className="table-fixed">
        <TableHeader className="bg-muted/40">
          <TableRow className="hover:bg-transparent">
            <TableHead className="text-xs">Ordine</TableHead>
            <TableHead className="w-32 text-xs">Posizione</TableHead>
            <TableHead className="w-48 text-xs">Importo</TableHead>
            <TableHead className="w-24" aria-label="Azioni" />
          </TableRow>
        </TableHeader>
        <TableBody>
          {lines.map((line) => (
            <OrderLineRow
              key={line.id}
              projectId={projectId}
              line={line}
              canDelete={lines.length > 1}
              onChanged={onChanged}
            />
          ))}
          <TableRow className="hover:bg-transparent">
            <TableCell colSpan={4} className="py-1.5">
              <Button
                variant="outline"
                size="sm"
                className="h-7 text-xs"
                disabled={addMutation.isPending}
                onClick={() => addMutation.mutate()}
              >
                <Plus className="size-3.5 mr-1" />
                Inserisci riga
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
        <TableFooter>
          <TableRow className="hover:bg-transparent">
            <TableCell colSpan={2} className="text-xs font-semibold uppercase tracking-wide">
              Totale Ordine
            </TableCell>
            <TableCell className="text-right font-semibold tabular-nums">
              {euro(orderTotal)}
            </TableCell>
            <TableCell />
          </TableRow>
          <TableRow className="hover:bg-transparent">
            <TableCell colSpan={2} className="text-xs">
              <span className="font-semibold uppercase tracking-wide">
                Totale Costi
              </span>
              <span className="ml-2 text-[10px] font-normal text-muted-foreground">
                colonna Netti di tutte le sezioni + trasferta
              </span>
            </TableCell>
            <TableCell className="text-right font-semibold tabular-nums">
              {euro(economic?.totalBudgetNetCost ?? null)}
            </TableCell>
            <TableCell />
          </TableRow>
          <TableRow className="hover:bg-transparent">
            <TableCell colSpan={2} className="text-xs">
              <span className="font-semibold uppercase tracking-wide">
                Totale Costi di Vendita
              </span>
              <span className="ml-2 text-[10px] font-normal text-muted-foreground">
                colonna Vendita di tutte le sezioni + trasferta
              </span>
            </TableCell>
            <TableCell className="text-right font-semibold tabular-nums">
              {euro(economic?.saleTotal ?? null)}
            </TableCell>
            <TableCell />
          </TableRow>
          <TableRow className="hover:bg-transparent">
            <TableCell colSpan={2} className="text-xs font-semibold uppercase tracking-wide">
              Margine di Sicurezza
              <span className="ml-2 text-[10px] font-normal normal-case text-muted-foreground">
                Ordine − Totale Costi di Vendita
              </span>
            </TableCell>
            <TableCell
              className={cn(
                "text-right font-semibold tabular-nums",
                signClass(economic?.orderDelta)
              )}
            >
              {euro(economic?.orderDelta ?? null)}
            </TableCell>
            <TableCell className="text-right">
              {economic ? (
                <Button
                  variant="ghost"
                  size="icon-sm"
                  title="Come si calcola il Margine di Sicurezza"
                  onClick={() => setExplainOpen(true)}
                >
                  <ChevronRight className="size-4" />
                </Button>
              ) : null}
            </TableCell>
          </TableRow>
        </TableFooter>
      </Table>

      {economic ? (
        <SafetyMarginDialog
          open={explainOpen}
          economic={economic}
          orderTotal={orderTotal}
          onClose={() => setExplainOpen(false)}
        />
      ) : null}
    </GridScroller>
  )
}

/**
 * Finestra «Margine di Sicurezza» (segnalazione #34): scompone il calcolo riga per riga e
 * dice che cosa il numero significa.
 *
 * Paolo lo definisce «l'importo effettivo di Contingency da gestire». La finestra lo scrive,
 * ma tiene i due concetti distinti: la Contingency della Scheda Prezzi è una **percentuale**
 * di imprevisti calcolata sul costo di vendita, questo è un **importo** che esce dal confronto
 * con l'ordine. Equipararli a video farebbe sparire una distinzione che serve.
 */
function SafetyMarginDialog({
  open,
  economic,
  orderTotal,
  onClose,
}: {
  open: boolean
  economic: BvaEconomicSummary
  orderTotal: number | null
  onClose: () => void
}) {
  const sale = economic.saleTotal
  const margin = economic.orderDelta

  const Row = ({
    label,
    value,
    strong,
    sign,
  }: {
    label: string
    value: number | null
    strong?: boolean
    sign?: boolean
  }) => (
    <div
      className={cn(
        "flex items-baseline justify-between gap-6 py-1",
        strong && "border-t pt-2 font-semibold"
      )}
    >
      <span className={strong ? undefined : "text-muted-foreground"}>{label}</span>
      <span className={cn("tabular-nums", sign && signClass(value))}>{euro(value)}</span>
    </div>
  )

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Margine di Sicurezza</DialogTitle>
        </DialogHeader>

        <div className="text-sm">
          <Row label="Totale Ordine" value={orderTotal} />
          <Row label="− Totale Costi di Vendita" value={sale} />
          <Row label="= Margine di Sicurezza" value={margin} strong sign />
        </div>

        <div className="space-y-3 text-sm text-muted-foreground">
          <p>
            È quanto resta dell'ordine una volta coperti tutti i costi di vendita
            preventivati: <b>l'importo di contingency effettivamente disponibile</b> da
            gestire durante la commessa.
          </p>
          <p>
            Il «Totale Costi di Vendita» è la somma della colonna Vendita di tutte le sezioni
            di costo, più le lavorazioni officine e la trasferta di preventivo. Lo calcola il
            programma: non è più un importo da digitare.
          </p>
          <p className="rounded-md border bg-muted/30 p-2">
            Da non confondere con la <b>Contingency</b> della Scheda Prezzi, che è un'altra
            cosa: una <b>percentuale</b> di imprevisti applicata al costo di vendita per
            arrivare al prezzo d'offerta. Questo è un <b>importo</b>, e nasce dal confronto
            con l'ordine che il cliente ha davvero firmato.
          </p>
        </div>

        <DialogFooter>
          <Button onClick={onClose}>Chiudi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// ═══════════════════════════════════════════════════════════════
// RIEPILOGO COSTI
// ═══════════════════════════════════════════════════════════════

/**
 * Scomposizione mostrata sotto «Spese Trasferta / indennità» a preventivo.
 *
 * Dopo la rimozione del K (06/08/2026) il totale è la somma secca dei due addendi, quindi
 * questa riga non serve più a giustificare un numero che «non torna»: resta perché sapere
 * quanto pesa l'indennità sul totale è comunque un'informazione utile a chi legge.
 */
function travelBudgetBreakdown(economic: BvaEconomicSummary): string | null {
  const markable = economic.budgetTravelMarkableCost
  const allowance = economic.budgetAllowanceCost
  // Guardia: se il server non manda i due addendi (API più vecchia) si torna all'hint
  // che arriva col DTO invece di scrivere «spese — · indennità —».
  if (!Number.isFinite(markable) || !Number.isFinite(allowance)) return null
  if (markable === 0 && allowance === 0) return null
  return `spese ${euro(markable)} · indennità ${euro(allowance)}`
}

function CostCell({
  value,
  hint,
  onOpenCalc,
}: {
  value: number | null
  hint?: string | null
  /**
   * Se c'è, la cella diventa il pulsante che apre la finestra di calcolo della voce —
   * come nel prototipo V32, dove nessuna voce del Riepilogo è un campo libero.
   */
  onOpenCalc?: () => void
}) {
  return (
    <TableCell className="text-right tabular-nums align-top">
      {onOpenCalc ? (
        <Button
          variant="ghost"
          size="sm"
          className="-my-1 h-auto py-1 font-normal tabular-nums"
          onClick={onOpenCalc}
        >
          <Calculator className="size-3.5 text-muted-foreground" />
          {euro(value)}
        </Button>
      ) : (
        <div>{euro(value)}</div>
      )}
      {hint ? (
        <div className="text-[10px] font-normal text-muted-foreground">{hint}</div>
      ) : null}
    </TableCell>
  )
}

/**
 * Tabella affiancata «Costi Preventivati | Costi Consuntivati»: le 4 voci, il
 * totale di sezione e la redditività (in € e in %) ripetuti per sezione.
 * Sostituisce il sottotitolo testuale che stava nei due KPI del conto economico.
 */
export function CostSummaryBlock({
  costLines,
  economic,
  onOpenWorkshopCalc,
}: {
  costLines: BvaCostLineDto[]
  economic: BvaEconomicSummary
  /** Apre il calcolo a righe della voce «Lavorazioni Officine» a preventivo. */
  onOpenWorkshopCalc?: () => void
}) {
  // Dalla #54 le officine sono due voci distinte (Esterne / Atec) più l'eventuale terza per
  // le righe senza tipo: la scomposizione a testo sotto il numero non serve più, la fa la
  // tabella. Resta quella della trasferta, che una voce sola ce l'ha ancora.
  const travelBreakdown = travelBudgetBreakdown(economic)

  const budgetTotal = costLines.some((l) => l.budget != null)
    ? costLines.reduce((sum, l) => sum + (l.budget ?? 0), 0)
    : null
  const actualTotal = costLines.some((l) => l.actual != null)
    ? costLines.reduce((sum, l) => sum + (l.actual ?? 0), 0)
    : null

  const hasOrder = economic.orderPrice > 0

  return (
    <GridScroller className="rounded-lg border">
      <Table className="table-fixed">
        <TableHeader className="bg-muted/40">
          <TableRow className="hover:bg-transparent">
            <TableHead className="text-xs">Voce</TableHead>
            <TableHead className="w-52 text-right text-xs">
              Costi Preventivati
            </TableHead>
            <TableHead className="w-52 text-right text-xs">
              Costi Consuntivati
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {costLines.map((line) => (
            <TableRow key={line.key}>
              <TableCell className="align-top">{line.label}</TableCell>
              <CostCell
                value={line.budget}
                hint={
                  line.key === "spese"
                    ? (travelBreakdown ?? (line.budgetHint || null))
                    : line.budgetHint || null
                }
                // Le due voci officine (Esterne e Atec) aprono lo STESSO foglio di calcolo:
                // è uno solo, con dentro le due sezioni. La terza voce («non classificate»)
                // no: quelle righe non stanno in nessuna sezione, non c'è dove portare.
                onOpenCalc={
                  line.key === "lavorazioni_esterne" ||
                  line.key === "lavorazioni_atec"
                    ? onOpenWorkshopCalc
                    : undefined
                }
              />
              <CostCell value={line.actual} hint={line.actualHint || null} />
            </TableRow>
          ))}
        </TableBody>
        <TableFooter>
          <TableRow className="hover:bg-transparent">
            <TableCell className="text-xs font-semibold">Totale Costi</TableCell>
            <TableCell className="text-right font-semibold tabular-nums">
              {euro(budgetTotal)}
            </TableCell>
            <TableCell className="text-right font-semibold tabular-nums">
              {euro(actualTotal)}
            </TableCell>
          </TableRow>
          <TableRow className="hover:bg-transparent">
            <TableCell className="text-xs font-semibold">
              Redditività
              <span className="ml-2 text-[10px] font-normal text-muted-foreground">
                Totale Ordine − Totale Costi
              </span>
            </TableCell>
            <TableCell
              className={cn(
                "text-right font-semibold tabular-nums",
                signClass(hasOrder ? economic.budgetProfitability : null)
              )}
            >
              {euro(hasOrder ? economic.budgetProfitability : null)}
            </TableCell>
            <TableCell
              className={cn(
                "text-right font-semibold tabular-nums",
                signClass(hasOrder ? economic.profitability : null)
              )}
            >
              {euro(hasOrder ? economic.profitability : null)}
            </TableCell>
          </TableRow>
          <TableRow className="hover:bg-transparent">
            <TableCell className="text-xs font-semibold">% Redditività</TableCell>
            <TableCell
              className={cn(
                "text-right font-semibold tabular-nums",
                signClass(hasOrder ? economic.budgetProfitabilityPct : null)
              )}
            >
              {percent(hasOrder ? economic.budgetProfitabilityPct : null)}
            </TableCell>
            <TableCell
              className={cn(
                "text-right font-semibold tabular-nums",
                signClass(hasOrder ? economic.profitabilityPct : null)
              )}
            >
              {percent(hasOrder ? economic.profitabilityPct : null)}
            </TableCell>
          </TableRow>
        </TableFooter>
      </Table>
    </GridScroller>
  )
}
