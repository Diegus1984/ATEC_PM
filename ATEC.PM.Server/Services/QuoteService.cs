using System.Data;
using Dapper;
using ATEC.PM.Shared.DTOs;
using MySqlConnector;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Operazioni DB condivise sui preventivi: totali, copia revisioni/duplicati, init costing.
/// </summary>
public static class QuoteService
{
    /// <summary>Ricalcola subtotal, IVA, costi e profit da quote_items (preventivi SERVICE).</summary>
    public static void RecalcTotals(MySqlConnection c, int quoteId, IDbTransaction? tx = null)
    {
        dynamic? totals = c.QueryFirstOrDefault<dynamic>(@"
            SELECT COALESCE(SUM(line_total),0) AS subtotal,
                   COALESCE(SUM(line_total * vat_pct / 100),0) AS vat_total,
                   COALESCE(SUM(CASE WHEN item_type='product' THEN quantity * cost_price ELSE 0 END),0) AS cost_total
            FROM quote_items WHERE quote_id=@Id AND COALESCE(is_active,1)=1 AND quantity>0",
            new { Id = quoteId }, tx);

        decimal subtotal = (decimal)(totals?.subtotal ?? 0m);
        decimal vatTotal = (decimal)(totals?.vat_total ?? 0m);
        decimal costTotal = (decimal)(totals?.cost_total ?? 0m);

        dynamic? discountInfo = c.QueryFirstOrDefault<dynamic>(
            "SELECT discount_pct, discount_abs FROM quotes WHERE id=@Id",
            new { Id = quoteId }, tx);

        decimal discPct = (decimal)(discountInfo?.discount_pct ?? 0m);
        decimal discAbs = (decimal)(discountInfo?.discount_abs ?? 0m);
        decimal discountAmount = subtotal * discPct / 100m + discAbs;
        decimal total = subtotal - discountAmount;
        decimal totalWithVat = total + vatTotal;
        decimal profit = total - costTotal;

        c.Execute(@"UPDATE quotes SET subtotal=@Sub, vat_total=@Vat, total=@Tot,
                    total_with_vat=@TotVat, cost_total=@Cost, profit=@Profit WHERE id=@Id",
            new { Sub = subtotal, Vat = vatTotal, Tot = total, TotVat = totalWithVat,
                  Cost = costTotal, Profit = profit, Id = quoteId }, tx);
    }

    /// <summary>Copia totali denormalizzati (preventivi IMPIANTO dopo duplicate).</summary>
    public static void CopyTotalsFromSource(MySqlConnection c, int fromQuoteId, int toQuoteId, IDbTransaction tx)
    {
        c.Execute(@"
            UPDATE quotes AS dst
            JOIN quotes AS src ON src.id = @OrigId
            SET dst.subtotal       = src.subtotal,
                dst.cost_total     = src.cost_total,
                dst.vat_total      = src.vat_total,
                dst.total          = src.total,
                dst.total_with_vat = src.total_with_vat,
                dst.profit         = src.profit
            WHERE dst.id = @NewId",
            new { OrigId = fromQuoteId, NewId = toQuoteId }, tx);
    }

