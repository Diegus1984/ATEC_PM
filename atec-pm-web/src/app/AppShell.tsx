import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Navigate, Outlet, useLocation, useNavigate } from "react-router-dom"
import { Bug, ChevronDown, LogOut, Search } from "lucide-react"

import { AppUpdateBanner } from "@/app/AppUpdateBanner"
import { ErrorBoundary } from "@/app/ErrorBoundary"
import { NoAccessNotice } from "@/app/NoAccessNotice"
import { BugReportDialog } from "@/features/bug-reports/BugReportDialog"
import { CommandPalette } from "@/components/shared/command-palette"
import { Collapsible } from "@/components/ui/collapsible"
import { AtecBrandIcon } from "@/components/branding/AtecBrandIcon"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Separator } from "@/components/ui/separator"
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuBadge,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
  useSidebar,
} from "@/components/ui/sidebar"
import {
  NAV_GROUPS,
  findNavItemByPath,
  type NavGroupConfig,
  type NavItemConfig,
} from "@/config/navigation"
import { useBugReportsCount } from "@/features/bug-reports/useBugReportsCount"
import { useProjectsRealtime } from "@/features/commesse/use-projects-realtime"
import { NotificationsBell } from "@/features/notifications/NotificationsBell"
import { ChatInboxBell } from "@/features/commesse/chat/ChatInboxBell"
import { useChatInboxBadge } from "@/features/commesse/chat/useChatInboxBadge"
import { useOreCommessaBadge } from "@/features/ore-commessa/useOreCommessaBadge"
import { useTravelBadge } from "@/features/trasferta/useTravelBadge"
import { useDeadlinesCount } from "@/features/scadenze/useDeadlinesCount"
import { useSalWarnings } from "@/features/sal/useSalWarnings"
import { useDdpUpdatedList } from "@/features/gestore-ddp/useDdpUpdatedList"
import { APP_BUILD, fetchServerVersion } from "@/lib/app-version"
import { cn } from "@/lib/utils"
import {
  canAccessFeature,
  canWriteFeature,
  getAuthFeaturesSnapshot,
  subscribeAuthFeatures,
} from "@/lib/auth/permissions"
import {
  clearSession,
  getSession,
  isLoginComplete,
  onSessionExpired,
} from "@/lib/auth/session"

function userInitials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) {
    return "UT"
  }
  if (parts.length === 1) {
    return parts[0].slice(0, 2).toUpperCase()
  }
  return `${parts[0][0] ?? ""}${parts[parts.length - 1][0] ?? ""}`.toUpperCase()
}

function filterNavGroups(): NavGroupConfig[] {
  return NAV_GROUPS.map((group) => ({
    ...group,
    items: group.items
      .map((item) => {
        if (item.children) {
          const visibleChildren = item.children.filter((child) =>
            canAccessFeature(child.featureKey)
          )
          return {
            ...item,
            children: visibleChildren,
          }
        }
        return item
      })
      .filter((item) => {
        if (item.children) {
          return item.children.length > 0
        }
        return canAccessFeature(item.featureKey)
      }),
  })).filter((group) => group.items.length > 0)
}

function navGroupsCollapsedKey(): string {
  const employeeId = getSession()?.user.employeeId ?? "anon"
  return `atec.sidebar.nav-groups.collapsed.${employeeId}`
}

function isNavActive(item: NavItemConfig, pathname: string): boolean {
  if (item.path === "/") {
    return pathname === "/"
  }
  if (item.path === "/mom") {
    return pathname === "/mom" || (pathname.startsWith("/mom/") && !pathname.startsWith("/mom/note"))
  }
  if (item.path === "/config-sezioni") {
    return pathname === "/config-sezioni"
  }
  if (item.path === "/admin/sal-conditions") {
    return pathname === "/admin/sal-conditions"
  }
  if (item.path === "/ddp-destinazioni") {
    return pathname === "/ddp-destinazioni"
  }
  return pathname === item.path || pathname.startsWith(item.path + "/")
}

/**
 * Voce di menu che apre un sottomenu (es. «Conf. Sezioni di costo»).
 *
 * Con la barra ridotta a sole icone il sottomenu lo nasconde il CSS di shadcn
 * (`group-data-[collapsible=icon]:hidden`): in quello stato il clic riapre la
 * barra invece di aprire un elenco che nessuno vedrebbe — altrimenti le pagine
 * figlie resterebbero irraggiungibili dal menu.
 */
