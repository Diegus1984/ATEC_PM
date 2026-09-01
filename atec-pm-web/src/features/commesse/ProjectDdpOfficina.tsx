import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { CellContext, ColumnDef } from "@tanstack/react-table"
import { History, Plus } from "lucide-react"
import { useSearchParams } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { StackedDateLabel } from "@/components/shared/date-field"
import { RowActionsMenu } from "@/components/shared/row-actions"
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
import { canWriteFeature } from "@/lib/auth/permissions"
import type {
  DdpStatusItem,
  OfficinaItem,
  OfficinaItemSaveRequest,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { notifyError } from "@/lib/toast"
import { useDeferredItemOrder } from "@/lib/use-deferred-item-order"
import { cn } from "@/lib/utils"

import { CodexPickerDialog } from "./CodexPickerDialog"
import { DdpItemHistoryDialog } from "./DdpItemHistoryDialog"
import { DdpStatusFilterBar } from "./DdpStatusFilterBar"
import { DdpStatusLegend } from "./DdpStatusLegend"
import { confirmDdpRowAnnul, DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { ddpTransitionsPerUtente } from "./ddp-constants"
import { WORK_TYPE_META } from "./ddp-work-type"
import { DaneaOrderDialog } from "@/components/shared/danea-order-dialog"
import { OfficinaDialog } from "./OfficinaDialog"
import { buildOfficinaColumns, GrezzoOrdineEye } from "./officina-columns"
import {
  buildOfficinaRows,
  collectParentIdsWithChildren,
  COLUMN_LABELS,
  toForm,
} from "./officina-shared"
import { useCodexPriceCheck } from "./use-codex-price-check"
import { useDdpQuantityAdjust } from "./use-ddp-quantity-adjust"
import { useOfficinaRowMutations } from "./use-officina-row-mutations"

/** Stessa resa della cella editabile (DdpQuantityStepper): interi senza decimali. */
function formatQuantity(quantity: number): string {
  return Number.isInteger(quantity)
    ? String(quantity)
    : quantity.toLocaleString("it-IT", { maximumFractionDigits: 2 })
}

/**
 * Rese in SOLA LETTURA delle celle che si scrivono (profili, segnalazione #63).
 * Un campo solo disabilitato direbbe «riprova più tardi», mentre qui la scrittura
 * non è proprio concessa: al posto di input, combo «⋮», stepper e menu di stato
 * resta il VALORE, che è quello che serve a chi la distinta la legge e basta.
 *
 * Stanno qui e non in `officina-columns` perché sono una scelta di questa pagina:
 * la funzione che costruisce le colonne resta quella di sempre — stesse colonne,
 * stesso ordine, stesse chiavi — e a cambiare è solo il contenuto della cella.
 */
function buildReadOnlyOfficinaCells(
  statusMap: Map<string, DdpStatusItem>,
  onStoria: (item: OfficinaItem) => void,
  // #142: l'occhio sull'ordine del grezzo resta anche in sola lettura (è consultazione).
  onOpenDaneaOrder: (idDoc: number) => void,
  onOpenDaneaOrderByRef: (rif: string) => void
): Record<string, (item: OfficinaItem) => React.ReactNode> {
  const text = (value: string) => (
    <span className="whitespace-nowrap">{value.trim() ? value : "—"}</span>
  )
  const date = (value: string | null) =>
    value ? (
      <StackedDateLabel value={value} />
    ) : (
      <span className="whitespace-nowrap">—</span>
    )

  return {
    requestedBy: (item) => text(item.requestedBy),
    quantity: (item) => (
      <span className="text-sm tabular-nums">{formatQuantity(item.quantity)}</span>
    ),
    quantityProduced: (item) => (
      <span className="text-xs tabular-nums">
        {item.quantityProduced}/{item.quantity}
      </span>
    ),
    treatment: (item) => (
      <span
        className="block truncate font-semibold"
        title={item.treatment || undefined}
      >
        {item.treatment || "—"}
      </span>
    ),
    workType: (item) => {
      const current = WORK_TYPE_META.find((t) => t.value === item.workType)
      return current ? (
        <span className="flex items-center gap-1.5">
          <span className={cn("size-2 shrink-0 rounded-full", current.dot)} />
          <span className="truncate">{current.label}</span>
        </span>
      ) : (
        <span className="text-muted-foreground">—</span>
      )
    },
    supplierName: (item) => (
      <span
        className="block max-w-[160px] truncate"
        title={item.supplierName || undefined}
      >
        {item.supplierName || "—"}
      </span>
    ),
    itemStatus: (item) => (
      <span className="font-semibold whitespace-nowrap">
        {statusMap.get(item.itemStatus)?.label ?? (item.itemStatus || "—")}
      </span>
    ),
    dateNeeded: (item) => date(item.dateNeeded),
    daneaRef: (item) => (
      <span className="flex items-center gap-1.5">
        {text(item.daneaRef)}
        <GrezzoOrdineEye
          item={item}
          onOpen={onOpenDaneaOrder}
          onOpenByRef={onOpenDaneaOrderByRef}
        />
      </span>
    ),
    orderDate: (item) => date(item.orderDate),
    deliveredAt: (item) => date(item.deliveredAt ?? null),
    destination: (item) => (
      <span className="block truncate font-semibold">
        {item.destination || "—"}
      </span>
    ),
    destinationSpec: (item) => text(item.destinationSpec),
    notes: (item) => (
      <span className="block max-w-[240px] whitespace-normal break-words">
        {item.notes || "—"}
      </span>
    ),
    // Del menu di riga resta la sola Cronistoria, che è lettura: «Modifica»,
    // «Annulla riga» ed «Elimina definitivamente» scrivono e spariscono.
    actions: (item) => (
      <RowActionsMenu
        label={item.partNumber || String(item.id)}
        actions={[
          { label: "Cronistoria", icon: History, onClick: () => onStoria(item) },
        ]}
      />
    ),
  }
}

/**
 * Sostituisce la sola `cell` delle colonne che si scrivono: id, ordine, filtri,
 * accessorFn (e quindi ricerca, ordinamento ed export) restano quelli originali.
 */
function applyReadOnlyCells(
  columns: ColumnDef<OfficinaItem>[],
  cells: Record<string, (item: OfficinaItem) => React.ReactNode>
): ColumnDef<OfficinaItem>[] {
  return columns.map((column) => {
    const key =
      column.id ?? ("accessorKey" in column ? String(column.accessorKey) : "")
    const render = cells[key]
    if (!render) return column
    return {
      ...column,
      cell: (ctx: CellContext<OfficinaItem, unknown>) => render(ctx.row.original),
    }
  })
}

export function ProjectDdpOfficina({ projectId }: { projectId: number }) {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  /**
   * Profili di permesso (#63): con «project.ddp_officina» in sola lettura il server
   * risponde 403 a ogni scrittura. La distinta si legge tutta — filtri di stato,
   * ricerca, colonne, totali e cronistoria restano — ma i comandi che scrivono non
   * si vedono nemmeno: un pulsante che porta solo a un errore rosso è peggio che assente.
   */
  const readOnly = !canWriteFeature("project.ddp_officina")
  // Cancellazione definitiva: la decide la chiave `action.delete_ddp_row` sulla persona,
  // non più il livello del ruolo. Il server la chiude con [RequireFeature] sulla stessa
  // chiave, in AND con la funzione di sezione; qui il pulsante sparisce anche a chi ha la
  // sezione in sola lettura, che le scritture non le può fare comunque.
  const canHardDelete = canWriteFeature("action.delete_ddp_row") && !readOnly
  const [searchParams] = useSearchParams()
  const highlightRowId = searchParams.get("item")

  const [dialog, setDialog] = React.useState<OfficinaItemSaveRequest | null>(null)
  /** Riga di cui si sta guardando la cronistoria degli stati. */
  const [storiaTarget, setStoriaTarget] = React.useState<OfficinaItem | null>(null)
  // #142 — popup dell'ordine Danea del GREZZO (occhio in colonna Rif. Danea):
  // per IDDoc quando l'ordine è nato da ATEC PM, per numero altrimenti.
  const [daneaOrderIdDoc, setDaneaOrderIdDoc] = React.useState<number | null>(null)
  const [daneaOrderRef, setDaneaOrderRef] = React.useState<string | null>(null)
  const [pickerOpen, setPickerOpen] = React.useState(false)
  const [selectedStatusKeys, setSelectedStatusKeys] = React.useState<Set<string>>(
    () => new Set()
  )
  const [collapsedParentIds, setCollapsedParentIds] = React.useState<Set<number>>(
    new Set()
  )
  /**
   * La distinta arriva ordinata dal server per priorità della lavorazione e data
   * «Necessario»: modificarle farebbe saltare la riga altrove al primo refetch.
   * L'ordine a video resta quello finché non si preme «Aggiorna».
   */
  const [layoutEpoch, setLayoutEpoch] = React.useState(0)

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
  // `undefined` = finestra completa: è così che chi ha il privilegio #140 vede tutti gli stati.
  const transitionMap = React.useMemo(
    () =>
      ddpTransitionsPerUtente(
        buildDdpTransitionMap(transitionsQuery.data ?? [], "OFFICINA")
      ),
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

  /**
   * 🪤 #135: una riga di officina non tocca più la sola distinta officina. Un particolare
   * a disegno «101» può derivare da un commerciale «201» (il grezzo da comprare), e la
   * riga del grezzo nella DDP COMMERCIALE la crea, aggiorna e cancella il server da solo
   * seguendo questa distinta: aggiunta dal picker, cancellazione, quantità e stato.
   * Invalidando la sola officina la scheda accanto resterebbe con la distinta vecchia
   * fino al ricaricamento della pagina. Sta qui e non sui singoli chiamanti perché di
   * qui passano tutte le scritture della pagina (picker, menu riga, dialog, real-time).
   */
  const invalidate = React.useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: ["project-ddp-officina", projectId],
      }),
      queryClient.invalidateQueries({
        queryKey: ["project-ddp", projectId, "COMMERCIAL"],
      }),
    ])
  }, [queryClient, projectId])

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
      // In sola lettura la finestra non si apre nemmeno: la guardia sta qui perché
      // è l'unico punto da cui passa la scrittura, e regge anche i chiamanti futuri.
      if (readOnly) return
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
    // `isPending` è già il freno di questo hook: in sola lettura resta tirato,
    // così il ± non parte nemmeno se qualcuno lo richiamasse da fuori la griglia.
    isPending: mutations.pending.quantity || readOnly,
    excludedSet,
    onApply: mutations.applyQuantityPatch,
  })

  const handleAnnulRow = React.useCallback(
    async (item: OfficinaItem) => {
      if (
        readOnly ||
        item.itemStatus === DDP_STATUS_CANCELLED ||
        mutations.pending.status
      ) {
        return
      }
      const rowLabel = item.partNumber || item.description || "questa riga"
      const ok = await confirmDdpRowAnnul(confirm, statusMap, rowLabel)
      if (ok) mutations.changeStatus(item, DDP_STATUS_CANCELLED)
    },
    [confirm, statusMap, mutations, readOnly]
  )

  const handleDeleteRow = React.useCallback(
    async (item: OfficinaItem) => {
      if (readOnly || mutations.pending.remove) return
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
    [confirm, mutations, query.data, readOnly]
  )

  /**
   * Unica porta d'ingresso della finestra di dettaglio (menu di riga e doppio clic).
   * In sola lettura NON si apre: `OfficinaDialog` è una maschera di sola scrittura —
   * non ha un modo di presentarsi senza il pulsante «Salva» e con i campi bloccati, e
   * dargliene uno vorrebbe dire riscriverla. Nessun dato va perso: la griglia mostra
   * già tutte le colonne della finestra, e la Cronistoria resta raggiungibile.
   */
  const handleEditRow = React.useCallback(
    (item: OfficinaItem) => {
      if (readOnly) return
      setDialog(toForm(item))
    },
    [readOnly]
  )

  // Ordine congelato sui soli padri: `buildOfficinaRows` riaggancia comunque i
  // componenti sotto al loro padre, quindi l'albero regge anche sulle righe nuove.
  const rawItems = React.useMemo(() => query.data ?? [], [query.data])
  const keepServerOrder = React.useCallback((list: OfficinaItem[]) => list, [])
  const orderedRawItems = useDeferredItemOrder(
    rawItems,
    keepServerOrder,
    layoutEpoch
  )
  const items = React.useMemo(
    () => buildOfficinaRows(orderedRawItems, parentIdsWithChildren),
    [orderedRawItems, parentIdsWithChildren]
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

  const columns = React.useMemo(() => {
    const editable = buildOfficinaColumns({
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
      onStoria: setStoriaTarget,
      onOpenDaneaOrder: setDaneaOrderIdDoc,
      onOpenDaneaOrderByRef: setDaneaOrderRef,
    })
    if (!readOnly) return editable
    return applyReadOnlyCells(
      editable,
      buildReadOnlyOfficinaCells(
        statusMap,
        setStoriaTarget,
        setDaneaOrderIdDoc,
        setDaneaOrderRef
      )
    )
  }, [
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
    setStoriaTarget,
    readOnly,
  ])

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
        // v2 (segnalazione #58): «Rif. Danea» nasce VISIBILE. La chiave va versionata o chi ha
        // già usato la pagina si tiene la scelta vecchia nel localStorage e la colonna resta
        // nascosta proprio a chi l'ha chiesta.
        // v3 (segnalazione #61): stessa storia per «Inserito da» e «Data inserimento».
        // v4 (#61): arriva «Creata da» (autore registrato dal server), che deve nascere
        // NASCOSTA — e senza versionare la chiave comparirebbe visibile proprio a chi ha
        // già una scelta salvata (localStorage sostituisce initialColumnVisibility, non lo fonde).
        // v5 (#81): «Note» nasce VISIBILE dopo Specifica (stesso campo multilinea dei Commerciali).
        visibilityStorageKey="table-visibility-ddp-officina-v5"
        gridLines
        description="Distinta particolari meccanici della commessa"
        columns={columns}
        data={statusFilteredItems}
        columnLabels={COLUMN_LABELS}
        isLoading={query.isLoading}
        isFetching={query.isFetching}
        error={query.error as Error | null}
        onRefresh={() => {
          setLayoutEpoch((n) => n + 1)
          void query.refetch()
        }}
        searchPlaceholder="Cerca nei particolari…"
        rowNoun="righe"
        emptyMessage="Nessun particolare meccanico."
        getRowId={(r) => String(r.id)}
        highlightRowId={highlightRowId}
        onRowDoubleClick={handleEditRow}
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
          // «Creata da» (autore registrato dal server) nasce nascosta: serve solo quando
          // si vuole sapere chi ha creato davvero la riga, e la griglia è già larga.
          // «Note» (#81) nasce visibile — come in DDP Commerciali — dopo Specifica.
          createdByName: false,
        }}
        toolbarActions={
          <>
            <DdpStatusLegend statuses={statuses} />
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
            {/* Il totale si legge sempre; ad aggiungere righe è solo chi scrive. */}
            {!readOnly && (
              /* Stesso nome e stesso picker del pulsante della DDP Commerciale:
                 è UN pulsante unico, lo smistamento lo fa il programma. */
              <Button
                size="sm"
                title="Cerca nel catalogo articoli e nel Codex e aggiungi righe alla distinta"
                onClick={() => setPickerOpen(true)}
              >
                <Plus />
                Aggiungi articolo
              </Button>
            )}
          </>
        }
      />

      {/* Rete di sicurezza, come in ProjectDdpCommercial: se un percorso di apertura
          sfuggisse, la finestra mostrerebbe comunque il pulsante «Salva». */}
      <OfficinaDialog
        form={readOnly ? null : dialog}
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

      <DdpItemHistoryDialog
        open={storiaTarget !== null}
        onOpenChange={(open) => {
          if (!open) setStoriaTarget(null)
        }}
        kind="OFFICINA"
        itemId={storiaTarget?.id ?? null}
        itemLabel={
          storiaTarget
            ? `${storiaTarget.partNumber ?? ""} ${storiaTarget.description ?? ""}`.trim()
            : undefined
        }
      />

      {/* Aggiunge righe alla distinta: chiuso a chiave anche qui, non solo nel pulsante. */}
      <CodexPickerDialog
        open={!readOnly && pickerOpen}
        projectId={projectId}
        ddpType="OFFICINA"
        onClose={() => setPickerOpen(false)}
        onAdded={() => void invalidate()}
      />

      {/* #142 — popup dell'ordine Danea del grezzo (stesso dialog della Commerciale:
          per IDDoc, o ricerca per numero anche nel vecchio archivio). */}
      <DaneaOrderDialog
        idDoc={daneaOrderIdDoc}
        daneaRef={daneaOrderRef}
        onClose={() => {
          setDaneaOrderIdDoc(null)
          setDaneaOrderRef(null)
        }}
      />
    </>
  )
}
