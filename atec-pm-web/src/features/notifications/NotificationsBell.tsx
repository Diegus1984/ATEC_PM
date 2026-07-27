import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useNavigate } from "react-router-dom"
import { Bell, BellOff, CheckCheck, RefreshCw, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { Switch } from "@/components/ui/switch"
import {
  deleteNotification,
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  cleanResolvedNotifications,
} from "@/lib/api/notifications"
import type { NotificationListItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import {
  NOTIFICATIONS_BADGE_KEY,
  useNotificationsBadge,
} from "./useNotificationsBadge"
import { severityStyle, timeAgo, isAlarmNotification } from "./notification-format"
import { getNotificationHref } from "./notification-navigation"

const NOTIFICATIONS_LIST_KEY = ["notifications-list"] as const

/**
 * Centro notifiche dell'header: campanella con badge non-letti (polling adattivo)
 * + popover con la lista. Click sulla riga → segna letta e, se collegata a una
 * commessa, naviga. Azioni: «Segna tutte lette», elimina singola.
 */
export function NotificationsBell() {
  const [open, setOpen] = React.useState(false)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { unreadCount } = useNotificationsBadge()

  // Stato per l'auto-pulizia delle notifiche risolte
  const [autoClean, setAutoClean] = React.useState(() => {
    const val = localStorage.getItem("atec-notifications-autoclean")
    return val !== "false" // Default a true
  })

  function invalidateAll() {
    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_BADGE_KEY })
    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_LIST_KEY })
  }

  const cleanMutation = useMutation({
    mutationFn: cleanResolvedNotifications,
    onSuccess: invalidateAll,
  })

  // Esegue l'auto-pulizia UNA volta all'apertura del popover, se abilitata.
  // NB: mai mettere `cleanMutation` nelle dipendenze — l'oggetto cambia identità
  // a ogni render e l'effetto richiamava mutate() in loop infinito; `mutate` è
  // stabile, `autoClean` letto via ref per non ri-scattare al toggle (che pulisce già da sé).
  const cleanMutate = cleanMutation.mutate
  const autoCleanRef = React.useRef(autoClean)
  autoCleanRef.current = autoClean
  React.useEffect(() => {
    if (open && autoCleanRef.current) {
      cleanMutate()
    }
  }, [open, cleanMutate])

  // La lista si carica solo quando il popover è aperto e non è in corso l'auto-pulizia
  const listQuery = useQuery({
    queryKey: NOTIFICATIONS_LIST_KEY,
    queryFn: () => fetchNotifications(false, 50),
    enabled: open && (!autoClean || !cleanMutation.isPending),
  })

  const readMutation = useMutation({
    mutationFn: markNotificationRead,
    onSuccess: invalidateAll,
  })
  const readAllMutation = useMutation({
    mutationFn: markAllNotificationsRead,
    onSuccess: invalidateAll,
  })
  const deleteMutation = useMutation({
    mutationFn: deleteNotification,
    onSuccess: invalidateAll,
  })

  function handleOpenItem(n: NotificationListItem) {
    if (!n.isRead) {
      readMutation.mutate(n.id)
    }
    const href = getNotificationHref(n)
    if (href) {
      setOpen(false)
      navigate(href)
    }
  }

  const items = listQuery.data ?? []
  const badgeText = unreadCount > 99 ? "99+" : String(unreadCount)

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="relative"
          aria-label="Notifiche"
        >
          <Bell />
          {unreadCount > 0 ? (
            <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-bold leading-none text-destructive-foreground">
              {badgeText}
            </span>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-96 p-0">
        <div className="flex flex-col border-b bg-muted/20">
          <div className="flex items-center justify-between px-4 py-2.5">
            <div className="text-sm font-semibold flex items-center gap-1">
              Notifiche
              {unreadCount > 0 ? (
                <span className="font-normal text-muted-foreground text-xs">
                  ({unreadCount})
                </span>
              ) : null}
            </div>
            {unreadCount > 0 ? (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 gap-1 px-2 text-xs text-muted-foreground hover:text-foreground"
                onClick={() => readAllMutation.mutate()}
                disabled={readAllMutation.isPending}
              >
                <CheckCheck className="size-3.5" />
                Segna tutte lette
              </Button>
            ) : null}
          </div>
          <div className="flex items-center justify-between px-4 py-1.5 border-t border-muted-foreground/10 bg-muted/40 text-[11px] text-muted-foreground select-none">
            <label className="flex items-center gap-1.5 cursor-pointer hover:text-foreground">
              <Switch
                size="sm"
                checked={autoClean}
                onCheckedChange={(checked) => {
                  setAutoClean(checked)
                  localStorage.setItem("atec-notifications-autoclean", String(checked))
                  if (checked) {
                    cleanMutate()
                  }
                }}
              />
              Auto-check (rimuovi risolte)
            </label>
            <Button
              variant="ghost"
              size="xs"
              className="h-5 px-1.5 text-[10px] text-muted-foreground hover:text-foreground gap-1"
              onClick={() => cleanMutation.mutate()}
              disabled={cleanMutation.isPending}
            >
              <RefreshCw className={cn("size-2.5", cleanMutation.isPending && "animate-spin")} />
              Pulisci ora
            </Button>
          </div>
        </div>

        <div className="max-h-[26rem] overflow-y-auto">
          {cleanMutation.isPending || listQuery.isLoading ? (
            <div className="px-4 py-8 text-center text-sm text-muted-foreground flex items-center justify-center gap-2">
              <RefreshCw className="size-4 animate-spin" />
              Caricamento…
            </div>
          ) : items.length === 0 ? (
            <div className="flex flex-col items-center gap-2 px-4 py-10 text-center text-sm text-muted-foreground">
              <BellOff className="size-6 opacity-50" />
              Nessuna notifica
            </div>
          ) : (
            <ul className="divide-y">
              {items.map((n) => {
                const alarm = isAlarmNotification(n)
                const sev = severityStyle(n.severity, n.notificationType)
                const SevIcon = sev.icon
                return (
                  <li
                    key={n.id}
                    className={cn(
                      "group relative flex gap-3 px-4 py-3 transition-colors hover:bg-muted/50",
                      !n.isRead && (alarm ? "bg-destructive/5" : "bg-primary/5")
                    )}
                  >
                    <SevIcon
                      className={cn("mt-0.5 size-4 shrink-0", sev.className)}
                    />
                    <button
                      type="button"
                      onClick={() => handleOpenItem(n)}
                      className="min-w-0 flex-1 text-left"
                    >
                      <div className="flex items-start gap-2">
                        <span
                          className={cn(
                            "min-w-0 flex-1 truncate text-sm",
                            !n.isRead ? "font-semibold" : "font-medium",
                            alarm && "text-destructive"
                          )}
                        >
                          {n.title}
                        </span>
                        {!n.isRead ? (
                          <span
                            className={cn(
                              "mt-1.5 size-2 shrink-0 rounded-full",
                              alarm ? "bg-destructive" : "bg-primary"
                            )}
                          />
                        ) : null}
                      </div>
                      {n.message ? (
                        <p
                          className={cn(
                            "mt-0.5 line-clamp-2 text-xs",
                            alarm
                              ? "text-destructive/80"
                              : "text-muted-foreground"
                          )}
                        >
                          {n.message}
                        </p>
                      ) : null}
                      <div
                        className={cn(
                          "mt-1 flex items-center gap-2 text-[11px]",
                          alarm ? "text-destructive/70" : "text-muted-foreground"
                        )}
                      >
                        {n.projectCode ? (
                          <span
                            className={cn(
                              "font-medium",
                              alarm ? "text-destructive" : "text-foreground/70"
                            )}
                          >
                            {n.projectCode}
                          </span>
                        ) : null}
                        <span>{timeAgo(n.createdAt)}</span>
                      </div>
                    </button>
                    <button
                      type="button"
                      aria-label="Elimina notifica"
                      onClick={() => deleteMutation.mutate(n.id)}
                      className="absolute right-2 top-2 rounded p-1 text-muted-foreground opacity-0 transition-opacity hover:bg-muted hover:text-foreground group-hover:opacity-100"
                    >
                      <X className="size-3.5" />
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
        <div className="border-t p-2 bg-muted/20 flex justify-center">
          <Button
            variant="link"
            size="sm"
            className="text-xs w-full h-8"
            onClick={() => {
              setOpen(false)
              navigate("/scadenze")
            }}
          >
            Vedi tutte le scadenze
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}
