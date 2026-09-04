using System.Globalization;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// I conti dell'Andamento sulle offerte: KPI, serie mensili, raggruppamenti, chance,
/// portafoglio ponderato.
///
/// <para><b>Perché è codice puro e non SQL.</b> Le regole non sono somme banali — «emesse»
/// esclude le bozze e le righe senza importo, «prese» si spacca in tre casi, il ponderato
/// dipende da una chance che può mancare — e sono le stesse che il vecchio applicativo
/// applicava in <c>APP.stats</c>. Scritte in una decina di GROUP BY sarebbero illeggibili e
/// improvabili; qui si provano senza database, e il registro conta 1.795 righe: caricarle
/// costa niente.</para>
///
/// <para><b>Fedeltà a <c>APP.stats</c></b>: le bozze non sono offerte emesse (sono numeri
/// riservati) e le righe con importo nullo o zero non entrano nei valori. Non è una svista
/// del vecchio programma: un'offerta senza importo falserebbe le medie e i totali senza
/// aggiungere niente.</para>
/// </summary>
public static class AndamentoOfferteCalcolo
{
    /// <summary>La riga come serve ai conti: nient'altro.</summary>
    public sealed record Riga(
        int Id,
        int Anno,
        int? Numero,
        string TipoCodice,
        string TipoNome,
        string Categoria,
        string VenditoreCodice,
        string VenditoreNome,
        DateTime? Data,
        string Cliente,
        string Descrizione,
        decimal? Importo,
        decimal? ImportoMax,
        string Stato,
        int? Chance,
        decimal? ImportoOrdine,
        bool AConsuntivo);

    private static readonly string[] Mesi =
    {
        "Gen", "Feb", "Mar", "Apr", "Mag", "Giu", "Lug", "Ago", "Set", "Ott", "Nov", "Dic",
    };

