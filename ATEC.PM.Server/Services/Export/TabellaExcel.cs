using System.Drawing;
using System.Globalization;
using System.Text.Json;
using OfficeOpenXml.Style;

namespace ATEC.PM.Server.Services.Export;

/// <summary>
/// Trasforma una <see cref="TabellaExcelRequest"/> in un file <c>.xlsx</c> vero.
///
/// <para>Regole del foglio, uguali per ogni export che passa di qui:</para>
/// <list type="bullet">
///   <item>intestazione in grassetto su fondo grigio, bloccata mentre si scorre;</item>
///   <item><b>filtro automatico su tutte le colonne</b> (richiesta esplicita della #150);</item>
///   <item>numeri, importi, percentuali e date entrano come <b>valori</b>, non come testo:
///   Excel li formatta con la lingua di chi apre il file, si sommano e si filtrano;</item>
///   <item>gli importi in euro sono arrotondati a 2 decimali (sono quello che va in fattura),
///   le percentuali restano intere nel valore e mostrano 2 decimali (dalla #130 hanno fino a
///   10 decimali: sono il numero da cui esce l'importo, non un'etichetta);</item>
///   <item>un testo che non è un numero in una colonna numerica resta testo: non si perde
///   mai un dato per un formato.</item>
/// </list>
///
/// <para>Le stringhe entrano sempre come stringhe: <c>cell.Value = "=1+1"</c> in EPPlus è
/// testo, non formula. Nessuna iniezione di formule dai dati.</para>
/// </summary>
public static class TabellaExcel
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const int MaxRighe = 100_000;
    public const int MaxColonne = 256;

    private static readonly Color GrigioIntestazione = Color.FromArgb(229, 231, 235);

    /// <summary>Genera il file. Lancia <see cref="ArgumentException"/> su richieste fuori misura.</summary>
    public static byte[] Genera(TabellaExcelRequest richiesta)
    {
        if (richiesta.Colonne.Count == 0)
            throw new ArgumentException("Serve almeno una colonna.");
        if (richiesta.Colonne.Count > MaxColonne)
            throw new ArgumentException($"Troppe colonne (massimo {MaxColonne}).");
        if (richiesta.Righe.Count > MaxRighe)
            throw new ArgumentException($"Troppe righe (massimo {MaxRighe:N0}).");

        using var package = new ExcelPackage();
        ExcelWorksheet ws = package.Workbook.Worksheets.Add(NomeFoglio(richiesta.Foglio));

        int colonne = richiesta.Colonne.Count;
        var larghezze = new int[colonne];

        // ── Intestazione ──────────────────────────────────────────────────────
        for (int c = 0; c < colonne; c++)
        {
            string etichetta = richiesta.Colonne[c].Etichetta ?? "";
            ws.Cells[1, c + 1].Value = etichetta;
            // +3: il pulsante del filtro copre la coda dell'etichetta.
            larghezze[c] = etichetta.Length + 3;
        }

        ExcelRange intestazione = ws.Cells[1, 1, 1, colonne];
        intestazione.Style.Font.Bold = true;
        intestazione.Style.Fill.PatternType = ExcelFillStyle.Solid;
        intestazione.Style.Fill.BackgroundColor.SetColor(GrigioIntestazione);
        intestazione.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        intestazione.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

        // ── Dati ──────────────────────────────────────────────────────────────
        for (int r = 0; r < richiesta.Righe.Count; r++)
        {
            List<JsonElement> riga = richiesta.Righe[r];
            for (int c = 0; c < colonne && c < riga.Count; c++)
            {
                ExcelRange cella = ws.Cells[r + 2, c + 1];
                int lunghezza = Scrivi(cella, richiesta.Colonne[c], riga[c]);
                if (lunghezza > larghezze[c]) larghezze[c] = lunghezza;
            }
        }

        // ── Formato colonne ───────────────────────────────────────────────────
        for (int c = 0; c < colonne; c++)
        {
            ws.Column(c + 1).Width = Math.Clamp(larghezze[c] + 1, 8, 60);
            ExcelHorizontalAlignment? allineamento = Allineamento(richiesta.Colonne[c].Tipo);
            if (allineamento.HasValue)
                ws.Cells[1, c + 1, Math.Max(2, richiesta.Righe.Count + 1), c + 1]
                    .Style.HorizontalAlignment = allineamento.Value;
        }

        // Filtro su ogni colonna, anche a tabella vuota (resta l'intestazione filtrabile).
        int ultimaRiga = Math.Max(1, richiesta.Righe.Count + 1);
        ws.Cells[1, 1, ultimaRiga, colonne].AutoFilter = true;

        // L'intestazione resta ferma mentre si scorre.
        ws.View.FreezePanes(2, 1);

        return package.GetAsByteArray();
    }

    /// <summary>Scrive la cella secondo il tipo della colonna e torna la lunghezza a video (per la larghezza).</summary>
    private static int Scrivi(ExcelRange cella, TabellaExcelColonna colonna, JsonElement valore)
    {
        if (valore.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return 0;

        int decimali = Math.Clamp(colonna.Decimali ?? 2, 0, 10);
        string zeri = decimali > 0 ? "." + new string('0', decimali) : "";

        switch ((colonna.Tipo ?? "testo").Trim().ToLowerInvariant())
        {
            case "intero":
                if (Numero(valore, out decimal n))
                {
                    cella.Value = Math.Round(n, 0, MidpointRounding.AwayFromZero);
                    cella.Style.Numberformat.Format = "0";
                    return cella.Value.ToString()!.Length;
                }
                return Testo(cella, valore);

            case "numero":
                if (Numero(valore, out n))
                {
                    cella.Value = Math.Round(n, decimali, MidpointRounding.AwayFromZero);
                    cella.Style.Numberformat.Format = "#,##0" + zeri;
                    return LunghezzaNumero(n, decimali);
                }
                return Testo(cella, valore);

            case "euro":
                if (Numero(valore, out n))
                {
                    // Gli importi sono quello che va in fattura: 2 decimali, sempre.
                    cella.Value = Math.Round(n, 2, MidpointRounding.AwayFromZero);
                    cella.Style.Numberformat.Format = "#,##0.00 \"€\"";
                    return LunghezzaNumero(n, 2) + 2;
                }
                return Testo(cella, valore);

            case "percento":
                if (Numero(valore, out n))
                {
                    // In punti percentuali dal client, frazione per Excel: 18,37 → 0,1837 → «18,37%».
                    // Divisione in decimal: esatta, niente 0,18370000000000003.
                    cella.Value = n / 100m;
                    cella.Style.Numberformat.Format = "0" + zeri + "%";
                    return LunghezzaNumero(n, decimali) + 1;
                }
                return Testo(cella, valore);

            case "data":
                if (Data(valore, out DateTime data))
                {
                    cella.Value = data.Date;
                    cella.Style.Numberformat.Format = "dd/mm/yyyy";
                    return 10;
                }
                return Testo(cella, valore);

            default:
                return Testo(cella, valore);
        }
    }

    private static int Testo(ExcelRange cella, JsonElement valore)
    {
        string testo = valore.ValueKind switch
        {
            JsonValueKind.String => valore.GetString() ?? "",
            JsonValueKind.True => "Sì",
            JsonValueKind.False => "No",
            _ => valore.GetRawText(),
        };
        if (testo.Length == 0) return 0;

        cella.Value = testo;
        if (testo.Contains('\n')) cella.Style.WrapText = true;

        // La riga più lunga decide la larghezza; oltre i 60 la colonna non cresce comunque.
        return testo.Split('\n').Max(r => r.Length);
    }

    private static bool Numero(JsonElement valore, out decimal numero)
    {
        numero = 0;
        switch (valore.ValueKind)
        {
            case JsonValueKind.Number:
                if (valore.TryGetDecimal(out numero)) return true;
                // Fuori dalla portata del decimal (non capita con importi e percentuali).
                numero = (decimal)Math.Clamp(valore.GetDouble(), (double)decimal.MinValue, (double)decimal.MaxValue);
                return true;

            case JsonValueKind.String:
                // Un numero già scritto all'italiana («1.234,50») lo si accetta lo stesso.
                string s = (valore.GetString() ?? "").Replace("€", "").Replace("%", "").Trim();
                if (s.Length == 0) return false;
                if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out numero)) return true;
                return decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out numero);

            default:
                return false;
        }
    }

    private static bool Data(JsonElement valore, out DateTime data)
    {
        data = default;
        if (valore.ValueKind != JsonValueKind.String) return false;
        string s = (valore.GetString() ?? "").Trim();
        if (s.Length == 0) return false;

        // ISO dal JSON del server («2026-05-29» o «2026-05-29T00:00:00»)…
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out data))
            return true;

        // …o già formattata all'italiana («29/05/2026»).
        return DateTime.TryParseExact(s, new[] { "dd/MM/yyyy", "d/M/yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out data);
    }

    private static int LunghezzaNumero(decimal n, int decimali)
    {
        // Cifre intere + separatori delle migliaia + virgola e decimali.
        int intere = Math.Abs(decimal.Truncate(n)).ToString(CultureInfo.InvariantCulture).Length;
        int migliaia = (intere - 1) / 3;
        return intere + migliaia + (decimali > 0 ? decimali + 1 : 0) + (n < 0 ? 1 : 0);
    }

    private static ExcelHorizontalAlignment? Allineamento(string? tipo) =>
        (tipo ?? "").Trim().ToLowerInvariant() switch
        {
            "intero" or "numero" or "euro" or "percento" => ExcelHorizontalAlignment.Right,
            "data" => ExcelHorizontalAlignment.Center,
            _ => null,
        };

    /// <summary>Excel vieta <c>: \ / ? * [ ]</c> nel nome del foglio e lo tronca a 31 caratteri.</summary>
    public static string NomeFoglio(string? nome)
    {
        var puliti = new string((nome ?? "").Select(ch => ":\\/?*[]".Contains(ch) ? ' ' : ch).ToArray());
        puliti = string.Join(' ', puliti.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (puliti.Length == 0) return "Dati";
        return puliti.Length > 31 ? puliti[..31].TrimEnd() : puliti;
    }

    /// <summary>Nome file sicuro per il Content-Disposition: solo lettere, cifre, <c>_ - .</c>.</summary>
    public static string NomeFile(string? nome)
    {
        string pulito = new string((nome ?? "").Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_').ToArray()).Trim('_', '.');
        if (pulito.Length == 0) pulito = "export";
        return pulito.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? pulito : pulito + ".xlsx";
    }
}