function NavSubmenuItem({
  item,
  pathname,
  open,
  onToggle,
  onNavigate,
}: {
  item: NavItemConfig
  pathname: string
  open: boolean
  onToggle: () => void
  onNavigate: (path: string) => void
}) {
  const { state, isMobile, setOpen } = useSidebar()
  const barCollapsed = !isMobile && state === "collapsed"
  const Icon = item.icon
  const children = item.children ?? []
  const isParentActive = children.some((child) => isNavActive(child, pathname))
  const isSubmenuOpen = open && !barCollapsed

  // Aprire il contenitore porta anche sulla prima pagina figlia: un clic solo e
  // sei dove serve. Se sei già dentro al gruppo NON ti sposta: staresti
  // lavorando su una figlia e ti ritroveresti sull'altra.
  function apriSullaPrimaFiglia() {
    const prima = children[0]
    if (prima && !isParentActive) onNavigate(prima.path)
  }

  function handleParentClick() {
    if (barCollapsed) {
      setOpen(true)
      if (!open) onToggle()
      apriSullaPrimaFiglia()
      return
    }
    if (!open) {
      onToggle()
      apriSullaPrimaFiglia()
      return
    }
    onToggle()
  }

  return (
    <SidebarMenuItem>
      <SidebarMenuButton
        isActive={isParentActive}
        tooltip={item.label}
        onClick={handleParentClick}
        className="cursor-pointer"
      >
        <Icon />
        <span className="truncate">{item.label}</span>
        <ChevronDown
          className={cn(
            "ml-auto size-3.5 shrink-0 text-sidebar-foreground/50 transition-transform duration-200",
            !isSubmenuOpen && "-rotate-90"
          )}
          aria-hidden="true"
        />
      </SidebarMenuButton>
      <Collapsible
        open={isSubmenuOpen}
        style={{ "--accordion-duration": "200ms" } as React.CSSProperties}
      >
        <SidebarMenuSub>
          {children.map((subItem) => {
            const SubIcon = subItem.icon
            return (
              <SidebarMenuSubItem key={subItem.id}>
                <SidebarMenuSubButton
                  asChild
                  isActive={isNavActive(subItem, pathname)}
                >
                  <button type="button" onClick={() => onNavigate(subItem.path)}>
                    <SubIcon />
                    <span className="truncate">{subItem.label}</span>
                  </button>
                </SidebarMenuSubButton>
              </SidebarMenuSubItem>
            )
          })}
        </SidebarMenuSub>
      </Collapsible>
    </SidebarMenuItem>
  )
}

