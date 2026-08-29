using System.Drawing;
using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// L'export Excel del cartellino individuale (PIANO-HR-PORT-ORIGINALE.md, voce 7).
///
/// <para>Nell'originale era un CSV di tutti; qui è il foglio della persona aperta, con le
/// stesse colonne del CSV — i tre stadi affiancati — e lo stile del calendario. Il foglio
/// va in ufficio, quindi si difende cella per cella: se le ore non entrano come numeri
/// nessuno le può sommare, e se una giornata da segnalare non si colora si perde nella
/// pagina.</para>
/// </summary>
public class CartellinoExcelTests
{
    static CartellinoExcelTests()
    {
        // In esercizio la licenza EPPlus la imposta Program.cs, che nei test non gira.
        ExcelPackage.License.SetNonCommercialOrganization("ATEC");
    }

    [Fact]
    public void Titolo_intestazioni_a_due_livelli_e_riquadri_bloccati()
    {
        ExcelWorksheet ws = Foglio(Cartellino());

        Assert.Equal("Cartellino Presenze — Mario Rossi — Febbraio 2026", ws.Cells[1, 1].Value);
        Assert.True(ws.Cells[1, 1].Style.Font.Bold);

        // Riga 3: i tre blocchi. Riga 4: le colonne.
        Assert.Equal("🔸 GREZZO (come timbrato)", ws.Cells[3, 3].Value);
        Assert.Equal("🔷 NORMALIZZATO (arrotondato)", ws.Cells[3, 9].Value);
        Assert.Equal("✅ FINALE (come vale)", ws.Cells[3, 15].Value);
        Assert.Equal("Giorno", ws.Cells[4, 1].Value);
        Assert.Equal("Ordinarie", ws.Cells[4, 20].Value);
        Assert.Equal("Straord.", ws.Cells[4, 21].Value);
        Assert.Equal("Nota", ws.Cells[4, 22].Value);

        // Intestazione e colonna del giorno restano ferme mentre si scorre il mese.
        Assert.Equal(5, ws.View.PaneSettings!.YSplit + 1);
        Assert.Equal(3, ws.View.PaneSettings.XSplit + 1);
    }

    [Fact]
    public void Le_ore_entrano_come_numeri_gli_orari_come_testo()
    {
        ExcelWorksheet ws = Foglio(Cartellino());

        // Prima riga di dati = 5 febbraio.
        Assert.Equal("05/02/2026", ws.Cells[5, 1].Value);
        Assert.Equal("G", ws.Cells[5, 2].Value);
        Assert.Equal("07:58", ws.Cells[5, 3].Value);      // 🔸 grezzo, testo
        Assert.Equal("08:00", ws.Cells[5, 15].Value);     // ✅ finale, testo

        // Ordinarie 8h 0m → 8,0 numerico con formato 0.0: sul foglio si sommano.
        Assert.Equal(8d, Assert.IsType<double>(ws.Cells[5, 20].Value));
        Assert.Equal("0.0", ws.Cells[5, 20].Style.Numberformat.Format);
        Assert.Equal(0.5d, Assert.IsType<double>(ws.Cells[5, 21].Value));
    }

    [Fact]
    public void La_giornata_non_calcolabile_resta_tre_trattini_non_zero()
    {
        // «---» non è zero: è «non si sa quante ore». Metterci 0 sarebbe una bugia sommabile.
        ExcelWorksheet ws = Foglio(Cartellino());
        Assert.Equal("---", ws.Cells[7, 20].Value);
    }

    [Fact]
    public void La_giornata_da_segnalare_si_colora_di_rosso()
    {
        ExcelWorksheet ws = Foglio(Cartellino());

        // Riga 7 = 7 febbraio, ERR: rossa da capo a fondo.
        Assert.Equal(Argb(255, 200, 200), ws.Cells[7, 1].Style.Fill.BackgroundColor.Rgb);
        Assert.Equal(Argb(255, 200, 200), ws.Cells[7, 22].Style.Fill.BackgroundColor.Rgb);

        // Riga 5 = 5 febbraio, regolare: ordinarie verdi, straordinario arancio.
        Assert.Equal(Argb(200, 240, 200), ws.Cells[5, 20].Style.Fill.BackgroundColor.Rgb);
        Assert.Equal(Argb(255, 230, 180), ws.Cells[5, 21].Style.Fill.BackgroundColor.Rgb);
    }

    [Fact]
    public void In_fondo_c_e_la_riga_dei_totali()
    {
        ExcelWorksheet ws = Foglio(Cartellino());

        // 3 giornate + intestazioni → i totali stanno alla riga 8.
        Assert.Equal("TOTALE", ws.Cells[8, 1].Value);
        // 8h + 8h (la terza è «---», non si somma) = 16.
        Assert.Equal(16d, Assert.IsType<double>(ws.Cells[8, 20].Value));
        Assert.Equal(0.5d, Assert.IsType<double>(ws.Cells[8, 21].Value));
        Assert.Equal(ExcelBorderStyle.Medium, ws.Cells[8, 1].Style.Border.Top.Style);
    }

    [Fact]
    public void Il_nome_del_file_porta_persona_e_mese()
    {
        (_, string nomeFile) = HrTimesheetExcel.Genera(Cartellino());
        Assert.Equal("Cartellino_Mario_Rossi_Febbraio_2026.xlsx", nomeFile);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private static HrMonthlyTimesheetDto Cartellino() => new()
    {
        EmployeeId = 1,
        EmployeeName = "Mario Rossi",
        Year = 2026,
        Month = 2,
        Days =
        {
            new HrDayDto
            {
                WorkDate = new DateTime(2026, 2, 5),
                HasData = true,
                ClockIn1 = "08:00",
                ClockOut1 = "12:30",
                ClockIn2 = "13:30",
                ClockOut2 = "17:30",
                RegularHours = "8h 0m",
                Overtime = "0h 30m",
                BreakTime = "1h 0m",
                Note = "OK",
                Punches = { new HrPunchDto { Id = 1, Direction = "IN", Source = "ECOS" } },
                Raw = new HrDayStageDto
                {
                    ClockIn1 = "07:58",
                    ClockOut1 = "12:32",
                    ClockIn2 = "13:28",
                    ClockOut2 = "17:34",
                    BreakTime = "0h 56m",
                    TotalHours = "8h 30m",
                },
            },
            new HrDayDto
            {
                WorkDate = new DateTime(2026, 2, 6),
                HasData = true,
                ClockIn1 = "08:00",
                ClockOut1 = "17:00",
                RegularHours = "8h 0m",
                Overtime = "0h 0m",
                BreakTime = "1h 0m",
                Note = "OK",
                Punches = { new HrPunchDto { Id = 2, Direction = "IN", Source = "ECOS" } },
            },
            new HrDayDto
            {
                WorkDate = new DateTime(2026, 2, 7),
                HasData = true,
                RegularHours = "---",
                Overtime = "---",
                BreakTime = "0h 0m",
                Note = "⚠ ERR: Verificare timbrature",
                HasAnomaly = true,
                CanRemind = true,
                Punches = { new HrPunchDto { Id = 3, Direction = "IN", Source = "ECOS" } },
            },
        },
    };

    private static ExcelWorksheet Foglio(HrMonthlyTimesheetDto cartellino)
    {
        (byte[] contenuto, _) = HrTimesheetExcel.Genera(cartellino);
        return new ExcelPackage(new MemoryStream(contenuto)).Workbook.Worksheets[0];
    }

    private static string Argb(int r, int g, int b) =>
        Color.FromArgb(255, r, g, b).ToArgb().ToString("X8");
}
