import * as React from "react"
import { ChevronRight, Printer } from "lucide-react"

import { GridScroller } from "@/components/shared/grid-scroller"
import { Button } from "@/components/ui/button"
import { Collapsible } from "@/components/ui/collapsible"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

import { DdpKpiCard } from "./DdpKpiCard"
import { DdpOverviewPie } from "./DdpOverviewPie"
import { DdpStatusBreakdown } from "./DdpStatusBreakdown"
import {
  barWidthPercent,
  sectionKeyForStatus,
  type DateWindow,
} from "./ddp-sintesi-logic"
import type { DdpViewProps } from "./ddp-view-props"

/**
 * Scheda «Stato DDP» — port della prima pagina del prototipo `Gestione_DDP_New_V41.html`:
 * card KPI di testata, controlli di igiene dati, torta panoramica, card degli stati di
 * avanzamento e ripartizione per stato con la tabella di dettaglio a scomparsa.
 *
 * Le card cliccabili non aprono un overlay come nel prototipo: portano alla sezione
 * corrispondente della scheda «Avanzamento», che è il drill-down del gestionale.
 */
export function DdpStatoView({ model, officina, printKey, goTo }: DdpViewProps) {
  const { kpi } = model
  const [detailOpen, setDetailOpen] = React.useState(false)

  // Denominatore delle percentuali: numero di righe d'ordine (mai un valore economico).
  const total = model.rows.length

  // A video le finestre stanno a 2 cifre d'anno; `kpi.finestra`/`kpi.insFinestra` restano
  // a 4 cifre perché servono alle stampe.
  const winLabel = (win: DateWindow) =>
    win.gg === 0
      ? "n/d"
      : `dal ${formatDateShort(win.dalIso)} al ${formatDateShort(win.alIso)} · ${win.gg} gg`

  return (
    <div className="space-y-4">
      {/* ── KPI di testata ── */}
      <div className="flex flex-wrap gap-2.5">
        <DdpKpiCard
          label="Totale acquisti"
          value={euro(kpi.totValue)}
          valueClassName="text-red-700 dark:text-red-400"
          tone="green"
          width={220}
          hint={`${kpi.count} righe · ${kpi.escluse} escluse (DDP stop)`}
          onOpen={() => goTo("top10")}
        />
        <DdpKpiCard
          label="Numero inserimenti"
          value={kpi.count}
          hint={winLabel(kpi.insFinestraWin)}
        />
        <DdpKpiCard
          label="Finestra consegne"
          value={winLabel(kpi.finestraWin)}
          tone="green"
          small
          width={250}
        />
        {/* Le etichette lunghe vengono troncate dalla card: larghezza su misura. */}
        <DdpKpiCard
          label="Materiale parzialmente consegnato"
          value={kpi.parziali}
          tone="amber"
          width={250}
          hint="parzialmente consegnato o costruito"
          onOpen={() => goTo("avanz", "par")}
        />
        <DdpKpiCard
          label="Materiale consegnato"
          value={kpi.consegnato}
          tone="green"
          hint="consegnate o gestite"
          onOpen={() => goTo("avanz", "del")}
        />
        <DdpKpiCard
          label="In ordine / consegna"
          value={kpi.inConsegna}
          tone="amber"
          hint="da evadere"
          onOpen={() => goTo("avanz", "tab")}
        />
        {/* MIT esiste anche fuori dalle Officine: la card compare solo se ha righe. */}
        {kpi.trattamento > 0 ? (
          <DdpKpiCard
            label="Materiale in trattamento"
            value={kpi.trattamento}
            tone="blue"
            width={200}
            onOpen={() => goTo("avanz", "mit")}
          />
        ) : null}
        <DdpKpiCard
          label="Materiale in ritardo"
          value={kpi.overdue}
          valueClassName={
            kpi.overdue > 0 ? "text-amber-600 dark:text-amber-400" : undefined
          }
          tone="red"
          hint="consegna prevista < oggi"
          onOpen={() => goTo("avanz", "rit")}
        />
      </div>

      {/* ── Igiene dati: refusi sulle date e costi assenti dove servirebbero ── */}
      {kpi.refusiDate > 0 || kpi.costoZero > 0 ? (
        <div className="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-200">
          Controllo dati:{" "}
          {kpi.refusiDate > 0 ? (
            <>
              <strong>{kpi.refusiDate}</strong> data/e di consegna con annualità
              implausibile (probabile refuso).{" "}
            </>
          ) : null}
          {kpi.costoZero > 0 ? (
            <>
              <strong>{kpi.costoZero}</strong> riga/e senza costo unitario in uno
              stato che dovrebbe averlo.
            </>
          ) : null}
        </div>
      ) : null}

      {/* Torta panoramica: gli insiemi arrivano dalle aggregazioni risolte (A2/A9),
          così le pillole non citano mai stati cablati. */}
      <DdpOverviewPie
        bars={model.ripartizione}
        variant={officina ? "officina" : "commercial"}
        delivered={model.sets.delivered}
        stop={model.sets.stop}
        onOpenStatus={(status) => {
          const sez = sectionKeyForStatus(status, model.sets)
          if (sez) goTo("avanz", sez)
          else goTo("avanz")
        }}
        onOpenDelivered={() => goTo("avanz", "del")}
        onOpenStop={() => goTo("avanz", "stop")}
        onOpenInProgress={() => goTo("avanz", "tab")}
      />

      {/* ── Stati Avanzamento ── */}
      <div>
        <div className="mb-2 flex items-center justify-between gap-2">
          <div>
            <h3 className="text-sm font-semibold">Stati Avanzamento</h3>
            <p className="text-xs text-muted-foreground">{model.avanzSub}</p>
          </div>
          <Button variant="outline" size="xs" onClick={() => printKey("stato")}>
            <Printer className="size-3" />
            Stampa PDF
          </Button>
        </div>
        <div className="flex flex-wrap gap-2.5">
          {model.avanzamento.map((card) => (
            <button
              key={card.label}
              type="button"
              title="Apri le righe in Avanzamento"
              onClick={() => goTo("avanz", card.sectionKey)}
              className="w-[132px] rounded-xl border px-3 py-2.5 text-left transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              // Tinte calcolate dai colori di Conf. DDP: restano chiare anche in dark,
              // per questo il testo è a colori fissi e non sui token del tema.
              style={{ backgroundColor: card.bg, borderColor: card.border }}
            >
              <div className="truncate text-[10px] font-semibold text-slate-600">
                {card.label}
              </div>
              <div className="mt-1 text-2xl font-bold tabular-nums text-slate-900">
                {card.count}
              </div>
              <div className="mt-0.5 text-[10px] text-slate-500">
                {card.pctLabel}
              </div>
            </button>
          ))}
        </div>
      </div>

      {/* ── Ripartizione per stato ── */}
      <div className="rounded-xl border bg-card p-4 shadow-xs">
        <div className="mb-3 flex items-start justify-between gap-2">
          <h3 className="text-sm font-semibold">Ripartizione per stato</h3>
          <Button variant="outline" size="xs" onClick={() => printKey("rip")}>
            <Printer className="size-3" />
            Stampa PDF
          </Button>
        </div>

        <DdpStatusBreakdown
          bars={model.ripartizione}
          subtitle={model.ripSub}
          sectionLabel="Ripartizione per stato"
          onOpenStatus={(status) => {
            const sez = sectionKeyForStatus(status, model.sets)
            if (sez) goTo("avanz", sez)
            else goTo("avanz")
          }}
        />

        <div className="mt-4 border-t pt-3">
          <button
            type="button"
            aria-expanded={detailOpen}
            className="flex items-center gap-2 text-sm font-semibold"
            onClick={() => setDetailOpen((open) => !open)}
          >
            <ChevronRight
              className={cn(
                "size-4 text-muted-foreground transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
                detailOpen && "rotate-90"
              )}
            />
            Dettaglio
          </button>
          <Collapsible open={detailOpen}>
            <div className="pt-3">
              <GridScroller className="rounded-lg border">
                <Table className="text-xs">
                  <TableHeader className="bg-muted/50">
                    <TableRow>
                      <TableHead className="h-8 w-20">Codice</TableHead>
                      <TableHead className="h-8">Descrizione stato</TableHead>
                      <TableHead className="h-8 w-24 text-right">
                        N. Righe
                      </TableHead>
                      <TableHead className="h-8 w-[240px]">
                        % sul totale
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {model.ripartizione.map((bar, index) => {
                      const sez = sectionKeyForStatus(bar.key, model.sets)
                      return (
                      <TableRow
                        key={bar.key || `stato-${index}`}
                        className={sez ? "cursor-pointer hover:bg-muted/60" : undefined}
                        title={sez ? "Apri le righe in Avanzamento" : undefined}
                        onClick={
                          sez
                            ? () => goTo("avanz", sez)
                            : undefined
                        }
                      >
                        <TableCell>
                          <span
                            className="inline-flex rounded-full px-2 py-0.5 text-[10px] font-bold leading-tight"
                            style={{ backgroundColor: bar.bg, color: bar.fg }}
                          >
                            {/* Le righe senza causale hanno chiave vuota: a video «ND». */}
                            {bar.key || "ND"}
                          </span>
                        </TableCell>
                        <TableCell>
                          {bar.label || "Stato non valorizzato"}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {bar.count}
                        </TableCell>
                        <TableCell>
                          <div className="flex items-center gap-2">
                            <div className="h-2 min-w-[80px] flex-1 overflow-hidden rounded-full bg-muted">
                              <div
                                className="h-full rounded-full transition-[width] duration-500 ease-out"
                                style={{
                                  width: `${barWidthPercent(bar.fraction, bar.count)}%`,
                                  backgroundColor: bar.bg,
                                }}
                              />
                            </div>
                            <span className="w-12 shrink-0 text-right tabular-nums text-muted-foreground">
                              {bar.pct}
                            </span>
                          </div>
                        </TableCell>
                      </TableRow>
                      )
                    })}
                    {/* Riga TOTALE: senza badge e senza barra, come nel prototipo. */}
                    <TableRow className="bg-muted/40 font-semibold">
                      <TableCell />
                      <TableCell>TOTALE</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {total}
                      </TableCell>
                      <TableCell className="tabular-nums">
                        {total > 0 ? "100,0%" : "—"}
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </GridScroller>
            </div>
          </Collapsible>
        </div>
      </div>
    </div>
  )
}
