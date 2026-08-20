import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import {
  Banknote,
  CalendarClock,
  CircleAlert,
  ExternalLink,
  Folder,
  FolderOpen,
  Printer,
  ReceiptText,
  RefreshCw,
  TriangleAlert,
} from "lucide-react"
import { Link, useSearchParams } from "react-router-dom"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { PmSidebar, type PmContainer, type PmQuickView } from "@/components/shared/pm-sidebar"
import { buildPmProjectSections } from "@/lib/pm-project-sections"
import { isCommessaCode } from "@/lib/project-code"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { fetchProjectsLookup } from "@/lib/api/projects"
import { fetchSal, fetchSalSummary } from "@/lib/api/sal"
import type { ProjectLookupItem, SalSummary } from "@/lib/api/types"
import { canAccessFeature } from "@/lib/auth/permissions"
import { euro } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { projectStatusMeta } from "@/features/commesse/project-status"
import { printSalSheet } from "@/features/commesse/sal-sheet-print"
import { salSummaryDots } from "@/features/commesse/sal-utils"
import { ProjectSal } from "@/features/commesse/ProjectSal"
import { SalIncassoProgress } from "./SalIncassoProgress"
import { SalAnalisiView } from "./SalAnalisiView"
import { SalCashFlowView } from "./SalCashFlowView"
import { SalProspettoView } from "./SalProspettoView"
import { SalWarningView } from "./SalWarningView"
import { SalTableView } from "./SalTableView"
import { useGlobalSalHub } from "@/lib/signalr/use-sal-hub"
import { cn } from "@/lib/utils"

const SUMMARY_QUERY_KEY = ["sal-summary"] as const

/** Schede SAL lasciate aperte (per utente/PC): sopravvivono a refresh e cambio pagina. */
const EXPANDED_STORAGE_KEY = "sal:expanded-projects:v1"
/** Modalità di visualizzazione commesse SAL: card o tabella. */
const DISPLAY_MODE_STORAGE_KEY = "sal:display-mode:v1"

