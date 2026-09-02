import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { useNavigate, useParams, useSearchParams } from "react-router-dom"
import { ArrowLeft, ArrowLeftRight, FileSpreadsheet, Printer } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { Button } from "@/components/ui/button"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { fetchDdpSummary } from "@/lib/api/ddp-manager"
import { fetchDdpAggregations, fetchDdpStatuses } from "@/lib/api/ddp-config"
import { fetchDdpRows } from "@/lib/api/project-ddp"
import type { DdpRowItem, DdpStatusItem } from "@/lib/api/types"
import { formatDateFull } from "@/lib/date-iso"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"

import { buildSintesiModel, type DdpSintesiModel } from "./ddp-sintesi-logic"
import { exportDdpExcel, printDdpTables } from "./ddp-export"
import {
  buildPrintTable,
  EMPTY_ROW,
  type PrintKey,
  type PrintTableCtx,
} from "./ddp-print-tables"
import { ddpRowToSintesiCells, sintesiTableHeaders } from "./ddp-sintesi-table"
import type { DdpTabKey, DdpViewProps } from "./ddp-view-props"
import { useDdpRowOff } from "./use-ddp-row-off"
import { useMarkDdpSeen } from "./useDdpUpdatedList"
import { DdpStatoView } from "./DdpStatoView"
import { DdpAvanzamentoView } from "./DdpAvanzamentoView"
import { DdpTop10View } from "./DdpTop10View"
import { DdpDestinazioniView } from "./DdpDestinazioniView"
import { DdpMancantiView } from "./DdpMancantiView"
import { DdpDistintaView } from "./DdpDistintaView"

const TABS: { key: DdpTabKey; label: string }[] = [
  { key: "stato", label: "Stato DDP" },
  { key: "avanz", label: "Avanzamento" },
  { key: "top10", label: "Top 10 Costi" },
  { key: "dest", label: "Destinazioni" },
  { key: "mancanti", label: "Dati Mancanti" },
  { key: "distinta", label: "Dati Distinta" },
]

const TAB_KEYS = TABS.map((tab) => tab.key)

/**
 * Sintesi DDP di una commessa — port delle schede di analisi del prototipo
 * `Gestione_DDP_New_V41.html` (Stato DDP, Avanzamento con Stampa Aggregato, Top 10 Costi,
 * Destinazioni, Dati Mancanti) sul modello dati del gestionale: stati della matrice v7 e
 * insiemi presi dalle aggregazioni A1..A9 configurabili, mai da elenchi cablati.
 */
