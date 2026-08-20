import * as React from "react"
import { Navigate, Outlet, useLocation, useNavigate } from "react-router-dom"
import { Bug, LogOut } from "lucide-react"

import { AppUpdateBanner } from "@/app/AppUpdateBanner"
import { ErrorBoundary } from "@/app/ErrorBoundary"
import { NoAccessNotice } from "@/app/NoAccessNotice"
import { BugReportDialog } from "@/features/bug-reports/BugReportDialog"
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
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
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
import { APP_BUILD } from "@/lib/app-version"
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
    items: group.items.filter((item) => canAccessFeature(item.featureKey)),
  })).filter((group) => group.items.length > 0)
}

function isNavActive(item: NavItemConfig, pathname: string): boolean {
  if (item.path === "/") {
    return pathname === "/"
  }
  return pathname.startsWith(item.path)
}

export function AppShell() {
  const location = useLocation()
  const navigate = useNavigate()
  const session = getSession()
  const [reportDialogOpen, setReportDialogOpen] = React.useState(false)
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
              <SidebarMenuButton size="lg" className="pointer-events-none">
                <AtecBrandIcon size="sm" />
                <div className="grid flex-1 text-left text-sm leading-tight">
                  <span className="truncate font-medium">ATEC PM</span>
                  {/* Build in chiaro: dice a colpo d'occhio (anche al telefono, leggendola
                      da chi chiama) su quale versione sta girando quella postazione. */}
                  <span
                    className="truncate text-xs text-sidebar-foreground/70"
                    title={`Versione installata: ${APP_BUILD}`}
                  >
                    Web · {APP_BUILD}
                  </span>
                </div>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarHeader>

        <SidebarContent>
          {navGroups.map((group) => (
            <SidebarGroup
              key={group.id}
              className={group.pinBottom ? "mt-auto" : undefined}
            >
              <SidebarGroupLabel>{group.label}</SidebarGroupLabel>
              <SidebarGroupContent>
                <SidebarMenu>
                  {group.items.map((item) => {
                    const Icon = item.icon
                    const active = isNavActive(item, location.pathname)
                    // Una mappa al posto della catena di ternari: con sei voci a numero
                    // proprio non si leggeva più. Il ramo `sectionCounts` resta il
                    // fallback per le voci alimentate dalle scadenze.
                    const badgeById: Record<string, number> = {
                      scadenze: pendingCount,
                      "bug-reports": bugReportsCount,
                      chat: chatUnreadCount,
                      trasferta: travelPending,
                      "ore-commessa": oreCommessaPending,
                    }
                    const badgeCount =
                      badgeById[item.id] ?? sectionCounts?.[item.id] ?? 0
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
            </SidebarGroup>
          ))}
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
            <h1 className="text-sm font-medium">
              {/* Senza permessi la rotta è comunque «/»: scrivere «Dashboard» sopra un
                  avviso di accesso negato farebbe credere che la pagina esista e sia rotta. */}
              {hasNoAccess ? "ATEC PM" : (currentPage?.label ?? "ATEC PM")}
            </h1>
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
