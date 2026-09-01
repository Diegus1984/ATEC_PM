import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  AlertTriangle,
  FileCheck2,
  Package,
  RefreshCw,
  Search,
  ShoppingCart,
  X,
} from "lucide-react"

import { DaneaOrderDialog } from "@/components/shared/danea-order-dialog"
import { Button } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { CatalogAtecAssignDialog } from "@/features/catalogo/CatalogAtecAssignDialog"
import { CodexDaneaMappingDialog } from "@/features/codex/CodexDaneaMappingDialog"
import { fetchCodex } from "@/lib/api/codex"
import { DdpStatusFilterBar } from "@/features/commesse/DdpStatusFilterBar"
import { DdpStatusLegend } from "@/features/commesse/DdpStatusLegend"
import { ddpCommercialRowToSaveRequest } from "@/features/commesse/ddp-commercial-row"
import { ddpTransitionsPerUtente } from "@/features/commesse/ddp-constants"
import { fetchAcquistiInbox } from "@/lib/api/ddp-commercial-inbox"
import {
  buildDdpTransitionMap,
  fetchDdpStatuses,
  fetchDdpStatusTransitions,
} from "@/lib/api/ddp-config"
import { updateDdpRow } from "@/lib/api/project-ddp"
import { fetchPurchaseRfqs } from "@/lib/api/purchase-rfqs"
import type {
  AcquistiInboxItem,
  CodexListItem,
  PurchaseRfqListItem,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { notifyError, notifyInfo } from "@/lib/toast"
import { useDeferredItemOrder } from "@/lib/use-deferred-item-order"
import { cn } from "@/lib/utils"

import { buildAcquistiColumns } from "./acquisti-columns"
import { AcquistiProjectCard } from "./AcquistiProjectCard"
import { KpiCard } from "./acquisti-ui"
import {
  buildProjectGroups,
  buildStatusCounts,
  isToBuy,
  isVisible,
  normalizeAtec,
  rowHasDaneaOrder,
  sortAcquistiByProjectAndAction,
  statusOf,
  VISIBLE_STATUSES,
} from "./acquisti-shared"
import { CreateRfqDialog } from "./CreateRfqDialog"
import { ProjectDaneaOrdersDialog } from "./ProjectDaneaOrdersDialog"
import { RfqDetailDialog } from "./RfqDetailDialog"

// Predicati delle card KPI cliccabili: ogni card filtra le griglie con lo STESSO
// criterio con cui conta, così card e griglia non possono divergere. «inGara»
// esclude le righe già ordinate (nella colonna Prossimo Passo l'ordine vince
// sulla gara: il filtro deve mostrare le stesse righe che portano quel badge).
const KPI_PREDICATES = {
  daComprare: (i: AcquistiInboxItem) => isToBuy(i),
  inOrdine: (i: AcquistiInboxItem) => rowHasDaneaOrder(i),
  inGara: (i: AcquistiInboxItem) =>
    !rowHasDaneaOrder(i) && (i.inActiveRfq || statusOf(i) === "RO"),
  senzaCodice: (i: AcquistiInboxItem) => !normalizeAtec(i.atecCode),
} as const
type KpiFilterKey = keyof typeof KPI_PREDICATES

const KPI_FILTER_LABELS: Record<KpiFilterKey, string> = {
  daComprare: "Da comprare",
  inOrdine: "In ordine Danea",
  inGara: "In gara",
  senzaCodice: "Senza codice ATEC",
}

export function AcquistiPage() {
  const queryClient = useQueryClient()

  const [searchQuery, setSearchQuery] = React.useState("")
  const [selectedStatusKeys, setSelectedStatusKeys] = React.useState<Set<string>>(new Set())
  // Filtro della card KPI cliccata (null = nessuno). Si toglie ri-cliccando la card.
  const [kpiFilter, setKpiFilter] = React.useState<KpiFilterKey | null>(null)
  /**
   * Le righe sono ordinate per «Prossimo Passo», che cambia appena si tocca lo stato
   * o si aggancia una RDO: senza questo l'articolo salterebbe in un altro punto della
   * card al primo refetch. L'ordine si ricalcola solo con «Aggiorna» o cambiando filtri.
   */
  const [layoutEpoch, setLayoutEpoch] = React.useState(0)

  // Dialogs
  const [assignDialogItem, setAssignDialogItem] = React.useState<AcquistiInboxItem | null>(
    null
  )
  /** Codice ATEC (o 201 del grezzo) senza articoli: apre CodexDaneaMappingDialog. */
  const [codexMappingTarget, setCodexMappingTarget] =
    React.useState<CodexListItem | null>(null)

  // Catena ambra / pillola grezzo (01/09/2026): dal codice al dialog di associazione —
  // la riga porta solo il CODICE, la riga Codex si ripesca dall'archivio (stesso
  // pattern delle DDP di commessa).
  const apriAssociaCodice = React.useCallback(async (codice: string) => {
    const raw = (codice ?? "").replace(/\./g, "").trim()
    if (!raw) return
    try {
      const page = await fetchCodex({ search: raw, pageSize: 20 })
      const codex =
        page.items.find(
          (i) =>
            i.codice.replace(/\./g, "") === raw ||
            (i.codiceNuovo ?? "").replace(/\./g, "") === raw
        ) ?? null
      if (!codex) {
        notifyError(`Codice ${codice} non trovato nel Codex.`)
        return
      }
      setCodexMappingTarget(codex)
    } catch (err) {
      notifyError(err)
    }
  }, [])
  const [selectedRfqDetailId, setSelectedRfqDetailId] = React.useState<number | null>(null)
  const [orderProject, setOrderProject] = React.useState<{
    projectId: number
    projectCode: string
  } | null>(null)
  const [daneaOrderIdDoc, setDaneaOrderIdDoc] = React.useState<number | null>(null)
  // Rif. Danea a mano senza IdDoc (migrazione): il popup cerca per numero.
  const [daneaOrderRef, setDaneaOrderRef] = React.useState<string | null>(null)
  const [createRfqTargetItems, setCreateRfqTargetItems] = React.useState<
    AcquistiInboxItem[] | null
  >(null)

  // ── Queries ───────────────────────────────────────────────────
  const { data: rawItems = [], isRefetching, refetch } = useQuery({
    queryKey: ["acquisti-inbox"],
    queryFn: () => fetchAcquistiInbox(),
    staleTime: 10_000,
  })

  const { data: rfqs = [] } = useQuery({
    queryKey: ["purchase-rfqs"],
    queryFn: () => fetchPurchaseRfqs(),
    staleTime: 10_000,
  })

  const { data: statuses = [] } = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })

  const { data: transitions = [] } = useQuery({
    queryKey: ["ddp-status-transitions"],
    queryFn: fetchDdpStatusTransitions,
  })

  const statusMap = React.useMemo(
    () => new Map(statuses.map((s) => [s.statusKey, s])),
    [statuses]
  )

  // `undefined` = finestra completa: è così che chi ha il privilegio #140 vede tutti gli stati.
  const transitionMap = React.useMemo(
    () => ddpTransitionsPerUtente(buildDdpTransitionMap(transitions, "COMMERCIAL")),
    [transitions]
  )

  /** Liste di pagina da rinfrescare dopo ogni scrittura (inbox + elenco RDO). */
  const invalidateLists = React.useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ["acquisti-inbox"] })
    queryClient.invalidateQueries({ queryKey: ["purchase-rfqs"] })
  }, [queryClient])

  // SignalR
  useProjectHub(
    "all",
    () => {
      queryClient.invalidateQueries({ queryKey: ["acquisti-inbox"] })
    },
    () => {
      queryClient.invalidateQueries({ queryKey: ["purchase-rfqs"] })
    }
  )

  // ── Filtri e derivate ─────────────────────────────────────────
  // Righe visibili grezze: base condivisa da griglie e barra filtri (una sola scansione).
  const visibleRawItems = React.useMemo(() => rawItems.filter(isVisible), [rawItems])

  const preKpiItems = React.useMemo(() => {
    let res = visibleRawItems
    if (selectedStatusKeys.size > 0) {
      res = res.filter((i) => selectedStatusKeys.has(i.itemStatus ?? ""))
    }
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase()
      res = res.filter(
        (i) =>
          i.description?.toLowerCase().includes(q) ||
          i.partNumber?.toLowerCase().includes(q) ||
          i.atecCode?.toLowerCase().includes(q) ||
          i.supplierName?.toLowerCase().includes(q) ||
          i.projectCode?.toLowerCase().includes(q) ||
          i.customerName?.toLowerCase().includes(q)
      )
    }
    return res
  }, [visibleRawItems, selectedStatusKeys, searchQuery])

  // Il filtro della card si applica DOPO i conteggi: le card contano sempre su
  // stato+ricerca, così i numeri non cambiano quando se ne clicca una.
  const visibleItems = React.useMemo(
    () => (kpiFilter ? preKpiItems.filter(KPI_PREDICATES[kpiFilter]) : preKpiItems),
    [preKpiItems, kpiFilter]
  )

  const toggleKpiFilter = React.useCallback((k: KpiFilterKey) => {
    setKpiFilter((cur) => (cur === k ? null : k))
  }, [])

  // Righe per commessa PRIMA del filtro card: il «Richiedi RDO» di testata deve
  // lavorare su tutta la commessa anche quando una card KPI sta filtrando le griglie
  // (altrimenti nascerebbe una gara parziale, o un errore che sembra un bug dei dati).
  const preKpiByProject = React.useMemo(() => {
    const map = new Map<number, AcquistiInboxItem[]>()
    for (const i of preKpiItems) {
      const arr = map.get(i.projectId)
      if (arr) arr.push(i)
      else map.set(i.projectId, [i])
    }
    return map
  }, [preKpiItems])

  const statusFilterItems = React.useMemo(
    () => buildStatusCounts(visibleRawItems, statusMap),
    [visibleRawItems, statusMap]
  )

  // KPI: una sola passata su visibleItems invece di quattro filter separati
  // (il totale stimato era inoltre ricalcolato nel JSX a ogni render).
  const kpi = React.useMemo(() => {
    let toBuyCount = 0
    let toBuyCost = 0
    let lateCount = 0
    let unmappedCount = 0
    let orderedCount = 0
    let inGaraCount = 0
    for (const i of preKpiItems) {
      if (isToBuy(i)) {
        toBuyCount++
        toBuyCost += (i.unitCost || 0) * i.quantity
      }
      if ((i.daysLate ?? 0) > 0) lateCount++
      if (!normalizeAtec(i.atecCode)) unmappedCount++
      // Stesso predicato della colonna «Prossimo Passo» (rowHasDaneaOrder): l'ordine
      // Danea avanza lo stato a IO solo se la matrice lo ammette, ma Rif. Danea/IDDoc
      // arrivano comunque — contare il solo stato IO farebbe divergere card e griglia.
      if (rowHasDaneaOrder(i)) orderedCount++
      // Il valore della card «In Gara» usa lo STESSO predicato del filtro: il numero
      // cliccato e le righe mostrate devono coincidere (le gare stanno nel sottotesto).
      if (KPI_PREDICATES.inGara(i)) inGaraCount++
    }
    return { toBuyCount, toBuyCost, lateCount, unmappedCount, orderedCount, inGaraCount }
  }, [preKpiItems])

  const activeRfqsCount = React.useMemo(
    () => rfqs.filter((r) => r.status === "DRAFT" || r.status === "SENT").length,
    [rfqs]
  )

  // RDO indicizzate per commessa: evita una scansione di `rfqs` per ogni gruppo.
  const rfqsByProject = React.useMemo(() => {
    const map = new Map<number, PurchaseRfqListItem[]>()
    for (const r of rfqs) {
      if (r.projectId == null) continue
      const arr = map.get(r.projectId)
      if (arr) arr.push(r)
      else map.set(r.projectId, [r])
    }
    return map
  }, [rfqs])

  // Ordine congelato: `buildProjectGroups` riceve le righe già in fila e non le
  // riordina più (i gruppi-commessa restano ordinati per codice).
  const filtersKey = `${[...selectedStatusKeys].sort().join(",")}|${searchQuery.trim()}|${kpiFilter ?? ""}`
  const orderedItems = useDeferredItemOrder(
    visibleItems,
    sortAcquistiByProjectAndAction,
    layoutEpoch,
    filtersKey
  )
  const groupsByProject = React.useMemo(
    () => buildProjectGroups(orderedItems, rfqsByProject, { keepItemOrder: true }),
    [orderedItems, rfqsByProject]
  )

  // ── Scritture ─────────────────────────────────────────────────
  const updateRowMutation = useMutation({
    mutationFn: (data: {
      projectId: number
      rowId: number
      itemStatus?: string
      unitCost?: number
      dateNeeded?: string | null
    }) => {
      const target = rawItems.find((i) => i.id === data.rowId)
      if (!target) throw new Error("Riga non trovata")
      const req = ddpCommercialRowToSaveRequest(data.projectId, target, {
        itemStatus: data.itemStatus ?? target.itemStatus,
        // Solo se il chiamante lo passa davvero: la presenza dell'override accende
        // `updateUnitCost` lato server (altrimenti il costo della riga resta quello a DB).
        unitCost: data.unitCost,
        dateNeeded: data.dateNeeded !== undefined ? data.dateNeeded : target.dateNeeded,
      })
      return updateDdpRow(data.projectId, data.rowId, req)
    },
    onSuccess: (_res, variables) => {
      queryClient.invalidateQueries({ queryKey: ["acquisti-inbox"] })
      queryClient.invalidateQueries({ queryKey: ["purchase-rfq-detail"] })
      // Il nuovo stato porta la riga fuori dai VISIBLE_STATUSES dell'Inbox:
      // senza avviso l'utente la vede solo sparire.
      const newStatus = variables.itemStatus?.toUpperCase()
      if (newStatus && !VISIBLE_STATUSES.has(newStatus)) {
        notifyInfo(
          "Stato aggiornato: la riga esce dall'Inbox Acquisti (la ritrovi nella DDP della commessa)."
        )
      } else {
        notifyInfo("Dati articolo aggiornati")
      }
    },
    onError: (err: Error) => {
      notifyError(`Errore aggiornamento: ${err.message}`)
    },
  })

  // Apertura del dialog RDO: solo articoli nello stato DO (Da ORDINARE) e non già
  // dentro una gara viva — il server li scarterebbe comunque («già in gara»), quindi
  // l'anteprima non deve contarli né prometterci sopra una RDO.
  const handleOpenRfqModal = React.useCallback((items: AcquistiInboxItem[]) => {
    const doItems = items.filter((i) => statusOf(i) === "DO")
    const liberi = doItems.filter((i) => !i.inActiveRfq)
    if (liberi.length === 0) {
      notifyError(
        doItems.length > 0
          ? `Le righe in "DA ORDINARE" di questa commessa sono già dentro una gara in corso (colonna Prossimo Passo): non serve crearne un'altra.`
          : `In questa commessa nessun articolo è nello stato "DA ORDINARE". Cambia lo stato della riga dal menu della colonna Stato, poi riprova.`
      )
      return
    }
    setCreateRfqTargetItems(liberi)
  }, [])

  // Stile di riga nativo basato sulla configurazione di Conf. DDP (s.colorBg / s.colorFg)
  const rowStyle = React.useCallback(
    (row: AcquistiInboxItem) => {
      const s = statusMap.get(row.itemStatus)
      return s && s.colorBg
        ? { backgroundColor: s.colorBg, color: s.colorFg ?? undefined }
        : undefined
    },
    [statusMap]
  )

  const handleStatusChange = React.useCallback(
    (item: AcquistiInboxItem, statusKey: string) => {
      updateRowMutation.mutate({
        projectId: item.projectId,
        rowId: item.id,
        itemStatus: statusKey,
      })
    },
    [updateRowMutation]
  )

  // Colonne per commessa costruite UNA volta per set di dati: costruirle inline nel
  // JSX le ricreava a ogni render della pagina (anche digitando in un dialog), con
  // il rischio di rimontare le celle e chiudere i popover aperti.
  const columnsByProject = React.useMemo(
    () =>
      new Map(
        groupsByProject.map(
          (g) =>
            [
              g.projectId,
              buildAcquistiColumns({
                gridItems: g.items,
                statuses,
                statusMap,
                transitionMap,
                statusChangePending: updateRowMutation.isPending,
                onStatusChange: handleStatusChange,
                onAssignAtec: setAssignDialogItem,
                onAssociaAtec: (codice) => void apriAssociaCodice(codice),
                onOpenRfqDetail: setSelectedRfqDetailId,
                onOpenDaneaOrder: setDaneaOrderIdDoc,
                onOpenDaneaOrderByRef: setDaneaOrderRef,
                onRequestRfq: handleOpenRfqModal,
              }),
            ] as const
        )
      ),
    [
      groupsByProject,
      statuses,
      statusMap,
      transitionMap,
      updateRowMutation.isPending,
      handleStatusChange,
      apriAssociaCodice,
      handleOpenRfqModal,
    ]
  )

  return (
    <div className="flex h-[calc(100vh-4rem)] w-full overflow-hidden bg-background p-6">
      <div className="flex flex-1 flex-col overflow-y-auto gap-6">
        {/* Header Title & Actions */}
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-foreground flex items-center gap-2">
              <ShoppingCart className="h-6 w-6 text-primary" />
              Inbox Acquisti
            </h1>
            <p className="text-sm text-muted-foreground">
              Vista unificata dello stato acquisti per commessa con evidenziazione automatica
              dei prossimi passi.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setLayoutEpoch((n) => n + 1)
                void refetch()
              }}
              disabled={isRefetching}
              className="gap-2"
              title="Ricarica i dati e rimette le righe in ordine di Prossimo Passo"
            >
              <RefreshCw className={cn("h-4 w-4", isRefetching && "animate-spin")} />
              Aggiorna
            </Button>
          </div>
        </div>

        {/* KPI Summary Cards */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <KpiCard
            label="Da Comprare"
            value={kpi.toBuyCount}
            unit="articoli"
            icon={Package}
            borderClassName="border-l-red-500"
            iconClassName="text-red-500"
            onClick={() => toggleKpiFilter("daComprare")}
            active={kpiFilter === "daComprare"}
          >
            <div>In verifica a magazzino + da ordinare</div>
            <div>
              Tot. Stimato:{" "}
              <span className="font-semibold text-foreground">{euro(kpi.toBuyCost)}</span>
            </div>
          </KpiCard>

          <KpiCard
            label="In Ordine Danea"
            value={kpi.orderedCount}
            unit="articoli"
            icon={ShoppingCart}
            borderClassName="border-l-amber-500"
            iconClassName="text-amber-500"
            onClick={() => toggleKpiFilter("inOrdine")}
            active={kpiFilter === "inOrdine"}
          >
            Ordini già emessi verso fornitori
          </KpiCard>

          <KpiCard
            label="In Gara (RDO)"
            value={kpi.inGaraCount}
            unit="articoli"
            icon={FileCheck2}
            borderClassName="border-l-purple-500"
            iconClassName="text-purple-500"
            onClick={() => toggleKpiFilter("inGara")}
            active={kpiFilter === "inGara"}
          >
            <div>
              {activeRfqsCount === 1 ? "1 gara RDO attiva" : `${activeRfqsCount} gare RDO attive`}
            </div>
            {kpi.lateCount > 0 ? (
              <span className="font-medium text-red-500">
                {kpi.lateCount === 1
                  ? "1 articolo oltre la Data Prevista"
                  : `${kpi.lateCount} articoli oltre la Data Prevista`}
              </span>
            ) : (
              "Nessun articolo oltre la Data Prevista"
            )}
          </KpiCard>

          <KpiCard
            label="Senza Codice ATEC"
            value={kpi.unmappedCount}
            icon={AlertTriangle}
            borderClassName="border-l-indigo-500"
            iconClassName="text-indigo-500"
            onClick={() => toggleKpiFilter("senzaCodice")}
            active={kpiFilter === "senzaCodice"}
          >
            Assegna il codice con l'icona catena nella colonna Cod. ATEC
          </KpiCard>
        </div>

        {/* Barra di Filtro Stati Standard (DdpStatusFilterBar) e Ricerca Rapida */}
        <div className="flex flex-col gap-3">
          <DdpStatusFilterBar
            items={statusFilterItems}
            selected={selectedStatusKeys}
            onChange={setSelectedStatusKeys}
          />

          <div className="flex items-center justify-between bg-card p-3 rounded-lg border shadow-sm gap-3">
            <div className="flex items-center gap-3 flex-1">
              <div className="relative w-full max-w-md">
                <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
                <Input
                  placeholder="Filtra per articolo, codice, fornitore, commessa..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="pl-9 h-9 text-xs"
                />
              </div>
              {kpiFilter && (
                <Button
                  variant="secondary"
                  size="sm"
                  className="h-8 gap-1 text-xs shrink-0"
                  title="Togli il filtro della card"
                  onClick={() => setKpiFilter(null)}
                >
                  Filtro: {KPI_FILTER_LABELS[kpiFilter]}
                  <X className="h-3.5 w-3.5" />
                </Button>
              )}
            </div>
            <DdpStatusLegend statuses={statuses} />
          </div>
        </div>

        {/* LISTA COMMESSE CON TABELLA ED EVIDENZIAZIONE COLORI DA CONF DDP */}
        <div className="space-y-6">
          {groupsByProject.length === 0 ? (
            <Card className="p-8 text-center text-muted-foreground text-sm">
              {kpiFilter
                ? `Nessuna riga «${KPI_FILTER_LABELS[kpiFilter]}» con i filtri attivi: clicca di nuovo la card (o il pulsante Filtro) per mostrare tutto.`
                : "Nessun articolo da acquistare qui: aggiungi righe nella DDP Commerciale della commessa, oppure allarga i filtri."}
            </Card>
          ) : (
            groupsByProject.map((group) => (
              <AcquistiProjectCard
                key={group.projectId}
                group={group}
                columns={columnsByProject.get(group.projectId) ?? []}
                rowStyle={rowStyle}
                // Dal pulsante di card si pesca la commessa INTERA (pre-filtro KPI);
                // il pulsante di riga continua a passare la sola riga dalle colonne.
                onRequestRfq={() =>
                  handleOpenRfqModal(preKpiByProject.get(group.projectId) ?? group.items)
                }
                onOrderDanea={setOrderProject}
              />
            ))
          )}
        </div>
      </div>

      <CreateRfqDialog
        items={createRfqTargetItems}
        onClose={() => setCreateRfqTargetItems(null)}
        onCreated={(createdIds) => {
          invalidateLists()
          setCreateRfqTargetItems(null)
          if (createdIds.length > 0) setSelectedRfqDetailId(createdIds[0])
        }}
      />

      <RfqDetailDialog
        rfqId={selectedRfqDetailId}
        onClose={() => setSelectedRfqDetailId(null)}
        onChanged={invalidateLists}
        onUpdateRow={(data) => updateRowMutation.mutate(data)}
        onOpenDaneaOrder={setDaneaOrderIdDoc}
        onOpenDaneaOrderByRef={setDaneaOrderRef}
      />

      {/* Dialog Assegnazione Codice ATEC */}
      {assignDialogItem && (
        <CatalogAtecAssignDialog
          item={null}
          bomTarget={{
            bomItemId: assignDialogItem.id,
            partNumber: assignDialogItem.partNumber || "",
            description: assignDialogItem.description || "",
          }}
          onClose={() => setAssignDialogItem(null)}
          onSaved={() => {
            queryClient.invalidateQueries({ queryKey: ["acquisti-inbox"] })
            setAssignDialogItem(null)
          }}
        />
      )}

      {/* Associazione articoli Danea a un codice (catena ambra / pillola grezzo). Alla
          chiusura l'inbox si ricarica: se l'articolo è stato agganciato, l'icona sparisce. */}
      {codexMappingTarget ? (
        <CodexDaneaMappingDialog
          item={codexMappingTarget}
          onClose={() => {
            setCodexMappingTarget(null)
            queryClient.invalidateQueries({ queryKey: ["acquisti-inbox"] })
          }}
        />
      ) : null}

      {/* Dialog «Ordina Danea» di commessa (batch multi-RDO per fornitore) */}
      <ProjectDaneaOrdersDialog
        project={orderProject}
        allRfqs={rfqs}
        onClose={() => setOrderProject(null)}
        onGenerated={invalidateLists}
      />

      {/* Popup anteprima ordine fornitore come su Danea */}
      <DaneaOrderDialog
        idDoc={daneaOrderIdDoc}
        daneaRef={daneaOrderRef}
        onClose={() => {
          setDaneaOrderIdDoc(null)
          setDaneaOrderRef(null)
        }}
      />
    </div>
  )
}
