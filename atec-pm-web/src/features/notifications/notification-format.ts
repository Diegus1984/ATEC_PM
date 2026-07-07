import {
  AlertCircle,
  AlertTriangle,
  CheckCircle2,
  Clock,
  Info,
  type LucideIcon,
} from "lucide-react"

import type { NotificationListItem } from "@/lib/api/types"

export interface SeverityStyle {
  icon: LucideIcon
  /** Classe colore per icona/accento (token del tema). */
  className: string
}

/** Materiali in ritardo e altre segnalazioni ALARM (DDP_OVERDUE). */
export function isAlarmNotification(
  item: Pick<NotificationListItem, "severity" | "notificationType">
): boolean {
  const severity = (item.severity || "").toUpperCase()
  const type = (item.notificationType || "").toUpperCase()
  return severity === "ALARM" || type === "DDP_OVERDUE"
}

/** Mappa la severità del DTO (INFO/WARNING/ERROR/SUCCESS/ALARM) a icona + colore. */
export function severityStyle(
  severity: string,
  notificationType?: string
): SeverityStyle {
  // Notifiche di scadenza (CHECKLIST_DUE / MOM_DUE / PROJECT_DUE): icona orologio,
  // rosso se scaduta (ALARM), giallo se in scadenza (WARNING).
  if (notificationType && notificationType.toUpperCase().endsWith("_DUE")) {
    return (severity || "").toUpperCase() === "ALARM"
      ? { icon: Clock, className: "text-destructive" }
      : { icon: Clock, className: "text-amber-500" }
  }

  if (notificationType && isAlarmNotification({ severity, notificationType })) {
    return { icon: AlertTriangle, className: "text-destructive" }
  }

  switch ((severity || "").toUpperCase()) {
    case "ALARM":
      return { icon: AlertTriangle, className: "text-destructive" }
    case "ERROR":
    case "CRITICAL":
      return { icon: AlertCircle, className: "text-destructive" }
    case "WARNING":
    case "WARN":
      return { icon: AlertTriangle, className: "text-amber-500" }
    case "SUCCESS":
      return { icon: CheckCircle2, className: "text-emerald-500" }
    default:
      return { icon: Info, className: "text-primary" }
  }
}

/**
 * Tempo relativo in italiano: "ora", "5 min fa", "3 h fa", "2 g fa";
 * oltre la settimana ripiega sulla data it-IT. Input ISO dal server.
 */
export function timeAgo(iso: string): string {
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) {
    return ""
  }
  const diffSec = Math.max(0, Math.floor((Date.now() - then) / 1000))
  if (diffSec < 60) {
    return "ora"
  }
  const diffMin = Math.floor(diffSec / 60)
  if (diffMin < 60) {
    return `${diffMin} min fa`
  }
  const diffH = Math.floor(diffMin / 60)
  if (diffH < 24) {
    return `${diffH} h fa`
  }
  const diffD = Math.floor(diffH / 24)
  if (diffD < 7) {
    return `${diffD} g fa`
  }
  return new Date(iso).toLocaleDateString("it-IT")
}
