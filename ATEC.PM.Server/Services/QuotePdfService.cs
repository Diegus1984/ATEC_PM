using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

public class QuotePdfService
{
    static QuotePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(QuoteDto quote)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(2, Unit.Centimetre);

                page.Header().Element(c => ComposeHeader(c, quote));
                page.Content().Element(c => ComposeContent(c, quote));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    // ═══════════════════════════════════════════════
    // HEADER
    // ═══════════════════════════════════════════════

    private void ComposeHeader(IContainer container, QuoteDto quote)
    {
        container.Column(col =>
        {
            // Top bar accent
            col.Item().Height(4).Background("#2563EB");
            col.Item().Height(8);

            col.Item().Row(row =>
            {
                // Sinistra: info ATEC
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("AUTOMATION TECHNOLOGY S.R.L.")
                        .FontSize(14).Bold().FontColor("#1A1D26");
                    left.Item().Text("Via Donatello, 15 — 10071 Borgaro T.se (TO)")
                        .FontSize(8).FontColor("#6B7280");
                    left.Item().Text("P.IVA IT09972180013 — Tel. +39 011 9913394")
                        .FontSize(8).FontColor("#6B7280");
                    left.Item().Text("info@atec.srl — www.atec.srl")
                        .FontSize(8).FontColor("#6B7280");
                });

                // Destra: info preventivo
                row.ConstantItem(200).AlignRight().Column(right =>
                {
                    right.Item().Background("#F3F4F6").Padding(10).Column(box =>
                    {
                        box.Item().Text("PREVENTIVO").FontSize(12).Bold().FontColor("#2563EB");
                        box.Item().Height(4);
                        box.Item().Text($"N. {quote.QuoteNumber}").FontSize(10).Bold();
                        if (quote.Revision > 0)
                            box.Item().Text($"REV. {quote.Revision}").FontSize(9).FontColor("#D97706");
                        box.Item().Text($"Data: {quote.CreatedAt:dd/MM/yyyy}").FontSize(9).FontColor("#374151");
                        if (quote.ValidityDays > 0)
                            box.Item().Text($"Validità: {quote.ValidityDays} giorni").FontSize(9).FontColor("#374151");
                    });
                });
            });

            col.Item().Height(12);

            // Destinatario
            col.Item().Background("#F8FAFC").Border(1).BorderColor("#E4E7EC").Padding(12).Column(dest =>
            {
                dest.Item().Text("Spett.le").FontSize(9).FontColor("#6B7280");
                dest.Item().Text(quote.CustomerName).FontSize(12).Bold().FontColor("#111827");
                if (!string.IsNullOrEmpty(quote.ContactName1))
                    dest.Item().Text($"Alla c.a. {quote.ContactName1}").FontSize(9).FontColor("#374151");
                if (!string.IsNullOrEmpty(quote.ContactName2))
                    dest.Item().Text($"c.c. {quote.ContactName2}").FontSize(9).FontColor("#374151");
                if (!string.IsNullOrEmpty(quote.ContactName3))
                    dest.Item().Text($"c.c. {quote.ContactName3}").FontSize(9).FontColor("#374151");
            });

            col.Item().Height(8);

            // Oggetto
            col.Item().Text($"Oggetto: {quote.Title}").FontSize(11).Bold().FontColor("#111827");

            col.Item().Height(12);
            col.Item().LineHorizontal(1).LineColor("#E4E7EC");
            col.Item().Height(8);
        });
    }

    // ═══════════════════════════════════════════════
    // CONTENT — Voci + Riepilogo
    // ═══════════════════════════════════════════════

    private void ComposeContent(IContainer container, QuoteDto quote)
    {
        container.Column(col =>
        {
            // Tabella voci prodotto
            var productItems = quote.Items.Where(i => i.ItemType == "product").OrderBy(i => i.SortOrder).ToList();
            var contentItems = quote.Items.Where(i => i.ItemType == "content").OrderBy(i => i.SortOrder).ToList();

            if (productItems.Count > 0)
            {
                col.Item().Table(table =>
                {
                    // Colonne
                    table.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(30);   // #
                        cd.RelativeColumn(3);    // Descrizione
                        cd.ConstantColumn(40);   // UdM
                        cd.ConstantColumn(45);   // Qtà
                        if (quote.ShowItemPrices)
                        {
                            cd.ConstantColumn(75);   // Prezzo unit.
                            cd.ConstantColumn(50);   // Sconto
                            cd.ConstantColumn(85);   // Totale
                        }
                    });

                    // Header
                    table.Header(header =>
                    {
                        void HeaderCell(string text) =>
                            header.Cell().Background("#2563EB").Padding(6)
                                .Text(text).FontSize(8).Bold().FontColor(Colors.White);

                        HeaderCell("#");
                        HeaderCell("Descrizione");
                        HeaderCell("UdM");
                        HeaderCell("Qtà");
                        if (quote.ShowItemPrices)
                        {
                            HeaderCell("Prezzo unit.");
                            HeaderCell("Sconto");
                            HeaderCell("Totale");
                        }
                    });

                    // Righe
                    int rowNum = 0;
                    foreach (var item in productItems)
                    {
                        rowNum++;
                        string bgColor = rowNum % 2 == 0 ? "#F8FAFC" : "#FFFFFF";

                        void Cell(string text, bool bold = false, bool right = false)
                        {
                            var cell = table.Cell().Background(bgColor).Padding(5);
                            var t = cell.Text(text).FontSize(9).FontColor("#111827");
                            if (bold) t.Bold();
                        }

                        void CellRight(string text, bool bold = false)
                        {
                            table.Cell().Background(bgColor).PaddingVertical(5).PaddingHorizontal(5)
                                .AlignRight().Text(text).FontSize(9).FontColor("#111827");
                        }

                        Cell(rowNum.ToString());
                        Cell($"{item.Name}" + (string.IsNullOrEmpty(item.Code) ? "" : $" ({item.Code})"));
                        Cell(item.Unit);
                        CellRight($"{item.Quantity:N0}");
                        if (quote.ShowItemPrices)
                        {
                            CellRight($"{item.SellPrice:N2}€");
                            CellRight(item.DiscountPct > 0 ? $"{item.DiscountPct}%" : "—");
                            CellRight($"{item.LineTotal:N2}€", bold: true);
                        }
                    }
                });
            }

            col.Item().Height(16);

            // ── Riepilogo economico ──
            if (quote.ShowSummary)
            {
                col.Item().AlignRight().Width(250).Column(summary =>
                {
                    void SummaryRow(string label, string value, bool bold = false, string? color = null)
                    {
                        summary.Item().Row(r =>
                        {
                            var lbl = r.RelativeItem().AlignRight().PaddingRight(12)
                                .Text(label).FontSize(10).FontColor("#374151");
                            if (bold) lbl.Bold();

                            var val = r.ConstantItem(100).AlignRight()
                                .Text(value).FontSize(10).FontColor(color ?? "#111827");
                            if (bold) val.Bold();
                        });
                        summary.Item().Height(3);
                    }

                    SummaryRow("Subtotale:", $"{quote.Subtotal:N2}€");
                    if (quote.DiscountPct > 0 || quote.DiscountAbs > 0)
                    {
                        decimal discAmount = quote.Subtotal * quote.DiscountPct / 100 + quote.DiscountAbs;
                        string discLabel = quote.DiscountPct > 0 ? $"Sconto ({quote.DiscountPct}%):" : "Sconto:";
                        SummaryRow(discLabel, $"-{discAmount:N2}€", color: "#DC2626");
                    }

                    summary.Item().Height(2);
                    summary.Item().LineHorizontal(1).LineColor("#E4E7EC");
                    summary.Item().Height(4);

                    SummaryRow("TOTALE IMPONIBILE:", $"{quote.Total:N2}€", bold: true);
                    SummaryRow("IVA:", $"{quote.VatTotal:N2}€");

                    summary.Item().Height(2);
                    summary.Item().LineHorizontal(1).LineColor("#2563EB");
                    summary.Item().Height(4);

                    SummaryRow("TOTALE IVA INCLUSA:", $"{quote.TotalWithVat:N2}€", bold: true, color: "#2563EB");
                });
            }

            col.Item().Height(20);

            // ── Blocchi testo (contenuti) ──
            foreach (var content in contentItems)
            {
                col.Item().Column(textBlock =>
                {
                    textBlock.Item().Text(content.Name).FontSize(10).Bold().FontColor("#1A1D26");
                    textBlock.Item().Height(4);
                    if (!string.IsNullOrEmpty(content.DescriptionRtf))
                    {
                        textBlock.Item().Text(content.DescriptionRtf).FontSize(9).FontColor("#374151").LineHeight(1.4f);
                    }
                    textBlock.Item().Height(12);
                });
            }

            // ── Condizioni ──
            col.Item().Height(8);
            col.Item().LineHorizontal(1).LineColor("#E4E7EC");
            col.Item().Height(8);

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c1 =>
                {
                    if (quote.DeliveryDays > 0)
                    {
                        c1.Item().Text($"Tempi di consegna: {quote.DeliveryDays} giorni lavorativi")
                            .FontSize(9).FontColor("#374151");
                    }
                    if (!string.IsNullOrEmpty(quote.PaymentType))
                    {
                        c1.Item().Text($"Pagamento: {quote.PaymentType}")
                            .FontSize(9).FontColor("#374151");
                    }
                    if (quote.ValidityDays > 0)
                    {
                        c1.Item().Text($"Validità offerta: {quote.ValidityDays} giorni dalla data di emissione")
                            .FontSize(9).FontColor("#374151");
                    }
                });
            });

            // ── Note preventivo ──
            if (!string.IsNullOrEmpty(quote.NotesQuote))
            {
                col.Item().Height(12);
                col.Item().Text("Note:").FontSize(9).Bold().FontColor("#374151");
                col.Item().Height(4);
                col.Item().Text(quote.NotesQuote).FontSize(9).FontColor("#374151").LineHeight(1.4f);
            }

            // ── Firma ──
            col.Item().Height(30);
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("In attesa di un Vs. cortese riscontro, porgiamo cordiali saluti.")
                        .FontSize(9).FontColor("#374151");
                    c.Item().Height(30);
                    c.Item().Text("AUTOMATION TECHNOLOGY S.R.L.")
                        .FontSize(10).Bold().FontColor("#1A1D26");
                    c.Item().LineHorizontal(1).LineColor("#9CA3AF");
                    c.Item().Text("Firma").FontSize(8).FontColor("#9CA3AF");
                });
                row.ConstantItem(200);
            });
        });
    }

    // ═══════════════════════════════════════════════
    // FOOTER
    // ═══════════════════════════════════════════════

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor("#E4E7EC");
            col.Item().Height(4);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("AUTOMATION TECHNOLOGY S.R.L. — Via Donatello, 15 — 10071 Borgaro T.se (TO)")
                    .FontSize(7).FontColor("#9CA3AF");
                row.ConstantItem(60).AlignRight()
                    .Text(text =>
                    {
                        text.Span("Pag. ").FontSize(7).FontColor("#9CA3AF");
                        text.CurrentPageNumber().FontSize(7).FontColor("#9CA3AF");
                        text.Span(" / ").FontSize(7).FontColor("#9CA3AF");
                        text.TotalPages().FontSize(7).FontColor("#9CA3AF");
                    });
            });
        });
    }
}