export function DdpSintesiPage() {
  const params = useParams<{ projectId: string }>()
  const [searchParams, setSearchParams] = useSearchParams()
  const navigate = useNavigate()
  const projectId = Number(params.projectId)
  const type = (searchParams.get("type") || "COMMERCIAL").toUpperCase()
  const officina = type === "OFFICINA"

  const tabParam = (searchParams.get("tab") || "stato") as DdpTabKey
  const tab: DdpTabKey = TAB_KEYS.includes(tabParam) ? tabParam : "stato"
  // Sezione da aprire arrivando da una card KPI (drill-down fra schede).
  const focusSection = searchParams.get("sez") ?? undefined

  const goTo = React.useCallback(
    (nextTab: DdpTabKey, sectionId?: string) => {
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          next.set("tab", nextTab)
          if (sectionId) next.set("sez", sectionId)
          else next.delete("sez")
          return next
        },
        { replace: false }
      )
    },
    [setSearchParams]
  )

  // Menu «Colonne» delle tabelle righe. Chiave separata per tipo di DDP (le colonne
  // sono diverse) e versionata: cambiando le colonne una config vecchia nasconderebbe
  // celle che non esistono più.
  const sintesiHeaders = sintesiTableHeaders(officina)
  const [visibleCols, setVisibleCols] = usePersistedColumnVisibility(
    `ddp-sintesi-columns-${officina ? "OFFICINA" : "COMMERCIAL"}-v2`,
    Object.fromEntries(sintesiHeaders.map((header) => [header, true]))
  )
  const columnToggles = sintesiHeaders.map((header) => ({
    id: header,
    label: header,
    checked: visibleCols[header] ?? true,
    onToggle: (value: boolean) =>
      setVisibleCols((prev) => ({ ...prev, [header]: value })),
  }))

  const [collapsedParentIds, setCollapsedParentIds] = React.useState<Set<number>>(
    new Set()
  )
  React.useEffect(() => {
    setCollapsedParentIds(new Set())
  }, [projectId, type])

  const toggleParentCollapse = React.useCallback((parentId: number) => {
    setCollapsedParentIds((prev) => {
      const next = new Set(prev)
      if (next.has(parentId)) next.delete(parentId)
      else next.add(parentId)
      return next
    })
  }, [])

  const rowsQuery = useQuery({
    queryKey: ["ddp-rows", projectId, type],
    queryFn: () => fetchDdpRows(projectId, type),
    enabled: Number.isFinite(projectId),
    staleTime: 0,
  })
  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
    staleTime: 0,
  })
  const aggregationsQuery = useQuery({
    queryKey: ["ddp-aggregations"],
    queryFn: fetchDdpAggregations,
    staleTime: 0,
  })
  const summaryQuery = useQuery({
    queryKey: ["ddp-summary"],
    queryFn: fetchDdpSummary,
    staleTime: 0,
  })

  const rowOff = useDdpRowOff(projectId, type)

  // Presa visione (#114): aprire la DDP la toglie dall'elenco «da verificare» della
  // Dashboard di chi guarda. Torna se un collega la aggiorna ancora.
  useMarkDdpSeen(projectId, type)

  useProjectHub(Number.isFinite(projectId) ? projectId : null, (change) => {
    void summaryQuery.refetch()
    if (change.ddpType && change.ddpType.toUpperCase() !== type) return
    void rowsQuery.refetch()
    // Anche le righe spente sono un dato condiviso: vanno rilette come le righe.
    rowOff.refetch()
  })

  const statusDefs = React.useMemo(() => {
    const map = new Map<string, DdpStatusItem>()
    for (const status of statusesQuery.data ?? []) map.set(status.statusKey, status)
    return map
  }, [statusesQuery.data])

  const aggSets = React.useMemo(() => {
    const map = new Map<string, Set<string>>()
    for (const agg of aggregationsQuery.data ?? [])
      map.set(agg.code, new Set(agg.statusKeys))
    return map
  }, [aggregationsQuery.data])

  const model: DdpSintesiModel = React.useMemo(
    () =>
      buildSintesiModel({
        rows: rowsQuery.data ?? [],
        statusDefs,
        aggSets,
      }),
    [rowsQuery.data, statusDefs, aggSets]
  )

  const statoLabel = React.useCallback(
    (key: string) => statusDefs.get(key)?.label ?? key,
    [statusDefs]
  )

  const meta =
    summaryQuery.data?.find(
      (item) => item.projectId === projectId && item.ddpType.toUpperCase() === type
    ) ?? summaryQuery.data?.find((item) => item.projectId === projectId)
  const code = meta?.code ?? `#${projectId}`
  const customer = meta?.customerName ?? ""
  const title = meta?.title?.trim() ?? ""
  // #146 (Zanoni): dopo il numero di commessa la sua descrizione, poi il cliente.
  const reportHeader = [title ? `${code} · ${title}` : code, customer]
    .filter(Boolean)
    .join(" — ")

  const otherDdpType = officina ? "COMMERCIAL" : "OFFICINA"
  const otherSummary = React.useMemo(
    () =>
      summaryQuery.data?.find(
        (item) =>
          item.projectId === projectId &&
          item.ddpType.toUpperCase() === otherDdpType &&
          item.totalRows > 0
      ),
    [summaryQuery.data, projectId, otherDdpType]
  )

  // ── Stampe ──
  const printCtx: PrintTableCtx = React.useMemo(
    () => ({
      model,
      officina,
      statoLabel,
      statusDefs,
      onlyOn: rowOff.onlyOn,
    }),
    [model, officina, statoLabel, statusDefs, rowOff.onlyOn]
  )

  const printKey = React.useCallback(
    (key: PrintKey) => {
      const table = buildPrintTable(key, printCtx)
      printDdpTables(
        `${table.title} — ${code}`,
        `${reportHeader} · data di riferimento ${formatDateFull(new Date())}`,
        [table]
      )
    },
    [printCtx, code, reportHeader]
  )

  const printAggregato = React.useCallback(
    (keys: PrintKey[], reportTitle = "Report Aggregato Avanzamento") => {
      const tables = keys.map((key) => buildPrintTable(key, printCtx))
      // Le tabelle vuote stampano la riga segnaposto «Nessuna riga.»: non è un dato e
      // non va contata, altrimenti il totale in testata non torna col dialogo.
      const totRows = tables.reduce(
        (sum, table) => sum + (table.rows[0] === EMPTY_ROW ? 0 : table.rows.length),
        0
      )
      printDdpTables(
        `${reportTitle} — ${code}`,
        `${reportHeader} · ${tables.length} ${
          tables.length === 1 ? "tabella" : "tabelle"
        } · ${totRows} righe complessive · data di riferimento ${formatDateFull(
          new Date()
        )}`,
        tables
      )
    },
    [printCtx, code, reportHeader]
  )

  /** Bottone «Stampa» di testata: riepilogo dell'intera Sintesi, non della sola scheda. */
  function printReport() {
    printAggregato(
      ["stato", "rip", "top10", "dest", "mancanti"],
      officina ? "Riepilogo Sintesi DDP Meccanica" : "Riepilogo Sintesi DDP commerciali"
    )
  }

  function exportExcel() {
    const fullRow = (row: DdpRowItem) =>
      ddpRowToSintesiCells(row, officina, statoLabel)
    exportDdpExcel(code, {
      title: "Dati Distinta",
      headers: [...sintesiHeaders],
      rows: model.distinta.map(fullRow),
      rowColors: model.distinta.map(
        (row) => statusDefs.get(row.itemStatus)?.colorBg ?? null
      ),
    })
  }

  const viewProps: DdpViewProps = {
    model,
    officina,
    statusDefs,
    statoLabel,
    visibleCols,
    rowOff,
    printKey,
    goTo,
    focusSection,
    code,
    reportHeader,
    collapsedParentIds,
    parentIdsWithChildren: model.parentIdsWithChildren,
    toggleParentCollapse,
  }

  return (
    <div className="flex flex-col gap-4">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-3">
          <Button variant="outline" size="sm" onClick={() => navigate("/gestore-ddp")}>
            <ArrowLeft />
            Indietro
          </Button>
          <span className="text-base font-semibold">
            {officina
              ? "Sintesi DDP Meccanica"
              : "Sintesi DDP commerciali"}{" "}
            · {reportHeader}
          </span>
        </div>
        <div className="flex flex-wrap gap-2">
          {otherSummary ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() => navigate(`/gestore-ddp/${projectId}?type=${otherDdpType}`)}
            >
              <ArrowLeftRight />
              {officina ? "Sintesi commerciale" : "Sintesi meccanica"}
            </Button>
          ) : null}
          <ColumnsMenu columns={columnToggles} />
          <Button variant="outline" size="sm" onClick={exportExcel}>
            <FileSpreadsheet />
            Esporta Excel
          </Button>
          <Button variant="outline" size="sm" onClick={printReport}>
            <Printer />
            Stampa
          </Button>
        </div>
      </div>

      {/* Schede */}
      <Tabs value={tab} onValueChange={(value) => goTo(value as DdpTabKey)}>
        <TabsList className="w-full justify-start overflow-x-auto">
          {TABS.map((item) => (
            <TabsTrigger key={item.key} value={item.key}>
              {item.label}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      {rowsQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : rowsQuery.isError ? (
        <p className="text-sm text-destructive">
          {(rowsQuery.error as Error).message}
        </p>
      ) : tab === "stato" ? (
        <DdpStatoView {...viewProps} />
      ) : tab === "avanz" ? (
        <DdpAvanzamentoView {...viewProps} onPrintAggregato={printAggregato} />
      ) : tab === "top10" ? (
        <DdpTop10View {...viewProps} />
      ) : tab === "dest" ? (
        <DdpDestinazioniView {...viewProps} />
      ) : tab === "mancanti" ? (
        <DdpMancantiView {...viewProps} />
      ) : (
        <DdpDistintaView {...viewProps} />
      )}
    </div>
  )
}
