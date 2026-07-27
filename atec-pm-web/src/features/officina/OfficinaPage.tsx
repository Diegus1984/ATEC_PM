import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import {
  AlertTriangle,
  Factory,
  FlaskConical,
  ListTodo,
  Truck,
} from "lucide-react"
import { Link } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { PmSidebar } from "@/components/shared/pm-sidebar"
import { ApiError } from "@/lib/api/client"
import {
  buildDdpTransitionMap,
  fetchDdpStatuses,
  fetchDdpStatusTransitions,
} from "@/lib/api/ddp-config"
import { fetchOfficinaInbox } from "@/lib/api/ddp-officina-inbox"
import { updateOfficinaItem } from "@/lib/api/project-ddp-officina"
import type {
  DdpStatusItem,
  OfficinaInboxItem,
  OfficinaItemSaveRequest,
} from "@/lib/api/types"
import { notifyError } from "@/lib/toast"
import { useProjectHub } from "@/lib/signalr/use-project-hub"

import { confirmDdpRowAnnul, DDP_STATUS_CANCELLED } from "@/features/commesse/ddp-annul-row"
import { DdpInlineDateCell } from "@/features/commesse/DdpInlineDateCell"
import { DdpInlineTextCell } from "@/features/commesse/DdpInlineTextCell"
import { DdpStatusMenu } from "@/features/commesse/DdpStatusMenu"
import {
  priorityMeta,
} from "@/features/checklist/checklist-utils"
import { Input } from "@/components/ui/input"
import { cn } from "@/lib/utils"
import { toDateOnly } from "@/lib/date-iso"

// Visibilità inbox (matrice stati v7): l'officina vede SOLO le righe rilasciate alla
// produzione — DC/PAR (pezzi da produrre / produzione parziale), IO (lavorazione esterna
// ordinata, in arrivo) e MIT (in trattamento). Gli stati a monte (VER/CHEK/RO/DO: triage
// e ordini non ancora emessi), SOSP e le righe concluse (DISP/ASS/RAM/ANN/SOST) restano
// fuori dalla coda: whitelist, non blacklist, così ogni stato futuro non ci finisce per sbaglio.
const VISIBLE = new Set(["DC", "PAR", "IO", "MIT"])
// Il lavoro effettivo dell'officina: da costruire o costruito solo in parte.
const TO_PRODUCE = new Set(["DC", "PAR"])

const COLUMN_LABELS: Record<string, string> = {
  project: "Commessa",
  priority: "Priorità",
  partNumber: "Codice",
  description: "Descrizione",
  quantity: "Qtà",
  quantityProduced: "Prodotti",
  itemStatus: "Stato",
  dateNeeded: "Necessario",
  material: "Materiale",
  treatment: "Trattamento",
  supplierName: "Fornitore",
  workRequest: "Lavorazione",
}

type OfficinaView =
  | { kind: "todo" }
  | { kind: "late" }
  | { kind: "io" }
  | { kind: "internal" }
  | { kind: "treatment" }
  | { kind: "project"; projectId: number }

function toForm(item: OfficinaInboxItem): OfficinaItemSaveRequest {
  return {
    id: item.id,
    projectId: item.projectId,
    partNumber: item.partNumber,
    description: item.description,
    quantity: item.quantity,
    quantityProduced: item.quantityProduced ?? 0,
    unitCost: item.unitCost,
    material: item.material,
    treatment: item.treatment,
    supplierId: item.supplierId,
    supplierName: item.supplierName,
    itemStatus: item.itemStatus,
    requestedBy: item.requestedBy,
    daneaRef: item.daneaRef,
    dateNeeded: item.dateNeeded,
    orderDate: item.orderDate,
    destination: item.destination,
    destinationSpec: item.destinationSpec ?? "",
    notes: item.notes,
    expectedUpdatedAt: item.updatedAt,
    parentOfficinaItemId: item.parentOfficinaItemId,
  }
}

