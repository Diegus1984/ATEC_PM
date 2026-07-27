import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ListTodo, CheckSquare, Truck, FlaskConical } from "lucide-react"
import { PmSidebar } from "@/components/shared/pm-sidebar"
import { fetchProjects } from "@/lib/api/projects"
import { fetchWorkRequests } from "@/lib/api/workRequests"
import { useWorkRequestsHub } from "@/lib/signalr/use-work-requests-hub"
import { ProjectWorkRequests } from "@/features/commesse/ProjectWorkRequests"
import {
  formatWorkRequestProjectLabel,
  isSystemProjectCode,
} from "@/lib/system-projects"

// Definisce lo stato delle visualizzazioni disponibili nel pannello laterale
export type WorkRequestsView =
  | { kind: "drafts" }
  | { kind: "priorities" }
  | { kind: "consegne" }
  | { kind: "trattamenti" }
  | { kind: "project"; projectId: number }

export function WorkRequestsPage() {
  const [view, setView] = React.useState<WorkRequestsView>({ kind: "drafts" })
  const [selectedProjectId, setSelectedProjectId] = React.useState<number | null>(null)

  // Carica i progetti per popolare la barra laterale
  const { data: projectsData } = useQuery({
    queryKey: ["projects"],
    queryFn: () => fetchProjects({ pageSize: 1000 }),
  })
  const projects = projectsData?.items ?? []

  // Query per ottenere tutte le lavorazioni (usato per calcolare i contatori globali della sidebar e per i tab globali)
  const { data: allWorkRequests = [], refetch } = useQuery({
    queryKey: ["all-work-requests"],
    queryFn: () => fetchWorkRequests(0),
  })

  // Real-time: gruppo globale workrequests-all — ricarica tutte le viste (bozze,
  // priorità, consegne, trattamenti) quando un altro utente modifica una lavorazione
  useWorkRequestsHub(true, () => {
    void refetch()
  })

  // Calcolo dei contatori per la sidebar
  const activeCount = allWorkRequests.filter((r) => !r.isStaging && !r.isDelivered).length
  const activeTreatmentsCount = allWorkRequests.filter((r) => !r.isStaging && r.hasTreatment && !r.isTreatmentConfirmed).length
  const stagingCount = allWorkRequests.filter((r) => r.isStaging).length

  // Viste rapide della barra laterale
  const quickViews = [
    {
      key: "drafts",
      selected: view.kind === "drafts",
      onClick: () => setView({ kind: "drafts" }),
      icon: <ListTodo className="size-4" />,
      label: "Bozze (Staging)",
      count: stagingCount,
    },
    {
      key: "priorities",
      selected: view.kind === "priorities",
      onClick: () => setView({ kind: "priorities" }),
      icon: <CheckSquare className="size-4" />,
      label: "Tabella Priorità",
      count: activeCount,
    },
    {
      key: "consegne",
      selected: view.kind === "consegne",
      onClick: () => setView({ kind: "consegne" }),
      icon: <Truck className="size-4" />,
      label: "Controllo Consegne",
      count: activeCount,
    },
    {
      key: "trattamenti",
      selected: view.kind === "trattamenti",
      onClick: () => setView({ kind: "trattamenti" }),
      icon: <FlaskConical className="size-4" />,
      label: "Trattamenti",
      count: activeTreatmentsCount,
    },
  ]

  // Contenitori della barra laterale (Commesse): INTERNA in testa, poi le reali.
  const containers = [...projects]
    .sort((a, b) => {
      const aSys = isSystemProjectCode(a.code) ? 0 : 1
      const bSys = isSystemProjectCode(b.code) ? 0 : 1
      if (aSys !== bSys) return aSys - bSys
      return a.code.localeCompare(b.code, "it")
    })
    .map((p) => {
      const projReqs = allWorkRequests.filter((r) => r.projectId === p.id && !r.isStaging)
      const count = projReqs.length
      
      const dots = []
      if (projReqs.some((r) => r.isUltraCritical)) {
        dots.push({ dotClass: "bg-rose-500", label: "Ultra critico" })
      }
      if (projReqs.some((r) => r.hasTreatment && !r.isTreatmentConfirmed)) {
        dots.push({ dotClass: "bg-amber-500", label: "Trattamento attivo" })
      }

      return {
        key: `p${p.id}`,
        selected: view.kind === "project" && view.projectId === p.id,
        onClick: () => {
          setView({ kind: "project", projectId: p.id })
          setSelectedProjectId(p.id)
        },
        label: formatWorkRequestProjectLabel(p.code, p.title),
        count,
        dots,
      }
    })

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight text-foreground">
          Pannello Lavorazioni
        </h1>
        <p className="text-sm text-muted-foreground">
          Pianifica, coordina e monitora le lavorazioni meccaniche interne ed esterne.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <PmSidebar
          storageKey="work-requests"
          quickViews={quickViews}
          containers={containers}
          containersLabel="Commesse / Progetti"
          emptyLabel="Nessuna commessa attiva"
        />

        <main className="min-w-0 flex-1 overflow-y-auto p-4">
          {view.kind === "drafts" && (
            <ProjectWorkRequests viewMode="drafts" />
          )}

          {view.kind === "priorities" && (
            <ProjectWorkRequests viewMode="priorities" />
          )}

          {view.kind === "consegne" && (
            <ProjectWorkRequests viewMode="consegne" />
          )}

          {view.kind === "trattamenti" && (
            <ProjectWorkRequests viewMode="trattamenti" />
          )}

          {view.kind === "project" && selectedProjectId && (
            <ProjectWorkRequests projectId={selectedProjectId} viewMode="project" />
          )}
        </main>
      </div>
    </div>
  )
}