    public static void CopyQuoteItems(MySqlConnection c, IDbTransaction tx, int fromId, int toId)
    {
        List<dynamic> items = c.Query<dynamic>(
            "SELECT * FROM quote_items WHERE quote_id=@Id ORDER BY sort_order",
            new { Id = fromId }, tx).ToList();

        Dictionary<int, int> idMap = new();

        foreach (dynamic item in items)
        {
            int? parentId = (int?)item.parent_item_id;
            int? mappedParent = parentId.HasValue && idMap.ContainsKey(parentId.Value)
                ? idMap[parentId.Value] : null;

            int newItemId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_items (quote_id, product_id, variant_id, item_type, code, name,
                    description_rtf, unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                    line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id, is_auto_include)
                VALUES (@QId, @PId, @VId, @IType, @Code, @Name,
                    @Desc, @Unit, @Qty, @Cost, @Sell, @Disc, @Vat,
                    @LT, @LP, @Sort, @Active, @Conf, @Parent, @Auto);
                SELECT LAST_INSERT_ID()",
                new
                {
                    QId = toId,
                    PId = (int?)item.product_id,
                    VId = (int?)item.variant_id,
                    IType = (string)item.item_type,
                    Code = (string?)item.code,
                    Name = (string?)item.name,
                    Desc = (string?)item.description_rtf,
                    Unit = (string?)item.unit,
                    Qty = (decimal)item.quantity,
                    Cost = (decimal)item.cost_price,
                    Sell = (decimal)item.sell_price,
                    Disc = (decimal)item.discount_pct,
                    Vat = (decimal)item.vat_pct,
                    LT = (decimal)item.line_total,
                    LP = (decimal)item.line_profit,
                    Sort = (int)item.sort_order,
                    Active = (bool)(item.is_active ?? true),
                    Conf = (bool)(item.is_confirmed ?? false),
                    Parent = mappedParent,
                    Auto = (bool)(item.is_auto_include ?? false)
                }, tx);

            idMap[(int)item.id] = newItemId;
        }

