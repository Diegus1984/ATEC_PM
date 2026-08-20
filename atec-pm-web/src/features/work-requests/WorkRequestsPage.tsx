import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { AlertTriangle, Box, Factory, FlaskConical, Plus, Truck } from "lucide-react"

import { DataTableCardFiltered } from "@/components/shared/data-table-card-filtered"
import { DateField } from "@/components/shared/date-field"
import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { PmSidebar } from "@/components/shared/pm-sidebar"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { ApiError } from "@/lib/api/client"
import { fetchDdpStatuses } from "@/lib/api/ddp-config"
import { fetchProjects } from "@/lib/api/projects"
import {
  createWorkRequest,
  deleteWorkRequest,
  patchWorkRequestField,
  updateWorkRequest,
} from "@/lib/api/workRequests"
import { fetchWorkshopRows, patchWorkshopField } from "@/lib/api/workshop"
import type {
  DdpStatusItem,
  WorkRequestSaveRequest,
  WorkshopRow,
} from "@/lib/api/types"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { useWorkRequestsHub } from "@/lib/signalr/use-work-requests-hub"
import { notifyError } from "@/lib/toast"
import { useDeferredItemOrder } from "@/lib/use-deferred-item-order"

import { WorkshopManualDialog } from "./WorkshopManualDialog"
import {
  buildWorkshopColumns,
  WORKSHOP_COLUMN_LABELS,
} from "./workshop-columns"
import {
  DESCRIZIONI_VISTA,
  TITOLI_VISTA,
  dataDiVista,
  filtraPerVista,
  isEsterna,
  isInterna,
  isStampa3D,
  isTrattamento,
  isUrgenza,
  rowKey,
  type WorkshopView,
} from "./workshop-shared"

/**
 * «Lavorazioni Officine» (#83). La pagina non tiene più copie delle righe di distinta —
 * niente bozze da promuovere, niente tracciamento consegne a parte: mostra le righe della
 * DDP Officina di tutte le commesse, divise nelle viste che servono in officina, e
 * ne possiede tre soli campi (Data Richiesta, Note, Urgente). Tutto il resto si cambia
 * nella distinta, che ne resta l'unica padrona.
 */
