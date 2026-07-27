import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus } from "lucide-react"
import { useSearchParams } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { Button } from "@/components/ui/button"
import {
  buildDdpTransitionMap,
  fetchActiveDdpDestinations,
  fetchActiveDdpTreatments,
  fetchDdpAggregations,
  fetchDdpStatuses,
  fetchDdpStatusTransitions,
} from "@/lib/api/ddp-config"
import {
  addOfficinaItem,
  fetchOfficinaItems,
  updateOfficinaItem,
} from "@/lib/api/project-ddp-officina"
import { getSession } from "@/lib/auth/session"
import type { OfficinaItem, OfficinaItemSaveRequest } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { notifyError } from "@/lib/toast"

import { CodexPickerDialog } from "./CodexPickerDialog"
import { DdpStatusFilterBar } from "./DdpStatusFilterBar"
import { confirmDdpRowAnnul, DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { OfficinaDialog } from "./OfficinaDialog"
import { buildOfficinaColumns } from "./officina-columns"
import {
  buildOfficinaRows,
  collectParentIdsWithChildren,
  COLUMN_LABELS,
  toForm,
} from "./officina-shared"
import { useCodexPriceCheck } from "./use-codex-price-check"
import { useDdpQuantityAdjust } from "./use-ddp-quantity-adjust"
import { useOfficinaRowMutations } from "./use-officina-row-mutations"

export function ProjectDdpOfficina({ projectId }: { projectId: number }) {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  // Cancellazione definitiva riservata ad ADMIN e PM (il server la blinda con [Authorize(Roles)]).
  const role = getSession()?.user.userRole ?? ""
  const canHardDelete = role === "ADMIN" || role === "PM"
  const [searchParams] = useSearchParams()
  const highlightRowId = searchParams.get("item")

  const [dialog, setDialog] = React.useState<OfficinaItemSaveRequest | null>(null)
  const [pickerOpen, setPickerOpen] = React.useState(false)
  const [selectedStatusKeys, setSelectedStatusKeys] = React.useState<Set<string>>(
    () => new Set()
  )
  const [collapsedParentIds, setCollapsedParentIds] = React.useState<Set<number>>(
    new Set()
  )

  const toggleParentCollapse = React.useCallback((parentId: number) => {
    setCollapsedParentIds((prev) => {
      const next = new Set(prev)
      if (next.has(parentId)) next.delete(parentId)
      else next.add(parentId)
      return next
    })
  }, [])

  // Arrivando da un link a una riga specifica i filtri di stato la nasconderebbero.
  React.useEffect(() => {
    if (highlightRowId) setSelectedStatusKeys(new Set())
  }, [highlightRowId])

  const query = useQuery({
    queryKey: ["project-ddp-officina", projectId],
    queryFn: () => fetchOfficinaItems(projectId),
    enabled: projectId > 0,
  })

  const parentIdsWithChildren = React.useMemo(
    () => collectParentIdsWithChildren(query.data ?? []),
    [query.data]
  )

  const [codexPriceInfo, setCodexPriceInfo] = useCodexPriceCheck(
    dialog,
    parentIdsWithChildren
  )

  // ── Configurazione DDP (stati, transizioni, destinazioni, trattamenti) ──
  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  const statuses = React.useMemo(
    () => statusesQuery.data ?? [],
    [statusesQuery.data]
  )
  const statusMap = React.useMemo(
    () => new Map(statuses.map((s) => [s.statusKey, s])),
    [statuses]
  )
  // Matrice avanzamenti (v7): restringe la finestra opzioni di menu ⋮ e dialog.
  const transitionsQuery = useQuery({
    queryKey: ["ddp-status-transitions"],
    queryFn: fetchDdpStatusTransitions,
  })
  const transitionMap = React.useMemo(
    () => buildDdpTransitionMap(transitionsQuery.data ?? [], "OFFICINA"),
    [transitionsQuery.data]
  )

  const destinationsQuery = useQuery({
    queryKey: ["ddp-destinations", "active"],
    queryFn: fetchActiveDdpDestinations,
  })
  const destinations = React.useMemo(
    () => destinationsQuery.data ?? [],
    [destinationsQuery.data]
  )
  const treatmentsQuery = useQuery({
    // Stessa fonte di Config. DDP (GetAll + filtro attivi in client).
    queryKey: ["ddp-treatments", "selectable"],
    queryFn: fetchActiveDdpTreatments,
  })
  const treatments = React.useMemo(
    () => treatmentsQuery.data ?? [],
    [treatmentsQuery.data]
  )
  const aggregationsQuery = useQuery({
    queryKey: ["ddp-aggregations"],
    queryFn: fetchDdpAggregations,
  })
  // Stati «esclusi da totale/conteggi» (aggregazione A9): fuori dal totale € e con quantità bloccata.
  const excludedSet = React.useMemo(
    () =>
      new Set(
        aggregationsQuery.data?.find((a) => a.code === "A9")?.statusKeys ?? []
      ),
    [aggregationsQuery.data]
  )

  const invalidate = React.useCallback(
    () =>
      queryClient.invalidateQueries({
        queryKey: ["project-ddp-officina", projectId],
      }),
    [queryClient, projectId]
  )

  const onDdpChange = React.useCallback(
    (change: { ddpType: string }) => {
      if (change.ddpType?.toUpperCase() === "OFFICINA") void invalidate()
    },
    [invalidate]
  )
  useProjectHub(projectId > 0 ? projectId : null, onDdpChange)

  const mutations = useOfficinaRowMutations(projectId, invalidate)

  const saveMutation = useMutation({
    mutationFn: async (form: OfficinaItemSaveRequest) => {
      if (form.id > 0) await updateOfficinaItem(projectId, form.id, form)
      else await addOfficinaItem(projectId, form)
    },
    onSuccess: async () => {
      setDialog(null)
      await invalidate()
    },
    onError: (err: Error) => notifyError(err),
  })

  const handleQuantityAdjust = useDdpQuantityAdjust({
    confirm,
    statusMap,
    isPending: mutations.pending.quantity,
    excludedSet,
    onApply: mutations.applyQuantityPatch,
  })

  const handleAnnulRow = React.useCallback(
    async (item: OfficinaItem) => {
      if (item.itemStatus === DDP_STATUS_CANCELLED || mutations.pending.status) {
        return
      }
      const rowLabel = item.partNumber || item.description || "questa riga"
      const ok = await confirmDdpRowAnnul(confirm, statusMap, rowLabel)
      if (ok) mutations.changeStatus(item, DDP_STATUS_CANCELLED)
    },
    [confirm, statusMap, mutations]
  )

  const handleDeleteRow = React.useCallback(
    async (item: OfficinaItem) => {
      if (mutations.pending.remove) return
      const rowLabel = item.partNumber || item.description || "questa riga"
      // «Comanda il padre» anche in cancellazione: i componenti collegati seguono il padre.
      const linkedChildren = (query.data ?? []).filter(
        (r) => r.parentOfficinaItemId === item.id
      ).length
      const ok = await confirm({
        title: "Eliminare definitivamente la riga?",
        description: `La riga "${rowLabel}" verrà eliminata dalla distinta insieme alla sua bozza di lavorazione in staging.${
          linkedChildren > 0
            ? `\n\nVerranno eliminati anche i ${linkedChildren} componenti importati dalla sua composizione (con le loro bozze di lavorazione).`
            : ""
        }\n\nL'operazione non è reversibile.`,
        confirmLabel: "Elimina definitivamente",
        destructive: true,
      })
      if (ok) mutations.removeRow(item)
    },
    [confirm, mutations, query.data]
  )

  const handleEditRow = React.useCallback(
    (item: OfficinaItem) => setDialog(toForm(item)),
    []
  )

  const items = React.useMemo(
    () => buildOfficinaRows(query.data ?? [], parentIdsWithChildren),
    [query.data, parentIdsWithChildren]
  )

  const statusFilterItems = React.useMemo(() => {
    const counts = new Map<string, number>()
    for (const row of items) {
      const key = row.itemStatus ?? ""
      counts.set(key, (counts.get(key) ?? 0) + 1)
    }
    return [...counts.entries()]
      .map(([value, count]) => {
        const def = statusMap.get(value)
        return {
          value,
          label: value ? (def?.label ?? value) : "Senza stato",
          count,
          colorBg: def?.colorBg,
          colorFg: def?.colorFg,
          sortOrder: def?.sortOrder ?? Number.MAX_SAFE_INTEGER,
        }
      })
      .sort(
        (a, b) =>
          a.sortOrder - b.sortOrder || a.label.localeCompare(b.label, "it")
      )
  }, [items, statusMap])

  const statusFilteredItems = React.useMemo(() => {
    let list = items
    if (selectedStatusKeys.size > 0) {
      list = list.filter((row) => selectedStatusKeys.has(row.itemStatus ?? ""))
    }
    // I componenti spariscono quando il loro padre è collassato.
    return list.filter((row) =>
      row.parentOfficinaItemId != null
        ? !collapsedParentIds.has(row.parentOfficinaItemId)
        : true
    )
  }, [items, selectedStatusKeys, collapsedParentIds])

  const columns = React.useMemo(
    () =>
      buildOfficinaColumns({
        statuses,
        statusMap,
        transitionMap,
        destinations,
        treatments,
        parentIdsWithChildren,
        collapsedParentIds,
        toggleParentCollapse,
        mutations,
        canHardDelete,
        onEdit: handleEditRow,
        onAnnul: (item) => void handleAnnulRow(item),
        onDelete: (item) => void handleDeleteRow(item),
        onQuantityAdjust: (item, delta) => void handleQuantityAdjust(item, delta),
      }),
    [
      statuses,
      statusMap,
      transitionMap,
      destinations,
      treatments,
      parentIdsWithChildren,
      collapsedParentIds,
      toggleParentCollapse,
      mutations,
      canHardDelete,
      handleEditRow,
      handleAnnulRow,
      handleDeleteRow,
      handleQuantityAdjust,
    ]
  )

  // Il totale include solo le righe non escluse (aggregazione A9). Esclude i figli
  // per evitare double-counting (costo già nel padre).
  const totalCost = items.reduce((s, i) => {
    if (excludedSet.has(i.itemStatus)) return s
    if (i.parentOfficinaItemId != null) return s
    return s + i.totalCost
  }, 0)
  const excludedRows = items.filter((i) => excludedSet.has(i.itemStatus))
  const excludedValue = excludedRows.reduce(
    (s, i) => (i.parentOfficinaItemId != null ? s : s + i.totalCost),
    0
  )

  const rowStyle = React.useCallback(
    (item: OfficinaItem) => {
      const s = statusMap.get(item.itemStatus)
      return s ? { backgroundColor: s.colorBg, color: s.colorFg } : undefined
    },
    [statusMap]
  )

  return (
    <>
      <DataTableCardFiltered
        title="DDP officina"
        visibilityStorageKey="table-visibility-ddp-officina-v1"
        description="Distinta particolari meccanici della commessa"
        columns={columns}
        data={statusFilteredItems}
        columnLabels={COLUMN_LABELS}
        isLoading={query.isLoading}
        isFetching={query.isFetching}
        error={query.error as Error | null}
        onRefresh={() => query.refetch()}
        searchPlaceholder="Cerca nei particolari…"
        rowNoun="righe"
        emptyMessage="Nessun particolare meccanico."
        getRowId={(r) => String(r.id)}
        highlightRowId={highlightRowId}
        onRowDoubleClick={(r) => setDialog(toForm(r))}
        rowStyle={rowStyle}
        externalFiltersActive={selectedStatusKeys.size > 0}
        onClearExternalFilters={() => setSelectedStatusKeys(new Set())}
        aboveTable={
          <DdpStatusFilterBar
            items={statusFilterItems}
            selected={selectedStatusKeys}
            onChange={setSelectedStatusKeys}
          />
        }
        initialColumnVisibility={{
          createdAt: false,
          requestedBy: false,
          daneaRef: false,
          notes: false,
        }}
        toolbarActions={
          <>
            <span className="self-center text-sm font-medium">
              Totale:{" "}
              <span className="text-lg font-bold tabular-nums ml-1 text-blue-600 dark:text-blue-400">
                {euro(totalCost)}
              </span>
              {excludedRows.length > 0 ? (
                <span className="ml-2 font-normal text-muted-foreground tabular-nums">
                  · escluse {excludedRows.length} ({euro(excludedValue)})
                </span>
              ) : null}
            </span>
            <Button size="sm" onClick={() => setPickerOpen(true)}>
              <Plus />
              Aggiungi da Codex
            </Button>
          </>
        }
      />

      <OfficinaDialog
        form={dialog}
        statuses={statuses}
        transitions={transitionMap}
        destinations={destinations}
        treatments={treatments}
        saving={saveMutation.isPending}
        hasChildren={dialog ? parentIdsWithChildren.has(dialog.id) : false}
        codexPriceInfo={codexPriceInfo}
        onCodexPriceInfoChange={setCodexPriceInfo}
        onClose={() => setDialog(null)}
        onChange={setDialog}
        onSave={() =>
          dialog &&
          saveMutation.mutate({
            ...dialog,
            updateCodexPrice: codexPriceInfo.checked,
          })
        }
      />

      <CodexPickerDialog
        open={pickerOpen}
        projectId={projectId}
        onClose={() => setPickerOpen(false)}
        onAdded={() => void invalidate()}
      />
    </>
  )
}
