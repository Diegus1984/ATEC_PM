using System.Text;
using System.Text.Json;
using ATEC.PM.Server.Services.Hr;
using Xunit;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Banco di prova del motore cartellino: 379 giornate vere calcolate dal motore VB.NET
/// del progetto «Timbrature» fra il 2 e il 24 febbraio 2026, quando era in esercizio.
///
/// <para>Il port in C# deve produrre <b>gli stessi identici numeri</b>. Questo test non
/// verifica che il calcolo sia «giusto» in astratto: verifica che non sia cambiato rispetto
/// a un motore collaudato sul campo. È la rete di sicurezza della traduzione, e serve anche
/// dopo: se domani si tocca una soglia, qui si vede subito quante giornate si spostano.</para>
///
/// <para>Le giornate senza timbrature (forfait e assenze piene) sono escluse: non le produce
/// il motore ma la riconciliazione delle assenze, che è un altro pezzo.</para>
/// </summary>
public class MotoreCartellinoTests
{
    /// <summary>Il giorno in cui il motore originale ha elaborato i dati: serve per «Giornata in corso».</summary>
    private static readonly DateTime GiornoElaborazione = new(2026, 2, 24);

    private sealed record Caso(
        string Dipendente, string Giorno, Config Config,
        List<Timbratura> Timbrature, Atteso Atteso);

    private sealed record Config(bool Forfait, double? ForfaitHours, bool CountsOvertime);
    private sealed record Timbratura(string Orario, string Verso, long? IdEsterno);
    private sealed record Atteso(
        string? Entrata1, string? Uscita1, string? Entrata2, string? Uscita2,
        string? OreTotali, string? Pausa, string? Straordinario,
        Dictionary<string, string>? Fasce, string? Nota);

    private static List<Caso> CaricaCasi()
    {
        string percorso = Path.Combine(AppContext.BaseDirectory, "Hr", "cartellini-collaudo.json");
        Assert.True(File.Exists(percorso),
            $"banco di prova non trovato in {percorso}: il file deve essere copiato in output dal csproj");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(percorso));
        var opzioni = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var casi = new List<Caso>();

        foreach (JsonElement e in doc.RootElement.GetProperty("casi").EnumerateArray())
        {
            JsonElement cfg = e.GetProperty("config");
            JsonElement att = e.GetProperty("atteso");

            casi.Add(new Caso(
                e.GetProperty("dipendente").GetString() ?? "",
                e.GetProperty("giorno").GetString() ?? "",
                new Config(
                    cfg.GetProperty("forfait").GetBoolean(),
                    cfg.TryGetProperty("oreForfait", out JsonElement of) && of.ValueKind == JsonValueKind.Number
                        ? of.GetDouble() : null,
                    cfg.GetProperty("conStraordinari").GetBoolean()),
                e.GetProperty("timbrature").EnumerateArray().Select(t => new Timbratura(
                    t.GetProperty("orario").GetString() ?? "",
                    t.GetProperty("verso").GetString() ?? "",
                    t.TryGetProperty("idEsterno", out JsonElement id) && id.ValueKind == JsonValueKind.Number
                        ? id.GetInt64() : null)).ToList(),
                JsonSerializer.Deserialize<Atteso>(att.GetRawText(), opzioni)!));
        }
        return casi;
    }

    [Fact]
    public void Il_banco_di_prova_e_caricabile_e_consistente()
    {
        List<Caso> casi = CaricaCasi();
        Assert.True(casi.Count >= 370, $"attesi ~379 casi, trovati {casi.Count}");
        Assert.True(casi.Count(c => c.Timbrature.Count > 0) >= 300,
            "troppo pochi casi con timbrature: il banco di prova è stato svuotato?");
    }

    [Fact]
    public void Il_port_riproduce_il_motore_originale()
    {
        List<Caso> casi = CaricaCasi().Where(c => c.Timbrature.Count > 0).ToList();

        var divergenze = new List<string>();
        int confrontati = 0;

        foreach (Caso caso in casi)
        {
            DateTime giorno = DateTime.Parse(caso.Giorno);
            var timbrature = caso.Timbrature
                .Select(t => new RawPunch(DateTime.Parse(t.Orario), t.Verso, t.IdEsterno))
                .ToList();

            TimesheetDay calcolato = TimesheetEngine.Calcola(
                giorno, timbrature, GiornoElaborazione,
                new TimesheetEngine.EmployeeConfig(caso.Config.CountsOvertime));

            confrontati++;
            var diff = new List<string>();
            Confronta(diff, "entrata1", caso.Atteso.Entrata1, calcolato.Entrata1);
            Confronta(diff, "uscita1", caso.Atteso.Uscita1, calcolato.Uscita1);
            Confronta(diff, "entrata2", caso.Atteso.Entrata2, calcolato.Entrata2);
            Confronta(diff, "uscita2", caso.Atteso.Uscita2, calcolato.Uscita2);
            Confronta(diff, "ore", caso.Atteso.OreTotali, calcolato.RegularHours);
            Confronta(diff, "pausa", caso.Atteso.Pausa, calcolato.BreakTime);
            Confronta(diff, "straord", caso.Atteso.Straordinario, calcolato.Overtime);
            Confronta(diff, "nota", caso.Atteso.Nota, calcolato.Note);

            if (caso.Atteso.Fasce is not null)
                foreach ((string lettera, string atteso) in caso.Atteso.Fasce)
                    Confronta(diff, $"fascia {lettera}", atteso,
                        calcolato.Fasce.TryGetValue(lettera, out string? v) ? v : "0h 0m");

            if (diff.Count > 0)
                divergenze.Add($"{caso.Dipendente} {caso.Giorno}: {string.Join(" · ", diff)}");
        }

        if (divergenze.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{divergenze.Count} giornate su {confrontati} divergono dal motore originale.");
            sb.AppendLine("(atteso = motore VB in esercizio, calcolato = port C#)");
            sb.AppendLine();
            foreach (string d in divergenze.Take(25)) sb.AppendLine("  " + d);
            if (divergenze.Count > 25) sb.AppendLine($"  … e altre {divergenze.Count - 25}.");
            Assert.Fail(sb.ToString());
        }
    }

    /// <summary>Confronto tollerante sul vuoto: il motore originale scriveva indifferentemente null o "".</summary>
    private static void Confronta(List<string> diff, string campo, string? atteso, string? calcolato)
    {
        string a = Normalizza(atteso);
        string c = Normalizza(calcolato);
        if (a != c) diff.Add($"{campo}: atteso «{a}» calcolato «{c}»");
    }

    private static string Normalizza(string? valore)
    {
        string v = (valore ?? "").Trim();
        return v is "--:--" ? "" : v;
    }
}