    /// <summary>I livelli di chance, «non valutata» compresa (che qui vale -1).</summary>
    public static readonly int[] LivelliChance = { -1, 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

    public static string EtichettaChance(int livello) =>
        livello < 0 ? "Non valutata" : $"{livello}%";

    /// <summary>
    /// Conversione: prese su (prese + perse). NULL finché non si è chiuso niente — uno zero
    /// direbbe «non ne prendiamo nessuna», che è un'altra cosa.
    /// </summary>
    public static decimal? Conversione(int prese, int perse)
    {
        int chiuse = prese + perse;
        return chiuse == 0 ? null : Math.Round((decimal)prese / chiuse * 100m, 1);
    }

    /// <summary>
    /// Portafoglio ponderato: Σ importo × chance/100 sulle aperte che una chance ce l'hanno.
    /// Le aperte senza chance NON valgono zero: semplicemente non ci entrano, e il numero di
    /// quelle che ci entrano si dichiara accanto al totale.
    /// </summary>
    public static decimal Ponderato(IEnumerable<Riga> aperte) =>
        aperte.Where(r => r.Chance != null && r.Importo != null)
              .Sum(r => r.Importo!.Value * r.Chance!.Value / 100m);

    public static AndamentoOfferteDto Calcola(IReadOnlyList<Riga> tutte, int anno)
    {
        var dto = new AndamentoOfferteDto { Anno = anno };

        List<Riga> dellAnno = tutte.Where(r => r.Anno == anno).ToList();

        // Come nel vecchio applicativo: le bozze sono numeri riservati, non offerte; le righe
        // senza importo non entrano nei valori.
        List<Riga> emesse = dellAnno.Where(r => r.Stato != "bozza" && (r.Importo ?? 0) > 0).ToList();

        List<Riga> prese = emesse.Where(r => r.Stato == "presa").ToList();
        List<Riga> aperte = emesse.Where(r => r.Stato == "aperta").ToList();
        List<Riga> perse = emesse.Where(r => r.Stato == "persa").ToList();
        List<Riga> sospese = emesse.Where(r => r.Stato == "sospesa").ToList();

        dto.Kpi = new AndamentoKpiDto
        {
            RigheTotali = dellAnno.Count,
            Bozze = dellAnno.Count(r => r.Stato == "bozza"),
            SenzaImporto = dellAnno.Count(r => r.Stato != "bozza" && (r.Importo ?? 0) <= 0),

            Emesse = emesse.Count,
            ValoreEmesse = Somma(emesse),
            ValoreEmesseMax = emesse.Sum(r => r.ImportoMax ?? r.Importo ?? 0),
            HaForbice = emesse.Any(r => r.ImportoMax != null && r.ImportoMax != r.Importo),

            Prese = prese.Count,
            ValorePrese = Somma(prese),
            ValoreOrdini = prese.Where(r => r.ImportoOrdine != null).Sum(r => r.ImportoOrdine!.Value),
            PreseACorpo = prese.Count(r => r.ImportoOrdine != null),
            PreseAConsuntivo = prese.Count(r => r.ImportoOrdine == null && r.AConsuntivo),
            PreseIncomplete = prese.Count(r => r.ImportoOrdine == null && !r.AConsuntivo),

            Aperte = aperte.Count,
            ValoreAperte = Somma(aperte),
            ValoreAperteMax = aperte.Sum(r => r.ImportoMax ?? r.Importo ?? 0),

            Perse = perse.Count,
            ValorePerse = Somma(perse),
            Sospese = sospese.Count,
            ValoreSospese = Somma(sospese),

            ConversionePct = Conversione(prese.Count, perse.Count),
            PortafoglioPonderato = Math.Round(Ponderato(aperte), 2),
            ApertConChance = aperte.Count(r => r.Chance != null),
        };

        // ── Mese per mese, l'anno scelto ─────────────────────────────────────────
        for (int m = 1; m <= 12; m++)
        {
            List<Riga> delMese = emesse.Where(r => r.Data?.Month == m).ToList();
            dto.Mensile.Add(new AndamentoMeseDto
            {
                Mese = m,
                Etichetta = Mesi[m - 1],
                Emesse = Somma(delMese),
                Prese = Somma(delMese.Where(r => r.Stato == "presa")),
                Aperte = Somma(delMese.Where(r => r.Stato == "aperta")),
                NumeroEmesse = delMese.Count,
            });
        }

        // ── Cinque anni a confronto ──────────────────────────────────────────────
        for (int a = anno - 4; a <= anno; a++)
        {
            List<Riga> annoRighe = tutte
                .Where(r => r.Anno == a && r.Stato != "bozza" && (r.Importo ?? 0) > 0)
                .ToList();
            var serie = new AndamentoSerieAnnoDto { Anno = a };
            for (int m = 1; m <= 12; m++)
                serie.Mesi.Add(Somma(annoRighe.Where(r => r.Data?.Month == m)));
            dto.MensileMultiAnno.Add(serie);
        }

        // Gli anni PRIMA dell'inizio dello storico si tolgono: una linea piatta a zero non è
        // un dato, è rumore che schiaccia la scala. Quelli in mezzo restano anche se vuoti,
        // o il grafico salterebbe un anno facendo sembrare consecutivi due anni che non lo sono.
        while (dto.MensileMultiAnno.Count > 1 && dto.MensileMultiAnno[0].Mesi.All(v => v == 0))
            dto.MensileMultiAnno.RemoveAt(0);

        // ── Raggruppamenti ───────────────────────────────────────────────────────
        dto.PerVenditore = Raggruppa(emesse, r => r.VenditoreCodice,
            g => g.First().VenditoreNome is { Length: > 0 } n ? $"{g.Key} — {n}" : g.Key);
        dto.PerTipo = Raggruppa(emesse, r => r.TipoCodice,
            g => g.First().TipoNome is { Length: > 0 } n ? $"{g.Key} — {n}" : g.Key);
        dto.PerCategoria = Raggruppa(emesse, r => string.IsNullOrEmpty(r.Categoria) ? "Altro" : r.Categoria,
            g => g.Key);
        dto.TopClienti = Raggruppa(emesse, r => r.Cliente, g => g.Key)
            .OrderByDescending(g => g.ValorePrese)
            .ThenByDescending(g => g.ValoreEmesse)
            .Take(10)
            .ToList();

        // ── Chance ───────────────────────────────────────────────────────────────
        foreach (int livello in LivelliChance)
        {
            List<Riga> gruppo = aperte
                .Where(r => livello < 0 ? r.Chance == null : r.Chance == livello)
                .ToList();
            if (gruppo.Count == 0) continue;
            dto.PerChance.Add(new AndamentoChanceDto
            {
                Livello = livello,
                Etichetta = EtichettaChance(livello),
                Offerte = gruppo.Count,
                Valore = Somma(gruppo),
                ValorePonderato = Math.Round(livello < 0 ? 0 : Somma(gruppo) * livello / 100m, 2),
            });
        }

        // ── Le quindici aperte più grandi ────────────────────────────────────────
        dto.TopAperte = aperte
            .OrderByDescending(r => r.Importo ?? 0)
            .Take(15)
            .Select(r => new AndamentoOffertaDto
            {
                Id = r.Id,
                Numero = SalesOfferRules.Composed(r.TipoCodice, r.Numero, r.Anno, r.VenditoreCodice),
                Cliente = r.Cliente,
                Descrizione = r.Descrizione,
                Data = r.Data,
                Importo = r.Importo,
                Chance = r.Chance,
                Venditore = r.VenditoreCodice,
            })
            .ToList();

        return dto;
    }

    private static decimal Somma(IEnumerable<Riga> righe) => righe.Sum(r => r.Importo ?? 0);

    private static List<AndamentoGruppoDto> Raggruppa(
        IEnumerable<Riga> righe,
        Func<Riga, string> chiave,
        Func<IGrouping<string, Riga>, string> etichetta) =>
        righe
            .Where(r => !string.IsNullOrWhiteSpace(chiave(r)))
            .GroupBy(chiave)
            .Select(g =>
            {
                int prese = g.Count(r => r.Stato == "presa");
                int perse = g.Count(r => r.Stato == "persa");
                return new AndamentoGruppoDto
                {
                    Chiave = g.Key,
                    Etichetta = etichetta(g),
                    Emesse = g.Count(),
                    ValoreEmesse = Somma(g),
                    Prese = prese,
                    ValorePrese = Somma(g.Where(r => r.Stato == "presa")),
                    Aperte = g.Count(r => r.Stato == "aperta"),
                    ValoreAperte = Somma(g.Where(r => r.Stato == "aperta")),
                    ConversionePct = Conversione(prese, perse),
                };
            })
            .OrderByDescending(g => g.ValoreEmesse)
            .ToList();

    /// <summary>
    /// Portafoglio aperto mese per mese, ricostruito <b>all'indietro</b> dal registro modifiche:
    /// si parte da com'è oggi e si disfano le modifiche successive alla data (come
    /// <c>APP.stateAt</c>).
    ///
    /// <para>Restituisce una lista <b>vuota</b> se il registro non copre il periodo: sullo
    /// storico importato non c'è nessuna riga di log, e una linea piatta sarebbe una bugia
    /// disegnata bene.</para>
    /// </summary>
    /// <param name="cambiStato">
    /// Le variazioni di stato, dalla più recente: (idOfferta, quando, valorePrecedente).
    /// </param>
    public static List<AndamentoPortafoglioDto> Portafoglio(
        IReadOnlyList<Riga> tutte,
        IReadOnlyList<(int OfferId, DateTime Quando, string StatoPrecedente)> cambiStato,
        DateTime oggi,
        int mesi = 12)
    {
        if (cambiStato.Count == 0) return new List<AndamentoPortafoglioDto>();

        var risultato = new List<AndamentoPortafoglioDto>();
        for (int i = mesi - 1; i >= 0; i--)
        {
            DateTime fineMese = new DateTime(oggi.Year, oggi.Month, 1).AddMonths(-i).AddMonths(1).AddDays(-1);

            decimal valore = 0;
            int quante = 0;
            foreach (Riga r in tutte)
            {
                if ((r.Importo ?? 0) <= 0) continue;

                // Stato alla data: si parte da quello attuale e si torna indietro sui cambi
                // più recenti della data, prendendo ogni volta il valore precedente.
                string stato = r.Stato;
                foreach ((int offerId, DateTime quando, string precedente) in cambiStato)
                    if (offerId == r.Id && quando.Date > fineMese.Date)
                        stato = precedente;

                if (stato != "aperta") continue;
                // Un'offerta emessa dopo quella data non era ancora nel portafoglio.
                if (r.Data != null && r.Data.Value.Date > fineMese.Date) continue;

                valore += r.Importo ?? 0;
                quante++;
            }

            risultato.Add(new AndamentoPortafoglioDto
            {
                Mese = new DateTime(fineMese.Year, fineMese.Month, 1),
                Etichetta = $"{Mesi[fineMese.Month - 1]} {fineMese:yy}",
                Valore = valore,
                Offerte = quante,
            });
        }
        return risultato;
    }

    /// <summary>Nome del mese, per chi deve etichettare fuori di qui.</summary>
    public static string NomeMese(int mese) =>
        mese >= 1 && mese <= 12 ? Mesi[mese - 1] : mese.ToString(CultureInfo.InvariantCulture);
}
