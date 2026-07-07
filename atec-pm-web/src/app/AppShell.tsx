import * as React from "react"
import { Navigate, Outlet, useLocation, useNavigate } from "react-router-dom"
import { LogOut } from "lucide-react"

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
import { NotificationsBell } from "@/features/notifications/NotificationsBell"
import { canAccessFeature } from "@/lib/auth/permissions"
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
  const navGroups = React.useMemo(() => filterNavGroups(), [location.pathname])
  const currentPage = findNavItemByPath(location.pathname)

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
                  <span className="truncate text-xs text-sidebar-foreground/70">
                    Web
                  </span>
                </div>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarHeader>

        <SidebarContent>
          {navGroups.map((group) => (
            <SidebarGroup key={group.id}>
              <SidebarGroupLabel>{group.label}</SidebarGroupLabel>
              <SidebarGroupContent>
                <SidebarMenu>
                  {group.items.map((item) => {
                    const Icon = item.icon
                    const active = isNavActive(item, location.pathname)

                    return (
                      <SidebarMenuItem key={item.id}>
                        <SidebarMenuButton
                          asChild
                          isActive={active}
                          tooltip={item.label}
                        >
                          <button type="button" onClick={() => navigate(item.path)}>
                            <Icon />
                            <span>{item.label}</span>
                          </button>
                        </SidebarMenuButton>
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
                  <DropdownMenuSeparator />
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
        <header className="flex h-14 shrink-0 items-center gap-2 border-b px-4">
          <SidebarTrigger className="-ml-1" />
          <Separator orientation="vertical" className="mr-2 h-4" />
          <div className="flex flex-1 items-center justify-between gap-2">
            <h1 className="text-sm font-medium">
              {currentPage?.label ?? "ATEC PM"}
            </h1>
            <div className="flex items-center gap-1">
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
              <Outlet />
            </div>
          </div>
        </main>
      </SidebarInset>
    </SidebarProvider>
  )
}
