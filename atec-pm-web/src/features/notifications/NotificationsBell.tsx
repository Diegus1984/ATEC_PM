import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useNavigate } from "react-router-dom"
import { Bell, BellOff, CheckCheck, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import {
  deleteNotification,
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
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

  // La lista si carica solo quando il popover è aperto.
  const listQuery = useQuery({
    queryKey: NOTIFICATIONS_LIST_KEY,
    queryFn: () => fetchNotifications(false, 50),
    enabled: open,
  })

  function invalidateAll() {
    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_BADGE_KEY })
    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_LIST_KEY })
  }

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
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div className="text-sm font-semibold">
            Notifiche
            {unreadCount > 0 ? (
              <span className="ml-1 font-normal text-muted-foreground">
                ({unreadCount})
              </span>
            ) : null}
          </div>
          {unreadCount > 0 ? (
            <Button
              variant="ghost"
              size="sm"
              className="h-7 gap-1 px-2 text-xs"
              onClick={() => readAllMutation.mutate()}
              disabled={readAllMutation.isPending}
            >
              <CheckCheck className="size-3.5" />
              Segna tutte lette
            </Button>
          ) : null}
        </div>

        <div className="max-h-[26rem] overflow-y-auto">
          {listQuery.isLoading ? (
            <div className="px-4 py-8 text-center text-sm text-muted-foreground">
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
      </PopoverContent>
    </Popover>
  )
}
