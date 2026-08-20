using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// #88: DashboardData / DashboardProjectRow / DashboardDailyHours rimossi con la vecchia
// «Panoramica» (card + grafico ore): la pagina d'ingresso sono le tabelle Commesse / Altre
// Attività, che leggono ProjectListItem da GET /api/projects.

// ── Dashboard a cartelle (blocco 7) ────────────────────────────────────────
// La pagina d'ingresso: una cartella per commessa con le tre statistiche già a
// video (milestone, avanzamento medio, periodo). La spunta «In dashboard» è un
// flag CONDIVISO su `projects`, non una preferenza personale: chi la toglie la
// toglie a tutti, esattamente come nel prototipo.

/// <summary>Cartella-commessa della dashboard d'ingresso.</summary>
public class DashboardFolderDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool InDashboard { get; set; }
    /// <summary>Milestone attive (spente escluse), come il contatore della scheda commessa.</summary>
    public int MilestoneCount { get; set; }
    /// <summary>Media degli avanzamenti delle righe attive; null = nessuna milestone (→ «—»).</summary>
    public int? AvgProgress { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
}

public class DashboardFoldersResponse
{
    /// <summary>Numero massimo di cartelle mostrate (DASH_MAX del prototipo, qui governabile).</summary>
    public int MaxCards { get; set; }
    /// <summary>Commesse in dashboard, in ordine di codice.</summary>
    public List<DashboardFolderDto> Projects { get; set; } = new();
    /// <summary>Commesse escluse a mano: la fascia di chip in fondo alla pagina.</summary>
    public List<DashboardFolderDto> Hidden { get; set; } = new();
}

public class DashboardSettingsDto
{
    public int MaxCards { get; set; }
}

public class DashboardFolderFlagRequest
{
    public bool InDashboard { get; set; }
}

public class ProjectDashboardData
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public string Description { get; set; } = "";
    public string ServerPath { get; set; } = "";
    public string Notes { get; set; } = "";

    public decimal BudgetTotal { get; set; }
    public decimal BudgetHoursTotal { get; set; }
    public decimal Revenue { get; set; }
    public decimal HoursWorked { get; set; }
    public decimal CostWorked { get; set; }
    public int TotalPhases { get; set; }
    public int CompletedPhases { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal MaterialCostCommercial { get; set; }
    public decimal MaterialCostOfficina { get; set; }
    /// <summary>Trasferta a consuntivo (projects.actual_travel_cost). Entra in TotalCost dal 04/08/2026.</summary>
    public decimal TravelCost { get; set; }
    /// <summary>
    /// Costo totale a consuntivo = ore + materiali + trasferta. Fino al 04/08/2026 la trasferta
    /// era esclusa, e il MARGINE del tab Dettagli non coincideva con la Redditività del conto
    /// economico (che l'ha sempre inclusa).
    /// </summary>
    public decimal TotalCost { get; set; }

    public List<DeptSummary> DepartmentSummaries { get; set; } = new();
    public List<RecentTimesheetEntry> RecentEntries { get; set; } = new();
    public List<ActiveTechSummary> ActiveTechnicians { get; set; } = new();

    // ── NUOVI DATI PER DASHBOARD MIGLIORATA ──────────────────
    public List<WeeklyHoursSummary> WeeklyHours { get; set; } = new();
    public List<PhaseGanttItem> PhaseGantt { get; set; } = new();
    public List<UpcomingDeadline> Deadlines { get; set; } = new();
}

public class DeptSummary
{
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public decimal CostingHours { get; set; }    // Ore preventivate (dal costing)
    public decimal AssignedHours { get; set; }   // Ore assegnate (dalle phase_assignments)
    public decimal HoursWorked { get; set; }     // Ore lavorate (dal timesheet)
    public decimal BudgetHours { get; set; }     // Legacy: max tra CostingHours e AssignedHours
    public int TotalPhases { get; set; }
    public int CompletedPhases { get; set; }
    public decimal MaterialCost { get; set; }
}

public class RecentTimesheetEntry
{
    public string EmployeeName { get; set; } = "";
    public string PhaseName { get; set; } = "";
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string EntryType { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class ActiveTechSummary
{
    public string EmployeeName { get; set; } = "";
    public string DepartmentCode { get; set; } = "";
    public decimal TotalHours { get; set; }
    public int PhaseCount { get; set; }
}

/// <summary>
/// Ore lavorate aggregate per settimana (line chart).
/// </summary>
public class WeeklyHoursSummary
{
    public int Year { get; set; }
    public int Week { get; set; }
    public decimal Hours { get; set; }
    public string WeekLabel { get; set; } = "";
}

/// <summary>
/// Dati per il Gantt semplificato delle fasi.
/// </summary>
public class PhaseGanttItem
{
    public int PhaseId { get; set; }
    public string PhaseName { get; set; } = "";
    public string DepartmentCode { get; set; } = "";
    public string Status { get; set; } = "";
    public int ProgressPct { get; set; }
    public decimal BudgetHours { get; set; }
    public decimal HoursWorked { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Scadenze prossime (fasi con end_date vicina).
/// </summary>
public class UpcomingDeadline
{
    public string PhaseName { get; set; } = "";
    public string DepartmentCode { get; set; } = "";
    public DateTime Deadline { get; set; }
    public int DaysRemaining { get; set; }
    public string Status { get; set; } = "";
    public int ProgressPct { get; set; }
}
