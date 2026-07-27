/** Dashboard generale — allineati a ATEC.PM.Shared/DTOs. */

export interface DashboardData {
  activeProjects: number
  draftProjects: number
  completedProjects: number
  totalEmployees: number
  totalCustomers: number
  hoursThisMonth: number
  hoursThisWeek: number
  totalRevenue: number
  recentProjects: DashboardProjectRow[]
  dailyHours: DashboardDailyHoursPoint[]
}

export interface DashboardDailyHoursPoint {
  workDate: string
  hours: number
}

export interface DashboardProjectRow {
  code: string
  title: string
  customerName: string
  status: string
  hoursWorked: number
  budgetHours: number
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
