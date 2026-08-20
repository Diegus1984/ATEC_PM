/** Dashboard generale — allineati a ATEC.PM.Shared/DTOs. */

// #88: DashboardData / DashboardProjectRow / DashboardDailyHoursPoint rimossi con la
// vecchia «Panoramica»: le tabelle d'ingresso leggono ProjectListItem da /api/projects.

// ── Dashboard a cartelle (blocco 7) ────────────────────────────────────────

export interface DashboardFolder {
  projectId: number
  code: string
  title: string
  customerName: string
  pmName: string
  status: string
  inDashboard: boolean
  /** Milestone attive (spente escluse). */
  milestoneCount: number
  /** Media avanzamenti delle righe attive; null = nessuna milestone. */
  avgProgress: number | null
  periodStart: string | null
  periodEnd: string | null
}

export interface DashboardFoldersResponse {
  /** Numero massimo di cartelle mostrate (DASH_MAX del prototipo). */
  maxCards: number
  projects: DashboardFolder[]
  /** Commesse escluse a mano: la fascia di chip in fondo. */
  hidden: DashboardFolder[]
}

export interface DashboardSettings {
  maxCards: number
}

export interface ActiveTechSummary {
  employeeName: string
  departmentCode: string
  totalHours: number
  phaseCount: number
}

export interface WeeklyHoursSummary {
  year: number
  week: number
  hours: number
  weekLabel: string
}

export interface UpcomingDeadline {
  phaseName: string
  departmentCode: string
  deadline: string
  daysRemaining: number
  status: string
  progressPct: number
}

export interface RecentTimesheetEntry {
  employeeName: string
  phaseName: string
  workDate: string
  hours: number
  entryType: string
  notes: string
}
