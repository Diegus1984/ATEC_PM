import * as React from "react"
import { Printer } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { formatDateShort } from "@/lib/date-iso"

import { DdpKpiCard } from "./DdpKpiCard"
import { DdpRowsTable } from "./DdpRowsTable"
import { DdpSection } from "./DdpSection"
import { DdpStampaAggregatoDialog } from "./DdpStampaAggregatoDialog"
import type { PrintKey } from "./ddp-print-tables"
import type { DdpSintesiModel } from "./ddp-sintesi-logic"
import type { DdpViewProps } from "./ddp-view-props"

/** Preferenza personale (non è un dato di commessa): resta in localStorage. */
const MULTI_OPEN_KEY = "ddp.sintesi.multiOpen"

function readMultiOpen(): boolean {
  try {
    return localStorage.getItem(MULTI_OPEN_KEY) === "1"
  } catch {
    return false
  }
}

/**
 * Percentuale di righe a magazzino sul totale. Si riusa quella già calcolata dal blocco
 * «Stati Avanzamento» quando c'è, così la card qui e la card della scheda «Stato DDP»
 * non possono mostrare due decimali diversi sullo stesso numero.
 */
function magazzinoPctLabel(model: DdpSintesiModel): string {
  const card = model.avanzamento.find((item) => item.label === "MAT. A MAGAZZINO")
  if (card) return card.pctLabel
  const total = model.rows.length
  if (total <= 0) return "—"
  const pct = ((model.kpi.magazzino / total) * 100).toLocaleString("it-IT", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 1,
  })
  return `${pct}% su Tot.`
}

/**
 * Scheda «Avanzamento» della Sintesi DDP (port della pagina omonima del prototipo V41):
 * card di sintesi + una sezione a fisarmonica per ogni stato/insieme, con le righe che
 * si possono spegnere per escluderle dalle stampe. Le sezioni e il loro ordine arrivano
 * già pronti da `model.sezioni`: qui non si filtra e non si riordina nulla.
 */