function isVisible(item: OfficinaInboxItem): boolean {
  return VISIBLE.has((item.itemStatus ?? "").toUpperCase())
}

function isToProduce(item: OfficinaInboxItem): boolean {
  return TO_PRODUCE.has((item.itemStatus ?? "").toUpperCase())
}

function filterByView(
  items: OfficinaInboxItem[],
  view: OfficinaView
): OfficinaInboxItem[] {
  switch (view.kind) {
    case "todo":
      return items.filter(isToProduce)
    case "late":
      return items
        .filter((i) => isVisible(i) && i.daysLate != null && i.daysLate > 0)
        .sort((a, b) => (b.daysLate ?? 0) - (a.daysLate ?? 0))
    case "io":
      return items.filter((i) => (i.itemStatus ?? "").toUpperCase() === "IO")
    case "internal":
      return items.filter((i) => (i.itemStatus ?? "").toUpperCase() === "DC")
    case "treatment":
      return items.filter(
        (i) => isVisible(i) && (i.treatment ?? "").trim().length > 0
      )
    case "project":
      return items.filter((i) => i.projectId === view.projectId && isVisible(i))
  }
}

function PriorityBadge({ item }: { item: OfficinaInboxItem }) {
  if (!item.workRequestId) {
    return <span className="text-xs text-muted-foreground">—</span>
  }
  if (item.isUltraCritical) {
    return (
      <span className="inline-flex items-center rounded-md border border-red-300 bg-red-50 px-1.5 py-0.5 text-[11px] font-semibold text-red-700">
        Ultra
      </span>
    )
  }
  if (item.wrPriority == null) {
    return <span className="text-xs text-muted-foreground">—</span>
  }
  const meta = priorityMeta(item.wrPriority)
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-[11px] font-medium",
        meta.trigger
      )}
    >
      <span className={cn("size-1.5 rounded-full", meta.dot)} />
      {meta.code}
    </span>
  )
}

function ProducedQtyCell({
  item,
  disabled,
  onCommit,
}: {
  item: OfficinaInboxItem
  disabled?: boolean
  onCommit: (value: number) => void
}) {
  const produced = item.quantityProduced ?? 0
  const [draft, setDraft] = React.useState(String(produced))

  React.useEffect(() => {
    setDraft(String(produced))
  }, [produced, item.id])

  const commit = () => {
    const n = Number(String(draft).replace(",", "."))
    const next = Number.isFinite(n) ? Math.max(0, Math.min(item.quantity, n)) : 0
    if (next !== produced) onCommit(next)
    else setDraft(String(produced))
  }

  return (
    <div
      className="inline-flex items-center gap-0 text-xs tabular-nums text-current"
      onClick={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
    >
      <Input
        inputMode="decimal"
        disabled={disabled}
        value={draft}
        className="h-7 w-9 border-transparent bg-transparent px-0.5 text-center text-xs tabular-nums text-current shadow-none focus:border-input focus:bg-white focus:text-foreground"
        onChange={(e) => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            e.currentTarget.blur()
          }
        }}
      />
      <span className="text-current">/{item.quantity}</span>
    </div>
  )
}

function viewTitle(view: OfficinaView): string {
  switch (view.kind) {
    case "todo":
      return "Da produrre"
    case "late":
      return "In ritardo"
    case "io":
      return "In ordine"
    case "internal":
      return "Interna / DC"
    case "treatment":
      return "Trattamenti"
    case "project":
      return "Per commessa"
  }
}

