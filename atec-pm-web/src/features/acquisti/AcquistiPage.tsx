import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  AlertTriangle,
  FileCheck2,
  Package,
  RefreshCw,
  Search,
  ShoppingCart,
} from "lucide-react"

import { DaneaOrderDialog } from "@/components/shared/danea-order-dialog"
import { Button } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { CatalogAtecAssignDialog } from "@/features/catalogo/CatalogAtecAssignDialog"
import { DdpStatusFilterBar } from "@/features/commesse/DdpStatusFilterBar"
import { ddpCommercialRowToSaveRequest } from "@/features/commesse/ddp-commercial-row"
import { fetchAcquistiInbox } from "@/lib/api/ddp-commercial-inbox"
import {
  buildDdpTransitionMap,
  fetchDdpStatuses,
  fetchDdpStatusTransitions,
} from "@/lib/api/ddp-config"
import { updateDdpRow } from "@/lib/api/project-ddp"
import { fetchPurchaseRfqs } from "@/lib/api/purchase-rfqs"
import type { AcquistiInboxItem, PurchaseRfqListItem } from "@/lib/api/types"
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
  sortAcquistiByProjectAndAction,
  statusOf,
} from "./acquisti-shared"
import { CreateRfqDialog } from "./CreateRfqDialog"
import { ProjectDaneaOrdersDialog } from "./ProjectDaneaOrdersDialog"
import { RfqDetailDialog } from "./RfqDetailDialog"

export function AcquistiPage() {
  const queryClient = useQueryClient()

  const [searchQuery, setSearchQuery] = React.useState("")
  const [selectedStatusKeys, setSelectedStatusKeys] = React.useState<Set<string>>(new Set())
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
  const [selectedRfqDetailId, setSelectedRfqDetailId] = React.useState<number | null>(null)
  const [orderProject, setOrderProject] = React.useState<{
    projectId: number
    projectCode: string
  } | null>(null)
  const [daneaOrderIdDoc, setDaneaOrderIdDoc] = React.useState<number | null>(null)
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

  const transitionMap = React.useMemo(
    () => buildDdpTransitionMap(transitions, "COMMERCIAL"),
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

  const visibleItems = React.useMemo(() => {
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
    for (const i of visibleItems) {
      if (isToBuy(i)) {
        toBuyCount++
        toBuyCost += (i.unitCost || 0) * i.quantity
      }
      if ((i.daysLate ?? 0) > 0) lateCount++
      if (!normalizeAtec(i.atecCode)) unmappedCount++
      if (statusOf(i) === "IO") orderedCount++
    }
    return { toBuyCount, toBuyCost, lateCount, unmappedCount, orderedCount }
  }, [visibleItems])

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
  const filtersKey = `${[...selectedStatusKeys].sort().join(",")}|${searchQuery.trim()}`
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
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["acquisti-inbox"] })
      queryClient.invalidateQueries({ queryKey: ["purchase-rfq-detail"] })
      notifyInfo("Dati articolo aggiornati")
    },
    onError: (err: Error) => {
      notifyError(`Errore aggiornamento: ${err.message}`)
    },
  })

  // Apertura del dialog RDO: solo articoli nello stato DO (Da ORDINARE).
  const handleOpenRfqModal = React.useCallback((items: AcquistiInboxItem[]) => {
    const doItems = items.filter((i) => statusOf(i) === "DO")
    if (doItems.length === 0) {
      notifyError("Nessun articolo nello stato 'Da ORDINARE' selezionato per la RDO.")
      return
    }
    setCreateRfqTargetItems(doItems)
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
                onOpenRfqDetail: setSelectedRfqDetailId,
                onOpenDaneaOrder: setDaneaOrderIdDoc,
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
              Inbox Acquisti (Controllo Commesse)
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
            label="Da Ordinare / RDO"
            value={kpi.toBuyCount}
            unit="articoli"
            icon={Package}
            borderClassName="border-l-red-500"
            iconClassName="text-red-500"
          >
            Tot. Stimato:{" "}
            <span className="font-semibold text-foreground">{euro(kpi.toBuyCost)}</span>
          </KpiCard>

          <KpiCard
            label="In Ordine Danea"
            value={kpi.orderedCount}
            unit="articoli"
            icon={ShoppingCart}
            borderClassName="border-l-amber-500"
            iconClassName="text-amber-500"
          >
            Ordini già emessi verso fornitori
          </KpiCard>

          <KpiCard
            label="Gare RDO Attive / In Ritardo"
            value={activeRfqsCount}
            unit={<span className="text-red-500">({kpi.lateCount} ritardi)</span>}
            icon={FileCheck2}
            borderClassName="border-l-purple-500"
            iconClassName="text-purple-500"
          >
            Gare d'offerta in corso con fornitori
          </KpiCard>

          <KpiCard
            label="Senza Codice ATEC"
            value={kpi.unmappedCount}
            icon={AlertTriangle}
            borderClassName="border-l-indigo-500"
            iconClassName="text-indigo-500"
          >
            Articoli da associare al Codex
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
            </div>
          </div>
        </div>

        {/* LISTA COMMESSE CON TABELLA ED EVIDENZIAZIONE COLORI DA CONF DDP */}
        <div className="space-y-6">
          {groupsByProject.length === 0 ? (
            <Card className="p-8 text-center text-muted-foreground text-sm">
              Nessuna riga d'acquisto trovata per i criteri selezionati.
            </Card>
          ) : (
            groupsByProject.map((group) => (
              <AcquistiProjectCard
                key={group.projectId}
                group={group}
                columns={columnsByProject.get(group.projectId) ?? []}
                rowStyle={rowStyle}
                onRequestRfq={handleOpenRfqModal}
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

      {/* Dialog «Ordina Danea» di commessa (batch multi-RDO per fornitore) */}
      <ProjectDaneaOrdersDialog
        project={orderProject}
        allRfqs={rfqs}
        onClose={() => setOrderProject(null)}
        onGenerated={invalidateLists}
      />

      {/* Popup anteprima ordine fornitore come su Danea */}
      <DaneaOrderDialog idDoc={daneaOrderIdDoc} onClose={() => setDaneaOrderIdDoc(null)} />
    </div>
  )
}
