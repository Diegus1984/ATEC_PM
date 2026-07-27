/** Notifiche, scadenze e digest email — allineati a ATEC.PM.Shared/DTOs. */

import type { PlanChangeLine } from "./resources"

export interface NotifyPendingEmployee {
  employeeId: number
  employeeName: string
  nuove: number
  modificate: number
  cancellate: number
  hasEmail: boolean
}

export interface NotifyPendingDto {
  totalChanges: number
  emailConfigurata: boolean
  employees: NotifyPendingEmployee[]
}

export interface NotifySendResultDto {
  emailInviate: number
  dipendentiSenzaEmail: number
  notifiedNames: string[]
  baselineCreated: boolean
  message: string
  responsabiliNotificati: number
  responsabiliNotificatiNomi: string[]
  pmNotificati: number
  pmNotificatiNomi: string[]
}

export interface SelectivePerson {
  employeeId: number
  employeeName: string
  hasEmail: boolean
  righe: PlanChangeLine[]
}

export interface SelectivePreviewDto {
  dipendenti: SelectivePerson[]
}

export interface SendSelectedRequest {
  assignmentIds: number[]
}

export interface PlanDigestSettingsDto {
  digestEnabled: boolean
  digestTime: string
  digestWeekends: boolean
  digestLastRun: string
}

export interface DigestLogEntry {
  runUtc: string
  trigger: string
  emailInviate: number
  senzaEmail: number
  esito: string
}

export interface DigestStatusDto {
  serverTimeLocal: string
  serverTimeUtc: string
  settings: PlanDigestSettingsDto
  emailConfigurata: boolean
  attivitaNelPiano: number
  dipendentiConEmail: number
  dipendentiSenzaEmail: number
  ultimeEsecuzioni: DigestLogEntry[]
}

export interface EmailSettingsDto {
  enabled: boolean
  smtpHost: string
  smtpPort: number
  security: string // auto | ssl | starttls | none
  from: string
  fromName: string
  username: string
  password: string | null
  hasPassword: boolean
  webUrl: string
}

export interface TestEmailRequest {
  toEmail: string
}

// ── Notifiche ───────────────────────────────────────────
// Allineate a ATEC.PM.Shared/DTOs/Notification_DTOs.cs
export interface NotificationListItem {
  /** Id del recipient (notification_recipients.id) — usato per read/delete. */
  id: number
  notificationId: number
  notificationType: string
  severity: string // INFO | WARNING | ERROR | SUCCESS | ALARM
  title: string
  message: string
  referenceType: string // PROJECT | PHASE | BOM | ""
  referenceId: number
  referenceLabel: string
  projectId: number | null
  projectCode: string
  createdByName: string
  isRead: boolean
  readAt: string | null
  createdAt: string
}

export interface NotificationBadge {
  unreadCount: number
}

export interface Deadline {
  type: "SAL" | "SAL_INCASSO" | "PROJECT" | "CHECKLIST" | "MOM" | "DDP"
  refType: "SAL_ROW" | "PROJECT" | "CHECKLIST" | "MOM_ACTION" | "BOM"
  refId: number
  projectId: number | null
  code: string
  title: string
  description: string
  dueDate: string
  days: number
}
