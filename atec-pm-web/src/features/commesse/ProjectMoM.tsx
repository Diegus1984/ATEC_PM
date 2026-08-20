import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useNavigate } from "react-router-dom"
import { Plus, RefreshCw, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { notifyError } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import { createMoM, deleteMoM, fetchMoMList } from "@/lib/api/mom"
import { canWriteFeature } from "@/lib/auth/permissions"
import { MoMClosedProgress } from "@/features/mom/mom-closed-progress"
import { MOM_CONDITION_STYLE } from "@/features/mom/mom-palette"
import type { MoMListItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { formatDateOrDash } from "@/lib/date-iso"

function periodLabel(item: MoMListItem): string {
  if (!item.periodStart && !item.periodEnd) return "—"
  return `${formatDateOrDash(item.periodStart)} → ${formatDateOrDash(item.periodEnd)}`
}

/** Verbali (MoM) filtrati sulla commessa — variante per-commessa del modulo MoM. */
export function ProjectMoM({ projectId }: { projectId: number }) {
  const navigate = useNavigate()
  const confirm = useConfirm()
  const queryClient = useQueryClient()

  // Funzione concessa in sola lettura: i verbali si aprono e si leggono, ma niente
  // creazione o eliminazione. A respingere davvero le scritture è l'API; qui si
  // tolgono i comandi, altrimenti ogni clic finirebbe in un errore rosso.
  const readOnly = !canWriteFeature("nav.mom")

  const query = useQuery({
    queryKey: ["mom-list", "project", projectId],
    queryFn: () => fetchMoMList(projectId),
    enabled: projectId > 0,
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["mom-list", "project", projectId] })

  const createMutation = useMutation({
    // Guardia nella mutation, non solo sul pulsante: stesso pattern delle altre pagine
    // in sola lettura (ProjectDdpOfficina, MoMDetailPage), regge anche i chiamanti futuri.
    mutationFn: async () => {
      if (readOnly) throw new Error("Verbali concessi in sola lettura.")
      return createMoM({
        tipo: "COMMESSA",
        projectId,
        title: "Nuovo verbale",
        meetingDate: new Date().toISOString().slice(0, 10),
        inDashboard: true,
      })
    },
    onSuccess: (newId) => navigate(`/mom/${newId}`, { state: { fromProject: true } }),
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteMoM,
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  async function handleDelete(item: MoMListItem) {
    // In sola lettura non si arriva nemmeno alla richiesta di conferma.
    if (readOnly) return
    const ok = await confirm({
      title: "Elimina verbale",
      description: `Eliminare il verbale "${item.title}" e tutte le sue azioni?`,
      confirmLabel: "Elimina",
    })
    if (ok) deleteMutation.mutate(item.id)
  }

  const items = query.data ?? []
  const pill = "rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums"

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        {!readOnly && (
          <Button
            size="sm"
            onClick={() => createMutation.mutate()}
            disabled={createMutation.isPending}
          >
            <Plus />
            Nuovo Verbale
          </Button>
        )}
        <Button
          variant="outline"
          size="sm"
          onClick={() => query.refetch()}
          disabled={query.isFetching}
        >
          <RefreshCw className={query.isFetching ? "animate-spin" : ""} />
          Aggiorna
        </Button>
      </div>

      {query.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : query.isError ? (
        <PageErrorAlert message={(query.error as Error).message} />
      ) : items.length === 0 ? (
        <Empty className="p-8">
          <EmptyHeader>
            <EmptyTitle>Nessun verbale per questa commessa</EmptyTitle>
            <EmptyDescription>
              {readOnly
                ? "Nessun verbale da consultare."
                : "Crea il primo verbale con «Nuovo Verbale»."}
            </EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <div className="flex flex-wrap gap-4">
          {items.map((item) => (
            <div
              key={item.id}
              className="w-[330px] overflow-hidden rounded-xl border bg-card shadow-xs"
            >
              <div className="h-1.5 bg-primary" />
              <button
                type="button"
                className="block w-full cursor-pointer p-3.5 text-left"
                onClick={() => navigate(`/mom/${item.id}`, { state: { fromProject: true } })}
              >
                <span className="truncate text-sm font-semibold">{item.title}</span>
                <div className="my-2 border-t" />
                <Row label="Milestones / attività" value={item.itemsCount} />
                <div className="flex items-center justify-between gap-2 py-0.5">
                  <span className="text-sm text-muted-foreground">Avanzamento</span>
                  <MoMClosedProgress
                    itemsCount={item.itemsCount}
                    openCount={item.openCount}
                    className="max-w-[65%] flex-1"
                  />
                </div>
                <div className="flex items-center justify-between gap-2 py-0.5">
                  <span className="text-sm text-muted-foreground">
                    Ripartizione priorità
                  </span>
                  <div className="flex gap-1">
                    <span className={cn(pill, MOM_CONDITION_STYLE.p1.pillClass)}>
                      {item.p1Count}
                    </span>
                    <span className={cn(pill, MOM_CONDITION_STYLE.p2.pillClass)}>
                      {item.p2Count}
                    </span>
                    <span className={cn(pill, MOM_CONDITION_STYLE.p3.pillClass)}>
                      {item.p3Count}
                    </span>
                  </div>
                </div>
                <Row label="Periodo attività" value={periodLabel(item)} />
                <Row label="Data riunione" value={formatDateOrDash(item.meetingDate)} />
              </button>
              {!readOnly && (
                <div className="flex justify-end border-t px-3 py-2">
                  <Button
                    variant="destructive"
                    size="sm"
                    onClick={() => void handleDelete(item)}
                  >
                    <Trash2 />
                    Elimina
                  </Button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-2 py-0.5">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-sm font-semibold">{value}</span>
    </div>
  )
}
