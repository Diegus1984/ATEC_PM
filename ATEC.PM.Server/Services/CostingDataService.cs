using System.Data;
using Dapper;
using ATEC.PM.Shared.DTOs;
using MySqlConnector;

namespace ATEC.PM.Server.Services;

/// <summary>Caricamento dati costing condiviso tra preventivo (quote_*) e commessa (project_*).</summary>
public static class CostingDataService
{
    public enum CostingScope { Quote, Project }

    public static (List<CostSectionGroupDto> Groups, List<CostSectionTemplateDto> Templates) GetAvailableTemplates(
        MySqlConnection c, CostingScope scope, int ownerId)
    {
        List<CostSectionGroupDto> allGroups = c.Query<CostSectionGroupDto>(
            "SELECT id, name, sort_order AS SortOrder, is_active AS IsActive FROM cost_section_groups WHERE is_active=1 ORDER BY sort_order").ToList();

        List<CostSectionTemplateDto> allTemplates = c.Query<CostSectionTemplateDto>(@"
            SELECT t.id, t.name, t.section_type AS SectionType, t.group_id AS GroupId,
                   g.name AS GroupName, t.is_default_project AS IsDefault, t.is_default_quote AS IsDefaultQuote, t.sort_order AS SortOrder
            FROM cost_section_templates t
            JOIN cost_section_groups g ON g.id = t.group_id
            WHERE t.is_active=1
            ORDER BY t.sort_order").ToList();

        string sectionsTable = scope == CostingScope.Quote ? "quote_cost_sections" : "project_cost_sections";
        string ownerColumn = scope == CostingScope.Quote ? "quote_id" : "project_id";

        HashSet<int> usedTemplateIds = c.Query<int?>(
            $"SELECT template_id FROM {sectionsTable} WHERE {ownerColumn}=@ownerId AND template_id IS NOT NULL",
            new { ownerId }).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();

        HashSet<string> usedGroupNames = c.Query<string>(
            $"SELECT DISTINCT group_name FROM {sectionsTable} WHERE {ownerColumn}=@ownerId",
            new { ownerId }).ToHashSet();

        List<CostSectionTemplateDto> availableTemplates = allTemplates.Where(t => !usedTemplateIds.Contains(t.Id)).ToList();
        List<CostSectionGroupDto> availableGroups = allGroups.Where(g =>
            !usedGroupNames.Contains(g.Name) ||
            availableTemplates.Any(t => t.GroupId == g.Id)).ToList();

        return (availableGroups, availableTemplates);
    }

    public static ProjectCostingData LoadQuote(MySqlConnection c, int quoteId)
    {
        int secCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM quote_cost_sections WHERE quote_id=@quoteId", new { quoteId });
        if (secCount == 0)
            return new ProjectCostingData { ProjectId = quoteId, IsInitialized = false };

        List<ProjectCostSectionDto> sections = c.Query<ProjectCostSectionDto>(@"
            SELECT id, quote_id AS ProjectId, template_id AS TemplateId, name, section_type AS SectionType,
                   group_name AS GroupName, sort_order AS SortOrder, is_enabled AS IsEnabled,
                   contingency_pct AS ContingencyPct, margin_pct AS MarginPct,
                   contingency_pinned AS ContingencyPinned, margin_pinned AS MarginPinned,
                   COALESCE(is_shadowed,0) AS IsShadowed
            FROM quote_cost_sections WHERE quote_id=@quoteId ORDER BY sort_order", new { quoteId }).ToList();

        AttachQuoteSectionDepartments(c, quoteId, sections);
        AttachQuoteResources(c, quoteId, sections);

        List<ProjectMaterialSectionDto> matSections = c.Query<ProjectMaterialSectionDto>(@"
            SELECT id, quote_id AS ProjectId, category_id AS CategoryId, name,
                   markup_value AS MarkupValue, commission_markup AS CommissionMarkup,
                   sort_order AS SortOrder, is_enabled AS IsEnabled
            FROM quote_material_sections WHERE quote_id=@quoteId ORDER BY sort_order", new { quoteId }).ToList();

        AttachQuoteMaterialItems(c, quoteId, matSections);

        ProjectPricingDto pricing = c.QueryFirstOrDefault<ProjectPricingDto>(@"
            SELECT id, quote_id AS ProjectId, contingency_pct AS ContingencyPct,
                   negotiation_margin_pct AS NegotiationMarginPct,
                   travel_markup AS TravelMarkup, allowance_markup AS AllowanceMarkup
            FROM quote_pricing WHERE quote_id=@quoteId", new { quoteId })
            ?? new ProjectPricingDto { ProjectId = quoteId };

        return new ProjectCostingData
        {
            ProjectId = quoteId,
            IsInitialized = true,
            CostSections = sections,
            MaterialSections = matSections,
            Pricing = pricing
        };
    }

    public static ProjectCostingData LoadProject(MySqlConnection c, int projectId)
    {
        int secCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_cost_sections WHERE project_id=@projectId", new { projectId });
        if (secCount == 0)
            return new ProjectCostingData { ProjectId = projectId, IsInitialized = false };

        List<ProjectCostSectionDto> sections = c.Query<ProjectCostSectionDto>(@"
            SELECT pcs.id, pcs.project_id AS ProjectId, pcs.template_id AS TemplateId, pcs.name,
                   pcs.section_type AS SectionType, pcs.group_name AS GroupName,
                   COALESCE(csg.bg_color, '#6B7280') AS GroupColor,
                   pcs.sort_order AS SortOrder, pcs.is_enabled AS IsEnabled,
                   pcs.contingency_pct AS ContingencyPct, pcs.margin_pct AS MarginPct,
                   pcs.contingency_pinned AS ContingencyPinned, pcs.margin_pinned AS MarginPinned,
                   COALESCE(pcs.is_shadowed,0) AS IsShadowed
            FROM project_cost_sections pcs
            LEFT JOIN cost_section_templates cst ON cst.id = pcs.template_id
            LEFT JOIN cost_section_groups csg ON csg.id = cst.group_id
            WHERE pcs.project_id=@projectId ORDER BY pcs.sort_order",
            new { projectId }).ToList();

        List<(int SectionId, int DepartmentId)> sectionDepts = c.Query<(int SectionId, int DepartmentId)>(@"
            SELECT project_cost_section_id AS SectionId, department_id AS DepartmentId
            FROM project_cost_section_departments psd
            JOIN project_cost_sections ps ON ps.id = psd.project_cost_section_id
            WHERE ps.project_id=@projectId", new { projectId }).ToList();

        foreach (ProjectCostSectionDto sec in sections)
            sec.DepartmentIds = sectionDepts.Where(d => d.SectionId == sec.Id).Select(d => d.DepartmentId).ToList();

        List<ProjectCostResourceDto> allResources = c.Query<ProjectCostResourceDto>(@"
            SELECT r.id, r.section_id AS SectionId, r.employee_id AS EmployeeId,
                   r.resource_name AS ResourceName,
                   r.work_days AS WorkDays, r.hours_per_day AS HoursPerDay, r.hourly_cost AS HourlyCost,
                   r.markup_value AS MarkupValue,
                   r.num_trips AS NumTrips, r.km_per_trip AS KmPerTrip, r.cost_per_km AS CostPerKm,
                   r.daily_food AS DailyFood, r.daily_hotel AS DailyHotel,
                   r.allowance_days AS AllowanceDays, r.daily_allowance AS DailyAllowance,
                   r.sort_order AS SortOrder
            FROM project_cost_resources r
            JOIN project_cost_sections s ON s.id = r.section_id
            WHERE s.project_id=@projectId ORDER BY r.sort_order",
            new { projectId }).ToList();

        foreach (ProjectCostSectionDto sec in sections)
            sec.Resources = allResources.Where(r => r.SectionId == sec.Id).ToList();

        List<ProjectMaterialSectionDto> matSections = c.Query<ProjectMaterialSectionDto>(@"
            SELECT id, project_id AS ProjectId, category_id AS CategoryId, name,
                   markup_value AS MarkupValue, commission_markup AS CommissionMarkup,
                   sort_order AS SortOrder, is_enabled AS IsEnabled
            FROM project_material_sections WHERE project_id=@projectId ORDER BY sort_order",
            new { projectId }).ToList();

        List<ProjectMaterialItemDto> allItems = c.Query<ProjectMaterialItemDto>(@"
            SELECT i.id, i.section_id AS SectionId, i.parent_item_id AS ParentItemId,
                   i.description AS Description,
                   i.quantity AS Quantity, i.unit_cost AS UnitCost,
                   i.markup_value AS MarkupValue, i.item_type AS ItemType,
                   i.sort_order AS SortOrder,
                   i.contingency_pct AS ContingencyPct, i.margin_pct AS MarginPct,
                   i.contingency_pinned AS ContingencyPinned, i.margin_pinned AS MarginPinned,
                   COALESCE(i.is_shadowed,0) AS IsShadowed
            FROM project_material_items i
            JOIN project_material_sections s ON s.id = i.section_id
            WHERE s.project_id=@projectId ORDER BY i.sort_order, i.id",
            new { projectId }).ToList();

        foreach (ProjectMaterialSectionDto ms in matSections)
            ms.Items = allItems.Where(i => i.SectionId == ms.Id).ToList();

        ProjectPricingDto pricing = c.QueryFirstOrDefault<ProjectPricingDto>(@"
            SELECT id, project_id AS ProjectId,
                   contingency_pct AS ContingencyPct,
                   negotiation_margin_pct AS NegotiationMarginPct,
                   travel_markup AS TravelMarkup, allowance_markup AS AllowanceMarkup
            FROM project_pricing WHERE project_id=@projectId",
            new { projectId }) ?? new ProjectPricingDto { ProjectId = projectId };

        return new ProjectCostingData
        {
            ProjectId = projectId,
            IsInitialized = true,
            CostSections = sections,
            MaterialSections = matSections,
            Pricing = pricing
        };
    }

    public static void SaveDistributionsBatch(
        MySqlConnection c,
        CostingScope scope,
        int ownerId,
        BatchDistributionRequest req,
        IDbTransaction tx)
    {
        string costSectionsTable = scope == CostingScope.Quote ? "quote_cost_sections" : "project_cost_sections";
        string materialItemsTable = scope == CostingScope.Quote ? "quote_material_items" : "project_material_items";
        string ownerFilter = scope == CostingScope.Quote ? " AND quote_id=@qid" : " AND project_id=@qid";

        // Le righe materiale non hanno la colonna del proprietario: ci arrivano attraverso la
        // sezione. Serve comunque un vincolo di appartenenza — vedi BUG-015 qui sotto.
        string materialSectionsTable = scope == CostingScope.Quote ? "quote_material_sections" : "project_material_sections";
        string materialOwnerColumn = scope == CostingScope.Quote ? "quote_id" : "project_id";

        foreach (BatchDistributionItem s in req.Sections ?? new())
        {
            c.Execute($@"UPDATE {costSectionsTable}
                SET contingency_pct=@ContPct, margin_pct=@MargPct, contingency_pinned=@ContPin, margin_pinned=@MargPin, is_shadowed=@Shadowed
                WHERE id=@Id{ownerFilter}",
                new { ContPct = s.ContingencyPct, MargPct = s.MarginPct, ContPin = s.ContingencyPinned, MargPin = s.MarginPinned, Shadowed = s.IsShadowed, s.Id, qid = ownerId }, tx);
        }

        // 🔒 BUG-015 (15/08/2026). Qui c'era `WHERE id=@Id` e basta, mentre la UPDATE delle
        // sezioni, tre righe sopra, il vincolo di appartenenza ce l'aveva: chiunque potesse
        // aprire un preventivo poteva riscrivere contingenza, margine e ombreggiatura delle righe
        // materiale di QUALUNQUE altro preventivo, semplicemente mettendo i loro id nel corpo
        // della richiesta. Sono percentuali che entrano nel prezzo d'offerta, e non lasciavano
        // traccia da nessuna parte.
        //
        // Il vincolo passa dalla sezione perché la riga materiale non ha una colonna propria che
        // dica a chi appartiene. La subquery è su un'ALTRA tabella, quindi MySQL la accetta dentro
        // una UPDATE (il divieto vale solo quando si rilegge la tabella che si sta scrivendo).
        foreach (BatchDistributionItem m in req.MaterialItems ?? new())
        {
            c.Execute($@"UPDATE {materialItemsTable}
                SET contingency_pct=@ContPct, margin_pct=@MargPct, contingency_pinned=@ContPin, margin_pinned=@MargPin, is_shadowed=@Shadowed
                WHERE id=@Id
                  AND section_id IN (SELECT id FROM {materialSectionsTable} WHERE {materialOwnerColumn}=@qid)",
                new { ContPct = m.ContingencyPct, MargPct = m.MarginPct, ContPin = m.ContingencyPinned, MargPin = m.MarginPinned, Shadowed = m.IsShadowed, m.Id, qid = ownerId }, tx);
        }
    }

    private static void AttachQuoteSectionDepartments(MySqlConnection c, int quoteId, List<ProjectCostSectionDto> sections)
    {
        List<(int SectionId, int DepartmentId)> sectionDepts = c.Query<(int SectionId, int DepartmentId)>(@"
            SELECT quote_cost_section_id AS SectionId, department_id AS DepartmentId
            FROM quote_cost_section_departments osd
            JOIN quote_cost_sections os ON os.id = osd.quote_cost_section_id
            WHERE os.quote_id=@quoteId", new { quoteId }).ToList();

        foreach (ProjectCostSectionDto sec in sections)
            sec.DepartmentIds = sectionDepts.Where(d => d.SectionId == sec.Id).Select(d => d.DepartmentId).ToList();
    }

    private static void AttachQuoteResources(MySqlConnection c, int quoteId, List<ProjectCostSectionDto> sections)
    {
        List<ProjectCostResourceDto> allResources = c.Query<ProjectCostResourceDto>(@"
            SELECT r.id, r.section_id AS SectionId, r.employee_id AS EmployeeId,
                   r.resource_name AS ResourceName, r.work_days AS WorkDays, r.hours_per_day AS HoursPerDay,
                   r.hourly_cost AS HourlyCost, r.markup_value AS MarkupValue,
                   r.num_trips AS NumTrips, r.km_per_trip AS KmPerTrip, r.cost_per_km AS CostPerKm,
                   r.daily_food AS DailyFood, r.daily_hotel AS DailyHotel,
                   r.allowance_days AS AllowanceDays, r.daily_allowance AS DailyAllowance,
                   r.sort_order AS SortOrder
            FROM quote_cost_resources r
            JOIN quote_cost_sections s ON s.id = r.section_id
            WHERE s.quote_id=@quoteId ORDER BY r.sort_order", new { quoteId }).ToList();

        foreach (ProjectCostSectionDto sec in sections)
            sec.Resources = allResources.Where(r => r.SectionId == sec.Id).ToList();
    }

    private static void AttachQuoteMaterialItems(MySqlConnection c, int quoteId, List<ProjectMaterialSectionDto> matSections)
    {
        List<ProjectMaterialItemDto> allItems = c.Query<ProjectMaterialItemDto>(@"
            SELECT i.id, i.section_id AS SectionId, i.parent_item_id AS ParentItemId,
                   i.product_id AS ProductId, i.variant_id AS VariantId,
                   COALESCE(i.code,'') AS Code, i.description AS Description,
                   i.description_rtf AS DescriptionRtf,
                   i.quantity AS Quantity, i.unit_cost AS UnitCost,
                   i.markup_value AS MarkupValue, i.item_type AS ItemType, i.sort_order AS SortOrder,
                   i.contingency_pct AS ContingencyPct, i.margin_pct AS MarginPct,
                   i.contingency_pinned AS ContingencyPinned, i.margin_pinned AS MarginPinned,
                   COALESCE(i.is_shadowed,0) AS IsShadowed, COALESCE(i.is_active,1) AS IsActive
            FROM quote_material_items i
            JOIN quote_material_sections s ON s.id = i.section_id
            WHERE s.quote_id=@quoteId ORDER BY i.sort_order", new { quoteId }).ToList();

        foreach (ProjectMaterialSectionDto ms in matSections)
            ms.Items = allItems.Where(i => i.SectionId == ms.Id).ToList();
    }
}
