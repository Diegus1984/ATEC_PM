import type { NotificationListItem, Deadline } from "@/lib/api/types"

/** Sezione commessa in base al reference_type (come MainWindow.NavigateToProject WPF). */
export function commessaSectionForReference(referenceType: string): string {
  switch ((referenceType || "").toUpperCase()) {
    case "BOM":
      return "ddp_commercial"
    case "BOM_OFFICINA":
      return "ddp_officina"
    case "PHASE":
      return "budget_vs_actual"
    case "CHECKLIST":
      return "checklist"
    case "WORK_REQUEST":
      return "work_requests"
    case "MOM_ACTION":
      return "mom"
    case "TIMESHEET":
      return "details"
    case "SAL_ROW":
      return "sal"
    case "CHAT":
      return "chat"
    default:
      return "details"
  }
}

/**
 * Destinazione del click su una notifica.
 * Ritorna `null` se non c'è una pagina di destinazione (es. anomalia ore senza commessa).
 */
export function getNotificationHref(
  notification: NotificationListItem
): string | null {
  const type = (notification.notificationType || "").toUpperCase()
  const ref = (notification.referenceType || "").toUpperCase()

  if (!notification.projectId) {
    if (type === "TIMESHEET_ANOMALY") {
      return "/timesheet"
    }
    if (type === "BUG_REPORT" || type === "BUG_RESOLVED" || ref === "BUG_REPORT") {
      return "/segnalazioni"
    }
    // Attività di gruppi generici / azioni di verbali di riunione: pagine standalone.
    if (ref === "CHECKLIST") {
      return "/checklist"
    }
    if (ref === "MOM_ACTION") {
      return "/mom"
    }
    return null
  }

  const section = commessaSectionForReference(ref)
  const params = new URLSearchParams()

  if (
    notification.referenceId > 0 &&
    (ref === "BOM" || ref === "BOM_OFFICINA" || ref === "PHASE")
  ) {
    params.set("item", String(notification.referenceId))
  }

  if (ref === "CHAT" && notification.referenceId > 0) {
    params.set("chat", String(notification.referenceId))
  }

  const query = params.toString()
  return `/commesse/${notification.projectId}/${section}${query ? `?${query}` : ""}`
}

/**
 * Destinazione della navigazione per una scadenza.
 */
export function deadlineHref(d: Deadline): string | null {
  const ref = (d.refType || "").toUpperCase()

  if (!d.projectId) {
    if (ref === "CHECKLIST") {
      return "/checklist"
    }
    if (ref === "MOM_ACTION") {
      return "/mom"
    }
    return null
  }

  if (ref === "PROJECT") {
    return `/commesse/${d.projectId}/details`
  }

  const section = commessaSectionForReference(ref)
  const params = new URLSearchParams()

  if (
    d.refId > 0 &&
    (ref === "BOM" || ref === "PHASE" || ref === "SAL_ROW")
  ) {
    params.set("item", String(d.refId))
  }

  const query = params.toString()
  return `/commesse/${d.projectId}/${section}${query ? `?${query}` : ""}`
}

