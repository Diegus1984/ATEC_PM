import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import {
  Ban,
  ChevronDown,
  ChevronRight,
  Eye,
  History,
  Link2,
  Pencil,
  Plus,
  Trash2,
} from "lucide-react"
import { Link, useSearchParams } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError, notifyInfo } from "@/lib/toast"
import { formatDateShort } from "@/lib/date-iso"
import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { StackedDateLabel } from "@/components/shared/date-field"
import { RowActionsMenu, type RowAction } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { ApiError } from "@/lib/api/client"
import {
  buildDdpTransitionMap,
  fetchActiveDdpDestinations,
  fetchDdpAggregations,
  fetchDdpStatuses,
  fetchDdpStatusTransitions,
} from "@/lib/api/ddp-config"
import { fetchDdpRows, updateDdpRow, deleteDdpRow } from "@/lib/api/project-ddp"
import { fetchCodex } from "@/lib/api/codex"
import type {
  CodexListItem,
  DdpRowItem,
  SupplierLookupItem,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { canAccessFeature, canWriteFeature } from "@/lib/auth/permissions"
import { useProjectHub } from "@/lib/signalr/use-project-hub"

import { AtecPickerDialog } from "./AtecPickerDialog"
import { CodexPickerDialog } from "./CodexPickerDialog"
import { DdpAtecAlternativesDialog } from "./DdpAtecAlternativesDialog"
import { CatalogAtecAssignDialog } from "@/features/catalogo/CatalogAtecAssignDialog"
import { CodexDaneaMappingDialog } from "@/features/codex/CodexDaneaMappingDialog"
import { DaneaOrderDialog } from "@/components/shared/danea-order-dialog"
import {
  DdpDestinationCell,
  DdpDestinationSpecCell,
} from "./DdpDestinationCell"
import { DdpInlineDateCell } from "./DdpInlineDateCell"
import { DdpInlineTextCell } from "./DdpInlineTextCell"
import { DdpQuantityStepper } from "./DdpQuantityStepper"
import { DdpSupplierCell } from "./DdpSupplierCell"
import { DdpItemHistoryDialog } from "./DdpItemHistoryDialog"
import { DdpRowDialog } from "./DdpRowDialog"
import { DdpStatusFilterBar } from "./DdpStatusFilterBar"
import { DdpStatusMenu } from "./DdpStatusMenu"
import { ddpCommercialRowToSaveRequest } from "./ddp-commercial-row"
import { confirmDdpRowAnnul, DDP_STATUS_CANCELLED } from "./ddp-annul-row"
import { isRawRow, RawRowBadge } from "./ddp-raw-row"
import {
  DDP_STATUS_TO_ORDER,
  ddpTransitionsPerUtente,
  isCommercialQtyEditable,
} from "./ddp-constants"
import { DdpStatusLegend } from "./DdpStatusLegend"
import {
  buildCompositionRows,
  collectParentIds,
} from "./ddp-composition-rows"
import { useDdpQuantityAdjust } from "./use-ddp-quantity-adjust"
import { toDateOnly } from "@/lib/date-iso"

function formatDate(value: string | null): string {
  if (!value) return "—"
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? "—" : formatDateShort(d)
}

/**
 * Quantità come la scrive il DdpQuantityStepper: serve alla versione in sola lettura,
 * dove le frecce spariscono ma il numero deve restare identico a quello che vedono
 * gli altri utenti (interi senza decimali, il resto con al massimo due).
 */
function formatQuantity(quantity: number): string {
  return Number.isInteger(quantity)
    ? String(quantity)
    : quantity.toLocaleString("it-IT", { maximumFractionDigits: 2 })
}

const COLUMN_LABELS: Record<string, string> = {
  rowNumber: "#",
  // Segnalazione #61: nomi e ordine delle DDP Excel — dopo la «#» vengono
  // «Inserito da» e «Data inserimento» (prima si chiamavano «Rich.» e «Data»,
  // ed erano nascoste di default, quindi per Paolo non esistevano).
  // «Creata da» è l'autore registrato dal server: resta anche se «Inserito da»
  // viene corretto a mano.
  requestedBy: "Inserito da",
  createdAt: "Data inserimento",
  createdByName: "Creata da",
  atecCode: "Cod. ATEC",
  partNumber: "Codice",
  description: "Descrizione",
  quantity: "Qtà",
  unit: "UM",
  supplierName: "Fornitore",
  manufacturer: "Produttore",
  itemStatus: "Stato",
  daneaRef: "Rif. Danea",
  dateNeeded: "Data Prevista",
  deliveredAt: "Consegnato il",
  destination: "Destinazione",
  destinationSpec: "Specifica",
  notes: "Note",
  unitCost: "€ Unit.",
  totalCost: "€ Totale",
}

export function ProjectDdpCommercial({ projectId }: { projectId: number }) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  // Sezione concessa in SOLA LETTURA (profili di permesso): la distinta si consulta,
  // si filtra e si esporta, ma niente scritture. A respingere davvero le scritture è
  // l'API (`RequireFeature` risponde 403): qui i comandi spariscono, altrimenti ogni
  // clic finirebbe in un errore rosso senza spiegare che manca il permesso.
  const readOnly = !canWriteFeature("project.ddp_commerciale")
  const canHardDelete = canWriteFeature("action.delete_ddp_row") && !readOnly
  const canMapAtec = canWriteFeature("action.assign_atec_code") && !readOnly
  // Link e cartello verso l'Inbox Acquisti solo per chi la può aprire: indicare
  // una pagina che risponde «Accesso negato» non aiuta nessuno.
  const canOpenAcquisti = canAccessFeature("nav.acquisti_inbox")
  // Cartello unico «la riga si lavora nell'Inbox Acquisti»: lo usano sia il cambio
  // stato in griglia sia il dialogo Modifica riga. Tace per chi non può aprirla.
  const avvisaInboxAcquisti = React.useCallback(() => {
    if (!canOpenAcquisti) return
    notifyInfo(
      "Riga in DA ORDINARE: gare e ordini si fanno da Acquisti → Inbox Acquisti (pulsante qui sopra)."
    )
  }, [canOpenAcquisti])
  const [searchParams] = useSearchParams()
  const highlightRowId = searchParams.get("item")
  const [dialogTarget, setDialogTarget] = React.useState<DdpRowItem | null>(null)
  const [pickerOpen, setPickerOpen] = React.useState(false)
  const [atecPickerOpen, setAtecPickerOpen] = React.useState(false)
  const [altsTarget, setAltsTarget] = React.useState<DdpRowItem | null>(null)
  /** Riga di cui si sta guardando la cronistoria degli stati. */
  const [storiaTarget, setStoriaTarget] = React.useState<DdpRowItem | null>(null)
  /** Riga senza ATEC: apre CatalogAtecAssignDialog (come Inbox Acquisti). */
  const [assignTarget, setAssignTarget] = React.useState<DdpRowItem | null>(null)
  /** #142 — 201 del grezzo «da associare»: apre CodexDaneaMappingDialog dalla riga. */
  const [rawMappingTarget, setRawMappingTarget] =
    React.useState<CodexListItem | null>(null)
  /** IDDoc dell'ordine Danea da mostrare nel popup di rendering (link sul Rif. Danea). */
  const [daneaOrderIdDoc, setDaneaOrderIdDoc] = React.useState<number | null>(null)
  // Rif. Danea a mano senza IdDoc (migrazione): il popup cerca per numero,
  // anche nel vecchio archivio. Attivo solo in sola lettura (in modifica la
  // cella è un campo di testo).
  const [daneaOrderRef, setDaneaOrderRef] = React.useState<string | null>(null)
  const [selectedStatusKeys, setSelectedStatusKeys] = React.useState<
    Set<string>
  >(() => new Set())

  React.useEffect(() => {
    if (highlightRowId) {
      setSelectedStatusKeys(new Set())
    }
  }, [highlightRowId])

  const rowsQuery = useQuery({
    queryKey: ["project-ddp", projectId, "COMMERCIAL"],
    queryFn: () => fetchDdpRows(projectId, "COMMERCIAL"),
    enabled: projectId > 0,
  })
  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  // Matrice avanzamenti (v7): restringe la finestra opzioni di menu ⋮ e dialog.
  const transitionsQuery = useQuery({
    queryKey: ["ddp-status-transitions"],
    queryFn: fetchDdpStatusTransitions,
  })
  // `undefined` = finestra completa: è così che chi ha il privilegio #140 vede tutti gli stati.
  const transitionMap = React.useMemo(
    () =>
      ddpTransitionsPerUtente(
        buildDdpTransitionMap(transitionsQuery.data ?? [], "COMMERCIAL")
      ),
    [transitionsQuery.data]
  )
  const destinationsQuery = useQuery({
    queryKey: ["ddp-destinations", "active"],
    queryFn: fetchActiveDdpDestinations,
  })
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
        queryKey: ["project-ddp", projectId, "COMMERCIAL"],
      }),
    [queryClient, projectId]
  )

  // #142 — dal codice al dialog di associazione degli articoli Danea: la riga porta
  // solo il CODICE (del grezzo, o l'ATEC della riga), la riga Codex vera si ripesca
  // dall'archivio. Lo usano la pillola «da associare» dei grezzi E l'icona sul
  // codice ATEC senza articoli (rovescio della codifica dal Catalogo, 01/09/2026).
  const apriAssociaCodice = React.useCallback(
    async (codice: string) => {
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
        setRawMappingTarget(codex)
      } catch (err) {
        notifyError(err)
      }
    },
    []
  )

  const onDdpChange = React.useCallback(
    (change: { ddpType: string }) => {
      if (change.ddpType?.toUpperCase() !== "OFFICINA") void invalidate()
    },
    [invalidate]
  )
  useProjectHub(projectId > 0 ? projectId : null, onDdpChange)

  // Errore comune agli update inline: 409 = conflitto di concorrenza, si ricarica per il token fresco.
  const onRowMutationError = React.useCallback(
    (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError(
          "La riga è stata modificata da un altro utente. Ricarica e riprova."
        )
        void invalidate()
        return
      }
      notifyError(err)
    },
    [invalidate]
  )

  const statusMutation = useMutation({
    mutationFn: ({
      row,
      statusKey,
    }: {
      row: DdpRowItem
      statusKey: string
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { itemStatus: statusKey })
      ),
    onSuccess: (_res, variables) => {
      invalidate()
      if (variables.statusKey === DDP_STATUS_TO_ORDER) avvisaInboxAcquisti()
    },
    onError: onRowMutationError,
  })

  const quantityMutation = useMutation({
    mutationFn: ({
      row,
      quantity,
      itemStatus,
    }: {
      row: DdpRowItem
      quantity?: number
      itemStatus?: string
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { itemStatus, quantity })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const destinationMutation = useMutation({
    mutationFn: ({
      row,
      destination,
      destinationSpec,
    }: {
      row: DdpRowItem
      destination: string
      destinationSpec?: string
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, {
          destination,
          destinationSpec,
        })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const destinationSpecMutation = useMutation({
    mutationFn: ({
      row,
      destinationSpec,
    }: {
      row: DdpRowItem
      destinationSpec: string
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { destinationSpec })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const supplierMutation = useMutation({
    mutationFn: ({
      row,
      supplier,
    }: {
      row: DdpRowItem
      supplier: SupplierLookupItem | null
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, {
          supplier: { id: supplier?.id ?? null },
        })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const daneaRefMutation = useMutation({
    mutationFn: ({ row, daneaRef }: { row: DdpRowItem; daneaRef: string }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { daneaRef })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const dateNeededMutation = useMutation({
    mutationFn: ({
      row,
      dateNeeded,
    }: {
      row: DdpRowItem
      dateNeeded: string | null
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { dateNeeded })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const deliveredAtMutation = useMutation({
    mutationFn: ({
      row,
      deliveredAt,
    }: {
      row: DdpRowItem
      deliveredAt: string | null
    }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { deliveredAt })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const notesMutation = useMutation({
    mutationFn: ({ row, notes }: { row: DdpRowItem; notes: string }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { notes })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  const requestedByMutation = useMutation({
    mutationFn: ({ row, requestedBy }: { row: DdpRowItem; requestedBy: string }) =>
      updateDdpRow(
        projectId,
        row.id,
        ddpCommercialRowToSaveRequest(projectId, row, { requestedBy })
      ),
    onSuccess: () => invalidate(),
    onError: onRowMutationError,
  })

  // Da qui in giù ogni scrittura parte da `readOnly === false`. Non basta togliere i
  // comandi dalla griglia: un campo lasciato aperto, un commit su blur o una scorciatoia
  // arriverebbero comunque qui, e la mutation partirebbe solo per prendersi un 403.
  const applyQuantityPatch = React.useCallback(
    (
      row: DdpRowItem,
      patch: { quantity?: number; itemStatus?: string }
    ) => {
      if (readOnly) {
        return
      }
      quantityMutation.mutate({ row, ...patch })
    },
    [quantityMutation, readOnly]
  )

  const handleStatusChange = React.useCallback(
    (row: DdpRowItem, statusKey: string) => {
      if (readOnly || statusKey === row.itemStatus || statusMutation.isPending) {
        return
      }
      statusMutation.mutate({ row, statusKey })
    },
    [statusMutation, readOnly]
  )

  const handleDestinationChange = React.useCallback(
    (row: DdpRowItem, destination: string) => {
      const nextSpec = !destination.trim()
        ? ""
        : !row.destination?.trim()
          ? ""
          : (row.destinationSpec ?? "")
      if (
        destination === (row.destination ?? "") &&
        nextSpec === (row.destinationSpec ?? "")
      ) {
        return
      }
      if (readOnly || destinationMutation.isPending) {
        return
      }
      destinationMutation.mutate({ row, destination, destinationSpec: nextSpec })
    },
    [destinationMutation, readOnly]
  )

  const handleDestinationSpecCommit = React.useCallback(
    (row: DdpRowItem, destinationSpec: string) => {
      if (
        readOnly ||
        !row.destination?.trim() ||
        destinationSpec === (row.destinationSpec ?? "") ||
        destinationSpecMutation.isPending
      ) {
        return
      }
      destinationSpecMutation.mutate({ row, destinationSpec })
    },
    [destinationSpecMutation, readOnly]
  )

  const handleSupplierChange = React.useCallback(
    (row: DdpRowItem, supplier: SupplierLookupItem | null) => {
      if (
        readOnly ||
        (supplier?.id ?? null) === (row.supplierId ?? null) ||
        supplierMutation.isPending
      ) {
        return
      }
      supplierMutation.mutate({ row, supplier })
    },
    [supplierMutation, readOnly]
  )

  const handleDaneaRefCommit = React.useCallback(
    (row: DdpRowItem, daneaRef: string) => {
      const next = daneaRef.trim()
      if (readOnly || next === (row.daneaRef ?? "") || daneaRefMutation.isPending) {
        return
      }
      daneaRefMutation.mutate({ row, daneaRef: next })
    },
    [daneaRefMutation, readOnly]
  )

  const handleDateNeededChange = React.useCallback(
    (row: DdpRowItem, dateNeeded: string | null) => {
      if (
        readOnly ||
        dateNeeded === toDateOnly(row.dateNeeded) ||
        dateNeededMutation.isPending
      ) {
        return
      }
      dateNeededMutation.mutate({ row, dateNeeded })
    },
    [dateNeededMutation, readOnly]
  )

  const handleDeliveredAtChange = React.useCallback(
    (row: DdpRowItem, deliveredAt: string | null) => {
      if (
        readOnly ||
        deliveredAt === toDateOnly(row.deliveredAt) ||
        deliveredAtMutation.isPending
      ) {
        return
      }
      deliveredAtMutation.mutate({ row, deliveredAt })
    },
    [deliveredAtMutation, readOnly]
  )

  const handleNotesCommit = React.useCallback(
    (row: DdpRowItem, notes: string) => {
      const next = notes.trim()
      if (readOnly || next === (row.notes ?? "") || notesMutation.isPending) {
        return
      }
      notesMutation.mutate({ row, notes: next })
    },
    [notesMutation, readOnly]
  )

  const handleRequestedByCommit = React.useCallback(
    (row: DdpRowItem, requestedBy: string) => {
      const next = requestedBy.trim()
      if (
        readOnly ||
        next === (row.requestedBy ?? "") ||
        requestedByMutation.isPending
      ) {
        return
      }
      requestedByMutation.mutate({ row, requestedBy: next })
    },
    [requestedByMutation, readOnly]
  )

  const statuses = statusesQuery.data ?? []
  const destinations = React.useMemo(
    () => destinationsQuery.data ?? [],
    [destinationsQuery.data]
  )

  const statusMap = React.useMemo(
    () => new Map(statuses.map((s) => [s.statusKey, s])),
    [statuses]
  )

  const handleAnnulRow = React.useCallback(
    async (row: DdpRowItem) => {
      if (
        readOnly ||
        row.itemStatus === DDP_STATUS_CANCELLED ||
        statusMutation.isPending
      ) {
        return
      }
      const rowLabel = row.partNumber || row.description || "questa riga"
      const ok = await confirmDdpRowAnnul(confirm, statusMap, rowLabel)
      if (ok) {
        statusMutation.mutate({ row, statusKey: DDP_STATUS_CANCELLED })
      }
    },
    [confirm, statusMap, statusMutation, readOnly]
  )

  const deleteMutation = useMutation({
    mutationFn: (row: DdpRowItem) => deleteDdpRow(projectId, row.id),
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const handleDeleteRow = React.useCallback(
    async (row: DdpRowItem) => {
      if (readOnly || deleteMutation.isPending) {
        return
      }
      const rowLabel = row.partNumber || row.description || "questa riga"
      const ok = await confirm({
        title: "Eliminare definitivamente la riga?",
        description: `La riga "${rowLabel}" verrà eliminata definitivamente dalla distinta.\n\nL'operazione non è reversibile.`,
        confirmLabel: "Elimina definitivamente",
        destructive: true,
      })
      if (ok) {
        deleteMutation.mutate(row)
      }
    },
    [confirm, deleteMutation, readOnly]
  )

  const handleQuantityAdjust = useDdpQuantityAdjust({
    confirm,
    statusMap,
    // In sola lettura si ferma prima ancora della richiesta di conferma: altrimenti
    // comparirebbe la domanda «Annullare la riga?» per una scrittura che non può avvenire.
    isPending: quantityMutation.isPending || readOnly,
    excludedSet,
    onApply: applyQuantityPatch,
  })

  // #119 — composizione: le righe importate da un gruppo Codex stanno sotto la loro
  // intestazione, che ne somma i costi. Stessa meccanica della DDP Officina, condivisa in
  // `ddp-composition-rows`: qui cambia solo il campo padre (`parentBomItemId`).
  const rawRows = React.useMemo(() => rowsQuery.data ?? [], [rowsQuery.data])
  const parentIdsWithChildren = React.useMemo(
    () => collectParentIds(rawRows, (r) => r.parentBomItemId),
    [rawRows]
  )
  const rows = React.useMemo(
    () =>
      buildCompositionRows(
        rawRows,
        (r) => r.parentBomItemId,
        parentIdsWithChildren,
        (r) => r.unitCost
      ),
    [rawRows, parentIdsWithChildren]
  )
  const [collapsedParentIds, setCollapsedParentIds] = React.useState<Set<number>>(
    () => new Set()
  )
  const toggleParentCollapse = React.useCallback((parentId: number) => {
    setCollapsedParentIds((prev) => {
      const next = new Set(prev)
      if (next.has(parentId)) next.delete(parentId)
      else next.add(parentId)
      return next
    })
  }, [])

  const statusFilterItems = React.useMemo(() => {
    const counts = new Map<string, number>()
    for (const row of rows) {
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
          a.sortOrder - b.sortOrder ||
          a.label.localeCompare(b.label, "it")
      )
  }, [rows, statusMap])

  const statusFilteredRows = React.useMemo(() => {
    const perStato =
      selectedStatusKeys.size === 0
        ? rows
        : rows.filter((row) => selectedStatusKeys.has(row.itemStatus ?? ""))
    // I componenti spariscono quando il loro padre è collassato.
    return perStato.filter((row) =>
      row.parentBomItemId != null
        ? !collapsedParentIds.has(row.parentBomItemId)
        : true
    )
  }, [rows, selectedStatusKeys, collapsedParentIds])

  const columns = React.useMemo<ColumnDef<DdpRowItem>[]>(
    () => [
      {
        accessorKey: "rowNumber",
        header: "#",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="tabular-nums opacity-80">{row.original.rowNumber}</span>
        ),
      },
      {
        accessorKey: "requestedBy",
        header: "Inserito da",
        // Lo compila da solo il picker col nome di chi è collegato; resta correggibile
        // a mano come nell'Excel (riga importata, o inserimento fatto per conto di altri).
        // In sola lettura è testo: un input disabilitato somiglia troppo a un campo
        // rotto, e qui il dato si deve solo leggere.
        cell: ({ row }) =>
          readOnly ? (
            <span>{row.original.requestedBy || "—"}</span>
          ) : (
            <DdpInlineTextCell
              value={row.original.requestedBy ?? ""}
              disabled={requestedByMutation.isPending}
              placeholder="—"
              onCommit={(value) => handleRequestedByCommit(row.original, value)}
            />
          ),
      },
      {
        accessorKey: "createdAt",
        header: "Data inserimento",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="whitespace-nowrap">
            {formatDate(row.original.createdAt)}
          </span>
        ),
      },
      {
        // «Creata da» è l'AUTORE VERO, scritto dal server a chi era collegato: serve quando
        // «Inserito da» qui sopra è stato corretto a mano e non dice più chi ha creato la riga.
        // Nasce NASCOSTA (il menu «Colonne» la riaccende).
        accessorKey: "createdByName",
        header: "Creata da",
        cell: ({ row }) => (
          <span className="whitespace-nowrap opacity-80">
            {row.original.createdByName || "—"}
          </span>
        ),
      },
      {
        accessorKey: "atecCode",
        header: "Cod. ATEC",
        cell: ({ row }) => {
          const item = row.original
          if (item.atecCode) {
            // Codice ATEC senza NESSUN articolo commerciale (01/09/2026): l'icona
            // catena ambra apre l'associazione al volo — il rovescio della codifica
            // dal Catalogo. I grezzi hanno già la loro pillola sul Codice: esclusi.
            const daAssociare =
              canMapAtec && item.atecNeedsMapping && !isRawRow(item)
            return (
              <span className="flex items-center gap-1">
                <span className="font-medium tabular-nums">{item.atecCode}</span>
                {daAssociare ? (
                  <button
                    type="button"
                    className="shrink-0 rounded p-0.5 text-amber-600 hover:bg-black/10 dark:text-amber-400"
                    title={`Il codice ${item.atecCode} non ha nessun articolo commerciale associato: clic per associarlo a un articolo Danea`}
                    onClick={(e) => {
                      e.stopPropagation()
                      void apriAssociaCodice(item.atecCode ?? "")
                    }}
                  >
                    <Link2 className="size-4" />
                  </button>
                ) : null}
              </span>
            )
          }
          // Stesso pattern Inbox Acquisti: icona a destra se manca l'ATEC e si può mappare.
          const canAssign =
            (item.catalogItemId != null && item.catalogItemId > 0) ||
            (item.partNumber ?? "").trim().length > 0
          if (canMapAtec && canAssign) {
            return (
              <div className="flex justify-end">
                <button
                  type="button"
                  title="Assegna codice ATEC"
                  className="rounded p-1 opacity-60 hover:bg-black/10 hover:opacity-100"
                  onClick={(e) => {
                    e.stopPropagation()
                    setAssignTarget(item)
                  }}
                >
                  <Link2 className="size-4" />
                </button>
              </div>
            )
          }
          return <span className="opacity-60">—</span>
        },
      },
      {
        accessorKey: "partNumber",
        header: "Codice",
        cell: ({ row }) => {
          const item = row.original
          const isChild = item.parentBomItemId != null
          const hasChildren = parentIdsWithChildren.has(item.id)
          const isCollapsed = collapsedParentIds.has(item.id)
          return (
            <span className="flex items-center gap-1 font-medium">
              {isChild ? (
                <span
                  className="mr-1 select-none"
                  title={`Componente di composizione (${item.compositionQty ?? 1} per padre): segue la quantità del padre`}
                >
                  ↳
                </span>
              ) : hasChildren ? (
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation()
                    toggleParentCollapse(item.id)
                  }}
                  className="mr-1 inline-flex size-5 items-center justify-center rounded hover:bg-muted"
                  title={isCollapsed ? "Espandi componenti" : "Collassa componenti"}
                >
                  {isCollapsed ? (
                    <ChevronRight className="size-4" strokeWidth={2.5} />
                  ) : (
                    <ChevronDown className="size-4" strokeWidth={2.5} />
                  )}
                </button>
              ) : null}
              {item.partNumber || "—"}
              {/* #135: la riga è il grezzo di uno o più particolari a disegno — non è
                  una riga come le altre e non si toglie da qui (title del badge).
                  #142: se il grezzo è «scoperto» la pillola apre l'associazione del 201. */}
              {isRawRow(item) ? (
                <RawRowBadge
                  row={item}
                  onAssocia={
                    canMapAtec && item.rawNeedsMapping
                      ? () => void apriAssociaCodice(item.rawCodexCode ?? "")
                      : undefined
                  }
                />
              ) : null}
            </span>
          )
        },
      },
      {
        accessorKey: "description",
        header: "Descrizione",
        cell: ({ row }) => (
          <span
            className="block min-w-[280px] max-w-[420px] line-clamp-2 whitespace-normal break-words leading-snug"
            title={row.original.description}
          >
            {row.original.description || "—"}
          </span>
        ),
      },
      {
        accessorKey: "quantity",
        header: "Qtà",
        enableColumnFilter: false,
        cell: ({ row }) => {
          const item = row.original
          // #135 — grezzo con la quantità corretta a mano: RESTA modificabile (da una
          // barra escono più pezzi), si segna solo che non è più quella della distinta.
          // Da lì in poi il ricalcolo del server non la tocca più.
          const autoQty =
            item.rawAutoQty != null && item.rawAutoQty !== item.quantity
              ? item.rawAutoQty
              : null
          const autoQtyMark =
            autoQty == null ? null : (
              <span
                className="shrink-0 opacity-60"
                title={`Corretta a mano: la distinta ne chiede ${formatQuantity(autoQty)}`}
              >
                <Pencil className="size-3" />
              </span>
            )
          // Sola lettura: via le frecce +/- (sono una scrittura), resta il numero.
          if (readOnly) {
            return (
              <span className="flex items-center gap-1">
                <span className="text-sm tabular-nums">
                  {formatQuantity(item.quantity)}
                </span>
                {autoQtyMark}
              </span>
            )
          }
          const canEditQty = isCommercialQtyEditable(item.itemStatus)
          // #119: un componente non ha quantità propria, segue quella del padre.
          const isChild = item.parentBomItemId != null
          const atMin =
            item.quantity <= 1 && item.itemStatus === DDP_STATUS_CANCELLED
          return (
            <span className="flex items-center gap-1">
              <span
                title={
                  isChild
                    ? "Componente di composizione: la quantità segue quella del padre"
                    : canEditQty
                      ? undefined
                      : 'La quantità è modificabile solo negli stati "Verificare se disponibile a mag." e "Da Ordinare".'
                }
              >
                <DdpQuantityStepper
                  quantity={item.quantity}
                  disabled={quantityMutation.isPending || !canEditQty || isChild}
                  decrementDisabled={atMin}
                  onIncrement={() => void handleQuantityAdjust(item, 1)}
                  onDecrement={() => void handleQuantityAdjust(item, -1)}
                />
              </span>
              {autoQtyMark}
            </span>
          )
        },
      },
      {
        accessorKey: "unit",
        header: "UM",
        // Filtro stretto su misura: l'input standard a larghezza intrinseca
        // allargherebbe la colonna ben oltre i 4-5 caratteri delle UM.
        meta: {
          filterInput: ({ value, onChange }) => (
            <Input
              value={(value as string) ?? ""}
              className="h-8 w-14 bg-background dark:bg-background"
              onChange={(event) => onChange(event.target.value)}
            />
          ),
        },
        cell: ({ row }) => row.original.unit || "—",
      },
      {
        accessorKey: "supplierName",
        header: "Fornitore",
        // Sola lettura: resta il nome, sparisce il «⋮» che apre la ricerca fornitori.
        cell: ({ row }) =>
          readOnly ? (
            <span
              className="block max-w-[160px] truncate whitespace-nowrap"
              title={row.original.supplierName || undefined}
            >
              {row.original.supplierName || "—"}
            </span>
          ) : (
            <DdpSupplierCell
              supplierId={row.original.supplierId ?? null}
              supplierName={row.original.supplierName || ""}
              disabled={supplierMutation.isPending}
              onSupplierChange={(supplier) =>
                handleSupplierChange(row.original, supplier)
              }
            />
          ),
      },
      {
        accessorKey: "manufacturer",
        header: "Produttore",
        cell: ({ row }) => row.original.manufacturer || "—",
      },
      {
        id: "itemStatus",
        accessorFn: (r) => statusMap.get(r.itemStatus)?.label ?? r.itemStatus,
        header: "Stato",
        cell: ({ row }) => {
          const s = statusMap.get(row.original.itemStatus)
          return (
            <div className="flex min-w-[120px] items-center gap-1">
              <span className="min-w-0 flex-1 truncate font-semibold whitespace-nowrap">
                {s ? s.label : row.original.itemStatus || "—"}
              </span>
              {/* Il menu «⋮» cambia lo stato della riga: in sola lettura non compare.
                  #142: un grezzo «scoperto» non avanza — menu spento (il server
                  rifiuterebbe comunque), la spiegazione sta nel title. */}
              {readOnly ? null : row.original.rawNeedsMapping ? (
                <span title="Grezzo da associare: il 201 di derivazione non è associato a nessun articolo commerciale. Associa l'articolo (pillola «da associare» sul Codice) e lo stato si sblocca.">
                  <DdpStatusMenu
                    currentStatusKey={row.original.itemStatus}
                    statuses={statuses}
                    transitions={transitionMap}
                    disabled
                    onSelect={() => undefined}
                  />
                </span>
              ) : (
                <DdpStatusMenu
                  currentStatusKey={row.original.itemStatus}
                  statuses={statuses}
                  transitions={transitionMap}
                  disabled={statusMutation.isPending}
                  onSelect={(statusKey) =>
                    handleStatusChange(row.original, statusKey)
                  }
                />
              )}
            </div>
          )
        },
      },
      {
        accessorKey: "daneaRef",
        header: "Rif. Danea",
        // REGOLA (01/09/2026, Diego): il link all'ordine è SEMPRE E SOLO l'occhio —
        // il numero resta testo (o campo editabile), mai un link sottolineato. Uguale
        // in tutte le combinazioni e uguale alla DDP Officina. Con l'ordine generato
        // da ATEC PM l'occhio apre per IDDoc e il rif non si edita inline (si cambia
        // dal dialog Modifica, e il server in quel caso stacca il link); con un rif
        // scritto a mano apre la ricerca per numero (anche nel VECCHIO archivio).
        cell: ({ row }) => {
          const item = row.original
          const rif = (item.daneaRef ?? "").trim()
          const perIdDoc = item.daneaOrderIdDoc != null
          const apriOrdine = perIdDoc
            ? () => setDaneaOrderIdDoc(item.daneaOrderIdDoc!)
            : rif
              ? () => setDaneaOrderRef(rif)
              : null
          const occhio = apriOrdine ? (
            <button
              type="button"
              className="shrink-0 rounded p-0.5 text-teal-700 hover:bg-accent hover:text-teal-800"
              title={
                perIdDoc
                  ? "Apri ordine Danea"
                  : `Apri l'ordine n. ${rif} (cerca in Danea, anche nel vecchio archivio)`
              }
              onClick={(e) => {
                e.stopPropagation()
                apriOrdine()
              }}
            >
              <Eye className="size-4" />
            </button>
          ) : null

          if (perIdDoc || readOnly) {
            return (
              <div className="flex items-center gap-1">
                <span className="min-w-0 flex-1 truncate font-medium">
                  {rif || "—"}
                </span>
                {occhio}
              </div>
            )
          }
          return (
            <div className="flex items-center gap-1">
              <div className="min-w-0 flex-1">
                <DdpInlineTextCell
                  value={item.daneaRef ?? ""}
                  disabled={daneaRefMutation.isPending}
                  placeholder="—"
                  onCommit={(value) => handleDaneaRefCommit(item, value)}
                />
              </div>
              {occhio}
            </div>
          )
        },
      },
      {
        accessorKey: "dateNeeded",
        header: "Data Prevista",
        enableColumnFilter: false,
        cell: ({ row }) =>
          readOnly ? (
            row.original.dateNeeded ? (
              <StackedDateLabel value={toDateOnly(row.original.dateNeeded)} />
            ) : (
              <span className="whitespace-nowrap">—</span>
            )
          ) : (
            <DdpInlineDateCell
              value={toDateOnly(row.original.dateNeeded)}
              disabled={dateNeededMutation.isPending}
              onChange={(value) => handleDateNeededChange(row.original, value)}
            />
          ),
      },
      {
        accessorKey: "deliveredAt",
        header: "Consegnato il",
        enableColumnFilter: false,
        cell: ({ row }) =>
          readOnly ? (
            row.original.deliveredAt ? (
              <StackedDateLabel value={toDateOnly(row.original.deliveredAt)} />
            ) : (
              <span className="whitespace-nowrap">—</span>
            )
          ) : (
            <DdpInlineDateCell
              value={toDateOnly(row.original.deliveredAt)}
              disabled={deliveredAtMutation.isPending}
              onChange={(value) =>
                handleDeliveredAtChange(row.original, value)
              }
            />
          ),
      },
      {
        accessorKey: "destination",
        header: "Destinazione",
        // Sola lettura: resta l'etichetta, sparisce il «⋮» che sceglie la destinazione.
        cell: ({ row }) =>
          readOnly ? (
            <span className="block truncate font-semibold whitespace-nowrap">
              {row.original.destination || "—"}
            </span>
          ) : (
            <DdpDestinationCell
              destination={row.original.destination ?? ""}
              destinations={destinations}
              disabled={destinationMutation.isPending}
              onDestinationChange={(destination) =>
                handleDestinationChange(row.original, destination)
              }
            />
          ),
      },
      {
        accessorKey: "destinationSpec",
        header: "Specifica",
        cell: ({ row }) =>
          readOnly ? (
            <span className={row.original.destinationSpec ? undefined : "opacity-60"}>
              {row.original.destinationSpec || "—"}
            </span>
          ) : (
            <DdpDestinationSpecCell
              destination={row.original.destination ?? ""}
              destinationSpec={row.original.destinationSpec ?? ""}
              disabled={destinationSpecMutation.isPending}
              onSpecCommit={(destinationSpec) =>
                handleDestinationSpecCommit(row.original, destinationSpec)
              }
            />
          ),
      },
      {
        accessorKey: "notes",
        header: "Note",
        // Sola lettura: le note restano leggibili per intero (vanno a capo come
        // nella textarea), ma non si scrivono.
        cell: ({ row }) =>
          readOnly ? (
            <span className="block max-w-[260px] whitespace-normal break-words">
              {row.original.notes || "—"}
            </span>
          ) : (
            <DdpInlineTextCell
              value={row.original.notes ?? ""}
              disabled={notesMutation.isPending}
              placeholder="—"
              multiline
              onCommit={(value) => handleNotesCommit(row.original, value)}
            />
          ),
      },
      {
        accessorKey: "unitCost",
        header: "€ Unit.",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="tabular-nums">{euro(row.original.unitCost)}</span>
        ),
      },
      {
        accessorKey: "totalCost",
        header: "€ Totale",
        enableColumnFilter: false,
        cell: ({ row }) => (
          <span className="font-semibold tabular-nums">
            {euro(row.original.totalCost)}
          </span>
        ),
      },
      {
        id: "actions",
        header: "",
        enableHiding: false,
        enableColumnFilter: false,
        cell: ({ row }) => {
          const item = row.original
          // In sola lettura resta la sola Cronistoria: «Modifica» apre un dialog che
          // salva, e «Alternative dal mapping…» sostituisce il codice della riga.
          const actions: RowAction[] = readOnly
            ? [
                {
                  label: "Cronistoria",
                  icon: History,
                  onClick: () => setStoriaTarget(item),
                },
              ]
            : [
                {
                  label: "Modifica",
                  icon: Pencil,
                  onClick: () => setDialogTarget(item),
                },
                {
                  label: "Cronistoria",
                  icon: History,
                  onClick: () => setStoriaTarget(item),
                },
              ]
          if (!readOnly && item.atecCode) {
            actions.push({
              label: "Alternative dal mapping…",
              icon: Link2,
              onClick: () => setAltsTarget(item),
            })
          }
          // #119: un componente non si annulla e non si elimina da solo — se ne va col
          // padre, altrimenti la composizione resterebbe monca senza che nessuno lo veda.
          // #135: il grezzo non si CANCELLA da qui — lo comanda la DDP Officina e il
          // server rifiuta già la DELETE: serve che non sembri nemmeno possibile. Il
          // perché sta nel title del badge «Grezzo» in colonna «Codice», perché sulle
          // voci disabilitate del menu un title non comparirebbe mai (pointer-events).
          // 🪤 L'ANNULLO invece resta: «questo grezzo ce l'abbiamo già a magazzino» è una
          // decisione di chi compra, e il ricalcolo la rispetta (su una riga fuori dagli
          // stati d'ingresso non scrive più la quantità).
          const isChild = item.parentBomItemId != null
          const isRaw = isRawRow(item)
          if (!readOnly && item.itemStatus !== DDP_STATUS_CANCELLED) {
            // «Annulla riga», non «Elimina»: la riga resta in distinta in stato
            // Annullato. Stessa etichetta e stessa icona della DDP Officina.
            actions.push({
              label: "Annulla riga",
              icon: Ban,
              destructive: true,
              separatorBefore: true,
              disabled: isChild,
              onClick: () => void handleAnnulRow(item),
            })
          }
          if (canHardDelete) {
            actions.push({
              label: "Elimina definitivamente",
              icon: Trash2,
              destructive: true,
              separatorBefore: item.itemStatus === DDP_STATUS_CANCELLED,
              disabled: isChild || isRaw,
              onClick: () => void handleDeleteRow(item),
            })
          }
          return (
            <RowActionsMenu
              label={item.partNumber || item.description}
              actions={actions}
            />
          )
        },
      },
    ],
    [
      // #119: senza queste tre le colonne non si ricostruiscono al collasso e il
      // chevron resta girato dalla parte sbagliata.
      collapsedParentIds,
      parentIdsWithChildren,
      toggleParentCollapse,
      statusMap,
      statuses,
      destinations,
      handleAnnulRow,
      handleDeleteRow,
      canHardDelete,
      handleStatusChange,
      handleDestinationChange,
      handleDestinationSpecCommit,
      handleSupplierChange,
      handleDaneaRefCommit,
      handleDateNeededChange,
      handleNotesCommit,
      handleRequestedByCommit,
      requestedByMutation.isPending,
      statusMutation.isPending,
      destinationMutation.isPending,
      destinationSpecMutation.isPending,
      quantityMutation.isPending,
      supplierMutation.isPending,
      daneaRefMutation.isPending,
      dateNeededMutation.isPending,
      notesMutation.isPending,
      handleQuantityAdjust,
      excludedSet,
      transitionMap,
      canMapAtec,
      apriAssociaCodice,
      // Senza questa dipendenza le celle resterebbero quelle scrivibili della prima
      // costruzione delle colonne (memo non ricalcolato) nonostante la sola lettura.
      readOnly,
    ]
  )

  // Il totale include solo le righe non escluse (aggregazione A9). Le escluse sono contate a parte.
  // #119: i componenti NON sommano — il loro costo è già arrotolato nell'intestazione del
  // gruppo, contarli di nuovo raddoppierebbe il valore della distinta.
  const totalValue = rows.reduce(
    (s, r) =>
      r.parentBomItemId != null || excludedSet.has(r.itemStatus)
        ? s
        : s + (r.totalCost || 0),
    0
  )
  const excludedRows = rows.filter(
    (r) => r.parentBomItemId == null && excludedSet.has(r.itemStatus)
  )
  const excludedValue = excludedRows.reduce((s, r) => s + (r.totalCost || 0), 0)

  const rowStyle = React.useCallback(
    (row: DdpRowItem): React.CSSProperties | undefined => {
      const s = statusMap.get(row.itemStatus)
      const base: React.CSSProperties = s
        ? { backgroundColor: s.colorBg, color: s.colorFg }
        : {}
      // #142: grezzo «scoperto» — bordo interno ambra che pulsa (keyframes in index.css)
      // finché il 201 non viene associato a un articolo commerciale.
      if (row.rawNeedsMapping) {
        return { ...base, animation: "ddp-raw-blink 1.4s ease-in-out infinite" }
      }
      return s ? base : undefined
    },
    [statusMap]
  )

  return (
    <>
      <DataTableCardFiltered
        title="DDP commerciale"
        description="Distinta materiali commerciali della commessa"
        columns={columns}
        data={statusFilteredRows}
        columnLabels={COLUMN_LABELS}
        isLoading={rowsQuery.isLoading}
        isFetching={rowsQuery.isFetching}
        error={rowsQuery.error as Error | null}
        onRefresh={() => rowsQuery.refetch()}
        searchPlaceholder="Cerca nella distinta…"
        rowNoun="righe"
        emptyMessage="Nessuna riga nella distinta commerciale."
        getRowId={(r) => String(r.id)}
        highlightRowId={highlightRowId}
        // Il doppio clic apre il dialog di modifica (che salva): in sola lettura la riga
        // non si apre più di lì, la consultazione passa dalla Cronistoria nel menu riga.
        onRowDoubleClick={readOnly ? undefined : (r) => setDialogTarget(r)}
        rowStyle={rowStyle}
        // Reticolo anche qui: la richiesta della #58 è sulla leggibilità «nella compilazione
        // delle DDP», e la distinta commerciale si compila nella scheda accanto a quella
        // officina — due tabelle gemelle con la griglia solo su una si notano subito.
        gridLines
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
          manufacturer: false,
          // «Creata da» (autore registrato dal server) nasce nascosta: serve solo quando
          // si vuole sapere chi ha creato davvero la riga, e la griglia è già larga.
          createdByName: false,
        }}
        // v4: colonna Cod. ATEC + picker per codice ATEC.
        // v6 (#61): «Inserito da» e «Data inserimento» nascono VISIBILI. La chiave va
        // versionata o chi ha già usato la pagina se le ritroverebbe ancora nascoste
        // (la scelta vecchia è salvata in localStorage) — stessa trappola della #58.
        // v7 (#61): arriva «Creata da», che deve nascere NASCOSTA — e senza versionare la
        // chiave comparirebbe visibile proprio a chi ha già una scelta salvata
        // (il valore letto da localStorage sostituisce initialColumnVisibility, non lo fonde).
        visibilityStorageKey="table-visibility-ddp-commerciale-v7"
        toolbarActions={
          <>
            <DdpStatusLegend statuses={statuses} />
            {/* Andata del giro acquisti: le righe in DA ORDINARE si lavorano lì. */}
            {canOpenAcquisti ? (
              <Button
                size="sm"
                variant="outline"
                asChild
                title="Le righe in DA ORDINARE si lavorano nell'Inbox Acquisti (gare RDO e ordini Danea)"
              >
                <Link to="/acquisti">Inbox Acquisti</Link>
              </Button>
            ) : null}
            <span className="self-center text-sm font-medium">
              Totale:{" "}
              <span className="text-lg font-bold tabular-nums ml-1 text-blue-600 dark:text-blue-400">
                {euro(totalValue)}
              </span>
              {excludedRows.length > 0 ? (
                <span className="ml-2 font-normal text-muted-foreground">
                  · escluse {excludedRows.length} ({euro(excludedValue)})
                </span>
              ) : null}
            </span>
            {/* Il totale resta (è una lettura); i due picker aggiungono righe alla
                distinta, quindi in sola lettura spariscono. */}
            {readOnly ? null : (
              <>
                <Button
                  size="sm"
                  variant="outline"
                  title="Se conosci già il codice ATEC: cercalo e scegli il fornitore"
                  onClick={() => setAtecPickerOpen(true)}
                >
                  <Link2 />
                  Per codice ATEC
                </Button>
                {/* Stesso nome e stesso picker del pulsante della DDP Officina:
                    è UN pulsante unico, lo smistamento lo fa il programma. */}
                <Button
                  size="sm"
                  title="Cerca nel catalogo articoli e nel Codex e aggiungi righe alla distinta"
                  onClick={() => setPickerOpen(true)}
                >
                  <Plus />
                  Aggiungi articolo
                </Button>
              </>
            )}
          </>
        }
      />

      {/* Dialoghi che scrivono: in sola lettura non si montano nemmeno — i comandi che
          li aprivano non ci sono più, e questa è la rete di sicurezza se ne sfugge uno
          (un `open` rimasto a true mostrerebbe il pulsante Salva). Restano montati
          Cronistoria e ordine Danea, che sono sola consultazione. */}
      <DdpRowDialog
        open={!readOnly && dialogTarget !== null}
        projectId={projectId}
        target={dialogTarget}
        statuses={statuses}
        transitions={transitionMap}
        destinations={destinations}
        onClose={() => setDialogTarget(null)}
        onSaved={async (savedStatusKey) => {
          // Lo stato di partenza va letto PRIMA di azzerare il target.
          const statoPrima = dialogTarget?.itemStatus
          setDialogTarget(null)
          await invalidate()
          // Stesso cartello del cambio stato in griglia: senza, chi passa dal
          // dialogo Modifica non saprebbe che il giro prosegue nell'Inbox.
          if (
            savedStatusKey === DDP_STATUS_TO_ORDER &&
            statoPrima !== DDP_STATUS_TO_ORDER
          ) {
            avvisaInboxAcquisti()
          }
        }}
        onConflict={() => void invalidate()}
      />

      <DdpItemHistoryDialog
        open={storiaTarget !== null}
        onOpenChange={(open) => {
          if (!open) setStoriaTarget(null)
        }}
        kind="COMMERCIAL"
        itemId={storiaTarget?.id ?? null}
        itemLabel={
          storiaTarget
            ? `${storiaTarget.partNumber ?? ""} ${storiaTarget.description ?? ""}`.trim()
            : undefined
        }
      />

      <CodexPickerDialog
        open={!readOnly && pickerOpen}
        projectId={projectId}
        ddpType="COMMERCIAL"
        onClose={() => setPickerOpen(false)}
        onAdded={() => void invalidate()}
      />

      <AtecPickerDialog
        open={!readOnly && atecPickerOpen}
        projectId={projectId}
        onClose={() => setAtecPickerOpen(false)}
        onAdded={() => void invalidate()}
      />

      <DdpAtecAlternativesDialog
        open={!readOnly && altsTarget !== null}
        projectId={projectId}
        row={altsTarget}
        onClose={() => setAltsTarget(null)}
        onApplied={() => void invalidate()}
      />

      <CatalogAtecAssignDialog
        item={null}
        bomTarget={
          assignTarget && !readOnly
            ? {
                bomItemId: assignTarget.id,
                partNumber: assignTarget.partNumber ?? "",
                description: assignTarget.description ?? "",
              }
            : null
        }
        onClose={() => setAssignTarget(null)}
        onSaved={() => {
          setAssignTarget(null)
          void invalidate()
        }}
      />

      <DaneaOrderDialog
        idDoc={daneaOrderIdDoc}
        daneaRef={daneaOrderRef}
        onClose={() => {
          setDaneaOrderIdDoc(null)
          setDaneaOrderRef(null)
        }}
      />

      {/* #142 — associazione del 201 del grezzo, aperta dalla pillola «da associare».
          Alla chiusura la lista si ricarica: se l'articolo è stato agganciato, flag e
          lampeggio spariscono da soli (il server li calcola, non li salviamo noi). */}
      {rawMappingTarget ? (
        <CodexDaneaMappingDialog
          item={rawMappingTarget}
          onClose={() => {
            setRawMappingTarget(null)
            void invalidate()
          }}
        />
      ) : null}
    </>
  )
}
