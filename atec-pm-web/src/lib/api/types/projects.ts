/** Commesse: anagrafica, cash flow, documenti, chat — allineati a ATEC.PM.Shared/DTOs. */

import type { ActiveTechSummary, RecentTimesheetEntry, UpcomingDeadline, WeeklyHoursSummary } from "./dashboard"
import type { DeptSummary } from "./employees"
import type { PhaseGanttItem } from "./phases"

export interface ProjectListItem {
  id: number
  code: string
  title: string
  customerName: string
  pmName: string
  status: string
  priority: string
  startDate: string | null
  endDatePlanned: string | null
  revenue: number
  budgetHoursTotal: number
  linkedQuoteId: number
}

/** Richiesta crea/modifica commessa (POST /api/projects, PUT /api/projects/{id}) e
 *  shape di lettura singola commessa (GET /api/projects/{id}). */
export interface ProjectSaveRequest {
  id: number
  code: string
  title: string
  customerId: number
  pmId: number
  description: string
  startDate: string | null
  endDatePlanned: string | null
  budgetTotal: number
  budgetHoursTotal: number
  revenue: number
  status: string
  priority: string
  serverPath: string
  notes: string
  createDefaultPhases: boolean
  linkedQuoteId: number
}

export interface ProjectDashboardData {
  code: string
  title: string
  customerName: string
  pmName: string
  status: string
  priority: string
  startDate: string | null
  endDatePlanned: string | null
  description: string
  serverPath: string
  notes: string
  budgetTotal: number
  budgetHoursTotal: number
  revenue: number
  hoursWorked: number
  costWorked: number
  totalPhases: number
  completedPhases: number
  materialCost: number
  materialCostCommercial: number
  materialCostOfficina: number
  totalCost: number
  departmentSummaries: DeptSummary[]
  recentEntries: RecentTimesheetEntry[]
  activeTechnicians: ActiveTechSummary[]
  weeklyHours: WeeklyHoursSummary[]
  phaseGantt: PhaseGanttItem[]
  deadlines: UpcomingDeadline[]
}

export interface CashFlowCategory {
  id: number
  name: string
  totalAmount: number
  notes: string
  sortOrder: number
  linkedSource: string | null
  isLinked: boolean
}

export interface CashFlowDataItem {
  dataType: string
  refId: number
  monthNumber: number
  numValue: number
  dateValue: string | null
}

export interface CashFlowData {
  projectId: number
  projectCode: string
  projectRevenue: number
  startDate: string | null
  paymentAmount: number
  monthCount: number
  isInitialized: boolean
  categories: CashFlowCategory[]
  dataItems: CashFlowDataItem[]
}

export interface ChatListItem {
  id: number
  projectId: number
  title: string
  createdByName: string
  createdAt: string
  participantCount: number
  messageCount: number
  lastMessageAt: string | null
  lastMessagePreview: string
  unreadCount: number
}

export interface ChatMessage {
  id: number
  employeeId: number
  employeeName: string
  employeeInitials: string
  message: string
  createdAt: string
  isMine: boolean
  hasAttachment: boolean
  attachmentName: string
  attachmentPath: string
}

export interface ChatParticipant {
  id: number
  employeeId: number
  employeeName: string
}

export interface FileTreeItem {
  name: string
  isFolder: boolean
  size: number
  relativePath: string
  modified: string | null
  children: FileTreeItem[]
}

/** Voce (file o cartella) del contenuto di una sotto-cartella commessa
 *  (GET /api/projects/{id}/files?subPath=). Lista piatta, non ad albero. */
export interface FileItem {
  name: string
  isFolder: boolean
  size: number
  relativePath: string
  modified: string | null
}

/** Notifica real-time (SignalR `DocumentsChanged`) di modifica ai documenti di una commessa. */
export interface DocumentsChange {
  projectId: number
  action: string // upload | create | rename | move | delete
}

/** Notifica real-time (SignalR `ChatChanged`) su una chat di commessa. */
export interface ChatChange {
  projectId: number
  chatId: number
  action: string // create | message | delete_message | delete_chat
}
