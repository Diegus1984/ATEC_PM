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
import type { MoMListItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

function fmtDate(value: string | null): string {
  if (!value) return "—"
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleDateString("it-IT")
}

function periodLabel(item: MoMListItem): string {
  if (!item.periodStart && !item.periodEnd) return "—"
  return `${fmtDate(item.periodStart)} → ${fmtDate(item.periodEnd)}`
}

/** Verbali (MoM) filtrati sulla commessa — variante per-commessa del modulo MoM. */
export function ProjectMoM({ projectId }: { projectId: number }) {
  const navigate = useNavigate()
  const confirm = useConfirm()
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: ["mom-list", "project", projectId],
    queryFn: () => fetchMoMList(projectId),
    enabled: projectId > 0,
  })

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["mom-list", "project", projectId] })

  const createMutation = useMutation({
    mutationFn: () =>
      createMoM({
        tipo: "COMMESSA",
        projectId,
        title: "Nuovo verbale",
        meetingDate: new Date().toISOString().slice(0, 10),
        inDashboard: true,
      }),
    onSuccess: (newId) => navigate(`/mom/${newId}`),
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteMoM,
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  async function handleDelete(item: MoMListItem) {
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
        <Button
          size="sm"
          onClick={() => createMutation.mutate()}
          disabled={createMutation.isPending}
        >
          <Plus />
          Nuovo Verbale
        </Button>
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
              Crea il primo verbale con «Nuovo Verbale».
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
                onClick={() => navigate(`/mom/${item.id}`)}
              >
                <span className="truncate text-sm font-semibold">{item.title}</span>
                <div className="my-2 border-t" />
                <Row label="Milestones / attività" value={item.itemsCount} />
                <div className="flex items-center justify-between gap-2 py-0.5">
                  <span className="text-sm text-muted-foreground">
                    Ripartizione priorità
                  </span>
                  <div className="flex gap-1">
                    <span className={cn(pill, "bg-red-100 text-red-700")}>
                      {item.p1Count}
                    </span>
                    <span className={cn(pill, "bg-yellow-100 text-yellow-700")}>
                      {item.p2Count}
                    </span>
                    <span className={cn(pill, "bg-green-100 text-green-700")}>
                      {item.p3Count}
                    </span>
                  </div>
                </div>
                <Row label="Periodo attività" value={periodLabel(item)} />
                <Row label="Data riunione" value={fmtDate(item.meetingDate)} />
              </button>
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
