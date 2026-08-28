using System.Drawing;
using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using OfficeOpenXml;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Il foglio Excel del calendario presenze, misurato contro l'export del programma
/// «Timbrature» (<c>CalendarPage.xaml.vb</c>, <c>btnEsportaExcel_Click</c>): titolo unito,
/// intestazioni a riga 3 con la lettera del giorno, festivi rossi e feriali azzurri, colori
/// delle celle, ore come numeri, riquadri bloccati, larghezze, separatore fra dipendenti.
///
/// <para>Serve a questo: il file che l'ufficio apre ogni mese non deve cambiare forma
/// perché è cambiato il programma che lo genera.</para>
/// </summary>
public class CalendarioExcelTests
{
    static CalendarioExcelTests()
    {
        // In esercizio la licenza EPPlus la imposta Program.cs, che nei test non gira.
        ExcelPackage.License.SetNonCommercialOrganization("ATEC");
    }

    private static HrMonthlyCalendarDto Calendario()
    {
        // Febbraio 2026: l'1 è domenica (non lavorativo), il 2 lunedì.
        var cal = new HrMonthlyCalendarDto { Year = 2026, Month = 2, DaysInMonth = 28 };
        for (int g = 1; g <= 28; g++)
        {
            var dt = new DateTime(2026, 2, g);
            cal.DayLabels[g] = dt.DayOfWeek switch
            {
                DayOfWeek.Monday => "L",
                DayOfWeek.Tuesday => "Ma",
                DayOfWeek.Wednesday => "Me",
                DayOfWeek.Thursday => "G",
                DayOfWeek.Friday => "V",
                DayOfWeek.Saturday => "S",
                _ => "D",
            };
            cal.NonWorkingDays[g] = dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        }

        cal.Employees.Add(new HrCalendarEmployeeDto { Id = 7, Name = "Mario Rossi" });

        var ordinarie = new HrCalendarRowDto
        {
            EmployeeId = 7,
            Employee = "Mario Rossi\nMatr. 1234",
            EmployeeKey = "Mario Rossi",
            Voce = "ORE ORDINARIE",
            VoceType = "ORE_ORDINARIE",
            Total = "15,5h",
        };
        ordinarie.Days[1] = new HrCalendarCellDto { Color = "GRAY" };
        ordinarie.Days[2] = new HrCalendarCellDto { Text = "8", Color = "GREEN", Tooltip = "E1: 08:00" };
        ordinarie.Days[3] = new HrCalendarCellDto { Text = "7.5", Color = "GREEN" };

        var ferie = new HrCalendarRowDto
        {
            EmployeeId = 7, EmployeeKey = "Mario Rossi", Voce = "FERIE", VoceType = "FERIE",
        };
        ferie.Days[4] = new HrCalendarCellDto { Text = "8", Color = "BLUE" };

        var infortunio = new HrCalendarRowDto
        {
            EmployeeId = 7, EmployeeKey = "Mario Rossi", Voce = "INFORTUNIO", VoceType = "INFORTUNIO",
        };

        cal.Rows.AddRange(new[] { ordinarie, ferie, infortunio });
        return cal;
    }

    private static ExcelWorksheet Foglio(byte[] contenuto)
    {
        var package = new ExcelPackage(new MemoryStream(contenuto));
        return package.Workbook.Worksheets[0];
    }

    [Fact]
    public void Il_foglio_ha_titolo_intestazioni_e_totale_come_l_originale()
    {
        (byte[] contenuto, string nomeFile) = HrCalendarExcel.Genera(Calendario(), null);
        ExcelWorksheet ws = Foglio(contenuto);

        Assert.Equal("Febbraio 2026", ws.Name);
        Assert.Equal("Calendario_Febbraio_2026.xlsx", nomeFile);
        Assert.Equal("Calendario Presenze — Febbraio 2026", ws.Cells[1, 1].Value);

        // Intestazioni a riga 3: Dipendente, Voce, i 28 giorni, TOTALE in coda.
        Assert.Equal("Dipendente", ws.Cells[3, 1].Value);
        Assert.Equal("Voce", ws.Cells[3, 2].Value);
        Assert.Equal("1\nD", ws.Cells[3, 3].Value);   // 1 febbraio 2026 = domenica
        Assert.Equal("2\nL", ws.Cells[3, 4].Value);
        Assert.Equal("TOTALE", ws.Cells[3, 28 + 3].Value);

        // Prima riga di dati: nome con matricola, voce, totale.
        Assert.Equal("Mario Rossi\nMatr. 1234", ws.Cells[4, 1].Value);
        Assert.Equal("ORE ORDINARIE", ws.Cells[4, 2].Value);
        Assert.Equal("15,5h", ws.Cells[4, 28 + 3].Value);
    }

