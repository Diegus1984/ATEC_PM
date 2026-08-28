using System.Drawing;
using System.Globalization;
using OfficeOpenXml.Style;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Il calendario presenze in Excel.
///
/// <para><b>Port fedele</b> dell'export del progetto «Timbrature»
/// (<c>CalendarPage.xaml.vb</c>, <c>btnEsportaExcel_Click</c>), che in azienda si usa da
/// prima di ATEC PM: stesse intestazioni, stessi colori, stesse larghezze, stessi riquadri
/// bloccati. Chi apre il file deve ritrovare il foglio di sempre, non «un export».</para>
///
/// <para>Il foglio si disegna dalle stesse righe che vede la pagina web
/// (<see cref="HrMonthlyCalendarDto"/>): una sola regola di riempimento, due disegni.</para>
/// </summary>
public static class HrCalendarExcel
{
    // I colori dell'originale, ARGB per ARGB.
    private static readonly Color HeaderFestivo = Color.FromArgb(255, 200, 200);
    private static readonly Color HeaderFeriale = Color.FromArgb(200, 230, 255);
    private static readonly Color Gray = Color.FromArgb(230, 230, 230);
    private static readonly Color Green = Color.FromArgb(200, 240, 200);
    private static readonly Color Red = Color.FromArgb(255, 200, 200);
    private static readonly Color Orange = Color.FromArgb(255, 230, 180);
    private static readonly Color Blue = Color.FromArgb(200, 215, 255);
    private static readonly Color Purple = Color.FromArgb(230, 200, 255);
    private static readonly Color Yellow = Color.FromArgb(255, 255, 200);

    // TEAL (assenza già approvata su Ecos) è nato nel VB dopo l'export, che infatti lo
    // lasciava bianco: qui ha il suo colore, altrimenti sul foglio sparirebbe la differenza
    // fra un'assenza approvata là e una ancora nostra.
    private static readonly Color Teal = Color.FromArgb(180, 235, 230);

    private static readonly Color DarkRed = Color.FromArgb(139, 0, 0);
    private static readonly Color DarkBlue = Color.FromArgb(0, 0, 139);