        RecalcTotals(c, toId, tx);
    }

    public static void CopyQuoteCosting(MySqlConnection c, IDbTransaction tx, int fromId, int toId)
    {
        List<dynamic> sections = c.Query<dynamic>(
            "SELECT * FROM quote_cost_sections WHERE quote_id=@Id ORDER BY sort_order",
            new { Id = fromId }, tx).ToList();

        foreach (dynamic sec in sections)
        {
            int newSecId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_cost_sections (quote_id, template_id, name, section_type, group_name,
                    sort_order, is_enabled, contingency_pct, margin_pct, contingency_pinned, margin_pinned, is_shadowed)
                VALUES (@qid, @tid, @name, @stype, @gname, @sort, @enabled, @cpct, @mpct, @cpin, @mpin, @shad);
                SELECT LAST_INSERT_ID()",
                new
                {
                    qid = toId,
                    tid = (int?)sec.template_id,
                    name = (string)sec.name,
                    stype = (string)sec.section_type,
                    gname = (string)sec.group_name,
                    sort = (int)sec.sort_order,
                    enabled = (bool)sec.is_enabled,
                    cpct = (decimal)sec.contingency_pct,
                    mpct = (decimal)sec.margin_pct,
                    cpin = (bool)(sec.contingency_pinned ?? false),
                    mpin = (bool)(sec.margin_pinned ?? false),
                    shad = (bool)(sec.is_shadowed ?? false)
                }, tx);

            c.Execute(@"INSERT INTO quote_cost_section_departments (quote_cost_section_id, department_id)
                        SELECT @newId, department_id FROM quote_cost_section_departments WHERE quote_cost_section_id=@oldId",
                new { newId = newSecId, oldId = (int)sec.id }, tx);

            c.Execute(@"INSERT INTO quote_cost_resources (section_id, employee_id, resource_name, work_days, hours_per_day,
                            hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                            allowance_days, daily_allowance, sort_order)
                        SELECT @newId, employee_id, resource_name, work_days, hours_per_day,
                            hourly_cost, markup_value, num_trips, km_per_trip, cost_per_km, daily_food, daily_hotel,
                            allowance_days, daily_allowance, sort_order
                        FROM quote_cost_resources WHERE section_id=@oldId",
                new { newId = newSecId, oldId = (int)sec.id }, tx);
        }

        List<dynamic> matSections = c.Query<dynamic>(
            "SELECT * FROM quote_material_sections WHERE quote_id=@Id ORDER BY sort_order",
            new { Id = fromId }, tx).ToList();

        foreach (dynamic ms in matSections)
        {
            int newMsId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_material_sections (quote_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
                VALUES (@qid, @catId, @name, @mk, @cmk, @sort, @enabled);
                SELECT LAST_INSERT_ID()",
                new
                {
                    qid = toId,
                    catId = (int?)ms.category_id,
                    name = (string)ms.name,
                    mk = (decimal)ms.markup_value,
                    cmk = (decimal)ms.commission_markup,
                    sort = (int)ms.sort_order,
                    enabled = (bool)ms.is_enabled
                }, tx);

            List<dynamic> items = c.Query<dynamic>(
                "SELECT * FROM quote_material_items WHERE section_id=@Id ORDER BY sort_order",
                new { Id = (int)ms.id }, tx).ToList();

            Dictionary<int, int> matIdMap = new();
            foreach (dynamic item in items)
            {
                int? parentId = (int?)item.parent_item_id;
                int? mappedParent = parentId.HasValue && matIdMap.ContainsKey(parentId.Value)
                    ? matIdMap[parentId.Value] : null;

                int newItemId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO quote_material_items (section_id, parent_item_id, product_id, variant_id,
                        code, description, description_rtf, quantity, unit_cost, markup_value,
                        item_type, sort_order, contingency_pct, margin_pct, contingency_pinned, margin_pinned, is_shadowed, is_active)
                    VALUES (@sid, @parent, @pid, @vid, @code, @desc, @descRtf, @qty, @ucost, @mk,
                        @itype, @sort, @cpct, @mpct, @cpin, @mpin, @shad, @active);
                    SELECT LAST_INSERT_ID()",
                    new
                    {
                        sid = newMsId,
                        parent = mappedParent,
                        pid = (int?)item.product_id,
                        vid = (int?)item.variant_id,
                        code = (string?)item.code,
                        desc = (string?)item.description,
                        descRtf = (string?)item.description_rtf,
                        qty = (decimal)item.quantity,
                        ucost = (decimal)item.unit_cost,
                        mk = (decimal)item.markup_value,
                        itype = (string)item.item_type,
                        sort = (int)item.sort_order,
                        cpct = (decimal)item.contingency_pct,
                        mpct = (decimal)item.margin_pct,
                        cpin = (bool)(item.contingency_pinned ?? false),
                        mpin = (bool)(item.margin_pinned ?? false),
                        shad = (bool)(item.is_shadowed ?? false),
                        active = (bool)(item.is_active ?? true)
                    }, tx);

                matIdMap[(int)item.id] = newItemId;
            }
        }

        c.Execute(@"INSERT INTO quote_pricing (quote_id, contingency_pct, negotiation_margin_pct, travel_markup, allowance_markup)
                    SELECT @newId, contingency_pct, negotiation_margin_pct, travel_markup, allowance_markup
                    FROM quote_pricing WHERE quote_id=@oldId",
            new { newId = toId, oldId = fromId }, tx);
    }

    public static void InitQuoteCosting(MySqlConnection c, IDbTransaction tx, int quoteId)
    {
        List<dynamic> templates = c.Query<dynamic>(@"
            SELECT t.id, t.name, t.section_type, g.name AS group_name, t.sort_order
            FROM cost_section_templates t
            JOIN cost_section_groups g ON g.id = t.group_id
            WHERE t.is_default_quote=1 AND t.is_active=1
            ORDER BY t.sort_order", transaction: tx).ToList();

        foreach (dynamic tmpl in templates)
        {
            int newSectionId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO quote_cost_sections (quote_id, template_id, name, section_type, group_name, sort_order, is_enabled)
                VALUES (@quoteId, @id, @name, @section_type, @group_name, @sort_order, 1);
                SELECT LAST_INSERT_ID()",
                new { quoteId, tmpl.id, tmpl.name, tmpl.section_type, tmpl.group_name, tmpl.sort_order }, tx);

            c.Execute(@"
                INSERT INTO quote_cost_section_departments (quote_cost_section_id, department_id)
                SELECT @newSectionId, department_id
                FROM cost_section_template_departments
                WHERE section_template_id = @templateId",
                new { newSectionId, templateId = (int)tmpl.id }, tx);
        }

        c.Execute(@"
            INSERT INTO quote_material_sections (quote_id, category_id, name, markup_value, commission_markup, sort_order, is_enabled)
            VALUES (@quoteId, NULL, 'Materiali', 1.300, 1.100, 0, 1)",
            new { quoteId }, tx);

        c.Execute("INSERT INTO quote_pricing (quote_id) VALUES (@quoteId)", new { quoteId }, tx);
    }

    public static void AutoPopulateItems(MySqlConnection c, IDbTransaction tx, int quoteId, int? priceListId)
    {
        string autoSql = @"
            SELECT p.id AS ProductId, p.item_type, p.code, p.name, p.description_rtf,
                   v.id AS VariantId, v.code AS VarCode, v.name AS VarName,
                   v.cost_price, v.markup_value
            FROM quote_products p
            JOIN quote_categories cat ON cat.id = p.category_id
            JOIN quote_groups g ON g.id = cat.group_id
            LEFT JOIN quote_product_variants v ON v.product_id = p.id
            WHERE p.auto_include = 1 AND p.is_active = 1";
        if (priceListId.HasValue && priceListId.Value > 0)
            autoSql += " AND g.price_list_id = @PriceListId";
        autoSql += " ORDER BY g.sort_order, cat.sort_order, p.sort_order, v.sort_order";

        List<dynamic> autoItems = c.Query<dynamic>(autoSql, new { PriceListId = priceListId }, tx).ToList();

        int sortOrder = 0;
        IEnumerable<IGrouping<int, dynamic>> grouped = autoItems.GroupBy(x => (int)x.ProductId);

        foreach (IGrouping<int, dynamic> grp in grouped)
        {
            dynamic first = grp.First();
            string productName = (string)first.name;
            string productCode = (string)(first.code ?? "");
            string productType = (string)first.item_type;
            string desc = (string?)(first.description_rtf) ?? "";
            bool hasVariants = grp.Any(x => x.VariantId != null);

            if (hasVariants)
            {
                int parentId = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO quote_items (quote_id, product_id, item_type,
                        code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, is_auto_include)
                    VALUES (@QId, @PId, @Type, @Code, @Name, @Desc, '', 0, 0, 0, 0, 0, 0, 0, @Sort, 1, 0, 1);
                    SELECT LAST_INSERT_ID()",
                    new { QId = quoteId, PId = grp.Key, Type = productType,
                          Code = productCode, Name = productName, Desc = desc,
                          Sort = sortOrder++ }, tx);

                foreach (dynamic v in grp.Where(x => x.VariantId != null))
                {
                    decimal cost = v.cost_price ?? 0m;
                    decimal markup = v.markup_value ?? 1m;
                    decimal qty = 1m;
                    decimal sell = cost * markup;
                    decimal disc = 0m;
                    decimal vat = 22m;
                    decimal lt = qty * sell * (1 - disc / 100m);
                    decimal lp = lt - (qty * cost);

                    c.Execute(@"
                        INSERT INTO quote_items (quote_id, product_id, variant_id, item_type,
                            code, name, unit, quantity, cost_price, sell_price, discount_pct, vat_pct,
                            line_total, line_profit, sort_order, is_active, is_confirmed, parent_item_id, is_auto_include)
                        VALUES (@QId, @PId, @VId, 'product', @Code, @Name, @Unit, @Qty,
                            @Cost, @Sell, @Disc, @Vat, @LT, @LP, @Sort, 0, 0, @ParentId, 1)",
                        new { QId = quoteId, PId = grp.Key, VId = (int?)v.VariantId,
                              Code = (string)(v.VarCode ?? ""), Name = (string)(v.VarName ?? productName),
                              Unit = "nr.", Qty = qty,
                              Cost = cost, Sell = sell, Disc = disc, Vat = vat,
                              LT = lt, LP = lp, Sort = sortOrder++, ParentId = parentId }, tx);
                }
            }
            else
            {
                c.Execute(@"
                    INSERT INTO quote_items (quote_id, product_id, item_type,
                        code, name, description_rtf, unit, quantity,
                        cost_price, sell_price, discount_pct, vat_pct,
                        line_total, line_profit, sort_order, is_active, is_confirmed, is_auto_include)
                    VALUES (@QId, @PId, @Type, @Code, @Name, @Desc, 'nr.', 0, 0, 0, 0, 0, 0, 0, @Sort, 1, 0, 1)",
                    new { QId = quoteId, PId = grp.Key, Type = productType,
                          Code = productCode, Name = productName, Desc = desc,
                          Sort = sortOrder++ }, tx);
            }
        }
    }
}
