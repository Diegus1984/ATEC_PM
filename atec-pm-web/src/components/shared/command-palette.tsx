import * as React from "react"
import { useLocation, useNavigate } from "react-router-dom"
import { useQuery } from "@tanstack/react-query"
import {
  ArrowRight,
  Bug,
  CalendarClock,
  ClipboardList,
  Database,
  FileText,
  LogOut,
  NotebookPen,
  Plane,
  Plus,
  ReceiptText,
  Truck,
} from "lucide-react"

import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
  CommandShortcut,
} from "@/components/ui/command"
import { Badge } from "@/components/ui/badge"
import { ALL_NAV_ITEMS } from "@/config/navigation"
import { fetchProjectsLookup } from "@/lib/api/projects"
import { canAccessFeature, canWriteFeature } from "@/lib/auth/permissions"
import { clearSession } from "@/lib/auth/session"
import { useDebounced } from "@/lib/use-debounced"
import { sezioniVisibili } from "@/features/commesse/commessa-sections"
import { projectStatusMeta } from "@/features/commesse/project-status"
import { cn } from "@/lib/utils"

export interface CommandPaletteProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onOpenBugReport?: () => void
}

/** Viste speciali ad accesso rapido non presenti nel menu principale a primo livello */
const EXTRA_VIEWS = [
  {
    id: "ddp-consegne",
    label: "Gestore DDP — Controllo Consegne",
    path: "/gestore-ddp/controllo/consegne",
    featureKey: "nav.gestore_ddp",
    icon: Truck,
    keywords: "ddp consegne materiali arrivi fornitori magazzino",
  },
  {
    id: "ddp-magazzino",
    label: "Gestore DDP — Feedback Magazzino",
    path: "/gestore-ddp/feedback/magazzino",
    featureKey: "nav.gestore_ddp",
    icon: ClipboardList,
    keywords: "ddp magazzino giacenze feedback prelievi",
  },
  {
    id: "sal-warning-fatt",
    label: "SAL — Warning Fatturazione",
    path: "/sal?view=warning-fatturazione",
    featureKey: "nav.sal",
    icon: ReceiptText,
    keywords: "sal warning fatturazione alert ritardi scadenze",
  },
  {
    id: "sal-warning-incasso",
    label: "SAL — Warning Incasso Fattura",
    path: "/sal?view=warning-incasso",
    featureKey: "nav.sal",
    icon: ReceiptText,
    keywords: "sal warning incassi fatture scadute crediti",
  },
  {
    id: "sal-prospetto",
    label: "SAL — Prospetto Ipotesi",
    path: "/sal?view=prospetto",
    featureKey: "nav.sal",
    icon: ReceiptText,
    keywords: "sal prospetto ipotesi fatture cashflow",
  },
  {
    id: "risorse-ferie",
    label: "Risorse — Piano Ferie & Assenze",
    path: "/risorse/ferie",
    featureKey: "nav.risorse",
    icon: CalendarClock,
    keywords: "ferie permessi assenze risorse calendario dipendenti",
  },
  {
    id: "mom-note-rapide",
    label: "Note Rapide MoM",
    path: "/mom-note",
    featureKey: "nav.mom",
    icon: NotebookPen,
    keywords: "note rapide verbali appunti riunioni",
  },
  {
    id: "codex-ricodifica",
    label: "Codex — Ricodifica Articoli",
    path: "/codex/ricodifica",
    featureKey: "nav.codex",
    icon: Database,
    keywords: "codex ricodifica articoli 5xx 6xx 7xx",
  },
]

