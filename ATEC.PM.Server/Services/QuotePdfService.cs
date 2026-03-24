using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

public class QuotePdfService
{
    private readonly string _cmsBasePath;

    // Branding
    private const string BluePrimary = "#2563EB";
    private const string TextDark = "#111827";
    private const string TextMedium = "#374151";
    private const string TextLight = "#6B7280";
    private const string BgLight = "#F8FAFC";
    private const string BorderColor = "#E4E7EC";

    static QuotePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public QuotePdfService(IConfiguration config)
    {
        _cmsBasePath = config["Uploads:CmsPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "uploads", "cms");
    }

    public byte[] Generate(QuoteDto quote)
    {
        // Pre-compute data
        var allItems = quote.Items.OrderBy(i => i.SortOrder).ToList();
        var normalItems = allItems.Where(i => !i.IsAutoInclude).ToList();
        var autoIncludeItems = allItems.Where(i => i.IsAutoInclude).ToList();

        // Parents and variants for normal products
        var parentProducts = normalItems.Where(i => i.ParentItemId == null && i.ItemType == "product").ToList();
        var variantItems = normalItems.Where(i => i.ParentItemId != null && i.ItemType == "product").ToList();
        var parentIdsWithChildren = variantItems.Select(v => v.ParentItemId).Distinct().ToHashSet();

        // Table rows (variants or standalone products)
        var tableItems = new List<QuoteItemDto>();
        foreach (var item in normalItems.Where(i => i.ItemType == "product"))
        {
            if (item.ParentItemId == null && parentIdsWithChildren.Contains(item.Id))
                continue; // skip parent row, show variants instead
            tableItems.Add(item);
        }

        // Content items (non auto-include)
        var contentItems = normalItems.Where(i => i.ItemType == "content" && i.ParentItemId == null).ToList();

        // Auto-include parents
        var autoIncludeParents = autoIncludeItems.Where(i => i.ParentItemId == null).ToList();

        // Quote number for footer
        string quoteRef = $"Prev. {quote.QuoteNumber}";

        return Document.Create(container =>
        {
            // ═══════════════════════════════════════
            // PAGE 1: COPERTINA
            // ═══════════════════════════════════════
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);

                page.Content().Column(col =>
                {
                    // Top accent bar
                    col.Item().Height(6).Background(BluePrimary);

                    col.Item().Padding(60).Column(inner =>
                    {
                        inner.Item().Height(80);

                        // Small label
                        inner.Item().Text("Preventivo riservato per")
                            .FontSize(14).FontColor(TextLight);
                        inner.Item().Height(12);

                        // Client name
                        inner.Item().Text(quote.CustomerName)
                            .FontSize(28).Bold().FontColor(TextDark);
                        inner.Item().Height(30);

                        // Divider
                        inner.Item().Width(80).Height(3).Background(BluePrimary);
                        inner.Item().Height(30);

                        // Quote title/object
                        inner.Item().Text(quote.Title)
                            .FontSize(18).FontColor(TextMedium).LineHeight(1.4f);
                    });

                    // Bottom section with quote info
                    col.Item().ExtendVertical().AlignBottom().Padding(60).Column(bottom =>
                    {
                        bottom.Item().LineHorizontal(1).LineColor(BorderColor);
                        bottom.Item().Height(12);
                        bottom.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"N. {quote.QuoteNumber}")
                                .FontSize(11).FontColor(TextMedium);
                            if (quote.Revision > 0)
                                row.ConstantItem(80).Text($"Rev. {quote.Revision}")
                                    .FontSize(11).FontColor("#D97706");
                            row.ConstantItem(120).AlignRight()
                                .Text($"Data: {quote.CreatedAt:dd/MM/yyyy}")
                                .FontSize(11).FontColor(TextMedium);
                        });
                    });

                    // Bottom accent bar
                    col.Item().Height(6).Background(BluePrimary);
                });