    /// <summary>
    /// Genera il file. <paramref name="employeeId"/> valorizzato = esporta il solo
    /// dipendente scelto, come il filtro della pagina.
    /// </summary>
    public static (byte[] Contenuto, string NomeFile) Genera(HrMonthlyCalendarDto calendario, int? employeeId)
    {
        var itIT = new CultureInfo("it-IT");
        string meseNome = itIT.TextInfo.ToTitleCase(
            itIT.DateTimeFormat.GetMonthName(calendario.Month));
        int giorni = calendario.DaysInMonth;

        List<HrCalendarRowDto> righe = employeeId.HasValue
            ? calendario.Rows.Where(r => r.EmployeeId == employeeId.Value).ToList()
            : calendario.Rows;

        string nomeDipendente = employeeId.HasValue
            ? righe.Select(r => r.EmployeeKey).FirstOrDefault() ?? ""
            : "";

        using var package = new ExcelPackage();
        ExcelWorksheet ws = package.Workbook.Worksheets.Add($"{meseNome} {calendario.Year}");

        // ── Titolo ────────────────────────────────────────────────────────────
        ws.Cells[1, 1].Value = $"Calendario Presenze — {meseNome} {calendario.Year}";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 14;
        ws.Cells[1, 1, 1, giorni + 3].Merge = true;

        // ── Intestazioni ──────────────────────────────────────────────────────
        const int headerRow = 3;
        ws.Cells[headerRow, 1].Value = "Dipendente";
        ws.Cells[headerRow, 2].Value = "Voce";

        for (int d = 1; d <= giorni; d++)
        {
            ExcelRange cella = ws.Cells[headerRow, d + 2];
            calendario.DayLabels.TryGetValue(d, out string? etichetta);
            cella.Value = $"{d}\n{etichetta}";
            cella.Style.WrapText = true;
            cella.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            bool nonLavorativo = calendario.NonWorkingDays.TryGetValue(d, out bool nl) && nl;
            Sfondo(cella, nonLavorativo ? HeaderFestivo : HeaderFeriale);
            cella.Style.Font.Color.SetColor(nonLavorativo ? DarkRed : DarkBlue);
        }

        ws.Cells[headerRow, giorni + 3].Value = "TOTALE";

        ExcelRange intestazione = ws.Cells[headerRow, 1, headerRow, giorni + 3];
        intestazione.Style.Font.Bold = true;
        intestazione.Style.Font.Size = 10;
        intestazione.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

        // ── Dati ──────────────────────────────────────────────────────────────
        int riga = headerRow + 1;

        foreach (HrCalendarRowDto r in righe)
        {
            ExcelRange dipendente = ws.Cells[riga, 1];
            dipendente.Value = r.Employee;
            dipendente.Style.WrapText = true;
            dipendente.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            if (!string.IsNullOrEmpty(r.Employee)) dipendente.Style.Font.Bold = true;

            ExcelRange voce = ws.Cells[riga, 2];
            voce.Value = r.Voce;
            voce.Style.Font.Size = 9;
            voce.Style.Font.Bold = true;

            for (int d = 1; d <= giorni; d++)
            {
                ExcelRange cella = ws.Cells[riga, d + 2];
                r.Days.TryGetValue(d, out HrCalendarCellDto? dato);
                string testo = dato?.Text ?? "";

                if (testo.Length > 0)
                {
                    // Le ore vanno dentro come numeri: sul foglio si sommano.
                    if (double.TryParse(testo, NumberStyles.Any, CultureInfo.InvariantCulture, out double valore))
                    {
                        cella.Value = valore;
                        cella.Style.Numberformat.Format = "0.0";
                    }
                    else
                    {
                        cella.Value = testo;
                    }
                }

                cella.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                switch (dato?.Color)
                {
                    case "GRAY": Sfondo(cella, Gray); break;
                    case "GREEN": Sfondo(cella, Green); break;
                    case "RED":
                        Sfondo(cella, Red);
                        cella.Style.Font.Color.SetColor(DarkRed);
                        break;
                    case "ORANGE": Sfondo(cella, Orange); break;
                    case "BLUE": Sfondo(cella, Blue); break;
                    case "PURPLE": Sfondo(cella, Purple); break;
                    case "YELLOW": Sfondo(cella, Yellow); break;
                    case "TEAL": Sfondo(cella, Teal); break;
                }
            }

            ExcelRange totale = ws.Cells[riga, giorni + 3];
            if (!string.IsNullOrEmpty(r.Total))
            {
                totale.Value = r.Total;
                totale.Style.Font.Bold = true;
            }
            totale.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // L'infortunio chiude il dipendente: sotto ci va la riga di separazione.
            if (r.VoceType == "INFORTUNIO")
                ws.Cells[riga, 1, riga, giorni + 3].Style.Border.Bottom.Style = ExcelBorderStyle.Medium;

            riga++;
        }

        // ── Formato colonne ───────────────────────────────────────────────────
        ws.Column(1).Width = 24;
        ws.Column(2).Width = 16;
        for (int d = 1; d <= giorni; d++) ws.Column(d + 2).Width = 5.5;
        ws.Column(giorni + 3).Width = 8;

        // Intestazioni e nome/voce restano fermi mentre si scorre il mese.
        ws.View.FreezePanes(headerRow + 1, 3);

        string nomeFile = string.IsNullOrEmpty(nomeDipendente)
            ? $"Calendario_{meseNome}_{calendario.Year}.xlsx"
            : $"Calendario_{nomeDipendente.Replace(",", "").Replace(" ", "_")}_{meseNome}_{calendario.Year}.xlsx";

        return (package.GetAsByteArray(), nomeFile);
    }

    private static void Sfondo(ExcelRange cella, Color colore)
    {
        cella.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cella.Style.Fill.BackgroundColor.SetColor(colore);
    }
}