export function DdpAvanzamentoView({
  model,
  officina,
  statusDefs,
  statoLabel,
  visibleCols,
  rowOff,
  printKey,
  focusSection,
  onPrintAggregato,
}: DdpViewProps & { onPrintAggregato: (keys: PrintKey[]) => void }) {
  const { kpi, sezioni } = model
  const win = kpi.finestraWin
  // Nessuna riga datata da evadere: la finestra non esiste, non vale zero.
  const noWindow = win.gg === 0

  const [printOpen, setPrintOpen] = React.useState(false)
  const [multiOpen, setMultiOpen] = React.useState(readMultiOpen)
  // Le sezioni nascono tutte chiuse, come nel prototipo.
  const [openSections, setOpenSections] = React.useState<Set<string>>(new Set())

  React.useEffect(() => {
    try {
      localStorage.setItem(MULTI_OPEN_KEY, multiOpen ? "1" : "0")
    } catch {
      /* preferenza non essenziale: se lo storage è pieno o negato si tira dritto */
    }
  }, [multiOpen])

  const toggleSection = React.useCallback(
    (id: string) => {
      setOpenSections((prev) => {
        // Con «Più sezioni aperte» spenta si riparte da zero: ne resta aperta una sola.
        const next = new Set(multiOpen ? prev : [])
        if (prev.has(id)) next.delete(id)
        else next.add(id)
        return next
      })
    },
    [multiOpen]
  )

  // Drill-down dalle card di «Stato DDP» (?sez=…): la sezione si apre e si porta a vista
  // una volta sola, altrimenti ogni refresh del modello rimbalzerebbe lo scroll.
  const focusedRef = React.useRef<string | null>(null)
  React.useEffect(() => {
    if (!focusSection || focusedRef.current === focusSection) return
    if (!sezioni.some((section) => section.key === focusSection)) return
    focusedRef.current = focusSection
    setOpenSections((prev) =>
      multiOpen ? new Set([...prev, focusSection]) : new Set([focusSection])
    )
    // Lo scroll parte dopo il frame di apertura, quando la sezione ha già la sua altezza.
    const frame = window.requestAnimationFrame(() => {
      document
        .getElementById(`ddp-section-${focusSection}`)
        ?.scrollIntoView({ behavior: "smooth", block: "start" })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [focusSection, sezioni, multiOpen])

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-semibold">Avanzamento</span>
        <div className="flex flex-wrap items-center gap-3">
          <div className="flex items-center gap-2">
            <Checkbox
              id="ddp-avanz-multiopen"
              checked={multiOpen}
              onCheckedChange={(checked) => setMultiOpen(checked === true)}
            />
            <Label
              htmlFor="ddp-avanz-multiopen"
              className="cursor-pointer text-xs font-normal text-muted-foreground"
            >
              Più sezioni aperte
            </Label>
          </div>
          <Button variant="outline" size="sm" onClick={() => setPrintOpen(true)}>
            <Printer />
            Stampa Aggregato
          </Button>
        </div>
      </div>

      {/* Card di sintesi della scheda */}
      <div className="flex flex-wrap gap-2">
        <DdpKpiCard
          label="Materiale in consegna"
          value={kpi.inConsegna}
          hint={`${kpi.datedCount} con data · ${kpi.noDateInTransit} senza data`}
          tone="red"
        />
        <DdpKpiCard
          label="Mat. par. cons."
          value={kpi.parziali}
          hint="Consegnato o costruito in parte"
          tone="amber"
        />
        <DdpKpiCard label="Attesa consegna" tone="red">
          <div className="mt-1 space-y-0.5">
            {/* A video le date stanno a 2 cifre d'anno (regola di progetto): il 4 cifre
                di `win.dal`/`win.al` è riservato a stampe ed export. */}
            <WindowRow
              label="Dal"
              value={noWindow ? "n/d" : formatDateShort(win.dalIso)}
            />
            <WindowRow
              label="Al"
              value={noWindow ? "n/d" : formatDateShort(win.alIso)}
            />
            <WindowRow label="GG." value={noWindow ? "n/d" : String(win.gg)} />
          </div>
        </DdpKpiCard>
        {kpi.trattamento > 0 ? (
          <DdpKpiCard
            label="Mat. in trattamento"
            value={kpi.trattamento}
            hint="Presso il fornitore di trattamento"
            tone="blue"
          />
        ) : null}
        <DdpKpiCard
          label="Materiale consegnato"
          value={kpi.consegnato}
          hint="Consegnato o gestito"
          tone="green"
        />
        <DdpKpiCard
          label="Mat. a magazzino"
          value={kpi.magazzino}
          hint={`${magazzinoPctLabel(model)} · parziali inclusi`}
          tone="green"
        />
        <DdpKpiCard
          label="Assegnato"
          value={kpi.assegnato}
          hint="Assegnato al montaggio"
        />
      </div>

      {/* Una sezione per stato/insieme, nell'ordine canonico del tipo di DDP */}
      <div className="space-y-2">
        {sezioni.map((section) => {
          const rowIds = section.rows.map((row) => row.id)
          // Contate sulle righe REALMENTE in sezione: le sezioni cambiano composizione
          // (stato, data) e sul DB possono restare spegnimenti di righe uscite di lì.
          const off =
            section.rows.length - rowOff.onlyOn(section.key, section.rows).length
          return (
            <DdpSection
              key={section.key}
              id={section.key}
              title={section.title}
              // Il contatore è sul totale: spegnere righe cambia solo le stampe.
              count={section.rows.length}
              open={openSections.has(section.key)}
              onToggle={toggleSection}
              onPrint={() => printKey(section.key)}
              actions={
                <>
                  {off > 0 ? (
                    <span className="text-[11px] tabular-nums text-muted-foreground">
                      {off} {off === 1 ? "spenta" : "spente"}
                    </span>
                  ) : null}
                  <Button
                    variant="outline"
                    size="xs"
                    disabled={rowIds.length === 0}
                    onClick={() => rowOff.setAll(section.key, rowIds, false)}
                  >
                    Tutte accese
                  </Button>
                  <Button
                    variant="outline"
                    size="xs"
                    disabled={rowIds.length === 0}
                    onClick={() => rowOff.setAll(section.key, rowIds, true)}
                  >
                    Tutte spente
                  </Button>
                </>
              }
            >
              <DdpRowsTable
                rows={section.rows}
                statusDefs={statusDefs}
                statoLabel={statoLabel}
                officina={officina}
                visibleCols={visibleCols}
                markOverdue={section.dueRed}
                today={model.today}
                emptyText={section.emptyText}
                rowOff={{
                  isOff: (id) => rowOff.isOff(section.key, id),
                  toggle: (id, value) => rowOff.toggle(section.key, id, value),
                }}
                // Niente collasso qui: nelle sezioni le righe sono già quelle contabili
                // (i padri di composizione non ci sono), quindi non ci sarebbe modo di
                // riaprire un padre chiuso altrove e la vista divergerebbe dal PDF.
              />
              <p className="mt-2 text-xs text-muted-foreground">{section.note}</p>
            </DdpSection>
          )
        })}
      </div>

      <DdpStampaAggregatoDialog
        open={printOpen}
        onOpenChange={setPrintOpen}
        sezioni={sezioni}
        isOff={rowOff.isOff}
        onConfirm={onPrintAggregato}
      />
    </div>
  )
}

/** Riga della card «Attesa consegna»: etichetta a sinistra, valore a destra. */
function WindowRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-2">
      <span className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        {label}
      </span>
      <span className="text-xs font-bold tabular-nums">{value}</span>
    </div>
  )
}
