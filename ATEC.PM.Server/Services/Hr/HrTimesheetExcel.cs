using System.Drawing;
using System.Globalization;
using ATEC.PM.Shared.DTOs;
using OfficeOpenXml.Style;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Il cartellino individuale in Excel (PIANO-HR-PORT-ORIGINALE.md, voce 7).
///
/// <para>Nell'originale l'export del <c>ReportPage</c> era un <b>CSV di tutti</b>; qui è un
/// <b>foglio della persona aperta</b>, perché è così che è fatta la pagina — il «tutti» è il
/// Calendario mensile, che l'Excel ce l'ha già. Le colonne però sono le stesse del CSV: i
/// tre stadi della giornata affiancati (🔸 grezzo · 🔷 normalizzato · ✅ finale) più la nota,
/// che è l'unico modo per vedere DOVE una giornata è cambiata.</para>
///
/// <para>Stile ricalcato su <see cref="HrCalendarExcel"/>: stessi colori, stesso titolo
/// unito, stesse intestazioni in grassetto, riquadri bloccati. Chi riceve i due file deve
/// riconoscerli come lo stesso foglio.</para>
/// </summary>
public static class HrTimesheetExcel
{
    // Gli stessi ARGB del calendario: due fogli, una tavolozza sola.
    private static readonly Color HeaderFestivo = Color.FromArgb(255, 200, 200);
    private static readonly Color HeaderFeriale = Color.FromArgb(200, 230, 255);
    private static readonly Color Gray = Color.FromArgb(230, 230, 230);
    private static readonly Color Green = Color.FromArgb(200, 240, 200);
    private static readonly Color Red = Color.FromArgb(255, 200, 200);
    private static readonly Color Orange = Color.FromArgb(255, 230, 180);
    private static readonly Color Blue = Color.FromArgb(200, 215, 255);
    private static readonly Color DarkRed = Color.FromArgb(139, 0, 0);
    private static readonly Color DarkBlue = Color.FromArgb(0, 0, 139);

    private const int RigaGruppi = 3;
    private const int RigaIntestazioni = 4;
    private const int PrimaRigaDati = 5;

    /// <summary>Le colonne, nell'ordine del CSV originale: 2 + 6 + 6 + 7 + 1 = 22.</summary>
    private const int ColGiorno = 1;
    private const int ColGiornoSett = 2;
    private const int ColGrezzo = 3;         // E1 U1 E2 U2 Pausa Ore
    private const int ColNormalizzato = 9;   // E1 U1 E2 U2 Pausa Ore
    private const int ColFinale = 15;        // E1 U1 E2 U2 Pausa Ordinarie Straord.
    private const int ColNota = 22;

