using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly DbService _db;
    public DashboardController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult Get()
    {
        using var c = _db.Open();

        var stats = new DashboardData
        {
            ActiveProjects = c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE status='ACTIVE'"),
            DraftProjects = c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE status='DRAFT'"),
            CompletedProjects = c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE status='COMPLETED'"),
            TotalEmployees = c.ExecuteScalar<int>("SELECT COUNT(*) FROM employees WHERE status='ACTIVE'"),
            TotalCustomers = c.ExecuteScalar<int>("SELECT COUNT(*) FROM customers WHERE is_active=1"),
            HoursThisMonth = c.ExecuteScalar<decimal>("SELECT COALESCE(SUM(hours),0) FROM timesheet_entries WHERE YEAR(work_date)=YEAR(CURDATE()) AND MONTH(work_date)=MONTH(CURDATE())"),
            HoursThisWeek = c.ExecuteScalar<decimal>("SELECT COALESCE(SUM(hours),0) FROM timesheet_entries WHERE YEARWEEK(work_date,1)=YEARWEEK(CURDATE(),1)"),
            TotalRevenue = c.ExecuteScalar<decimal>("SELECT COALESCE(SUM(revenue),0) FROM projects WHERE status IN ('ACTIVE','COMPLETED')"),
        };

        stats.RecentProjects = c.Query<DashboardProjectRow>(@"
            SELECT p.code AS Code, p.title AS Title, cu.company_name AS CustomerName, p.status AS Status,
                   COALESCE(SUM(te.hours),0) AS HoursWorked, p.budget_hours_total AS BudgetHours
            FROM projects p
            JOIN customers cu ON cu.id = p.customer_id
            LEFT JOIN project_phases pp ON pp.project_id = p.id
            LEFT JOIN timesheet_entries te ON te.project_phase_id = pp.id
            WHERE p.status IN ('ACTIVE','DRAFT')
            GROUP BY p.id
            ORDER BY p.created_at DESC
            LIMIT 10").ToList();

        return Ok(ApiResponse<DashboardData>.Ok(stats));
    }
}