                page.Footer().Text(""); // no footer on cover
            });

            // ═══════════════════════════════════════
            // PAGE 2+: HEADER + INTRO + INDICE + DESCRIZIONI + CONTENUTI + RIEPILOGO
            // ═══════════════════════════════════════
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2, Unit.Centimetre);

                page.Header().Element(c => ComposePageHeader(c, quote));
                page.Footer().Element(c => ComposeFooter(c, quoteRef));

                page.Content().Column(col =>
                {
                    // ── Destinatario ──
                    col.Item().Background(BgLight).Border(1).BorderColor(BorderColor)
                        .Padding(12).Column(dest =>
                    {
                        dest.Item().Text("Spett.le").FontSize(9).FontColor(TextLight);
                        dest.Item().Text(quote.CustomerName).FontSize(12).Bold().FontColor(TextDark);
                        if (!string.IsNullOrEmpty(quote.ContactName1))
                            dest.Item().Text($"Alla c.a. {quote.ContactName1}").FontSize(9).FontColor(TextMedium);
                        if (!string.IsNullOrEmpty(quote.ContactName2))
                            dest.Item().Text($"c.c. {quote.ContactName2}").FontSize(9).FontColor(TextMedium);
                        if (!string.IsNullOrEmpty(quote.ContactName3))
                            dest.Item().Text($"c.c. {quote.ContactName3}").FontSize(9).FontColor(TextMedium);
                    });

                    col.Item().Height(8);

                    // ── Info preventivo ──
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Data: {quote.CreatedAt:dd/MM/yyyy}").FontSize(9).FontColor(TextMedium);
                            c.Item().Text($"N° Preventivo: {quote.QuoteNumber}").FontSize(9).FontColor(TextMedium);
                        });
                    });

                    col.Item().Height(12);

                    // ── Titolo PREVENTIVO ──
                    col.Item().Text("PREVENTIVO").FontSize(16).Bold().FontColor(BluePrimary);
                    col.Item().Height(8);

                    // ── Testo introduttivo ──
                    col.Item().Text("In riferimento alla Vostra gradita richiesta, siamo lieti di sottoporvi la nostra migliore offerta degli articoli di vostro interesse.")
                        .FontSize(9).FontColor(TextMedium).LineHeight(1.4f);
                    col.Item().Height(8);

                    // ── Indice articoli ──
                    if (parentProducts.Count > 0 || contentItems.Count > 0)
                    {
                        col.Item().Text("Di seguito sono presentati i seguenti articoli:")
                            .FontSize(9).FontColor(TextMedium);
                        col.Item().Height(6);

                        col.Item().Column(idx =>
                        {
                            foreach (var p in parentProducts)
                            {
                                idx.Item().PaddingLeft(12).PaddingVertical(2)
                                    .Text(p.Name.ToUpperInvariant())
                                    .FontSize(10).Bold().FontColor(TextDark);
                            }
                        });

                        col.Item().Height(8);
                        col.Item().Text("A seguire descrizioni e costi")
                            .FontSize(9).Italic().FontColor(TextLight);
                    }

                    col.Item().Height(16);

                    // ═══════════════════════════════════════
                    // DESCRIZIONI PRODOTTI
                    // ═══════════════════════════════════════
                    foreach (var parent in parentProducts)
                    {
                        col.Item().Column(section =>
                        {
                            // Sezione header
                            section.Item().Height(8);
                            section.Item().Background(BluePrimary).Padding(8)
                                .Text(parent.Name.ToUpperInvariant())
                                .FontSize(11).Bold().FontColor(Colors.White);
                            section.Item().Height(8);

                            // Descrizione del prodotto
                            if (!string.IsNullOrEmpty(parent.DescriptionRtf))
                            {
                                section.Item().Element(c => RenderDescription(c, parent.DescriptionRtf));
                                section.Item().Height(8);
                            }

                            // Varianti attive come sotto-lista
                            var variants = variantItems.Where(v => v.ParentItemId == parent.Id && v.IsActive).ToList();
                            if (variants.Count > 0)
                            {
                                section.Item().PaddingLeft(10).Column(vCol =>
                                {
                                    foreach (var v in variants)
                                    {
                                        vCol.Item().PaddingVertical(2).Text($"• {v.Name}")
                                            .FontSize(9).FontColor(TextMedium);
                                    }
                                });
                            }

                            section.Item().Height(12);
                        });
                    }

                    // ═══════════════════════════════════════
                    // CONTENUTI NORMALI (non auto-include)
                    // ═══════════════════════════════════════
                    foreach (var content in contentItems)
                    {
                        col.Item().Column(section =>
                        {
                            section.Item().Height(8);
                            section.Item().Background(BluePrimary).Padding(8)
                                .Text(content.Name.ToUpperInvariant())
                                .FontSize(11).Bold().FontColor(Colors.White);
                            section.Item().Height(8);

                            if (!string.IsNullOrEmpty(content.DescriptionRtf))
                            {
                                section.Item().Element(c => RenderDescription(c, content.DescriptionRtf));
                            }
                            section.Item().Height(12);
                        });
                    }

                    // ═══════════════════════════════════════
                    // CONTENUTI AUTOMATICI (sempre in fondo, prima del riepilogo)
                    // ═══════════════════════════════════════
                    foreach (var autoParent in autoIncludeParents)
                    {
                        col.Item().Column(section =>
                        {
                            section.Item().Height(8);
                            section.Item().Background(BluePrimary).Padding(8)
                                .Text(autoParent.Name.ToUpperInvariant())
                                .FontSize(11).Bold().FontColor(Colors.White);
                            section.Item().Height(8);

                            if (!string.IsNullOrEmpty(autoParent.DescriptionRtf))
                            {
                                section.Item().Element(c => RenderDescription(c, autoParent.DescriptionRtf));
                            }
                            section.Item().Height(12);
                        });
                    }

                    // ═══════════════════════════════════════
                    // CONDIZIONI DI VENDITA
                    // ═══════════════════════════════════════
                    col.Item().Column(condSection =>
                    {
                        bool hasConditions = quote.ValidityDays > 0
                            || quote.DeliveryDays > 0
                            || !string.IsNullOrEmpty(quote.PaymentType)
                            || !string.IsNullOrEmpty(quote.NotesQuote);

                        if (hasConditions)
                        {
                            condSection.Item().Height(8);
                            condSection.Item().Background(BluePrimary).Padding(8)
                                .Text("CONDIZIONI DI VENDITA")
                                .FontSize(11).Bold().FontColor(Colors.White);
                            condSection.Item().Height(10);

                            condSection.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cd =>
                                {
                                    cd.ConstantColumn(160);
                                    cd.RelativeColumn();
                                });

                                void CondRow(string label, string value)
                                {
                                    table.Cell().Border(0.5f).BorderColor(BorderColor).Padding(6)
                                        .Text(label).FontSize(9).Bold().FontColor(TextMedium);
                                    table.Cell().Border(0.5f).BorderColor(BorderColor).Padding(6)
                                        .Text(value).FontSize(9).FontColor(TextDark).LineHeight(1.3f);
                                }

                                if (quote.ValidityDays > 0)
                                    CondRow("Validità offerta",
                                        $"{quote.ValidityDays} giorni solari, dalla data di emissione offerta.");

                                if (quote.DeliveryDays > 0)
                                    CondRow("Tempo di consegna",
                                        $"{quote.DeliveryDays} giorni lavorativi dalla conferma d'ordine.");

                                CondRow("Prezzi",
                                    "I prezzi sono da considerarsi esclusi di IVA.");

                                if (!string.IsNullOrEmpty(quote.PaymentType))
                                    CondRow("Pagamento", quote.PaymentType);

                                if (!string.IsNullOrEmpty(quote.NotesQuote))
                                    CondRow("Note", quote.NotesQuote);
                            });
                        }
                    });

                });
            });

            // ═══════════════════════════════════════
            // PAGINA FINALE: RIEPILOGO PREVENTIVO (sempre su pagina nuova)
            // ═══════════════════════════════════════
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2, Unit.Centimetre);

                page.Header().Element(c => ComposePageHeader(c, quote));
                page.Footer().Element(c => ComposeFooter(c, quoteRef));

                page.Content().Column(riepilogo =>
                {
                    riepilogo.Item().Background(BluePrimary).Padding(8)
                        .Text("RIEPILOGO PREVENTIVO")
                        .FontSize(11).Bold().FontColor(Colors.White);
                    riepilogo.Item().Height(8);

                    if (tableItems.Count > 0)
                    {
                        bool hideQty = quote.HideQuantities;

                        riepilogo.Item().Table(table =>
                        {
                            // Colonne — se hideQty mostra solo Nome + Totale
                            table.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(3);    // Servizio preventivato
                                if (!hideQty)
                                {
                                    cd.ConstantColumn(75);   // Costo
                                    cd.ConstantColumn(45);   // Qtà
                                    cd.ConstantColumn(50);   // Sconto
                                }
                                cd.ConstantColumn(85);   // Totale
                            });

                            // Header
                            table.Header(header =>
                            {
                                void HC(string text) =>
                                    header.Cell().Background("#1E40AF").Padding(6)
                                        .Text(text).FontSize(8).Bold().FontColor(Colors.White);

                                HC("Servizio preventivato");
                                if (!hideQty)
                                {
                                    HC("Costo");
                                    HC("Qtà");
                                    HC("Sconto");
                                }
                                HC("Totale");
                            });

                            // Solo righe varianti
                            int rowIdx = 0;
                            foreach (var item in tableItems)
                            {
                                string bg = rowIdx % 2 == 0 ? "#FFFFFF" : "#F8FAFC";
                                rowIdx++;

                                string itemName = item.Name
                                    + (string.IsNullOrEmpty(item.Code) ? "" : $" ({item.Code})");

                                table.Cell().Background(bg).Padding(5)
                                    .Text(itemName).FontSize(8).FontColor(TextMedium);

                                if (!hideQty)
                                {
                                    table.Cell().Background(bg).Padding(5).AlignRight()
                                        .Text($"€{item.SellPrice:N2}").FontSize(8).FontColor(TextDark);

                                    table.Cell().Background(bg).Padding(5).AlignRight()
                                        .Text($"{item.Quantity:N0}").FontSize(8).FontColor(TextDark);

                                    table.Cell().Background(bg).Padding(5).AlignRight()
                                        .Text(item.DiscountPct > 0 ? $"{item.DiscountPct}%" : "---")
                                        .FontSize(8).FontColor(TextDark);
                                }

                                table.Cell().Background(bg).Padding(5).AlignRight()
                                    .Text($"€{item.LineTotal:N2}").FontSize(8).Bold().FontColor(TextDark);
                            }
                        });
                    }

                    riepilogo.Item().Height(8);

                    // ── Totale ──
                    riepilogo.Item().AlignRight().Width(280).Column(summary =>
                    {
                        summary.Item().Height(4);
                        summary.Item().LineHorizontal(2).LineColor(BluePrimary);
                        summary.Item().Height(6);

                        summary.Item().Row(r =>
                        {
                            r.RelativeItem().AlignRight().PaddingRight(12)
                                .Text("Totale preventivo a Voi riservato")
                                .FontSize(11).Bold().FontColor(TextDark);
                            r.ConstantItem(100).AlignRight()
                                .Text($"€{quote.Total:N2}")
                                .FontSize(11).Bold().FontColor(BluePrimary);
                        });

                        if (quote.ShowSummary)
                        {
                            summary.Item().Height(6);

                            if (quote.DiscountPct > 0 || quote.DiscountAbs > 0)
                            {
                                decimal discAmount = quote.Subtotal * quote.DiscountPct / 100 + quote.DiscountAbs;
                                string discLabel = quote.DiscountPct > 0 ? $"Sconto ({quote.DiscountPct}%):" : "Sconto:";
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().AlignRight().PaddingRight(12)
                                        .Text(discLabel).FontSize(9).FontColor(TextMedium);
                                    r.ConstantItem(100).AlignRight()
                                        .Text($"-€{discAmount:N2}").FontSize(9).FontColor("#DC2626");
                                });
                                summary.Item().Height(3);
                            }

                            summary.Item().Row(r =>
                            {
                                r.RelativeItem().AlignRight().PaddingRight(12)
                                    .Text("IVA:").FontSize(9).FontColor(TextMedium);
                                r.ConstantItem(100).AlignRight()
                                    .Text($"€{quote.VatTotal:N2}").FontSize(9).FontColor(TextDark);
                            });
                            summary.Item().Height(3);

                            summary.Item().Row(r =>
                            {
                                r.RelativeItem().AlignRight().PaddingRight(12)
                                    .Text("Totale IVA inclusa:").FontSize(10).Bold().FontColor(TextDark);
                                r.ConstantItem(100).AlignRight()
                                    .Text($"€{quote.TotalWithVat:N2}").FontSize(10).Bold().FontColor(BluePrimary);
                            });
                        }
                    });

                    riepilogo.Item().Height(30);

                    // ── Firma ──
                    riepilogo.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Data di conferma").FontSize(9).FontColor(TextLight);
                            c.Item().Height(30);
                            c.Item().LineHorizontal(1).LineColor("#9CA3AF");
                        });
                        row.ConstantItem(40); // spacer
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Firma per accettazione").FontSize(9).FontColor(TextLight);
                            c.Item().Height(30);
                            c.Item().LineHorizontal(1).LineColor("#9CA3AF");
                        });
                    });

                    riepilogo.Item().Height(8);
                    riepilogo.Item().Text($"N° Preventivo: {quote.QuoteNumber}")
                        .FontSize(9).FontColor(TextMedium);
                    riepilogo.Item().Text(quote.CustomerName)
                        .FontSize(9).Bold().FontColor(TextDark);
                });
            });
        }).GeneratePdf();
    }

    // ═══════════════════════════════════════════════════════
    // GENERATE IMPIANTO — PDF con costing, materiali, distribuzione
    // ═══════════════════════════════════════════════════════

    // Colori gruppi (stessi del client CostingTreeControl)
    private static readonly Dictionary<string, string> GroupColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GESTIONE"] = "#3B82F6",
        ["PRESCHIERAMENTO"] = "#F59E0B",
        ["INSTALLAZIONE"] = "#8B5CF6",
        ["OPZIONE"] = "#EF4444"
    };

    public byte[] GenerateImpianto(QuoteDto quote, ProjectCostingData costing)
    {
        var allItems = quote.Items.OrderBy(i => i.SortOrder).ToList();
        var normalItems = allItems.Where(i => !i.IsAutoInclude).ToList();
        var autoIncludeItems = allItems.Where(i => i.IsAutoInclude).ToList();
        // IMPIANTO: NO prodotti catalogo CMS — solo contenuti (content) e auto-include
        var contentItems = normalItems.Where(i => i.ItemType == "content" && i.ParentItemId == null).ToList();
        var autoIncludeParents = autoIncludeItems.Where(i => i.ParentItemId == null).ToList();
        string quoteRef = $"Prev. {quote.QuoteNumber}";

        // Costing calculations
        var enabledSections = (costing.CostSections ?? new()).Where(s => s.IsEnabled).ToList();
        var groups = enabledSections.GroupBy(s => s.GroupName ?? "ALTRO").OrderBy(g => g.First().SortOrder);
        var matSections = (costing.MaterialSections ?? new()).Where(s => s.IsEnabled).ToList();
        var allMatItems = matSections.SelectMany(ms => ms.Items ?? new()).ToList();
        var pricing = costing.Pricing ?? new();

        // Totali risorse
        decimal totalResourceCost = enabledSections.Sum(s => (s.Resources ?? new()).Sum(r => r.TotalCost));
        decimal totalResourceSale = enabledSections.Sum(s => (s.Resources ?? new()).Sum(r => r.TotalSale));
        decimal totalTravelCost = enabledSections.Sum(s => (s.Resources ?? new()).Sum(r => r.TravelTotal + r.AccommodationTotal));
        decimal totalAllowanceCost = enabledSections.Sum(s => (s.Resources ?? new()).Sum(r => r.AllowanceTotal));

        // Totali materiali (solo leaf items — parent esclusi)
        var leafMatItems = allMatItems.Where(i => !allMatItems.Any(c => c.ParentItemId == i.Id)).ToList();
        decimal totalMaterialCost = leafMatItems.Sum(i => i.TotalCost);
        decimal totalMaterialSale = leafMatItems.Sum(i => i.TotalSale);

        // Pricing
        decimal netPrice = totalResourceSale + totalMaterialSale;
        decimal contingencyAmt = netPrice * pricing.ContingencyPct;
        decimal offerPrice = netPrice + contingencyAmt;
        decimal marginAmt = offerPrice * pricing.NegotiationMarginPct;
        decimal finalPrice = offerPrice + marginAmt;

        // Distribuzione (solo non-shadowed)
        var distSections = enabledSections
            .GroupBy(s => s.Name?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Where(s => !s.IsShadowed)
            .ToList();
        var distMatItems = leafMatItems.Where(i => !i.IsShadowed).ToList();

        return Document.Create(container =>
        {
            // ═══ COPERTINA (identica al SERVICE) ═══
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.Content().Column(col =>
                {
                    col.Item().Height(6).Background(BluePrimary);
                    col.Item().Padding(60).Column(inner =>
                    {
                        inner.Item().Height(80);
                        inner.Item().Text("Preventivo riservato per").FontSize(14).FontColor(TextLight);
                        inner.Item().Height(12);
                        inner.Item().Text(quote.CustomerName).FontSize(28).Bold().FontColor(TextDark);
                        inner.Item().Height(30);
                        inner.Item().Width(80).Height(3).Background(BluePrimary);
                        inner.Item().Height(30);
                        inner.Item().Text(quote.Title).FontSize(18).FontColor(TextMedium).LineHeight(1.4f);
                    });
                    col.Item().ExtendVertical().AlignBottom().Padding(60).Column(bottom =>
                    {
                        bottom.Item().LineHorizontal(1).LineColor(BorderColor);
                        bottom.Item().Height(12);
                        bottom.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"N. {quote.QuoteNumber}").FontSize(11).FontColor(TextMedium);
                            if (quote.Revision > 0)
                                row.ConstantItem(80).Text($"Rev. {quote.Revision}").FontSize(11).FontColor("#D97706");
                            row.ConstantItem(120).AlignRight().Text($"Data: {quote.CreatedAt:dd/MM/yyyy}").FontSize(11).FontColor(TextMedium);
                        });
                    });
                    col.Item().Height(6).Background(BluePrimary);
                });
                page.Footer().Text("");
            });

            // ═══ CONTENUTO: descrizioni + contenuti + condizioni ═══
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2, Unit.Centimetre);
                page.Header().Element(c => ComposePageHeader(c, quote));
                page.Footer().Element(c => ComposeFooter(c, quoteRef));

                page.Content().Column(col =>
                {
                    // Destinatario
                    col.Item().Background(BgLight).Border(1).BorderColor(BorderColor).Padding(12).Column(dest =>
                    {
                        dest.Item().Text("Spett.le").FontSize(9).FontColor(TextLight);
                        dest.Item().Text(quote.CustomerName).FontSize(12).Bold().FontColor(TextDark);
                        if (!string.IsNullOrEmpty(quote.ContactName1))
                            dest.Item().Text($"Alla c.a. {quote.ContactName1}").FontSize(9).FontColor(TextMedium);
                        if (!string.IsNullOrEmpty(quote.ContactName2))
                            dest.Item().Text($"c.c. {quote.ContactName2}").FontSize(9).FontColor(TextMedium);
                        if (!string.IsNullOrEmpty(quote.ContactName3))
                            dest.Item().Text($"c.c. {quote.ContactName3}").FontSize(9).FontColor(TextMedium);
                    });
                    col.Item().Height(8);

                    // Info
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Data: {quote.CreatedAt:dd/MM/yyyy}").FontSize(9).FontColor(TextMedium);
                            c.Item().Text($"N° Preventivo: {quote.QuoteNumber}").FontSize(9).FontColor(TextMedium);
                        });
                    });
                    col.Item().Height(12);
                    col.Item().Text("PREVENTIVO").FontSize(16).Bold().FontColor(BluePrimary);
                    col.Item().Height(8);
                    col.Item().Text("In riferimento alla Vostra gradita richiesta, siamo lieti di sottoporvi la nostra migliore offerta.")
                        .FontSize(9).FontColor(TextMedium).LineHeight(1.4f);
                    col.Item().Height(16);

                    // ── Descrizioni prodotti materiale (con foto, ogni prodotto su pagina nuova) ──
                    var matParents = allMatItems.Where(i => i.ParentItemId == null && !string.IsNullOrEmpty(i.DescriptionRtf)).ToList();
                    foreach (var matParent in matParents)
                    {
                        col.Item().PageBreak();
                        col.Item().Column(section =>
                        {
                            section.Item().Height(8);
                            section.Item().Background("#7C3AED").Padding(8)
                                .Text((matParent.Description ?? "").ToUpperInvariant()).FontSize(11).Bold().FontColor(Colors.White);
                            section.Item().Height(8);
                            section.Item().Element(c => RenderDescription(c, matParent.DescriptionRtf!));
                            section.Item().Height(12);
                        });
                    }

                    // Contenuti normali
                    foreach (var content in contentItems)
                    {
                        col.Item().Column(section =>
                        {
                            section.Item().Height(8);
                            section.Item().Background(BluePrimary).Padding(8)
                                .Text(content.Name.ToUpperInvariant()).FontSize(11).Bold().FontColor(Colors.White);
                            section.Item().Height(8);
                            if (!string.IsNullOrEmpty(content.DescriptionRtf))
                                section.Item().Element(c => RenderDescription(c, content.DescriptionRtf));
                            section.Item().Height(12);
                        });
                    }

                    // Auto-include (ogni contenuto su pagina nuova)
                    foreach (var autoParent in autoIncludeParents)
                    {
                        col.Item().PageBreak();
                        col.Item().Column(section =>
                        {
                            section.Item().Height(8);
                            section.Item().Background(BluePrimary).Padding(8)
                                .Text(autoParent.Name.ToUpperInvariant()).FontSize(11).Bold().FontColor(Colors.White);
                            section.Item().Height(8);
                            if (!string.IsNullOrEmpty(autoParent.DescriptionRtf))
                                section.Item().Element(c => RenderDescription(c, autoParent.DescriptionRtf));
                            section.Item().Height(12);
                        });
                    }

                    // Condizioni
                    bool hasConditions = quote.ValidityDays > 0 || quote.DeliveryDays > 0
                        || !string.IsNullOrEmpty(quote.PaymentType) || !string.IsNullOrEmpty(quote.NotesQuote);
                    if (hasConditions)
                    {
                        col.Item().Height(8);
                        col.Item().Background(BluePrimary).Padding(8)
                            .Text("CONDIZIONI DI VENDITA").FontSize(11).Bold().FontColor(Colors.White);
                        col.Item().Height(10);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cd => { cd.ConstantColumn(160); cd.RelativeColumn(); });
                            void CR(string l, string v)
                            {
                                table.Cell().Border(0.5f).BorderColor(BorderColor).Padding(6).Text(l).FontSize(9).Bold().FontColor(TextMedium);
                                table.Cell().Border(0.5f).BorderColor(BorderColor).Padding(6).Text(v).FontSize(9).FontColor(TextDark).LineHeight(1.3f);
                            }
                            if (quote.ValidityDays > 0) CR("Validità offerta", $"{quote.ValidityDays} giorni solari, dalla data di emissione offerta.");
                            if (quote.DeliveryDays > 0) CR("Tempo di consegna", $"{quote.DeliveryDays} giorni lavorativi dalla conferma d'ordine.");
                            CR("Prezzi", "I prezzi sono da considerarsi esclusi di IVA.");
                            if (!string.IsNullOrEmpty(quote.PaymentType)) CR("Pagamento", quote.PaymentType);
                            if (!string.IsNullOrEmpty(quote.NotesQuote)) CR("Note", quote.NotesQuote);
                        });
                    }
                });
            });

            // ═══ PAGINA RIEPILOGO DISTRIBUZIONE ═══
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2, Unit.Centimetre);
                page.Header().Element(c => ComposePageHeader(c, quote));
                page.Footer().Element(c => ComposeFooter(c, quoteRef));

                page.Content().Column(col =>
                {
                    // ── RIEPILOGO PREVENTIVO ──
                    col.Item().Background(BluePrimary).Padding(8)
                        .Text("RIEPILOGO PREVENTIVO").FontSize(11).Bold().FontColor(Colors.White);
                    col.Item().Height(8);

                    // Tabella commerciale: Nome voce → Prezzo totale (incluso contingency+margine spalmato)
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(4);   // Voce
                            cd.ConstantColumn(100);  // Importo
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background("#1E40AF").Padding(6)
                                .Text("Voce").FontSize(8).Bold().FontColor(Colors.White);
                            header.Cell().Background("#1E40AF").Padding(6).AlignRight()
                                .Text("Importo").FontSize(8).Bold().FontColor(Colors.White);
                        });

                        // Calcolo distribuzione commerciale:
                        // Ogni riga visibile riceve una quota proporzionale del prezzo finale
                        // (contingency + margine + shadow spalmato) in base alla sua % di vendita sul totale visibile
                        decimal visibleSaleTotal = distSections.Sum(s => (s.Resources ?? new()).Sum(r => r.TotalSale))
                                                 + distMatItems.Sum(i => i.TotalSale);

                        int ri = 0;
                        decimal grandTotal = 0;

                        // Righe risorse (solo non-shadowed con vendita > 0)
                        foreach (var sec in distSections)
                        {
                            decimal sale = (sec.Resources ?? new()).Sum(r => r.TotalSale);
                            if (sale == 0) continue;
                            decimal pct = visibleSaleTotal > 0 ? sale / visibleSaleTotal : 0;
                            decimal total = Math.Round(finalPrice * pct, 2);
                            grandTotal += total;
                            string bg = ri++ % 2 == 0 ? "#FFFFFF" : BgLight;

                            table.Cell().Background(bg).Padding(6)
                                .Text(sec.Name ?? "").FontSize(9).FontColor(TextDark);
                            table.Cell().Background(bg).Padding(6).AlignRight()
                                .Text($"€ {total:N2}").FontSize(9).FontColor(TextDark);
                        }

                        // Righe materiali (solo non-shadowed con vendita > 0)
                        foreach (var item in distMatItems)
                        {
                            if (item.TotalSale == 0) continue;
                            decimal pct = visibleSaleTotal > 0 ? item.TotalSale / visibleSaleTotal : 0;
                            decimal total = Math.Round(finalPrice * pct, 2);
                            grandTotal += total;
                            string bg = ri++ % 2 == 0 ? "#FFFFFF" : BgLight;

                            table.Cell().Background(bg).Padding(6)
                                .Text(item.Description ?? "").FontSize(9).FontColor(TextDark);
                            table.Cell().Background(bg).Padding(6).AlignRight()
                                .Text($"€ {total:N2}").FontSize(9).FontColor(TextDark);
                        }

                        // Riga TOTALE (usa finalPrice per evitare discrepanze arrotondamento)
                        table.Cell().Background(BluePrimary).Padding(6)
                            .Text("TOTALE").FontSize(10).Bold().FontColor(Colors.White);
                        table.Cell().Background(BluePrimary).Padding(6).AlignRight()
                            .Text($"€ {finalPrice:N2}").FontSize(10).Bold().FontColor(Colors.White);
                    });

                    col.Item().Height(30);

                    // ── Firma ──
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Data di conferma").FontSize(9).FontColor(TextLight);
                            c.Item().Height(30);
                            c.Item().LineHorizontal(1).LineColor("#9CA3AF");
                        });
                        row.ConstantItem(40);
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Firma per accettazione").FontSize(9).FontColor(TextLight);
                            c.Item().Height(30);
                            c.Item().LineHorizontal(1).LineColor("#9CA3AF");
                        });
                    });

                    col.Item().Height(8);
                    col.Item().Text($"N° Preventivo: {quote.QuoteNumber}").FontSize(9).FontColor(TextMedium);
                    col.Item().Text(quote.CustomerName).FontSize(9).Bold().FontColor(TextDark);
                });
            });
        }).GeneratePdf();
    }

    // ═══════════════════════════════════════════════
    // PAGE HEADER (pagine 2+)
    // ═══════════════════════════════════════════════

    private void ComposePageHeader(IContainer container, QuoteDto quote)
    {
        container.Column(col =>
        {
            col.Item().Height(4).Background(BluePrimary);
            col.Item().Height(6);

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("AUTOMATION TECHNOLOGY s.r.l.")
                        .FontSize(11).Bold().FontColor(TextDark);
                    left.Item().Text("Via Donatello n.15 Cap 10071 - Borgaro Torinese (TO) - Italy")
                        .FontSize(7).FontColor(TextLight);
                    left.Item().Text("P.IVA: 09972180013 - SDI HHBD9AK")
                        .FontSize(7).FontColor(TextLight);
                    left.Item().Text("Tel.: +39 011.991.33.94")
                        .FontSize(7).FontColor(TextLight);
                    left.Item().Text("E-mail: info@atec.srl — PEC: automation.tecnology.srl@pec.it")
                        .FontSize(7).FontColor(TextLight);
                    left.Item().Text("Sito web: www.atec.srl")
                        .FontSize(7).FontColor(TextLight);
                });
            });

            col.Item().Height(6);
            col.Item().LineHorizontal(1).LineColor(BorderColor);
            col.Item().Height(6);
        });
    }

    // ═══════════════════════════════════════════════
    // DESCRIPTION — Rendering HTML → QuestPDF
    // ═══════════════════════════════════════════════

    private void RenderDescription(IContainer container, string html)
    {
        container.Column(col =>
        {
            // Controlla se c'è una tabella HTML (TinyMCE produce <table> con <tr><td>)
            var tableMatch = Regex.Match(html, @"<table[^>]*>(.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (tableMatch.Success)
            {
                // Contenuto prima della tabella
                string beforeTable = html.Substring(0, tableMatch.Index);
                RenderHtmlBlock(col, beforeTable);

                // Renderizza la tabella come layout a colonne
                RenderHtmlTable(col, tableMatch.Groups[1].Value);

                // Contenuto dopo la tabella
                string afterTable = html.Substring(tableMatch.Index + tableMatch.Length);
                RenderHtmlBlock(col, afterTable);
            }
            else
            {
                // Nessuna tabella — rendering lineare
                RenderHtmlBlock(col, html);
            }
        });
    }

    /// <summary>
    /// Renderizza una tabella HTML come layout a colonne QuestPDF.
    /// Gestisce righe (<tr>) con celle (<td>) contenenti testo e/o immagini.
    /// </summary>
    private void RenderHtmlTable(ColumnDescriptor col, string tableInnerHtml)
    {
        // Estrai tutte le righe <tr>
        var rowMatches = Regex.Matches(tableInnerHtml, @"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match rowMatch in rowMatches)
        {
            string rowHtml = rowMatch.Groups[1].Value;

            // Estrai celle <td>
            var cellMatches = Regex.Matches(rowHtml, @"<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (cellMatches.Count == 0) continue;

            if (cellMatches.Count == 1)
            {
                // Singola cella — rendering lineare
                RenderHtmlBlock(col, cellMatches[0].Groups[1].Value);
            }
            else
            {
                // Multiple celle — layout a colonne affiancate
                col.Item().Row(row =>
                {
                    for (int i = 0; i < cellMatches.Count; i++)
                    {
                        string cellHtml = cellMatches[i].Groups[1].Value;
                        int cellIdx = i;

                        row.RelativeItem().Padding(4).Column(cellCol =>
                        {
                            RenderHtmlBlock(cellCol, cellHtml);
                        });

                        // Spacer tra celle (tranne l'ultima)
                        if (i < cellMatches.Count - 1)
                            row.ConstantItem(8);
                    }
                });
            }

            col.Item().Height(4);
        }
    }

    /// <summary>
    /// Renderizza un blocco HTML (senza tabelle) con immagini e testo.
    /// </summary>
    private void RenderHtmlBlock(ColumnDescriptor col, string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return;

        // Estrai immagini (con dimensioni opzionali da attributi width/height)
        var imgMatches = Regex.Matches(html, @"<img[^>]+src\s*=\s*""([^""]+)""[^>]*>", RegexOptions.IgnoreCase);
        foreach (Match m in imgMatches)
        {
            var srcMatch = Regex.Match(m.Value, @"src\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!srcMatch.Success) continue;
            string src = srcMatch.Groups[1].Value;
            string? localPath = ResolveImagePath(src);
            if (localPath != null && File.Exists(localPath))
            {
                try
                {
                    byte[] imgBytes = File.ReadAllBytes(localPath);
                    col.Item().Height(4);
                    // Limita altezza immagine per evitare che occupi tutta la pagina
                    col.Item().MaxHeight(250).Image(imgBytes, ImageScaling.FitArea);
                    col.Item().Height(4);
                }
                catch { }
            }
        }

        // Estrai testo
        string text = StripHtml(html);
        if (!string.IsNullOrWhiteSpace(text))
        {
            col.Item().Text(text).FontSize(9).FontColor(TextMedium).LineHeight(1.4f);
        }
    }

    private string? ResolveImagePath(string src)
    {
        var match = Regex.Match(src, @"/uploads/cms/(.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string relativePath = match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(_cmsBasePath, relativePath);
        }

        if (File.Exists(src))
            return src;

        return null;
    }

    private static string StripHtml(string html)
    {
        string result = Regex.Replace(html, @"<img[^>]*>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</p>|</div>|</li>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<[^>]+>", "");
        result = result.Replace("&nbsp;", " ").Replace("&amp;", "&")
                       .Replace("&lt;", "<").Replace("&gt;", ">")
                       .Replace("&quot;", "\"").Replace("&#39;", "'");
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        return result.Trim();
    }

    // ═══════════════════════════════════════════════
    // FOOTER
    // ═══════════════════════════════════════════════

    private void ComposeFooter(IContainer container, string quoteRef)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(BorderColor);
            col.Item().Height(4);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(quoteRef)
                    .FontSize(7).FontColor("#9CA3AF");
                row.ConstantItem(80).AlignRight()
                    .Text(text =>
                    {
                        text.Span("Pag. ").FontSize(7).FontColor("#9CA3AF");
                        text.CurrentPageNumber().FontSize(7).FontColor("#9CA3AF");
                        text.Span("/").FontSize(7).FontColor("#9CA3AF");
                        text.TotalPages().FontSize(7).FontColor("#9CA3AF");
                    });
            });
        });
    }
}
