namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Regole di calcolo del cartellino presenze: soglie, arrotondamenti e maggiorazioni CCNL.
///
/// <para><b>Copia unica.</b> Questi numeri sono tarati sul campo e vengono dal motore VB.NET
/// del progetto «Timbrature» (<c>Classes/GVL.vb</c>), in esercizio su dati veri. Non vanno
/// duplicati altrove: chi ha bisogno di una soglia la legge da qui.</para>
///
/// <para><b>Fonte delle maggiorazioni</b>: CCNL metalmeccanici, Circolare n. 12 del
/// 23.12.2024, colonna «Non a turni». Se esce una circolare nuova si cambia QUI e i test
/// del banco di prova dicono subito cosa si muove.</para>
/// </summary>
public static class RegoleCartellino
{
    /// <summary>
    /// Versione delle regole, scritta su ogni riga di <c>hr_giornate</c> (<c>regole_versione</c>).
    /// Si alza quando cambia una soglia o una maggiorazione: le giornate con versione più
    /// bassa le ricalcola da sé <c>HrPresenzeService.RiparaGiornate</c> al primo import.
    ///
    /// <para>Storia: <b>1</b> primo port del motore VB · <b>2</b> aggiunto il filtro dei
    /// doppioni di strisciata sotto i 5 minuti (era nella CTE SQL del VB, fuori dal motore).</para>
    /// </summary>
    public const int Versione = 2;

    /// <summary>Giornata lavorativa ordinaria: oltre questa soglia è straordinario.</summary>
    public const int MinutiGiornataStandard = 480;

    /// <summary>Sotto questa pausa si considera che il tempo sia stato recuperato.</summary>
    public const int PausaMinimaMinuti = 30;

    /// <summary>
    /// Doppioni di strisciata: una timbratura a meno di questi minuti dalla PRECEDENTE
    /// (riga precedente, non «precedente tenuta»: semantica LAG della CTE del VB,
    /// <c>ReportProcessor.vb</c> righe 27-48) si scarta prima di ogni altro calcolo.
    /// </summary>
    public const int FiltroDoppioniMinuti = 5;

    /// <summary>Pausa dedotta d'ufficio quando non è stata timbrata.</summary>
    public const int PausaForzataMinuti = 60;

    /// <summary>Passo dell'arrotondamento delle timbrature.</summary>
    public const int ScattoMinuti = 30;

    /// <summary>Entro questi minuti l'orario resta allo scatto in corso invece di saltare al successivo.</summary>
    public const int TolleranzaMinuti = 10;

    /// <summary>Dalle 22:00 il lavoro è notturno.</summary>
    public const int OraInizioNotturno = 22;

    // ── Maggiorazioni (Circolare n. 12 del 23.12.2024, «Non a turni») ──────────
    public const double MaggiorazioneA = 0.20;   // a. straordinario diurno
    public const double MaggiorazioneB1 = 0.25;  // b. notturno fino alle 22
    public const double MaggiorazioneB2 = 0.35;  // b. notturno oltre le 22
    public const double MaggiorazioneC = 0.55;   // c. festivo
    public const double MaggiorazioneD = 0.10;   // d. festivo con riposo compensativo
    public const double MaggiorazioneE = 0.55;   // e. straordinario festivo (oltre 8h)
    public const double MaggiorazioneF = 0.35;   // f. straord. festivo con riposo comp. (oltre 8h)
    public const double MaggiorazioneG1 = 0.50;  // g. straordinario notturno (prime 2h)
    public const double MaggiorazioneG2 = 0.60;  // g. straordinario notturno (ore successive)
    public const double MaggiorazioneH = 0.35;   // h. notturno e festivo
    public const double MaggiorazioneL = 0.75;   // l. straord. notturno festivo (oltre 8h)
    public const double MaggiorazioneM = 0.55;   // m. straord. nott. festivo con riposo comp.

    /// <summary>
    /// Giorno festivo: domenica, festività nazionali, patrono di Borgaro Torinese
    /// (San Vincenzo Martire, 22 gennaio) e Lunedì dell'Angelo.
    /// </summary>
    public static bool EFestivo(DateTime giorno)
    {
        if (giorno.DayOfWeek == DayOfWeek.Sunday) return true;

        bool fissa = (giorno.Month, giorno.Day) switch
        {
            (1, 1) or (1, 6) or (1, 22) or (4, 25) or (5, 1) or (6, 2)
                or (8, 15) or (11, 1) or (12, 8) or (12, 25) or (12, 26) => true,
            _ => false,
        };
        if (fissa) return true;

        return giorno.Date == LunediDellAngelo(giorno.Year).Date;
    }

    /// <summary>Lunedì dell'Angelo (Pasqua + 1), algoritmo di Gauss-Butcher.</summary>
    public static DateTime LunediDellAngelo(int anno)
    {
        int a = anno % 19;
        int b = anno / 100;
        int c = anno % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int mese = (h + l - 7 * m + 114) / 31;
        int giorno = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateTime(anno, mese, giorno).AddDays(1);
    }

    /// <summary>
    /// Arrotonda una timbratura allo scatto di 30 minuti, nel verso che NON regala tempo:
    /// l'entrata sale, l'uscita scende. Entro la tolleranza si resta allo scatto in corso.
    /// </summary>
    public static DateTime Arrotonda(DateTime orario, bool eIngresso) =>
        eIngresso ? ArrotondaSu(orario) : ArrotondaGiu(orario);

    private static DateTime ArrotondaSu(DateTime orario)
    {
        int minuti = orario.Hour * 60 + orario.Minute;
        int scattoPrecedente = minuti / ScattoMinuti * ScattoMinuti;
        int delta = minuti - scattoPrecedente;
        int arrotondati = delta <= TolleranzaMinuti ? scattoPrecedente : scattoPrecedente + ScattoMinuti;
        return DaMinuti(orario, arrotondati);
    }

    private static DateTime ArrotondaGiu(DateTime orario)
    {
        int minuti = orario.Hour * 60 + orario.Minute;
        int scattoSuccessivo = (int)(Math.Ceiling(minuti / (double)ScattoMinuti) * ScattoMinuti);
        int delta = scattoSuccessivo - minuti;
        int arrotondati = delta <= TolleranzaMinuti ? scattoSuccessivo : scattoSuccessivo - ScattoMinuti;
        return DaMinuti(orario, arrotondati);
    }

    private static DateTime DaMinuti(DateTime giorno, int minutiDaMezzanotte)
    {
        int ore = minutiDaMezzanotte / 60;
        int minuti = minutiDaMezzanotte % 60;
        return ore >= 24
            ? giorno.Date.AddDays(1)
            : new DateTime(giorno.Year, giorno.Month, giorno.Day, ore, minuti, 0);
    }

    /// <summary>«8h 30m» dai minuti; mai negativo.</summary>
    public static string Durata(int minuti) =>
        minuti <= 0 ? "0h 0m" : $"{minuti / 60}h {minuti % 60}m";

    /// <summary>«07:30», oppure «--:--» se la timbratura manca.</summary>
    public static string Orario(DateTime? valore) =>
        valore.HasValue ? valore.Value.ToString("HH:mm") : "--:--";
}
