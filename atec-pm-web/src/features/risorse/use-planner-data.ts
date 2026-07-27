// ── Dati del planner: caricamento, realtime, messaggi di stato, digest email ──

import * as React from "react"

import { ApiError } from "@/lib/api/client"
import { fetchNotifyPending } from "@/lib/api/digest"
import {
  fetchAssignments,
  fetchProjectLookups,
  fetchResourceLookups,
} from "@/lib/api/resource-planner"
import type { LookupItem, ResAssignmentDto } from "@/lib/api/types"
import { useResourcePlannerHub } from "@/lib/signalr/use-resource-planner-hub"

export function usePlannerData(canEdit: boolean) {
  const [assignments, setAssignments] = React.useState<ResAssignmentDto[]>([])
  const [resources, setResources] = React.useState<LookupItem[]>([])
  const [projects, setProjects] = React.useState<LookupItem[]>([])
  const [loading, setLoading] = React.useState(true)
  const [status, setStatus] = React.useState<string | null>(null)

  // Digest email: badge "modifiche da notificare" + dialog di invio selettivo.
  const [pendingNotify, setPendingNotify] = React.useState(0)
  const [emailConfigurata, setEmailConfigurata] = React.useState(false)

  /** Id della connessione SignalR: si passa alle API di scrittura per non
   *  rimbalzare a sé stessi l'evento realtime della propria modifica. */
  const connRef = React.useRef<string | null>(null)

  const flashStatus = React.useCallback((msg: string) => {
    setStatus(msg)
    window.setTimeout(() => setStatus(null), 2500)
  }, [])

  const reload = React.useCallback(async () => {
    try {
      const list = await fetchAssignments()
      setAssignments(list)
    } catch (e) {
      flashStatus(e instanceof ApiError ? e.message : "Errore di caricamento")
    }
  }, [flashStatus])

  React.useEffect(() => {
    let alive = true
    void (async () => {
      setLoading(true)
      try {
        const [a, res, proj] = await Promise.all([
          fetchAssignments(),
          fetchResourceLookups(),
          fetchProjectLookups(),
        ])
        if (!alive) return
        setAssignments(a)
        setResources(res)
        setProjects(proj)
      } catch (e) {
        if (alive)
          flashStatus(
            e instanceof ApiError ? e.message : "Errore di caricamento"
          )
      } finally {
        if (alive) setLoading(false)
      }
    })()
    return () => {
      alive = false
    }
  }, [flashStatus])

  const refreshPendingNotify = React.useCallback(async () => {
    if (!canEdit) return
    try {
      const pending = await fetchNotifyPending()
      setPendingNotify(pending.totalChanges)
      setEmailConfigurata(pending.emailConfigurata)
    } catch {
      /* badge best-effort: non disturbare l'utente per un conteggio informativo */
    }
  }, [canEdit])

  React.useEffect(() => {
    void refreshPendingNotify()
  }, [refreshPendingNotify])

  // ── Realtime: ricarica quando un altro utente modifica ────────
  const onRealtime = React.useCallback(() => {
    void reload().then(() => flashStatus("Aggiornato"))
    void refreshPendingNotify()
  }, [reload, refreshPendingNotify, flashStatus])
  const onlineIds = useResourcePlannerHub(onRealtime, connRef)

  return {
    assignments,
    setAssignments,
    resources,
    projects,
    loading,
    status,
    flashStatus,
    reload,
    pendingNotify,
    emailConfigurata,
    refreshPendingNotify,
    connRef,
    onlineIds,
  }
}