    [Fact]
    public void Le_ore_sono_numeri_col_formato_dell_originale()
    {
        (byte[] contenuto, _) = HrCalendarExcel.Genera(Calendario(), null);
        ExcelWorksheet ws = Foglio(contenuto);

        // «8» e «7.5» entrano come numeri (si sommano nel foglio), non come testo.
        Assert.Equal(8d, Convert.ToDouble(ws.Cells[4, 4].Value));
        Assert.Equal(7.5d, Convert.ToDouble(ws.Cells[4, 5].Value));
        Assert.Equal("0.0", ws.Cells[4, 5].Style.Numberformat.Format);
    }

    [Fact]
    public void I_colori_sono_quelli_del_programma_originale()
    {
        (byte[] contenuto, _) = HrCalendarExcel.Genera(Calendario(), null);
        ExcelWorksheet ws = Foglio(contenuto);

        // Intestazione: domenica rossa, lunedì azzurro.
        Assert.Equal(Sfondo(255, 200, 200), ws.Cells[3, 3].Style.Fill.BackgroundColor.Rgb);
        Assert.Equal(Sfondo(200, 230, 255), ws.Cells[3, 4].Style.Fill.BackgroundColor.Rgb);

        // Celle: GRAY, GREEN, BLUE.
        Assert.Equal(Sfondo(230, 230, 230), ws.Cells[4, 3].Style.Fill.BackgroundColor.Rgb);
        Assert.Equal(Sfondo(200, 240, 200), ws.Cells[4, 4].Style.Fill.BackgroundColor.Rgb);
        Assert.Equal(Sfondo(200, 215, 255), ws.Cells[5, 6].Style.Fill.BackgroundColor.Rgb);
    }

    [Fact]
    public void Larghezze_riquadri_bloccati_e_separatore_fra_dipendenti()
    {
        (byte[] contenuto, _) = HrCalendarExcel.Genera(Calendario(), null);
        ExcelWorksheet ws = Foglio(contenuto);

        Assert.Equal(24, ws.Column(1).Width, 1);
        Assert.Equal(16, ws.Column(2).Width, 1);
        Assert.Equal(8, ws.Column(28 + 3).Width, 1);

        // Bloccati: le 3 righe di testa e le colonne nome/voce.
        Assert.Equal(4, ws.View.PaneSettings!.YSplit + 1);
        Assert.Equal(3, ws.View.PaneSettings.XSplit + 1);

        // Sotto INFORTUNIO (terza riga di dati) si chiude il dipendente.
        Assert.Equal(OfficeOpenXml.Style.ExcelBorderStyle.Medium,
            ws.Cells[6, 1].Style.Border.Bottom.Style);
    }

    [Fact]
    public void Filtrando_un_dipendente_il_nome_del_file_lo_riporta()
    {
        (byte[] contenuto, string nomeFile) = HrCalendarExcel.Genera(Calendario(), employeeId: 7);
        ExcelWorksheet ws = Foglio(contenuto);

        Assert.Equal("Calendario_Mario_Rossi_Febbraio_2026.xlsx", nomeFile);
        Assert.Equal("ORE ORDINARIE", ws.Cells[4, 2].Value);

        // Un dipendente che non c'è lascia il foglio senza righe, non solleva.
        (_, string vuoto) = HrCalendarExcel.Genera(Calendario(), employeeId: 999);
        Assert.Equal("Calendario_Febbraio_2026.xlsx", vuoto);
    }

    private static string Sfondo(int r, int g, int b) =>
        Color.FromArgb(255, r, g, b).ToArgb().ToString("X8");
}
