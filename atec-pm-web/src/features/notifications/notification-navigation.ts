import type { NotificationListItem } from "@/lib/api/types"

/** Sezione commessa in base al reference_type (come MainWindow.NavigateToProject WPF). */
function commessaSectionForReference(referenceType: string): string {
  switch ((referenceType || "").toUpperCase()) {
    case "BOM":
      return "ddp_commercial"
    case "BOM_OFFICINA":
      return "ddp_officina"
    case "PHASE":
      return "budget_vs_actual"
    case "CHECKLIST":
      return "checklist"
    case "MOM_ACTION":
      return "mom"
    case "TIMESHEET":
      return "details"
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

  const query = params.toString()
  return `/commesse/${notification.projectId}/${section}${query ? `?${query}` : ""}`
}