export function CommandPalette({
  open,
  onOpenChange,
  onOpenBugReport,
}: CommandPaletteProps) {
  const navigate = useNavigate()
  const location = useLocation()
  const [search, setSearch] = React.useState("")
  const debouncedSearch = useDebounced(search.trim(), 150)

  // Rileva se ci troviamo all'interno del dettaglio di una commessa
  const projectMatch = location.pathname.match(/^\/commesse\/(\d+)/)
  const currentProjectId = projectMatch ? Number(projectMatch[1]) : null

  const canSeeEconomics = canAccessFeature("data.budget")

  // Caricamento commesse per la ricerca
  const projectsQuery = useQuery({
    queryKey: ["command-palette-projects", debouncedSearch],
    queryFn: () =>
      fetchProjectsLookup({
        search: debouncedSearch || undefined,
        includeClosed: true,
        pageSize: 30,
      }),
    enabled: open,
    staleTime: 1000 * 30,
  })

  // Reset del termine di ricerca quando la modale viene chiusa
  React.useEffect(() => {
    if (!open) {
      setSearch("")
    }
  }, [open])

  function handleSelect(callback: () => void) {
    onOpenChange(false)
    callback()
  }

  function handleLogout() {
    clearSession()
    navigate("/login", { replace: true })
  }

  const projects = projectsQuery.data?.items ?? []
  const visibleNavItems = ALL_NAV_ITEMS.filter((item) =>
    canAccessFeature(item.featureKey)
  )
  const visibleExtraViews = EXTRA_VIEWS.filter((item) =>
    canAccessFeature(item.featureKey)
  )

  // Sezioni per il contesto della commessa corrente
  const activeProjectSections = currentProjectId
    ? sezioniVisibili(canSeeEconomics, canAccessFeature)
    : []

  return (
    <CommandDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Ricerca universale e comandi"
      description="Cerca una commessa, naviga tra i moduli o esegui un'azione rapida"
      className="sm:max-w-2xl"
    >
      <CommandInput
        placeholder="Cerca commessa, cliente, codice DDP, pagina o comando..."
        value={search}
        onValueChange={setSearch}
      />

      <CommandList className="scrollbar-visible max-h-[380px] p-1.5">
        <CommandEmpty className="py-8 text-center text-sm text-muted-foreground">
          {projectsQuery.isLoading ? (
            <div className="flex items-center justify-center gap-2">
              <span className="inline-block size-4 animate-spin rounded-full border-2 border-primary border-t-transparent" />
              <span>Ricerca in corso...</span>
            </div>
          ) : (
            <div className="space-y-1">
              <p className="font-medium text-foreground">Nessun risultato trovato</p>
              <p className="text-xs">
                Prova a cercare per codice commessa, cliente, descrizione o modulo.
              </p>
            </div>
          )}
        </CommandEmpty>

        {/* 🎯 CONTESTO COMMESSA CORRENTE */}
        {currentProjectId && activeProjectSections.length > 0 && (
          <CommandGroup heading={`🎯 Commessa Corrente (#${currentProjectId})`}>
            {activeProjectSections.map((sec) => (
              <CommandItem
                key={`current-sec-${sec.key}`}
                value={`commessa corrente ${currentProjectId} ${sec.label} ${sec.key}`}
                onSelect={() =>
                  handleSelect(() =>
                    navigate(`/commesse/${currentProjectId}/${sec.key}`)
                  )
                }
              >
                <span className="mr-2 text-base">{sec.icon ?? "📁"}</span>
                <span className="font-medium">{sec.label}</span>
                <span className="ml-auto text-xs text-muted-foreground">
                  Sezione #{currentProjectId}
                </span>
              </CommandItem>
            ))}
          </CommandGroup>
        )}

        {/* ⚡ AZIONI RAPIDE */}
        <CommandGroup heading="⚡ Azioni Rapide">
          {canWriteFeature("nav.commesse") && (
            <CommandItem
              value="nuova commessa crea progetto aggiungi nuovo"
              onSelect={() =>
                handleSelect(() =>
                  navigate("/commesse", { state: { newProject: true } })
                )
              }
            >
              <Plus className="mr-2 size-4 text-primary" />
              <span>Nuova Commessa...</span>
              <CommandShortcut>Alt + N</CommandShortcut>
            </CommandItem>
          )}

          {canAccessFeature("nav.timesheet") && (
            <CommandItem
              value="timesheet compila ore registra imputa oggi"
              onSelect={() => handleSelect(() => navigate("/timesheet"))}
            >
              <CalendarClock className="mr-2 size-4 text-emerald-600 dark:text-emerald-400" />
              <span>Compila Timesheet (Ore di oggi)</span>
              <CommandShortcut>Timesheet</CommandShortcut>
            </CommandItem>
          )}

          {canWriteFeature("nav.mom") && (
            <CommandItem
              value="nuovo verbale mom riunione crea verbale"
              onSelect={() =>
                handleSelect(() =>
                  navigate("/mom", { state: { newVerbale: true } })
                )
              }
            >
              <FileText className="mr-2 size-4 text-blue-600 dark:text-blue-400" />
              <span>Nuovo Verbale MoM...</span>
              <CommandShortcut>MoM</CommandShortcut>
            </CommandItem>
          )}

          {canAccessFeature("nav.trasferta") && (
            <CommandItem
              value="trasferta nota spese registra viaggio cantiere"
              onSelect={() => handleSelect(() => navigate("/trasferta"))}
            >
              <Plane className="mr-2 size-4 text-amber-600 dark:text-amber-400" />
              <span>Nuova Nota Trasferta / Spese</span>
              <CommandShortcut>Trasferte</CommandShortcut>
            </CommandItem>
          )}

          {canWriteFeature("nav.bug_reports") && onOpenBugReport && (
            <CommandItem
              value="segnala un problema bug report ticket errore assistenza"
              onSelect={() => handleSelect(onOpenBugReport)}
            >
              <Bug className="mr-2 size-4 text-destructive" />
              <span>Segnala un problema...</span>
              <CommandShortcut>Bug</CommandShortcut>
            </CommandItem>
          )}

          <CommandItem
            value="esci logout disconnetti chiudi sessione"
            onSelect={() => handleSelect(handleLogout)}
          >
            <LogOut className="mr-2 size-4 text-muted-foreground" />
            <span>Esci (Logout)</span>
          </CommandItem>
        </CommandGroup>

        <CommandSeparator />

        {/* 📁 COMMESSE & PROGETTI */}
        {projects.length > 0 && (
          <CommandGroup heading="📁 Commesse & Progetti">
            {projects.map((project) => {
              const meta = projectStatusMeta(project.status)
              const StatusIcon = meta.icon
              const searchValue = `${project.code} ${project.title} ${project.customerName ?? ""} ${project.pmName ?? ""} ${meta.label} commessa`

              return (
                <CommandItem
                  key={`project-${project.id}`}
                  value={searchValue}
                  onSelect={() =>
                    handleSelect(() => navigate(`/commesse/${project.id}`))
                  }
                  className="flex items-center gap-2.5 py-2"
                >
                  <StatusIcon
                    className={cn("size-4 shrink-0", meta.className)}
                  />
                  <div className="flex flex-1 min-w-0 items-baseline gap-2">
                    <span className="font-semibold text-foreground shrink-0 font-mono text-xs">
                      {project.code}
                    </span>
                    <span className="truncate text-xs text-foreground/90">
                      {project.title}
                    </span>
                    {project.customerName && (
                      <span className="truncate text-[11px] text-muted-foreground hidden sm:inline">
                        · {project.customerName}
                      </span>
                    )}
                  </div>

                  <div className="flex items-center gap-1.5 shrink-0 ml-auto">
                    {project.pmName && (
                      <span className="text-[10px] text-muted-foreground font-medium hidden md:inline px-1 py-0.5 bg-muted rounded">
                        PM: {project.pmName}
                      </span>
                    )}
                    <Badge
                      variant="outline"
                      className={cn(
                        "text-[10px] px-1.5 py-0 h-4.5 font-normal",
                        meta.borderClassName,
                        meta.className
                      )}
                    >
                      {meta.label}
                    </Badge>
                  </div>
                </CommandItem>
              )
            })}
          </CommandGroup>
        )}

        <CommandSeparator />

        {/* 🔍 VISTE RAPIDE PM & OPERATIVE */}
        {visibleExtraViews.length > 0 && (
          <CommandGroup heading="🔍 Viste Rapide">
            {visibleExtraViews.map((item) => {
              const Icon = item.icon
              return (
                <CommandItem
                  key={item.id}
                  value={`${item.label} ${item.keywords}`}
                  onSelect={() => handleSelect(() => navigate(item.path))}
                >
                  <Icon className="mr-2 size-4 text-muted-foreground" />
                  <span>{item.label}</span>
                  <ArrowRight className="ml-auto size-3 opacity-40" />
                </CommandItem>
              )
            })}
          </CommandGroup>
        )}

        <CommandSeparator />

        {/* 🧭 NAVIGAZIONE MODULI */}
        <CommandGroup heading="🧭 Navigazione Moduli">
          {visibleNavItems.map((item) => {
            const Icon = item.icon
            return (
              <CommandItem
                key={`nav-${item.id}`}
                value={`${item.label} ${item.description ?? ""} navigazione vai`}
                onSelect={() => handleSelect(() => navigate(item.path))}
              >
                <Icon className="mr-2 size-4 text-muted-foreground" />
                <div className="flex flex-col">
                  <span className="font-medium leading-none">{item.label}</span>
                  {item.description && (
                    <span className="line-clamp-1 text-[11px] text-muted-foreground mt-0.5">
                      {item.description}
                    </span>
                  )}
                </div>
                <CommandShortcut>{item.path}</CommandShortcut>
              </CommandItem>
            )
          })}
        </CommandGroup>
      </CommandList>

      {/* FOOTER SCORCIATOIE */}
      <div className="flex items-center justify-between border-t border-border/40 bg-muted/20 px-3 py-2 text-[11px] text-muted-foreground">
        <div className="flex items-center gap-3">
          <span className="inline-flex items-center gap-1">
            <kbd className="rounded border bg-muted px-1.5 py-0.5 font-mono text-[10px] font-medium">↑</kbd>
            <kbd className="rounded border bg-muted px-1.5 py-0.5 font-mono text-[10px] font-medium">↓</kbd>
            <span>Naviga</span>
          </span>
          <span className="inline-flex items-center gap-1">
            <kbd className="rounded border bg-muted px-1.5 py-0.5 font-mono text-[10px] font-medium">↵</kbd>
            <span>Seleziona</span>
          </span>
          <span className="inline-flex items-center gap-1">
            <kbd className="rounded border bg-muted px-1.5 py-0.5 font-mono text-[10px] font-medium">ESC</kbd>
            <span>Chiudi</span>
          </span>
        </div>
        <div className="hidden sm:flex items-center gap-1.5">
          <span className="font-medium text-foreground/70">ATEC PM</span>
          <span>Command Palette</span>
        </div>
      </div>
    </CommandDialog>
  )
}
