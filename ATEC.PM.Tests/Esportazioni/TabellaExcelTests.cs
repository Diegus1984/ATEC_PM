using System.Text.Json;
using ATEC.PM.Server.Services.Export;
using ATEC.PM.Shared.DTOs;
using OfficeOpenXml;

namespace ATEC.PM.Tests.Esportazioni;

/// <summary>
/// Il foglio Excel generico di <c>POST /api/export/xlsx</c> (#150): numeri, importi,
/// percentuali e date entrano come valori veri, il filtro copre tutte le colonne, l'intestazione
/// è bloccata. Le richieste partono da JSON, come dal client: così si prova anche il giro
/// <see cref="JsonElement"/> → cella, non solo il generatore.
/// </summary>
public class TabellaExcelTests
{
    static TabellaExcelTests()
    {
        // In esercizio la licenza EPPlus la imposta Program.cs, che nei test non gira.
        ExcelPackage.License.SetNonCommercialOrganization("ATEC");
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static TabellaExcelRequest Richiesta(string json) =>
        JsonSerializer.Deserialize<TabellaExcelRequest>(json, Json)!;

    /// <summary>Le colonne del Prospetto SAL ridotte all'osso, coi numeri veri della #150.</summary>
    private const string ProspettoJson = """
        {
          "foglio": "Prospetto SAL",
          "colonne": [
            { "etichetta": "Commessa", "tipo": "testo" },
            { "etichetta": "Scad.", "tipo": "intero" },
            { "etichetta": "%", "tipo": "percento" },
            { "etichetta": "Importo", "tipo": "euro" },
            { "etichetta": "Ipotesi Fatturazione", "tipo": "data" }
          ],
          "righe": [
            ["C260415_203", 1, 18.373443646, 26205.1240001075, "2026-08-05T00:00:00"],
            ["C260319_201", 2, 20, 27122.172, "2026-07-10"],
            ["C260518_206", 3, null, null, null]
          ]
        }
        """;

    private static ExcelWorksheet Foglio(byte[] contenuto)
    {
        var package = new ExcelPackage(new MemoryStream(contenuto));
        return package.Workbook.Worksheets[0];
    }

    [Fact]
    public void Percentuali_e_importi_sono_numeri_veri_col_formato_giusto()
    {
        ExcelWorksheet ws = Foglio(TabellaExcel.Genera(Richiesta(ProspettoJson)));

        // La percentuale a 10 decimali (#130) resta intera nel valore e mostra 2 decimali:
        // Excel scrive «18,37%», il numero da cui esce l'importo non si perde.
        Assert.Equal(0.18373443646m, Convert.ToDecimal(ws.Cells[2, 3].Value));
        Assert.Equal("0.00%", ws.Cells[2, 3].Style.Numberformat.Format);

        // L'importo è quello che va in fattura: 2 decimali nel VALORE, non solo a video.
        Assert.Equal(26205.12m, Convert.ToDecimal(ws.Cells[2, 4].Value));
        Assert.Equal(27122.17m, Convert.ToDecimal(ws.Cells[3, 4].Value));
        Assert.Contains("€", ws.Cells[2, 4].Style.Numberformat.Format);

        // Gli interi restano interi.
        Assert.Equal(1m, Convert.ToDecimal(ws.Cells[2, 2].Value));
        Assert.Equal("0", ws.Cells[2, 2].Style.Numberformat.Format);

        // Il testo è testo.
        Assert.Equal("C260415_203", ws.Cells[2, 1].Value);
    }

    [Fact]
    public void Le_date_sono_date_vere_in_formato_italiano()
    {
        ExcelWorksheet ws = Foglio(TabellaExcel.Genera(Richiesta(ProspettoJson)));

        Assert.Equal(new DateTime(2026, 8, 5), ws.Cells[2, 5].GetValue<DateTime>());
        Assert.Equal(new DateTime(2026, 7, 10), ws.Cells[3, 5].GetValue<DateTime>());
        Assert.Equal("dd/mm/yyyy", ws.Cells[2, 5].Style.Numberformat.Format);
    }

    [Fact]
    public void I_null_lasciano_la_cella_vuota()
    {
        ExcelWorksheet ws = Foglio(TabellaExcel.Genera(Richiesta(ProspettoJson)));

        Assert.Null(ws.Cells[4, 3].Value);
        Assert.Null(ws.Cells[4, 4].Value);
        Assert.Null(ws.Cells[4, 5].Value);
    }

    [Fact]
    public void Filtro_su_tutte_le_colonne_intestazione_in_grassetto_e_bloccata()
    {
        ExcelWorksheet ws = Foglio(TabellaExcel.Genera(Richiesta(ProspettoJson)));

        Assert.Equal("Prospetto SAL", ws.Name);
        Assert.Equal("A1:E4", ws.AutoFilter.Address.Address);
        Assert.True(ws.Cells[1, 1].Style.Font.Bold);
        Assert.Equal("Ipotesi Fatturazione", ws.Cells[1, 5].Value);

        // Prima riga bloccata (YSplit = righe sopra la divisione).
        Assert.Equal(1, ws.View.PaneSettings!.YSplit);
    }

    [Fact]
    public void Senza_righe_resta_l_intestazione_col_filtro()
    {
        var richiesta = Richiesta("""
            { "colonne": [ { "etichetta": "A", "tipo": "testo" }, { "etichetta": "B", "tipo": "euro" } ], "righe": [] }
            """);
        ExcelWorksheet ws = Foglio(TabellaExcel.Genera(richiesta));

        Assert.Equal("Dati", ws.Name);
        Assert.Equal("A1:B1", ws.AutoFilter.Address.Address);
        Assert.Equal("B", ws.Cells[1, 2].Value);
    }

    [Fact]
    public void Un_testo_che_sembra_una_formula_resta_testo()
    {
        var richiesta = Richiesta("""
            { "colonne": [ { "etichetta": "Nota", "tipo": "testo" } ], "righe": [ ["=1+1"], ["=HYPERLINK(\"x\")"] ] }
            """);
        ExcelWorksheet ws = Foglio(TabellaExcel.Genera(richiesta));

        Assert.Equal("=1+1", ws.Cells[2, 1].Value);
        Assert.Equal("", ws.Cells[2, 1].Formula);
        Assert.Equal("=HYPERLINK(\"x\")", ws.Cells[3, 1].Value);
        Assert.Equal("", ws.Cells[3, 1].Formula);
    }

    [Fact]
    public void Un_valore_non_numerico_in_colonna_numerica_resta_testo_e_non_si_perde()
    {
        var richiesta = Richiesta("""
            { "colonne": [ { "etichetta": "Importo", "tipo": "euro" }, { "etichetta": "%", "tipo": "percento" } ],
              "righe": [ ["n.d.", "1.234,5"], ["1.234,50 €", "abc"] ] }
            """);
        ExcelWorksheet ws = Foglio(TabellaExcel.Genera(richiesta));

        Assert.Equal("n.d.", ws.Cells[2, 1].Value);
        // Una stringa già scritta all'italiana si accetta come numero.
        Assert.Equal(12.345m, Convert.ToDecimal(ws.Cells[2, 2].Value));
        Assert.Equal(1234.5m, Convert.ToDecimal(ws.Cells[3, 1].Value));
        Assert.Equal("abc", ws.Cells[3, 2].Value);
    }

    [Fact]
    public void Nome_del_foglio_e_del_file_ripuliti_da_quello_che_Excel_vieta()
    {
        Assert.Equal("Prospetto SAL 2026", TabellaExcel.NomeFoglio("Prospetto: SAL/2026?"));
        Assert.Equal("Dati", TabellaExcel.NomeFoglio("  "));
        Assert.Equal(31, TabellaExcel.NomeFoglio(new string('x', 40)).Length);

        Assert.Equal("prospetto-sal.xlsx", TabellaExcel.NomeFile("prospetto-sal.xlsx"));
        Assert.Equal("prospetto-sal.xlsx", TabellaExcel.NomeFile("prospetto-sal"));
        Assert.Equal("a_b.xlsx", TabellaExcel.NomeFile("../a b"));
        Assert.Equal("export.xlsx", TabellaExcel.NomeFile(null));
    }

    [Fact]
    public void Richieste_fuori_misura_vengono_rifiutate()
    {
        Assert.Throws<ArgumentException>(() => TabellaExcel.Genera(new TabellaExcelRequest()));

        var troppeColonne = new TabellaExcelRequest();
        for (int i = 0; i <= TabellaExcel.MaxColonne; i++)
            troppeColonne.Colonne.Add(new TabellaExcelColonna { Etichetta = "c" + i });
        Assert.Throws<ArgumentException>(() => TabellaExcel.Genera(troppeColonne));
    }
}