export function SalPage() {
  const queryClient = useQueryClient()

  // Le viste economiche (Cash Flow / Analisi) dipendono dalla funzione `sal.economics`
  // (livello 2 = PM/ADMIN come prima, più i reparti a cui è concessa); l'endpoint fa 403.
  const canSeeEconomics = canAccessFeature("sal.economics")

  // Deep-link delle viste via query param `?view=` (sostituisce la rotta figlia
  // /sal/cashflow prevista da D8): valori validi prospetto|cashflow;
  // cashflow solo per PM/ADMIN, altrimenti il param viene ignorato.
  const [searchParams, setSearchParams] = useSearchParams()

  // Stato di vista: all = tutte le commesse attive; prospetto = prospetto SAL;
  // cashflow = vista economica globale con card + grafico Analisi (solo PM/ADMIN);
  // perProject = commessa singola
  const [view, setView] = React.useState<
    "all" | "prospetto" | "warnFatt" | "warnIncasso" | "cashflow" | "perProject"
  >(() => {
    const v = searchParams.get("view")
    if (v === "prospetto") return "prospetto"
    if (v === "warn-fatturazione") return "warnFatt"
    if (v === "warn-incasso") return "warnIncasso"
    // "analisi" è un alias storico: l'Analisi vive sotto le card del Cash Flow
    if ((v === "cashflow" || v === "analisi") && canSeeEconomics) return "cashflow"
    return "all"
  })
  const [selectedProjectId, setSelectedProjectId] = React.useState<number | null>(null)
  // Schede aperte: persistite, non più solo stato React. Chi lavora su due o tre commesse
  // le riapriva a mano a ogni refresh o cambio pagina.
  const [expandedProjects, setExpandedProjects] = React.useState<Record<number, boolean>>(
    () => {
      try {
        const raw = localStorage.getItem(EXPANDED_STORAGE_KEY)
        if (raw) return JSON.parse(raw) as Record<number, boolean>
      } catch {
        // ignore
      }
      return {}
    }
  )

  const toggleExpanded = React.useCallback((projectId: number) => {
    setExpandedProjects((prev) => {
      const next = { ...prev, [projectId]: !prev[projectId] }
      // Le schede chiuse non si salvano: la chiave crescerebbe con ogni commessa vista.
      const trimmed = Object.fromEntries(
        Object.entries(next).filter(([, open]) => open)
      )
      try {
        localStorage.setItem(EXPANDED_STORAGE_KEY, JSON.stringify(trimmed))
      } catch {
        // ignore
      }
      return next
    })
  }, [])

  // Modalità di visualizzazione della lista commesse: card vs tabella
  const [displayMode, setDisplayMode] = React.useState<"cards" | "table">(() => {
    try {
      const raw = localStorage.getItem(DISPLAY_MODE_STORAGE_KEY)
      if (raw === "table" || raw === "cards") return raw
    } catch {
      // ignore
    }
    return "cards"
  })

  const changeDisplayMode = (mode: "cards" | "table") => {
    setDisplayMode(mode)
    try {
      localStorage.setItem(DISPLAY_MODE_STORAGE_KEY, mode)
    } catch {
      // ignore
    }
  }

  // Mantiene l'URL allineato alla vista corrente (replace: niente voci di history);
  // le viste senza deep-link (all/perProject) rimuovono il param.
  React.useEffect(() => {
    const next =
      view === "prospetto" || view === "cashflow"
        ? view
        : view === "warnFatt"
          ? "warn-fatturazione"
          : view === "warnIncasso"
            ? "warn-incasso"
            : null
    if ((searchParams.get("view") ?? null) === next) return
    const params = new URLSearchParams(searchParams)
    if (next) params.set("view", next)
    else params.delete("view")
    setSearchParams(params, { replace: true })
  }, [view, searchParams, setSearchParams])

  // Carica i progetti per l'area principale e il summary per la sidebar
  const projectsQuery = useQuery({
    queryKey: ["pm-sal-projects"],
    queryFn: () => fetchProjectsLookup({ page: 1, pageSize: 250 }),
  })

  const summaryQuery = useQuery({
    queryKey: SUMMARY_QUERY_KEY,
    queryFn: fetchSalSummary,
  })

  const refetchAll = React.useCallback(() => {
    void projectsQuery.refetch()
    void summaryQuery.refetch()
    void queryClient.invalidateQueries({ queryKey: ["sal"] })
    void queryClient.invalidateQueries({ queryKey: ["sal-prospetto"] })
  }, [projectsQuery, summaryQuery, queryClient])

  useGlobalSalHub(true, refetchAll)

  // #85: nella gestione SAL entrano le sole commesse vere (codice `C` + data). Le Altre
  // Attività a codice libero (INTERNA, SERVICE _ SANGRATO…) restano fuori da tutto — elenco,
  // barra laterale e viste aggregate, che il server filtra con la stessa regola.
  const allProjects = React.useMemo(() => {
    return (projectsQuery.data?.items ?? []).filter((p) => isCommessaCode(p.code))
  }, [projectsQuery.data])

  const activeProjects = React.useMemo(() => {
    return allProjects.filter((p) => p.status === "ACTIVE")
  }, [allProjects])

  // Somma di warn + pre + incassi scaduti per il conteggio del Prospetto SAL
  const prospettoCount = React.useMemo(() => {
    const rows = summaryQuery.data ?? []
    return rows.reduce(
      (acc, curr) => acc + (curr.warn ?? 0) + (curr.pre ?? 0) + (curr.incasso ?? 0),
      0
    )
  }, [summaryQuery.data])

  // Contatori delle due liste di allarme dedicate (fatturazione da emettere / incassi scaduti).
  const warnFattCount = React.useMemo(() => {
    const rows = summaryQuery.data ?? []
    return rows.reduce((acc, curr) => acc + (curr.warn ?? 0) + (curr.pre ?? 0), 0)
  }, [summaryQuery.data])

  const warnIncassoCount = React.useMemo(() => {
    const rows = summaryQuery.data ?? []
    return rows.reduce((acc, curr) => acc + (curr.incasso ?? 0), 0)
  }, [summaryQuery.data])

  const quickViews: PmQuickView[] = [
    {
      key: "all",
      selected: view === "all",
      onClick: () => {
        setView("all")
        setSelectedProjectId(null)
      },
      icon: <ReceiptText />,
      label: "Tutte le commesse",
      count: activeProjects.length,
    },
    {
      key: "prospetto",
      selected: view === "prospetto",
      onClick: () => {
        setView("prospetto")
        setSelectedProjectId(null)
      },
      icon: <CalendarClock />,
      label: "Prospetto SAL",
      count: prospettoCount,
    },
    // Le due liste di allarme: il Prospetto le contiene tutte ma non è filtrabile per
    // segnalazione, quindi «cosa devo fatturare» e «cosa non ho incassato» avevano
    // bisogno di due viste proprie.
    {
      key: "warnFatt",
      selected: view === "warnFatt",
      onClick: () => {
        setView("warnFatt")
        setSelectedProjectId(null)
      },
      icon: <TriangleAlert />,
      label: "Warning Fatturazione",
      count: warnFattCount,
    },
    {
      key: "warnIncasso",
      selected: view === "warnIncasso",
      onClick: () => {
        setView("warnIncasso")
        setSelectedProjectId(null)
      },
      icon: <CircleAlert />,
      label: "Warning incasso fattura",
      count: warnIncassoCount,
    },
  ]

  if (canSeeEconomics) {
    const monitoredCount = summaryQuery.data?.length ?? 0
    // Un'unica voce: la vista Cash Flow contiene le card e, sotto, il grafico Analisi
    quickViews.push({
      key: "cashflow",
      selected: view === "cashflow",
      onClick: () => {
        setView("cashflow")
        setSelectedProjectId(null)
      },
      icon: <Banknote />,
      label: "Cash Flow",
      count: monitoredCount,
    })
  }

  // Una sola sezione «Commesse»: dal #85 le Altre Attività non entrano nel SAL, quindi non
  // c'è più un secondo gruppo da mostrare (il filtro sul codice resta comunque, perché il
  // riepilogo di una vecchia versione del server potrebbe ancora mandarle).
  const sections = React.useMemo(() => {
    const rows = (summaryQuery.data ?? []).filter((s) => isCommessaCode(s.code))
    const entries = rows.map((s) => ({
      code: s.code,
      container: {
        key: `p${s.projectId}`,
        selected: view === "perProject" && selectedProjectId === s.projectId,
        onClick: () => {
          setSelectedProjectId(s.projectId)
          setView("perProject")
        },
        label: s.title ? `${s.code} — ${s.title}` : s.code,
        count: s.open,
        dots: salSummaryDots(s),
      } satisfies PmContainer,
    }))

    return buildPmProjectSections(entries, {
      emptyLabel: "Nessuna commessa con SAL",
      includeOther: false,
    })
  }, [summaryQuery.data, selectedProjectId, view])

  const visibleProjects = React.useMemo(() => {
    if (view === "perProject" && selectedProjectId !== null) {
      const found = allProjects.find((p) => p.id === selectedProjectId)
      return found ? [found] : []
    }
    if (view === "all") {
      return activeProjects
    }
    return []
  }, [view, selectedProjectId, allProjects, activeProjects])

  const expandAll = () => {
    const next: Record<number, boolean> = {}
    visibleProjects.forEach((p) => {
      next[p.id] = true
    })
    setExpandedProjects(next)
  }

  const collapseAll = () => {
    setExpandedProjects({})
  }

  // Espandi automaticamente se viene selezionato un singolo progetto
  React.useEffect(() => {
    if (view === "perProject" && selectedProjectId !== null) {
      setExpandedProjects((prev) => ({ ...prev, [selectedProjectId]: true }))
    }
  }, [view, selectedProjectId])

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold tracking-tight">SAL / Fatturazione Commesse</h1>
        <p className="text-sm text-muted-foreground">
          Piani di fatturazione a stati d'avanzamento di tutte le commesse.
        </p>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden rounded-lg border bg-background">
        <PmSidebar
          storageKey="sal"
          quickViews={quickViews}
          sections={sections}
        />

        <main className="flex min-w-0 flex-1 flex-col gap-4 p-4 min-h-0">
          <header className="flex shrink-0 flex-row flex-wrap items-center justify-end gap-2">
            {(view === "all" || view === "perProject") && visibleProjects.length > 0 && (
              <Select
                value={displayMode}
                onValueChange={(v) => changeDisplayMode(v as "cards" | "table")}
              >
                <SelectTrigger size="sm" className="w-[130px]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="table">Tabella</SelectItem>
                  <SelectItem value="cards">Card</SelectItem>
                </SelectContent>
              </Select>
            )}

            {view !== "prospetto" && visibleProjects.length > 0 && (
              <div className="flex items-center gap-1.5 border rounded-lg bg-card p-1">
                <Button variant="ghost" size="sm" className="h-7 text-xs px-2" onClick={expandAll}>
                  Espandi tutte
                </Button>
                <div className="h-3 w-px bg-muted" />
                <Button variant="ghost" size="sm" className="h-7 text-xs px-2" onClick={collapseAll}>
                  Comprimi tutte
                </Button>
              </div>
            )}

            <Button
              variant="outline"
              size="sm"
              className="h-9"
              onClick={refetchAll}
              disabled={projectsQuery.isFetching || summaryQuery.isFetching}
            >
              <RefreshCw
                className={cn(
                  "size-4 mr-1.5",
                  (projectsQuery.isFetching || summaryQuery.isFetching) && "animate-spin"
                )}
              />
              Aggiorna
            </Button>
          </header>

          {/* Lista di card in uno scroller: contenitore a BLOCCO (space-y), non flex.
              Con `flex flex-col` le card sono figli flex e `Card` ha `overflow-hidden`,
              che azzera il loro min-height automatico: da una certa lunghezza in poi il
              browser le comprime tutte e taglia la riga «Cliente / PM». Vedi BLOCKS-RULES. */}
          <section className="flex-1 space-y-4 overflow-y-auto pr-1 min-h-0 pb-4">
            {view === "prospetto" ? (
              <SalProspettoView />
            ) : view === "warnFatt" ? (
              <SalWarningView mode="fatturazione" />
            ) : view === "warnIncasso" ? (
              <SalWarningView mode="incasso" />
            ) : view === "cashflow" ? (
              <>
                {/* Card di sintesi + grafico Analisi sulla stessa pagina (richiesta 09/07) */}
                <SalCashFlowView />
                <SalAnalisiView />
              </>
            ) : projectsQuery.isLoading ? (
              <p className="text-sm text-muted-foreground p-4">Caricamento commesse...</p>
            ) : projectsQuery.isError ? (
              <PageErrorAlert message={(projectsQuery.error as Error).message} />
            ) : visibleProjects.length === 0 ? (
              <div className="text-center py-16 border border-dashed rounded-lg bg-muted/10">
                <p className="text-sm font-medium text-muted-foreground">Nessuna commessa trovata</p>
              </div>
            ) : displayMode === "table" ? (
              <SalTableView
                projects={visibleProjects}
                summaryData={summaryQuery.data}
                expandedProjects={expandedProjects}
                onToggleExpanded={toggleExpanded}
                canSeeEconomics={canSeeEconomics}
                isLoading={projectsQuery.isLoading}
                isFetching={projectsQuery.isFetching || summaryQuery.isFetching}
                error={projectsQuery.error as Error | null}
                onRefresh={refetchAll}
              />
            ) : (
              visibleProjects.map((p) => {
                const isExpanded = !!expandedProjects[p.id]
                const salSummary = summaryQuery.data?.find((s) => s.projectId === p.id)
                return (
                  <ProjectSalCard
                    key={p.id}
                    project={p}
                    salSummary={salSummary}
                    expanded={isExpanded}
                    onToggleExpanded={() => toggleExpanded(p.id)}
                    canSeeEconomics={canSeeEconomics}
                  />
                )
              })
            )}
          </section>
        </main>
      </div>

    </div>
  )
}