    /// <summary>Genera il foglio del cartellino di una persona.</summary>
    public static (byte[] Contenuto, string NomeFile) Genera(HrMonthlyTimesheetDto cartellino)
    {
        var itIT = new CultureInfo("it-IT");
        string meseNome = itIT.TextInfo.ToTitleCase(itIT.DateTimeFormat.GetMonthName(cartellino.Month));

        using var package = new ExcelPackage();
        ExcelWorksheet ws = package.Workbook.Worksheets.Add($"{meseNome} {cartellino.Year}");

        // ── Titolo ────────────────────────────────────────────────────────────
        ws.Cells[1, 1].Value =
            $"Cartellino Presenze — {cartellino.EmployeeName} — {meseNome} {cartellino.Year}";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 14;
        ws.Cells[1, 1, 1, ColNota].Merge = true;

        // ── Intestazioni a due livelli ────────────────────────────────────────
        Gruppo(ws, ColGrezzo, 6, "🔸 GREZZO (come timbrato)");
        Gruppo(ws, ColNormalizzato, 6, "🔷 NORMALIZZATO (arrotondato)");
        Gruppo(ws, ColFinale, 7, "✅ FINALE (come vale)");

        string[] intestazioni =
        {
            "Giorno", "Gg",
            "E1", "U1", "E2", "U2", "Pausa", "Ore",
            "E1", "U1", "E2", "U2", "Pausa", "Ore",
            "E1", "U1", "E2", "U2", "Pausa", "Ordinarie", "Straord.",
            "Nota",
        };
        for (int i = 0; i < intestazioni.Length; i++)
            ws.Cells[RigaIntestazioni, i + 1].Value = intestazioni[i];

        ExcelRange testata = ws.Cells[RigaIntestazioni, 1, RigaIntestazioni, ColNota];
        testata.Style.Font.Bold = true;
        testata.Style.Font.Size = 10;
        testata.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        testata.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        Sfondo(testata, HeaderFeriale);
        testata.Style.Font.Color.SetColor(DarkBlue);

        // ── Dati ──────────────────────────────────────────────────────────────
        int riga = PrimaRigaDati;
        int totaleOrdinarie = 0;
        int totaleStraordinario = 0;

        foreach (HrDayDto g in cartellino.Days)
        {
            bool festivo = g.IsHoliday
                || g.WorkDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            ExcelRange giorno = ws.Cells[riga, ColGiorno];
            giorno.Value = g.WorkDate.ToString("dd/MM/yyyy", itIT);
            giorno.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            ExcelRange gg = ws.Cells[riga, ColGiornoSett];
            gg.Value = EtichettaGiorno(g.WorkDate);
            gg.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            if (festivo)
            {
                Sfondo(ws.Cells[riga, ColGiorno, riga, ColGiornoSett], HeaderFestivo);
                ws.Cells[riga, ColGiorno, riga, ColGiornoSett].Style.Font.Color.SetColor(DarkRed);
            }

            Stadio(ws, riga, ColGrezzo, g.Raw.ClockIn1, g.Raw.ClockOut1, g.Raw.ClockIn2,
                g.Raw.ClockOut2, g.Raw.BreakTime, g.Raw.TotalHours);
            Stadio(ws, riga, ColNormalizzato, g.Normalized.ClockIn1, g.Normalized.ClockOut1,
                g.Normalized.ClockIn2, g.Normalized.ClockOut2, g.Normalized.BreakTime,
                g.Normalized.TotalHours);

            Orario(ws, riga, ColFinale, g.ClockIn1);
            Orario(ws, riga, ColFinale + 1, g.ClockOut1);
            Orario(ws, riga, ColFinale + 2, g.ClockIn2);
            Orario(ws, riga, ColFinale + 3, g.ClockOut2);
            Durata(ws, riga, ColFinale + 4, g.BreakTime);

            ExcelRange ordinarie = Durata(ws, riga, ColFinale + 5, g.RegularHours);
            ExcelRange straordinario = Durata(ws, riga, ColFinale + 6, g.Overtime);

            int minutiOrd = HrAttendanceService.MinutesFrom(g.RegularHours);
            int minutiStr = HrAttendanceService.MinutesFrom(g.Overtime);
            totaleOrdinarie += minutiOrd;
            totaleStraordinario += minutiStr;

            if (minutiOrd > 0) Sfondo(ordinarie, Green);
            if (minutiStr > 0) Sfondo(straordinario, Orange);

            ExcelRange nota = ws.Cells[riga, ColNota];
            nota.Value = g.Note;
            nota.Style.Font.Size = 9;

            // La giornata da segnalare si vede a colpo d'occhio, come il 📧 lampeggiante
            // dell'originale: sul foglio l'unico modo è il colore.
            if (g.CanRemind || g.HasAnomaly)
            {
                Sfondo(ws.Cells[riga, ColGiorno, riga, ColNota], Red);
                ws.Cells[riga, ColNota].Style.Font.Color.SetColor(DarkRed);
            }
            else if (festivo || !g.HasData)
            {
                Sfondo(ws.Cells[riga, ColGrezzo, riga, ColNota], Gray);
            }
            else if (g.Punches.Count == 0)
            {
                // Giornata coperta ma non timbrata: assenza approvata o forfait.
                Sfondo(ws.Cells[riga, ColGrezzo, riga, ColNota], Blue);
            }

            riga++;
        }

        // ── Totali ────────────────────────────────────────────────────────────
        ExcelRange etichettaTotale = ws.Cells[riga, ColGiorno, riga, ColGiornoSett];
        etichettaTotale.Merge = true;
        etichettaTotale.Value = "TOTALE";
        etichettaTotale.Style.Font.Bold = true;
        etichettaTotale.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        Numero(ws, riga, ColFinale + 5, totaleOrdinarie);
        Numero(ws, riga, ColFinale + 6, totaleStraordinario);
        Sfondo(ws.Cells[riga, ColFinale + 5], Green);
        Sfondo(ws.Cells[riga, ColFinale + 6], Orange);

        ExcelRange rigaTotali = ws.Cells[riga, 1, riga, ColNota];
        rigaTotali.Style.Font.Bold = true;
        rigaTotali.Style.Border.Top.Style = ExcelBorderStyle.Medium;

        // ── Formato colonne ───────────────────────────────────────────────────
        ws.Column(ColGiorno).Width = 12;
        ws.Column(ColGiornoSett).Width = 4;
        for (int col = ColGrezzo; col < ColNota; col++) ws.Column(col).Width = 7;
        ws.Column(ColFinale + 5).Width = 9;
        ws.Column(ColFinale + 6).Width = 9;
        ws.Column(ColNota).Width = 34;

        // Intestazioni e colonna del giorno restano ferme mentre si scorre il mese.
        ws.View.FreezePanes(PrimaRigaDati, ColGrezzo);

        string nomeFile =
            $"Cartellino_{PulisciNome(cartellino.EmployeeName)}_{meseNome}_{cartellino.Year}.xlsx";

        return (package.GetAsByteArray(), nomeFile);
    }