export function AppShell() {
  const location = useLocation()
  const navigate = useNavigate()
  const session = getSession()
  const [reportDialogOpen, setReportDialogOpen] = React.useState(false)
  const [commandPaletteOpen, setCommandPaletteOpen] = React.useState(false)

  // Versione del server: cambia solo quando si pubblica, quindi non la si ricontrolla di
  // continuo. Se non arriva (server piu' vecchio, o momento del riavvio) resta null e la
  // riga qui sotto mostra solo la parte «Web», come faceva prima.
  const versioneServerQuery = useQuery({
    queryKey: ["server-version"],
    queryFn: fetchServerVersion,
    staleTime: 10 * 60 * 1000,
    refetchOnWindowFocus: false,
  })
  const versioneServer = versioneServerQuery.data ?? null

  // Persistenza delle macro sezioni collassate (ricordate per utente in localStorage)
  const [collapsedGroupIds, setCollapsedGroupIds] = React.useState<Set<string>>(() => {
    try {
      const raw = localStorage.getItem(navGroupsCollapsedKey())
      if (!raw) return new Set()
      const parsed = JSON.parse(raw) as unknown
      return Array.isArray(parsed)
        ? new Set(parsed.filter((k): k is string => typeof k === "string"))
        : new Set()
    } catch {
      return new Set()
    }
  })

  const toggleGroupCollapse = React.useCallback((groupId: string) => {
    setCollapsedGroupIds((prev) => {
      const next = new Set(prev)
      if (next.has(groupId)) {
        next.delete(groupId)
      } else {
        next.add(groupId)
      }
      try {
        localStorage.setItem(navGroupsCollapsedKey(), JSON.stringify([...next]))
      } catch {
        /* storage non disponibile */
      }
      return next
    })
  }, [])

  // Sottomenu aperti (es. Conf. Sezioni di costo, Anagrafiche SAL, Conf. DDP)
  const [openSubmenuIds, setOpenSubmenuIds] = React.useState<Set<string>>(
    () =>
      new Set([
        "config-sezioni-group",
        "sal-anagrafiche-group",
        "ddp-config-group",
      ])
  )

  const toggleSubmenu = React.useCallback((itemId: string) => {
    setOpenSubmenuIds((prev) => {
      const next = new Set(prev)
      if (next.has(itemId)) {
        next.delete(itemId)
      } else {
        next.add(itemId)
      }
      return next
    })
  }, [])

  // Atterrando su una pagina figlia (link diretto, Command Palette) il suo
  // sottomenu si apre da solo. NON lo teniamo forzato aperto finché è attivo,
  // altrimenti il clic sul contenitore non riuscirebbe più a richiuderlo.
  const { pathname } = location
  React.useEffect(() => {
    const parent = NAV_GROUPS.flatMap((group) => group.items).find((item) =>
      item.children?.some((child) => isNavActive(child, pathname))
    )
    if (!parent) return
    setOpenSubmenuIds((prev) =>
      prev.has(parent.id) ? prev : new Set(prev).add(parent.id)
    )
  }, [pathname])

  // Scorciatoia globale Ctrl+K / Cmd+K e '/' per aprire la Command Palette universale
  React.useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if ((e.key === "k" || e.key === "K") && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        setCommandPaletteOpen((prev) => !prev)
        return
      }
      if (
        e.key === "/" &&
        !commandPaletteOpen &&
        !["INPUT", "TEXTAREA", "SELECT"].includes(
          (e.target as HTMLElement)?.tagName
        ) &&
        !(e.target as HTMLElement)?.isContentEditable
      ) {
        e.preventDefault()
        setCommandPaletteOpen(true)
      }
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [commandPaletteOpen])
  // I permessi stanno in variabili di modulo, non nello stato di React: senza sottoscrizione
  // il menu resterebbe quello del primo render — vuoto per sempre anche dopo un «Riprova»
  // riuscito. È anche l'aggancio per il futuro aggiornamento dei permessi a caldo, ed è per
  // questo che si guarda l'istantanea intera e non il solo stato: una ricarica che parte da
  // «ready» e ci torna cambia i permessi ma non lo stato, e il menu non si ridisegnerebbe.
  const { status: featuresStatus } = React.useSyncExternalStore(
    subscribeAuthFeatures,
    getAuthFeaturesSnapshot
  )
  // Ricalcolato a ogni render, senza `useMemo`: `filterNavGroups()` legge i permessi da
  // fuori React, quindi memorizzarlo su una lista di dipendenze finte (era `[pathname]`)
  // rispondeva solo per caso. Sono due filtri su una manciata di voci: costa meno del bug.
  const navGroups = filterNavGroups()
  // Nessuna voce di menu = nessuna funzione concessa, oppure permessi mai arrivati. In
  // entrambi i casi non c'è NIENTE da aprire: al posto del contenuto va scritto il perché,
  // dentro la shell — la testata con «Esci» deve restare a portata di mano.
  const hasNoAccess = navGroups.length === 0
  const currentPage = findNavItemByPath(location.pathname)
  const { pendingCount, sectionCounts } = useDeadlinesCount()
  const bugReportsCount = useBugReportsCount()
  // Messaggi di chat non letti: stessa query della campanella (nessuna richiesta in più),
  // mostrati come pallino rosso accanto alla voce «Chat» del menu (#78).
  const { unreadCount: chatUnreadCount } = useChatInboxBadge()
  // Scarico ore da verificare (#102/#109): persone che hanno imputato ore che il PM non
  // ha ancora dichiarato di aver guardato. Accendono la voce di menu, non solo il pallino.
  const travelPending = useTravelBadge()
  const oreCommessaPending = useOreCommessaBadge()
  // Warning SAL (#117): stessa sorgente delle viste «Warning Fatturazione» e «Warning
  // incasso fattura» della pagina /sal, cioè gli alert del prospetto. Prima il pallino
  // veniva dalle scadenze a 7 giorni e diceva un numero diverso da quello delle viste —
  // lo stesso disallineamento che la #114 aveva già corretto per la card della Dashboard,
  // lasciando però indietro la voce di menu.
  const salWarningsCount = useSalWarnings().length
  // DDP da verificare (#118): stessa lista della card «DDP Commesse» in Dashboard, stessa
  // chiave di cache — nessuna richiesta in più. Prende il posto del vecchio conteggio dalle
  // scadenze (materiale con data entro 7 giorni): erano due cose diverse sullo stesso
  // pallino, e quella che serve al PM è «qualcuno ha toccato una DDP e non l'ho ancora
  // aperta». Il materiale in scadenza resta nella campanella «Scadenze».
  const ddpDaVerificare = useDdpUpdatedList(7).length
  // Anagrafica commesse in tempo reale: una commessa creata/eliminata da un collega
  // compare o sparisce da tutti gli elenchi aperti senza ricaricare la pagina.
  useProjectsRealtime()

  React.useEffect(() => {
    return onSessionExpired(() => {
      clearSession()
      navigate("/login", { replace: true })
    })
  }, [navigate])

  if (!isLoginComplete()) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  function handleLogout() {
    clearSession()
    navigate("/login", { replace: true })
  }

  const fullName = session?.user.fullName ?? "Utente"

  return (
    <SidebarProvider>
      <Sidebar variant="inset" collapsible="icon">
        <SidebarHeader>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton size="lg" className="pointer-events-none h-auto py-1.5">
                <AtecBrandIcon size="sm" />
                <div className="grid flex-1 text-left text-sm leading-tight">
                  <span className="truncate font-medium">ATEC PM</span>
                  {/* Build in chiaro: dice a colpo d'occhio (anche al telefono, leggendola
                      da chi chiama) su quale versione sta girando quella postazione.
                      Due righe e non una: affiancate non ci stanno nella barra e la
                      seconda finiva tagliata a meta'. */}
                  <span
                    className="truncate text-xs text-sidebar-foreground/70"
                    title={`Client web installato su questa postazione: ${APP_BUILD}`}
                  >
                    Web · {APP_BUILD}
                  </span>
                  {versioneServer && (
                    <span
                      className="truncate text-xs text-sidebar-foreground/70"
                      title={`Server installato: ${versioneServer}`}
                    >
                      Srv · {versioneServer}
                    </span>
                  )}
                </div>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarHeader>

        <SidebarContent>
          {navGroups.map((group) => {
            const isGroupOpen = !collapsedGroupIds.has(group.id)
            const badgeById: Record<string, number> = {
              scadenze: pendingCount,
              "bug-reports": bugReportsCount,
              chat: chatUnreadCount,
              trasferta: travelPending,
              "ore-commessa": oreCommessaPending,
              sal: salWarningsCount,
              "gestore-ddp": ddpDaVerificare,
            }
            const groupBadgeCount = group.items.reduce(
              (sum, item) => sum + (badgeById[item.id] ?? sectionCounts?.[item.id] ?? 0),
              0
            )

            return (
              <SidebarGroup
                key={group.id}
                className={group.pinBottom ? "mt-auto" : undefined}
              >
                <SidebarGroupLabel asChild>
                  <button
                    type="button"
                    onClick={() => toggleGroupCollapse(group.id)}
                    className="flex w-full cursor-pointer items-center justify-between rounded-md px-2 text-xs font-medium text-sidebar-foreground/70 transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground select-none group-data-[collapsible=icon]:pointer-events-none"
                    aria-expanded={isGroupOpen}
                  >
                    <span className="truncate">{group.label}</span>
                    <div className="flex items-center gap-1.5 shrink-0">
                      {!isGroupOpen && groupBadgeCount > 0 ? (
                        <span className="flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1.5 font-mono text-[10px] font-semibold tabular-nums text-destructive-foreground">
                          {groupBadgeCount}
                        </span>
                      ) : null}
                      <ChevronDown
                        className={cn(
                          "size-3.5 shrink-0 text-sidebar-foreground/50 transition-transform duration-200",
                          !isGroupOpen && "-rotate-90"
                        )}
                        aria-hidden="true"
                      />
                    </div>
                  </button>
                </SidebarGroupLabel>
                <Collapsible
                  open={isGroupOpen}
                  style={{ "--accordion-duration": "200ms" } as React.CSSProperties}
                >
                  <SidebarGroupContent>
                    <SidebarMenu>
                      {group.items.map((item) => {
                        const Icon = item.icon
                        const badgeCount =
                          badgeById[item.id] ?? sectionCounts?.[item.id] ?? 0

                        if (item.children && item.children.length > 0) {
                          return (
                            <NavSubmenuItem
                              key={item.id}
                              item={item}
                              pathname={location.pathname}
                              open={openSubmenuIds.has(item.id)}
                              onToggle={() => toggleSubmenu(item.id)}
                              onNavigate={(path) => navigate(path)}
                            />
                          )
                        }

                        const active = isNavActive(item, location.pathname)
                        // #102/#109: le due voci dello scarico ore si accendono anche nel
                        // testo — verde grassetto — perché il pallino sparisce quando la
                        // barra è compressa a sole icone.
                        const daVerificare =
                          (item.id === "trasferta" || item.id === "ore-commessa") &&
                          badgeCount > 0

                        return (
                          <SidebarMenuItem key={item.id}>
                            <SidebarMenuButton
                              asChild
                              isActive={active}
                              tooltip={item.label}
                            >
                              <button type="button" onClick={() => navigate(item.path)}>
                                <Icon className={cn(daVerificare && "text-success")} />
                                <span className={cn(daVerificare && "font-bold text-success")}>
                                  {item.label}
                                </span>
                              </button>
                            </SidebarMenuButton>
                            {badgeCount > 0 && (
                              <SidebarMenuBadge className="bg-destructive text-destructive-foreground font-semibold">
                                {badgeCount}
                              </SidebarMenuBadge>
                            )}
                          </SidebarMenuItem>
                        )
                      })}
                    </SidebarMenu>
                  </SidebarGroupContent>
                </Collapsible>
              </SidebarGroup>
            )
          })}
        </SidebarContent>

        <SidebarFooter>
          <SidebarMenu>
            <SidebarMenuItem>
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <SidebarMenuButton
                    size="lg"
                    className="data-[state=open]:bg-sidebar-accent data-[state=open]:text-sidebar-accent-foreground"
                  >
                    <Avatar className="size-8 rounded-lg">
                      <AvatarFallback className="rounded-lg text-xs">
                        {userInitials(fullName)}
                      </AvatarFallback>
                    </Avatar>
                    <div className="grid flex-1 text-left text-sm leading-tight">
                      <span className="truncate font-medium">{fullName}</span>
                      <span className="truncate text-xs text-sidebar-foreground/70">
                        {session?.user.userRole}
                      </span>
                    </div>
                  </SidebarMenuButton>
                </DropdownMenuTrigger>
                <DropdownMenuContent
                  className="w-[--radix-dropdown-menu-trigger-width] min-w-56 rounded-lg"
                  side="top"
                  align="end"
                  sideOffset={4}
                >
                  <DropdownMenuLabel className="p-0 font-normal">
                    <div className="flex items-center gap-2 px-1 py-1.5 text-left text-sm">
                      <Avatar className="size-8 rounded-lg">
                        <AvatarFallback className="rounded-lg text-xs">
                          {userInitials(fullName)}
                        </AvatarFallback>
                      </Avatar>
                      <div className="grid flex-1 text-left text-sm leading-tight">
                        <span className="truncate font-medium">{fullName}</span>
                        <span className="truncate text-xs text-muted-foreground">
                          {session?.user.userRole}
                        </span>
                      </div>
                    </div>
                  </DropdownMenuLabel>
                  {canWriteFeature("nav.bug_reports") ? (
                    <>
                      <DropdownMenuItem onClick={() => setReportDialogOpen(true)}>
                        <Bug />
                        Segnala un problema
                      </DropdownMenuItem>
                      <DropdownMenuSeparator />
                    </>
                  ) : null}
                  <DropdownMenuItem onClick={handleLogout}>
                    <LogOut />
                    Esci
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarFooter>
        <SidebarRail />
      </Sidebar>

      <SidebarInset>
        <AppUpdateBanner />
        <header className="flex h-14 shrink-0 items-center gap-2 border-b px-4">
          <SidebarTrigger className="-ml-1" />
          <Separator orientation="vertical" className="mr-2 h-4" />
          <div className="flex flex-1 items-center justify-between gap-2">
            <div className="flex items-center gap-3">
              <h1 className="text-sm font-medium hidden lg:inline-block">
                {/* Senza permessi la rotta è comunque «/»: scrivere «Dashboard» sopra un
                    avviso di accesso negato farebbe credere che la pagina esista e sia rotta. */}
                {hasNoAccess ? "ATEC PM" : (currentPage?.label ?? "ATEC PM")}
              </h1>

              {/* Barra / Pulsante Trigger per la Command Palette */}
              <Button
                variant="outline"
                size="sm"
                className="h-8 w-44 sm:w-60 md:w-72 lg:w-80 justify-between bg-muted/30 hover:bg-muted text-xs text-muted-foreground hover:text-foreground border-border/60 shadow-none px-2.5 cursor-pointer"
                onClick={() => setCommandPaletteOpen(true)}
                title="Apri ricerca e comandi (Ctrl+K o /)"
              >
                <div className="flex items-center gap-2 truncate">
                  <Search className="size-3.5 shrink-0 opacity-60" />
                  <span className="truncate">Cerca commessa o comando...</span>
                </div>
                <kbd className="pointer-events-none hidden h-5 select-none items-center gap-0.5 rounded border bg-background px-1.5 font-mono text-[10px] font-medium text-muted-foreground opacity-100 sm:flex">
                  <span className="text-[10px]">Ctrl</span>K
                </kbd>
              </Button>
            </div>

            <div className="flex items-center gap-1">
              {canWriteFeature("nav.bug_reports") ? (
                <Button
                  variant="ghost"
                  size="sm"
                  className="gap-1.5 text-xs text-muted-foreground hover:text-foreground"
                  onClick={() => setReportDialogOpen(true)}
                  title="Segnala un problema da questa schermata"
                >
                  <Bug className="size-3.5" />
                  <span className="hidden sm:inline">Segnala</span>
                </Button>
              ) : null}
              {canAccessFeature("project.chat") ? <ChatInboxBell /> : null}
              <NotificationsBell />
              <Button variant="outline" size="sm" onClick={handleLogout}>
                <LogOut />
                Esci
              </Button>
            </div>
          </div>
        </header>

        <main className="flex flex-1 flex-col pt-0">
          <div className="@container/main flex flex-1 flex-col gap-4 px-4 py-4 md:gap-6 md:py-6 lg:px-6">
            <div className="animate-in fade-in-0 slide-in-from-bottom-2 duration-300">
              {/* `key` sulla rotta: un error boundary NON si azzera da solo: dopo un crash
                  resterebbe sul messaggio d'errore anche cambiando pagina dalla barra
                  laterale, e l'unica via d'uscita sarebbe ricaricare. Cambiando chiave React
                  monta un boundary nuovo a ogni rotta, quindi il crash resta confinato alla
                  pagina che l'ha causato. */}
              <ErrorBoundary key={location.pathname}>
                {hasNoAccess ? (
                  <NoAccessNotice status={featuresStatus} />
                ) : (
                  <Outlet />
                )}
              </ErrorBoundary>
            </div>
          </div>
        </main>
      </SidebarInset>

      {/* Command Palette Globale (Ctrl+K) */}
      <CommandPalette
        open={commandPaletteOpen}
        onOpenChange={setCommandPaletteOpen}
        onOpenBugReport={() => setReportDialogOpen(true)}
      />

      {canWriteFeature("nav.bug_reports") && reportDialogOpen ? (
        <BugReportDialog
          open={reportDialogOpen}
          bug={null}
          isAdmin={canWriteFeature("action.manage_bug_reports")}
          canWrite={true}
          onClose={() => setReportDialogOpen(false)}
          onSaved={() => setReportDialogOpen(false)}
        />
      ) : null}
    </SidebarProvider>
  )
}