interface ProjectSalCardProps {
  project: ProjectLookupItem
  salSummary?: SalSummary
  expanded: boolean
  onToggleExpanded: () => void
  canSeeEconomics: boolean
}

function ProjectSalCard({
  project,
  salSummary,
  expanded,
  onToggleExpanded,
  canSeeEconomics,
}: ProjectSalCardProps) {
  const showIncasso = salSummary != null && salSummary.percTotal > 0
  const statusMeta = projectStatusMeta(project.status)
  const [printing, setPrinting] = React.useState(false)

  const handlePrintPdf = async () => {
    if (printing) return
    setPrinting(true)
    try {
      const bundle = await fetchSal(project.id)
      printSalSheet(bundle, {
        projectCode: project.code,
        projectTitle: project.title,
        canSeeEconomics,
      })
    } catch (err) {
      notifyError(err as Error)
    } finally {
      setPrinting(false)
    }
  }

  return (
    <Card className="overflow-hidden py-0 shadow-xs transition-colors hover:border-primary/40">
      <CardHeader
        onClick={onToggleExpanded}
        className={cn(
          // `[.border-b]:pb-3!` — il `!` va in CODA in Tailwind v4: serve a battere la
          // variante di serie `[.border-b]:pb-(--card-spacing)` di CardHeader, che con il
          // corpo sotto porterebbe il padding inferiore a 24px contro i 12 di sopra.
          "flex flex-row items-center justify-between border-b py-3 px-4 bg-muted/20 cursor-pointer select-none hover:bg-muted/40 transition-colors [.border-b]:pb-3!"
        )}
      >
        <div className="flex flex-wrap items-center gap-2 min-w-0 flex-1">
          <span className="font-mono text-xs font-bold bg-muted px-1.5 py-0.5 rounded border">
            {project.code}
          </span>
          <CardTitle className="text-sm font-semibold truncate">
            {project.title}
          </CardTitle>
          {showIncasso && (
            <SalIncassoProgress
              compact
              percTotal={salSummary.percTotal}
              percPaid={salSummary.percPaid}
            />
          )}
          {/* Badge di stato standard, sempre visibile — come /bilancio (#97):
              via lo span ambra col testo grezzo dello stato. */}
          <Badge
            variant="outline"
            className={cn(
              "gap-1 text-[10px]",
              statusMeta.className,
              statusMeta.borderClassName
            )}
          >
            <statusMeta.icon className="size-3" />
            {statusMeta.label}
          </Badge>
        </div>

        <div className="flex items-center gap-3 shrink-0 ml-4" onClick={(e) => e.stopPropagation()}>
          <Button
            variant="outline"
            size="sm"
            className="h-8 gap-1.5"
            disabled={printing}
            onClick={() => void handlePrintPdf()}
            title="Stampa PDF del SAL di questa commessa"
          >
            <Printer className={cn("size-3.5", printing && "animate-pulse")} />
            Stampa PDF
          </Button>

          {/* Chi non ha accesso alle Commesse (ruoli di reparto) resterebbe sull'«Accesso
              negato»: il SAL lo legge e lo modifica da qui, dentro la pagina. */}
          {canAccessFeature("nav.commesse") ? (
            <Button asChild variant="outline" size="sm" className="h-8 gap-1">
              <Link to={`/commesse/${project.id}/sal`} state={{ fromGlobal: "/sal" }}>
                <ExternalLink className="size-3.5" />
                Apri
              </Link>
            </Button>
          ) : null}

          <Button
            variant="ghost"
            size="icon"
            className="h-8 w-8"
            onClick={onToggleExpanded}
          >
            {expanded ? (
              <FolderOpen className="size-4 text-muted-foreground" />
            ) : (
              <Folder className="size-4 text-muted-foreground" />
            )}
          </Button>
        </div>
      </CardHeader>

      {/* Corpo in stile MoM card (#91): righe label/valore con i dati dell'intestazione
          del foglio SAL. Le commesse senza righe SAL non hanno riga di riepilogo
          (HAVING COUNT>0 lato server) → i campi mostrano «—». */}
      <div
        className={cn(
          "grid gap-x-8 gap-y-1.5 px-4 py-3 text-sm sm:grid-cols-2 xl:grid-cols-3",
          expanded && "border-b"
        )}
      >
        <SalCardRow label="Cliente">{project.customerName || "—"}</SalCardRow>
        <SalCardRow label="PM">{project.pmName || "—"}</SalCardRow>
        <SalCardRow label="PO - Ordine">{salSummary?.po || "—"}</SalCardRow>
        <SalCardRow label="Rif. Offerta">{salSummary?.rifOfferta || "—"}</SalCardRow>
        {canSeeEconomics && (
          <SalCardRow label="Importo Ordine">
            <span className="tabular-nums">{euro(salSummary?.valore)}</span>
          </SalCardRow>
        )}
        <SalCardRow label="Step aperti">
          {salSummary ? (
            <span className="flex items-center gap-1.5">
              <span className="tabular-nums">
                {salSummary.open} / {salSummary.total}
              </span>
              {salSummary.warn > 0 && (
                <span className="rounded bg-red-100 px-1 text-[10px] font-bold text-red-700">
                  {salSummary.warn} scadute
                </span>
              )}
              {salSummary.pre > 0 && (
                <span className="rounded bg-yellow-100 px-1 text-[10px] font-bold text-yellow-700">
                  {salSummary.pre} imminenti
                </span>
              )}
              {salSummary.incasso > 0 && (
                <span className="rounded bg-rose-100 px-1 text-[10px] font-bold text-rose-700">
                  {salSummary.incasso} incassi scaduti
                </span>
              )}
            </span>
          ) : (
            "—"
          )}
        </SalCardRow>
      </div>

      {expanded && (
        <CardContent className="p-4 bg-zinc-50/50">
          <ProjectSal
            projectId={project.id}
            projectCode={project.code}
            projectTitle={project.title}
          />
        </CardContent>
      )}
    </Card>
  )
}

/** Riga label/valore in stile MoM card (etichetta a sinistra, valore a destra). */
function SalCardRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-2">
      <span className="text-muted-foreground">{label}</span>
      <span className="min-w-0 truncate text-right font-medium">{children}</span>
    </div>
  )
}