    private static void Gruppo(ExcelWorksheet ws, int colonna, int quante, string titolo)
    {
        ExcelRange range = ws.Cells[RigaGruppi, colonna, RigaGruppi, colonna + quante - 1];
        range.Merge = true;
        range.Value = titolo;
        range.Style.Font.Bold = true;
        range.Style.Font.Size = 10;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
    }

    private static void Stadio(
        ExcelWorksheet ws, int riga, int colonna,
        string e1, string u1, string e2, string u2, string pausa, string ore)
    {
        Orario(ws, riga, colonna, e1);
        Orario(ws, riga, colonna + 1, u1);
        Orario(ws, riga, colonna + 2, e2);
        Orario(ws, riga, colonna + 3, u2);
        Durata(ws, riga, colonna + 4, pausa);
        Durata(ws, riga, colonna + 5, ore);
    }

    /// <summary>Gli orari restano testo: «--:--» e il suffisso «*» degli stimati sono informazione.</summary>
    private static void Orario(ExcelWorksheet ws, int riga, int colonna, string? valore)
    {
        ExcelRange cella = ws.Cells[riga, colonna];
        cella.Value = string.IsNullOrWhiteSpace(valore) ? "" : valore;
        cella.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
    }

    /// <summary>
    /// Le durate («8h 30m») vanno dentro come <b>numeri di ore</b>: sul foglio si sommano.
    /// «---» (giornata non calcolabile) resta testo: non è zero, è «non si sa».
    /// </summary>
    private static ExcelRange Durata(ExcelWorksheet ws, int riga, int colonna, string? valore)
    {
        ExcelRange cella = ws.Cells[riga, colonna];
        cella.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        if (string.IsNullOrWhiteSpace(valore)) return cella;
        if (valore == "---")
        {
            cella.Value = "---";
            return cella;
        }

        int minuti = HrAttendanceService.MinutesFrom(valore);
        if (minuti == 0 && valore != "0h 0m")
        {
            cella.Value = valore;
            return cella;
        }

        cella.Value = Math.Round(minuti / 60d, 2);
        cella.Style.Numberformat.Format = "0.0";
        return cella;
    }

    private static void Numero(ExcelWorksheet ws, int riga, int colonna, int minuti)
    {
        ExcelRange cella = ws.Cells[riga, colonna];
        cella.Value = Math.Round(minuti / 60d, 2);
        cella.Style.Numberformat.Format = "0.0";
        cella.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
    }

    private static string EtichettaGiorno(DateTime data) => data.DayOfWeek switch
    {
        DayOfWeek.Monday => "L",
        DayOfWeek.Tuesday => "Ma",
        DayOfWeek.Wednesday => "Me",
        DayOfWeek.Thursday => "G",
        DayOfWeek.Friday => "V",
        DayOfWeek.Saturday => "S",
        _ => "D",
    };

    private static string PulisciNome(string nome) =>
        string.IsNullOrWhiteSpace(nome) ? "Cartellino" : nome.Replace(",", "").Replace(" ", "_");

    private static void Sfondo(ExcelRange cella, Color colore)
    {
        cella.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cella.Style.Fill.BackgroundColor.SetColor(colore);
    }
}