export function OfficinaPage() {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const [view, setView] = React.useState<OfficinaView>({ kind: "todo" })

  const inboxQuery = useQuery({
    queryKey: ["officina-inbox"],
    queryFn: () => fetchOfficinaInbox(),
  })

  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  // Matrice avanzamenti (v7): restringe la finestra opzioni del menu ⋮ stato.
  const transitionsQuery = useQuery({
    queryKey: ["ddp-status-transitions"],
    queryFn: fetchDdpStatusTransitions,
  })
  const transitionMap = React.useMemo(
    () => buildDdpTransitionMap(transitionsQuery.data ?? [], "OFFICINA"),
    [transitionsQuery.data]
  )

  const statuses = statusesQuery.data ?? []
  const statusMap = React.useMemo(() => {
    const m = new Map<string, DdpStatusItem>()
    for (const s of statuses) m.set(s.statusKey, s)
    return m
  }, [statuses])

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["officina-inbox"] })
  }, [queryClient])

  useProjectHub("all", (change) => {
    if (change.ddpType === "OFFICINA") invalidate()
  })

  const allItems = inboxQuery.data ?? []

  const statusMutation = useMutation({
    mutationFn: ({
      item,
      statusKey,
    }: {
      item: OfficinaInboxItem
      statusKey: string
    }) =>
      updateOfficinaItem(item.projectId, item.id, {
        ...toForm(item),
        itemStatus: statusKey,
      }),
    onSuccess: () => invalidate(),
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        invalidate()
        return
      }
      notifyError(err)
    },
  })

  const patchMutation = useMutation({
    mutationFn: ({
      item,
      patch,
    }: {
      item: OfficinaInboxItem
      patch: Partial<OfficinaItemSaveRequest>
    }) =>
      updateOfficinaItem(item.projectId, item.id, {
        ...toForm(item),
        ...patch,
      }),
    onSuccess: () => invalidate(),
    onError: (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        invalidate()
        return
      }
      notifyError(err)
    },
  })

  const handleStatusChange = React.useCallback(
    async (item: OfficinaInboxItem, statusKey: string) => {
      if (statusKey === item.itemStatus || statusMutation.isPending) return
      if (statusKey === DDP_STATUS_CANCELLED) {
        const ok = await confirmDdpRowAnnul(
          confirm,
          statusMap,
          item.partNumber || item.description || `#${item.id}`
        )
        if (!ok) return
      }
      statusMutation.mutate({ item, statusKey })
    },
    [confirm, statusMap, statusMutation]
  )

  const counts = React.useMemo(() => {
    const todo = allItems.filter(isToProduce).length
    const late = allItems.filter(
      (i) => isVisible(i) && i.daysLate != null && i.daysLate > 0
    ).length
    const io = allItems.filter(
      (i) => (i.itemStatus ?? "").toUpperCase() === "IO"
    ).length
    const internal = allItems.filter(
      (i) => (i.itemStatus ?? "").toUpperCase() === "DC"
    ).length
    const treatment = allItems.filter(
      (i) => isVisible(i) && (i.treatment ?? "").trim().length > 0
    ).length
    return { todo, late, io, internal, treatment }
  }, [allItems])

  const projectContainers = React.useMemo(() => {
    const byProject = new Map<
      number,
      { code: string; title: string; customer: string; count: number; late: boolean }
    >()
    for (const item of allItems) {
      if (!isVisible(item)) continue
      const cur = byProject.get(item.projectId)
      if (cur) {
        cur.count += 1
        if (item.daysLate != null && item.daysLate > 0) cur.late = true
      } else {
        byProject.set(item.projectId, {
          code: item.projectCode,
          title: item.projectTitle,
          customer: item.customerName,
          count: 1,
          late: (item.daysLate ?? 0) > 0,
        })
      }
    }
    return [...byProject.entries()]
      .sort((a, b) => a[1].code.localeCompare(b[1].code, "it"))
      .map(([projectId, info]) => ({
        key: `p${projectId}`,
        selected: view.kind === "project" && view.projectId === projectId,
        onClick: () => setView({ kind: "project", projectId }),
        label: info.customer
          ? `${info.code} — ${info.customer}`
          : info.title
            ? `${info.code} — ${info.title}`
            : info.code,
        count: info.count,
        dots: info.late
          ? [{ dotClass: "bg-rose-500", label: "In ritardo" }]
          : [],
      }))
  }, [allItems, view])

  const filtered = React.useMemo(
    () => filterByView(allItems, view),
    [allItems, view]
  )

  const columns = React.useMemo<ColumnDef<OfficinaInboxItem>[]>(
    () => [
      {
        id: "project",
        header: "Commessa",
        accessorFn: (r) => r.projectCode,
        cell: ({ row }) => (
          <Link
            to={`/commesse/${row.original.projectId}/ddp_officina?item=${row.original.id}`}
            className="font-medium text-primary hover:underline"
          >
            {row.original.projectCode}
          </Link>
        ),
      },
      {
        id: "priority",
        header: "Priorità",
        accessorFn: (r) =>
          r.isUltraCritical ? -1 : (r.wrPriority ?? 99),
        cell: ({ row }) => <PriorityBadge item={row.original} />,
      },
      {
        accessorKey: "partNumber",
        header: "Codice",
        cell: ({ row }) => (
          <span className="font-mono text-xs font-semibold">
            {row.original.partNumber || "—"}
            {row.original.parentOfficinaItemId ? (
              <span className="ml-1 text-[10px] text-muted-foreground">
                figlio
              </span>
            ) : null}
          </span>
        ),
      },
      {
        accessorKey: "description",
        header: "Descrizione",
        cell: ({ row }) => (
          <span className="line-clamp-2 max-w-[220px] text-sm">
            {row.original.description || "—"}
          </span>
        ),
      },
      {
        accessorKey: "quantity",
        header: "Qtà",
        cell: ({ row }) => (
          <span className="tabular-nums text-sm">{row.original.quantity}</span>
        ),
      },
      {
        accessorKey: "quantityProduced",
        header: "Prodotti",
        cell: ({ row }) => (
          <ProducedQtyCell
            item={row.original}
            disabled={
              patchMutation.isPending ||
              (row.original.itemStatus ?? "").toUpperCase() === "ANN"
            }
            onCommit={(value) =>
              patchMutation.mutate({
                item: row.original,
                patch: { quantityProduced: value },
              })
            }
          />
        ),
      },
      {
        accessorKey: "itemStatus",
        header: "Stato",
        cell: ({ row }) => {
          const st = statusMap.get(row.original.itemStatus)
          return (
            <div className="flex items-center gap-1">
              <span
                className="inline-flex items-center rounded-md border px-1.5 py-0.5 text-[11px] font-medium"
                style={
                  st
                    ? {
                        backgroundColor: st.colorBg,
                        color: st.colorFg,
                        borderColor: st.colorFg,
                      }
                    : undefined
                }
              >
                {st?.label ?? row.original.itemStatus}
              </span>
              <DdpStatusMenu
                currentStatusKey={row.original.itemStatus}
                statuses={statuses}
                transitions={transitionMap}
                disabled={statusMutation.isPending}
                onSelect={(key) => void handleStatusChange(row.original, key)}
              />
            </div>
          )
        },
      },
      {
        accessorKey: "dateNeeded",
        header: "Necessario",
        cell: ({ row }) => {
          // Tutte le viste filtrano su stati aperti (whitelist VISIBLE): basta il ritardo.
          const late = (row.original.daysLate ?? 0) > 0
          return (
            <div className="flex flex-col gap-0.5">
              <DdpInlineDateCell
                value={toDateOnly(row.original.dateNeeded)}
                disabled={patchMutation.isPending}
                onChange={(value) =>
                  patchMutation.mutate({
                    item: row.original,
                    patch: { dateNeeded: value },
                  })
                }
              />
              {late ? (
                <span className="text-[10px] font-medium text-rose-600">
                  +{row.original.daysLate} gg
                </span>
              ) : null}
            </div>
          )
        },
      },
      {
        accessorKey: "material",
        header: "Materiale",
        cell: ({ row }) => (
          <DdpInlineTextCell
            value={row.original.material ?? ""}
            disabled={patchMutation.isPending}
            placeholder="—"
            onCommit={(value) =>
              patchMutation.mutate({
                item: row.original,
                patch: { material: value },
              })
            }
          />
        ),
      },
      {
        accessorKey: "treatment",
        header: "Trattamento",
        cell: ({ row }) => (
          <DdpInlineTextCell
            value={row.original.treatment ?? ""}
            disabled={patchMutation.isPending}
            placeholder="—"
            onCommit={(value) =>
              patchMutation.mutate({
                item: row.original,
                patch: { treatment: value },
              })
            }
          />
        ),
      },
      {
        accessorKey: "supplierName",
        header: "Fornitore",
        cell: ({ row }) => (
          <span className="line-clamp-1 max-w-[140px] text-sm">
            {row.original.supplierName || "—"}
          </span>
        ),
      },
      {
        id: "workRequest",
        header: "Lavorazione",
        cell: ({ row }) =>
          row.original.workRequestId ? (
            <Link
              to={`/commesse/${row.original.projectId}/work_requests?item=${row.original.workRequestId}`}
              className="text-xs text-primary hover:underline"
            >
              #{row.original.workRequestId}
            </Link>
          ) : (
            <span className="text-xs text-muted-foreground">—</span>
          ),
      },
    ],
    [
      handleStatusChange,
      patchMutation,
      statusMap,
      statusMutation.isPending,
      statuses,
      transitionMap,
    ]
  )

  const quickViews = [
    {
      key: "todo",
      selected: view.kind === "todo",
      onClick: () => setView({ kind: "todo" }),
      icon: <ListTodo className="size-4" />,
      label: "Da produrre",
      count: counts.todo,
    },
    {
      key: "late",
      selected: view.kind === "late",
      onClick: () => setView({ kind: "late" }),
      icon: <AlertTriangle className="size-4" />,
      label: "In ritardo",
      count: counts.late,
    },
    {
      key: "io",
      selected: view.kind === "io",
      onClick: () => setView({ kind: "io" }),
      icon: <Truck className="size-4" />,
      label: "In ordine",
      count: counts.io,
    },
    {
      key: "internal",
      selected: view.kind === "internal",
      onClick: () => setView({ kind: "internal" }),
      icon: <Factory className="size-4" />,
      label: "Interna / DC",
      count: counts.internal,
    },
    {
      key: "treatment",
      selected: view.kind === "treatment",
      onClick: () => setView({ kind: "treatment" }),
      icon: <FlaskConical className="size-4" />,
      label: "Trattamenti",
      count: counts.treatment,
    },
  ]

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight text-foreground">
          Officina
        </h1>
        <p className="text-sm text-muted-foreground">
          Coda operativa DDP Officina cross-commessa: stati, scadenze, materiali.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <PmSidebar
          storageKey="officina-inbox"
          quickViews={quickViews}
          containers={projectContainers}
          containersLabel="Commesse"
          emptyLabel="Nessuna riga in produzione"
        />

        <main className="min-w-0 flex-1 overflow-y-auto p-4">
          <DataTableCardFiltered
            title={viewTitle(view)}
            description={
              view.kind === "project"
                ? "Righe in produzione della commessa selezionata."
                : "Cambio stato e date inline · apri la distinta dal codice commessa."
            }
            columns={columns}
            data={filtered}
            isLoading={inboxQuery.isLoading}
            error={inboxQuery.error as Error | null}
            onRefresh={() => void inboxQuery.refetch()}
            searchPlaceholder="Cerca commessa, codice, materiale…"
            columnLabels={COLUMN_LABELS}
            getRowId={(row) => String(row.id)}
            visibilityStorageKey="officina-inbox-table"
            rowStyle={(row) =>
              (row.daysLate ?? 0) > 0 && isVisible(row)
                ? { backgroundColor: "rgba(244, 63, 94, 0.08)" }
                : undefined
            }
          />
        </main>
      </div>
    </div>
  )
}