export function WorkRequestsPage() {
  const queryClient = useQueryClient()
  const [view, setView] = React.useState<WorkshopView>("interne")
  const [filtroCommessa, setFiltroCommessa] = React.useState<number | null>(null)
  const [dataDa, setDataDa] = React.useState<string | null>(null)
  const [dataA, setDataA] = React.useState<string | null>(null)
  const [dialogAperto, setDialogAperto] = React.useState(false)
  const [rigaInModifica, setRigaInModifica] = React.useState<WorkshopRow | null>(null)
  /** L'ordine a video resta fermo finché non si preme «Aggiorna» o non si cambia vista. */
  const [layoutEpoch, setLayoutEpoch] = React.useState(0)

  const righeQuery = useQuery({
    queryKey: ["workshop-rows"],
    queryFn: () => fetchWorkshopRows(),
  })

  const projectsQuery = useQuery({
    queryKey: ["projects"],
    queryFn: () => fetchProjects({ pageSize: 1000 }),
  })
  const projects = React.useMemo(
    () => projectsQuery.data?.items ?? [],
    [projectsQuery.data]
  )

  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  const statusMap = React.useMemo(() => {
    const m = new Map<string, DdpStatusItem>()
    for (const s of statusesQuery.data ?? []) m.set(s.statusKey, s)
    return m
  }, [statusesQuery.data])

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["workshop-rows"] })
  }, [queryClient])

  // Due sorgenti, due canali: le righe manuali passano dal gruppo delle lavorazioni,
  // quelle di distinta cambiano quando qualcuno tocca la DDP Officina di una commessa.
  useWorkRequestsHub(true, invalidate)
  useProjectHub("all", (change) => {
    if (change.ddpType === "OFFICINA") invalidate()
  })

  const righe = React.useMemo(() => righeQuery.data ?? [], [righeQuery.data])

  const gestisciErrore = React.useCallback(
    (err: Error) => {
      if (err instanceof ApiError && err.status === 409) {
        notifyError("La riga è stata modificata da un altro utente. Ricarica e riprova.")
        invalidate()
        return
      }
      notifyError(err)
    },
    [invalidate]
  )

  // Scrittura dei tre campi che questa pagina possiede. Le righe manuali non stanno nella
  // distinta: per loro si passa dal salvataggio della lavorazione.
  const patchMutation = useMutation({
    mutationFn: ({
      row,
      field,
      value,
    }: {
      row: WorkshopRow
      field: "request_date" | "notes" | "is_ultra_critical"
      value: string | boolean | null
    }) => {
      if (row.source === "DDP") {
        return patchWorkshopField(row.id, field, value, row.updatedAt)
      }
      // Le righe manuali non stanno nella distinta: si passa dall'endpoint delle
      // lavorazioni, che conosce questi tre campi con gli stessi nomi di colonna.
      return patchWorkRequestField(row.id, field, value)
    },
    onSuccess: () => invalidate(),
    onError: gestisciErrore,
  })

  const salvaManuale = useMutation({
    mutationFn: ({ payload, id }: { payload: WorkRequestSaveRequest; id?: number }) =>
      id ? updateWorkRequest(id, payload) : createWorkRequest(payload).then(() => undefined),
    onSuccess: () => invalidate(),
    onError: gestisciErrore,
  })

  const eliminaManuale = useMutation({
    mutationFn: (id: number) => deleteWorkRequest(id),
    onSuccess: () => invalidate(),
    onError: gestisciErrore,
  })

  const conteggi = React.useMemo(
    () => ({
      interne: righe.filter(isInterna).length,
      stampa3d: righe.filter(isStampa3D).length,
      esterne: righe.filter(isEsterna).length,
      urgenze: righe.filter(isUrgenza).length,
      trattamenti: righe.filter(isTrattamento).length,
    }),
    [righe]
  )

  // Vista → filtri di testata. L'intervallo lavora sulla data della vista: quella richiesta
  // ovunque, «Consegnato il» sulle esterne (#83).
  const filtrate = React.useMemo(() => {
    let list = filtraPerVista(righe, view)
    if (filtroCommessa != null) {
      list = list.filter((r) => r.projectId === filtroCommessa)
    }
    if (dataDa) list = list.filter((r) => {
      const d = dataDiVista(r, view)
      return d !== "" && d >= dataDa
    })
    if (dataA) list = list.filter((r) => {
      const d = dataDiVista(r, view)
      return d !== "" && d <= dataA
    })
    return list
  }, [righe, view, filtroCommessa, dataDa, dataA])

  const tieniOrdineServer = React.useCallback((list: WorkshopRow[]) => list, [])
  const displayRows = useDeferredItemOrder(
    filtrate,
    tieniOrdineServer,
    layoutEpoch,
    view
  )

  const columns = React.useMemo(
    () =>
      buildWorkshopColumns(
        view,
        {
          onRequestDate: (row, value) =>
            patchMutation.mutate({ row, field: "request_date", value }),
          onNotes: (row, value) => patchMutation.mutate({ row, field: "notes", value }),
          onUltraCritical: (row, value) =>
            patchMutation.mutate({ row, field: "is_ultra_critical", value }),
          disabled: patchMutation.isPending,
        },
        statusMap
      ),
    [view, patchMutation, statusMap]
  )

  const quickViews = (
    [
      { key: "interne", icon: <Factory className="size-4" />, count: conteggi.interne },
      { key: "stampa3d", icon: <Box className="size-4" />, count: conteggi.stampa3d },
      { key: "esterne", icon: <Truck className="size-4" />, count: conteggi.esterne },
      { key: "urgenze", icon: <AlertTriangle className="size-4" />, count: conteggi.urgenze },
      {
        key: "trattamenti",
        icon: <FlaskConical className="size-4" />,
        count: conteggi.trattamenti,
      },
    ] as const
  ).map((v) => ({
    key: v.key,
    selected: view === v.key,
    onClick: () => {
      setView(v.key)
      setLayoutEpoch((n) => n + 1)
    },
    icon: v.icon,
    label: TITOLI_VISTA[v.key],
    count: v.count,
  }))

  const filtriAttivi = filtroCommessa != null || dataDa != null || dataA != null

  const pulisciFiltri = () => {
    setFiltroCommessa(null)
    setDataDa(null)
    setDataA(null)
  }

  const etichettaData = view === "esterne" ? "Consegnato il" : "Data Richiesta"

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight text-foreground">
          Lavorazioni Officine
        </h1>
        <p className="text-sm text-muted-foreground">
          Le righe della DDP Officina di tutte le commesse, divise per quello che serve in
          officina. Si scrivono qui solo data richiesta, note e urgenza.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <PmSidebar storageKey="lavorazioni-officine" quickViews={quickViews} sections={[]} />

        <main className="min-w-0 flex-1 overflow-y-auto p-4">
          <DataTableCardFiltered
            // La tabella prende ordinamento e colonne visibili una volta sola, al montaggio:
            // senza `key` il cambio vista terrebbe l'ordinamento della vista precedente (e la
            // visibilità colonne salvata sotto la chiave sbagliata).
            key={view}
            title={TITOLI_VISTA[view]}
            description={DESCRIZIONI_VISTA[view]}
            columns={columns}
            data={displayRows}
            isLoading={righeQuery.isLoading}
            error={righeQuery.error as Error | null}
            onRefresh={() => {
              setLayoutEpoch((n) => n + 1)
              void righeQuery.refetch()
            }}
            searchPlaceholder="Cerca commessa, codice, descrizione…"
            columnLabels={WORKSHOP_COLUMN_LABELS}
            getRowId={rowKey}
            visibilityStorageKey={`lavorazioni-officine-${view}-v1`}
            // #84: chi urge sta in cima, poi la data. Sulle esterne la spunta non esiste
            // (la #83 la tiene sulle sole interne) e si ordina per «Consegnato il».
            defaultSorting={
              view === "esterne"
                ? [{ id: "deliveredAt", desc: false }]
                : [
                    { id: "ultra", desc: true },
                    { id: "requestDate", desc: false },
                  ]
            }
            gridLines
            externalFiltersActive={filtriAttivi}
            onClearExternalFilters={pulisciFiltri}
            onRowDoubleClick={(row) => {
              // Solo le righe manuali si modificano da qui: quelle di distinta si cambiano
              // nella DDP, e il doppio clic che apre un dialogo di sola lettura confonde.
              if (row.source !== "MANUAL") return
              setRigaInModifica(row)
              setDialogAperto(true)
            }}
            toolbarActions={
              <Button
                size="sm"
                onClick={() => {
                  setRigaInModifica(null)
                  setDialogAperto(true)
                }}
              >
                <Plus className="size-4" />
                Riga manuale
              </Button>
            }
            aboveTable={
              <div className="mb-3 flex flex-wrap items-end gap-3 rounded-lg border bg-muted/30 p-3">
                <div className="grid min-w-[260px] flex-1 gap-1.5">
                  <Label className="text-xs">Commessa</Label>
                  <LookupCombobox
                    options={projects.map((p) => ({
                      id: p.id,
                      name: `${p.code} — ${p.title}`,
                      hint: p.customerName || undefined,
                    }))}
                    value={filtroCommessa}
                    onValueChange={setFiltroCommessa}
                    placeholder="Tutte le commesse"
                    searchPlaceholder="Cerca commessa…"
                    emptyText="Nessuna commessa trovata"
                    noneLabel="— tutte —"
                  />
                </div>
                <div className="grid gap-1.5">
                  <Label className="text-xs">{etichettaData} dal</Label>
                  <DateField value={dataDa} onChange={setDataDa} clearable size="sm" />
                </div>
                <div className="grid gap-1.5">
                  <Label className="text-xs">al</Label>
                  <DateField
                    value={dataA}
                    onChange={setDataA}
                    clearable
                    size="sm"
                    disabled={!dataDa}
                    disableBefore={dataDa ? new Date(dataDa) : undefined}
                  />
                </div>
              </div>
            }
            rowStyle={(row) =>
              row.isUltraCritical
                ? { backgroundColor: "rgba(244, 63, 94, 0.10)" }
                : (row.daysLate ?? 0) > 0
                  ? { backgroundColor: "rgba(244, 63, 94, 0.05)" }
                  : undefined
            }
          />
        </main>
      </div>

      <WorkshopManualDialog
        open={dialogAperto}
        onOpenChange={setDialogAperto}
        projects={projects}
        row={rigaInModifica}
        defaultType={
          view === "esterne" ? "External" : view === "stampa3d" ? "Print3D" : "Internal"
        }
        onSave={(payload, id) => salvaManuale.mutate({ payload, id })}
        onDelete={
          rigaInModifica
            ? (id) => {
                eliminaManuale.mutate(id)
                setDialogAperto(false)
              }
            : undefined
        }
      />
    </div>
  )
}

