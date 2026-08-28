namespace ATEC.PM.Server.Services.Hr;

/// <summary>Una timbratura come arriva dal rilevatore: punched_at e direction, niente di elaborato.</summary>
/// <param name="Orario">Istante grezzo, mai modificato.</param>
/// <param name="Verso">Verso dichiarato dal terminale (IN/OUT, ENTRATA/USCITA...).</param>
/// <param name="ExternalId">Identificativo del rilevatore, per risalire alla timbratura originale.</param>
public record RawPunch(DateTime PunchedAt, string Direction, long? ExternalId = null);

/// <summary>Il cartellino di una giornata: cosa risulta lavorato e come si scompone.</summary>
public class TimesheetDay
{
    public DateTime WorkDate { get; init; }

    /// <summary>Orari come vanno letti a video. L'asterisco segnala un punched_at messo dal sistema.</summary>
    public string Entrata1 { get; set; } = "";
    public string Uscita1 { get; set; } = "";
    public string Entrata2 { get; set; } = "";
    public string Uscita2 { get; set; } = "";

    /// <summary>Ore ordinarie (mai oltre la giornata standard); «---» se non calcolabili.</summary>
    public string RegularHours { get; set; } = "0h 0m";
    public string Overtime { get; set; } = "0h 0m";
    public string BreakTime { get; set; } = "0h 0m";

    /// <summary>Overtime per fascia CCNL: chiave = lettera della circolare (A, C, D, E, F, G, H, L, M).</summary>
    public Dictionary<string, string> Fasce { get; } = NewBands();

    // ── I due stadi che stanno PRIMA del risultato ────────────────────────────
    //
    // Il cartellino non nasce già fatto: le timbrature passano dal grezzo (come sono
    // arrivate) al normalizzato (arrotondato allo scatto) e solo allora diventano il
    // risultato qui sopra. Il ReportPage del programma «Timbrature» mostra i tre stadi
    // affiancati — 🔸 grezzo, 🔷 normalizzato, ✅ finale — ed è così che in ufficio si
    // capisce PERCHÉ una giornata è venuta com'è venuta. Sono di sola lettura: nessun
    // calcolo li guarda.

    /// <summary>🔸 Le timbrature come sono arrivate dal rilevatore, senza arrotondamento.</summary>
    public string RawEntrata1 { get; set; } = "--:--";
    public string RawUscita1 { get; set; } = "--:--";
    public string RawEntrata2 { get; set; } = "--:--";
    public string RawUscita2 { get; set; } = "--:--";
    public string RawTotal { get; set; } = "0h 0m";
    public string RawBreak { get; set; } = "0h 0m";

    /// <summary>🔷 Le stesse timbrature dopo l'arrotondamento (scatto 30', tolleranza 10').</summary>
    public string NormEntrata1 { get; set; } = "--:--";
    public string NormUscita1 { get; set; } = "--:--";
    public string NormEntrata2 { get; set; } = "--:--";
    public string NormUscita2 { get; set; } = "--:--";
    public string NormTotal { get; set; } = "0h 0m";
    public string NormBreak { get; set; } = "0h 0m";

    /// <summary>Cosa è successo: «OK», la pausa dedotta, il turno riconosciuto o l'has_anomaly.</summary>
    public string Note { get; set; } = "";

    /// <summary>true se la giornata richiede un intervento umano (timbratura mancante o incoerente).</summary>
    public bool HasAnomaly => Note.StartsWith("⚠");

    internal static Dictionary<string, string> NewBands() =>
        new() { ["A"] = "0h 0m", ["C"] = "0h 0m", ["D"] = "0h 0m", ["E"] = "0h 0m", ["F"] = "0h 0m",
                ["G"] = "0h 0m", ["H"] = "0h 0m", ["L"] = "0h 0m", ["M"] = "0h 0m" };
}

/// <summary>
/// Trasforma le timbrature grezze di una giornata nel cartellino: raggruppa i doppioni,
/// assegna entrate e uscite, riconosce il turno, deduce la pausa e scompone lo straordinario
/// nelle fasce del CCNL.
///
/// <para><b>Port fedele</b> del motore VB.NET del progetto «Timbrature»
/// (<c>Classes/ReportProcessor.vb</c>), già in esercizio su dati veri. Le euristiche qui
/// dentro (specie l'assegnazione con tre timbrature) sono state tarate sul campo: NON si
/// «migliorano» a intuito. Ogni modifica va misurata contro il banco di prova
/// <c>ATEC.PM.Tests/Hr/cartellini-collaudo.json</c>, che contiene 379 giornate vere
/// calcolate dal motore originale.</para>
///
/// <para>La classe è <b>pura</b>: nessun accesso al database, nessun orologio di sistema —
/// «oggi» si passa da fuori, altrimenti la giornata in corso non sarebbe riproducibile.</para>
/// </summary>
public static class TimesheetEngine
{
    /// <summary>Configurazione della persona che incide sul calcolo.</summary>
    /// <param name="CountsOvertime">false = overtime is not counted for this employee.</param>
    public record EmployeeConfig(bool CountsOvertime = true);

    /// <summary>Timbrature assegnate ai quattro posti del cartellino, già arrotondate.</summary>
    private sealed class Assignment
    {
        public DateTime? Entrata1, Uscita1, Entrata2, Uscita2;
        public int NumIngressi, NumUscite;

        /// <summary>Gli stessi quattro posti PRIMA dell'arrotondamento: servono solo a mostrare il grezzo.</summary>
        public DateTime? RawEntrata1, RawUscita1, RawEntrata2, RawUscita2;
    }

    /// <summary>
    /// Calcola il cartellino di una giornata.
    /// </summary>
    /// <param name="work_date">Giornata di competenza.</param>
    /// <param name="timbrature">Timbrature grezze del work_date, in qualsiasi ordine.</param>
    /// <param name="oggi">Data odierna: serve a riconoscere la giornata ancora in corso.</param>
    /// <param name="config">Configurazione della persona.</param>
    public static TimesheetDay Calcola(
        DateTime work_date,
        IEnumerable<RawPunch> timbrature,
        DateTime oggi,
        EmployeeConfig? config = null)
    {
        config ??= new EmployeeConfig();
        var cartellino = new TimesheetDay { WorkDate = work_date.Date };

        var ordinate = timbrature.OrderBy(t => t.PunchedAt).ToList();
        if (ordinate.Count == 0)
        {
            cartellino.Note = "";
            return cartellino;
        }

        Assignment dati = Assign(ordinate);

        // I tre stadi si mostrano affiancati: il grezzo c'è sempre, il normalizzato solo
        // quando la giornata è chiusa — su una giornata in corso non c'è niente da
        // arrotondare, ed è così anche nell'originale.
        RiempiStadio(cartellino, dati.RawEntrata1, dati.RawUscita1, dati.RawEntrata2, dati.RawUscita2,
            dati.NumIngressi, dati.NumUscite, grezzo: true);

        // Giornata ancora aperta: si mostra quel che c'è, senza calcolare nulla.
        if (work_date.Date == oggi.Date && dati.NumIngressi <= 1 && dati.NumUscite <= 1)
        {
            cartellino.Note = "Giornata in corso";
            return cartellino;
        }

        RiempiStadio(cartellino, dati.Entrata1, dati.Uscita1, dati.Entrata2, dati.Uscita2,
            dati.NumIngressi, dati.NumUscite, grezzo: false);

        int minutiLavorati = ProcessDay(cartellino, dati);
        if (minutiLavorati < 0) return cartellino;   // ramo d'errore: ha già scritto tutto

        if (minutiLavorati > 0)
            ScomponiOvertime(cartellino, minutiLavorati, work_date, config.CountsOvertime);
        else
            ClearTotals(cartellino);

        return cartellino;
    }

    // ── ASSEGNAZIONE ──────────────────────────────────────────────────────────

    /// <summary>
    /// Due stadi, come nell'originale. Primo: via i doppioni di strisciata — una timbratura
    /// a meno di 5 minuti dalla riga precedente si scarta (nel VB lo faceva la CTE SQL di
    /// <c>ReportProcessor.vb</c> PRIMA del motore: senza questo stadio un doppione può fare
    /// da ponte nel raggruppamento a 30' e inghiottire una timbratura vera). Secondo:
    /// raggruppa le ravvicinate (meno di 30 minuti = stesso gesto ripetuto, tiene la prima)
    /// e assegna quelle rimaste ai posti del cartellino.
    /// </summary>
    private static Assignment Assign(List<RawPunch> timbrature)
    {
        // Stadio 1 — semantica LAG: il gap si misura dalla riga precedente (anche se
        // scartata), e si tronca ai minuti interi come il CAST AS INTEGER del VB.
        var pulite = new List<RawPunch> { timbrature[0] };
        for (int i = 1; i < timbrature.Count; i++)
        {
            int gap = (int)(timbrature[i].PunchedAt - timbrature[i - 1].PunchedAt).TotalMinutes;
            if (gap >= TimesheetRules.DuplicatePunchFilterMinutes)
                pulite.Add(timbrature[i]);
        }

        // Stadio 2 — raggruppamento a 30 minuti.
        var filtrate = new List<RawPunch>();
        RawPunch inizioGruppo = pulite[0];
        for (int i = 1; i < pulite.Count; i++)
        {
            double gap = (pulite[i].PunchedAt - pulite[i - 1].PunchedAt).TotalMinutes;
            if (gap >= 30)
            {
                filtrate.Add(inizioGruppo);
                inizioGruppo = pulite[i];
            }
        }
        filtrate.Add(inizioGruppo);

        var dati = new Assignment();
        switch (filtrate.Count)
        {
            case 1:
                Posiziona(dati, filtrate[0], null, null, null, numIngressi: 1, numUscite: 0);
                break;

            case 2:
                Posiziona(dati, filtrate[0], filtrate[1], null, null, numIngressi: 1, numUscite: 1);
                break;

            case 3:
                AssignThree(filtrate, dati);
                break;

            default:
                Posiziona(dati, filtrate[0], filtrate[1], filtrate[2], filtrate[3],
                    numIngressi: 2, numUscite: 2);
                break;
        }
        return dati;
    }

    /// <summary>
    /// Mette le timbrature ai quattro posti del cartellino, tenendo accanto all'orario
    /// arrotondato quello grezzo da cui viene (serve solo a mostrarli affiancati: nessun
    /// calcolo guarda il grezzo).
    /// </summary>
    private static void Posiziona(
        Assignment dati,
        RawPunch? entrata1, RawPunch? uscita1, RawPunch? entrata2, RawPunch? uscita2,
        int numIngressi, int numUscite)
    {
        dati.Entrata1 = entrata1 is null ? null : Norm(entrata1);
        dati.Uscita1 = uscita1 is null ? null : Norm(uscita1);
        dati.Entrata2 = entrata2 is null ? null : Norm(entrata2);
        dati.Uscita2 = uscita2 is null ? null : Norm(uscita2);

        dati.RawEntrata1 = entrata1?.PunchedAt;
        dati.RawUscita1 = uscita1?.PunchedAt;
        dati.RawEntrata2 = entrata2?.PunchedAt;
        dati.RawUscita2 = uscita2?.PunchedAt;

        dati.NumIngressi = numIngressi;
        dati.NumUscite = numUscite;
    }

    /// <summary>
    /// Tre timbrature: manca sempre qualcosa, e quale sia lo si deduce dagli orari.
    /// Le soglie (15, 12, 11, 90 min, 180 min) vengono dal motore originale: non toccarle
    /// senza rimisurare il banco di prova.
    /// </summary>
    private static void AssignThree(List<RawPunch> t, Assignment dati)
    {
        DateTime t1 = Norm(t[0]), t2 = Norm(t[1]), t3 = Norm(t[2]);
        double gap12 = (t2 - t1).TotalMinutes;
        double gap23 = (t3 - t2).TotalMinutes;

        // Mattina + due timbrature nel pomeriggio: turno unico, la centrale è di troppo.
        if (t2.Hour >= 15 && t3.Hour >= 15 && t1.Hour < 12)
        {
            Posiziona(dati, t[0], t[2], null, null, numIngressi: 1, numUscite: 1);
            return;
        }

        if (t3.Hour < 15)
        {
            Posiziona(dati, t[0], t[1], t[2], null, numIngressi: 2, numUscite: 1);
        }
        else if (gap23 > 90)
        {
            Posiziona(dati, t[0], t[1], null, t[2], numIngressi: 1, numUscite: 2);
        }
        else if (t2.Hour >= 12 && t2.Hour <= 14 && gap12 > 180)
        {
            Posiziona(dati, t[0], null, t[1], t[2], numIngressi: 2, numUscite: 1);
        }
        else if (t1.Hour >= 11)
        {
            Posiziona(dati, null, t[0], t[1], t[2], numIngressi: 1, numUscite: 2);
        }
        else
        {
            Posiziona(dati, t[0], t[1], t[2], null, numIngressi: 2, numUscite: 1);
        }
    }

    /// <summary>
    /// Scrive uno dei due stadi che precedono il risultato. Orari «--:--» dove non c'è
    /// niente, pausa = stacco fra prima uscita e seconda entrata, totale = somma delle
    /// sessioni davvero chiuse — le formule di <c>CalcGap</c> e <c>CalcMinuti</c> del VB.
    /// </summary>
    private static void RiempiStadio(
        TimesheetDay c,
        DateTime? entrata1, DateTime? uscita1, DateTime? entrata2, DateTime? uscita2,
        int numIngressi, int numUscite, bool grezzo)
    {
        int minuti = 0;
        if (numIngressi >= 1 && numUscite >= 1 && entrata1.HasValue && uscita1.HasValue)
            minuti += (int)(uscita1.Value - entrata1.Value).TotalMinutes;
        if (numIngressi >= 2 && numUscite >= 2 && entrata2.HasValue && uscita2.HasValue)
            minuti += (int)(uscita2.Value - entrata2.Value).TotalMinutes;
        minuti = Math.Max(0, minuti);

        int pausa = uscita1.HasValue && entrata2.HasValue
            ? (int)Math.Max(0, (entrata2.Value - uscita1.Value).TotalMinutes)
            : 0;

        if (grezzo)
        {
            c.RawEntrata1 = TimesheetRules.FormatClock(entrata1);
            c.RawUscita1 = TimesheetRules.FormatClock(uscita1);
            c.RawEntrata2 = TimesheetRules.FormatClock(entrata2);
            c.RawUscita2 = TimesheetRules.FormatClock(uscita2);
            c.RawTotal = TimesheetRules.FormatDuration(minuti);
            c.RawBreak = TimesheetRules.FormatDuration(pausa);
        }
        else
        {
            c.NormEntrata1 = TimesheetRules.FormatClock(entrata1);
            c.NormUscita1 = TimesheetRules.FormatClock(uscita1);
            c.NormEntrata2 = TimesheetRules.FormatClock(entrata2);
            c.NormUscita2 = TimesheetRules.FormatClock(uscita2);
            c.NormTotal = TimesheetRules.FormatDuration(minuti);
            c.NormBreak = TimesheetRules.FormatDuration(pausa);
        }
    }

    private static DateTime Norm(RawPunch t)
    {
        string direction = (t.Direction ?? "").ToUpperInvariant();
        bool eIngresso = direction.Contains("IN") || direction.Contains("ENTR");
        return TimesheetRules.RoundTime(t.PunchedAt, eIngresso);
    }

    // ── TURNI ─────────────────────────────────────────────────────────────────

    /// <summary>Riconosce il turno e riempie il cartellino. Torna i minuti lavorati, o -1 se non calcolabile.</summary>
    private static int ProcessDay(TimesheetDay c, Assignment d)
    {
        if (d.NumIngressi >= 2 && d.NumUscite >= 2 && d.Entrata1.HasValue && d.Uscita1.HasValue
            && d.Entrata2.HasValue && d.Uscita2.HasValue)
            return TurnoRegolare(c, d);

        if (d.NumIngressi == 1 && d.NumUscite == 2 && d.Entrata1.HasValue && d.Uscita1.HasValue)
            return TurnoEntrataMancante(c, d);

        if (d.NumIngressi == 1 && d.NumUscite == 1 && d.Entrata1.HasValue && d.Uscita1.HasValue)
            return TurnoUnico(c, d);

        if (d.NumIngressi == 2 && d.NumUscite == 1 && d.Entrata1.HasValue && d.Uscita1.HasValue
            && d.Entrata2.HasValue)
            return TurnoUscitaMancante(c, d);

        if (d.NumIngressi == 1 && d.NumUscite == 0 && d.Entrata1.HasValue)
        {
            // Manca l'uscita: si mostra l'entrata e si segnala. Zero minuti lavorati,
            // quindi i totali finiscono azzerati (non «---»: qui la giornata è note, è
            // solo incompleta — nel motore originale questo ramo prosegue apposta).
            c.Note = "⚠ INCOMPLETO: Solo entrata";
            c.Entrata1 = TimesheetRules.FormatClock(d.Entrata1);
            c.Uscita1 = "??:??";
            c.BreakTime = "0h 0m";
            return 0;
        }

        c.Note = "⚠ ERR: Verificare timbrature";
        c.RegularHours = "---";
        c.Overtime = "---";
        foreach (string f in c.Fasce.Keys.ToList()) c.Fasce[f] = "---";
        return -1;
    }

    /// <summary>Quattro timbrature: la pausa è quella vera, salvo che sia assente o troppo corta.</summary>
    private static int TurnoRegolare(TimesheetDay c, Assignment d)
    {
        DateTime e1 = d.Entrata1!.Value, u1 = d.Uscita1!.Value;
        DateTime e2 = d.Entrata2!.Value, u2 = d.Uscita2!.Value;
        int pausa = (int)Math.Max(0, (e2 - u1).TotalMinutes);
        c.BreakTime = TimesheetRules.FormatDuration(pausa);

        if (pausa == 0)
        {
            // Nessuno stacco: si impone la pausa canonica 12:30-13:30.
            var fineMattino = new DateTime(e1.Year, e1.Month, e1.Day, 12, 30, 0);
            var ripresa = new DateTime(e1.Year, e1.Month, e1.Day, 13, 30, 0);
            c.Entrata1 = e1.ToString("HH:mm");
            c.Uscita1 = "12:30*";
            c.Entrata2 = "13:30*";
            c.Uscita2 = u2.ToString("HH:mm");
            c.BreakTime = "1h 0m";
            c.Note = "AUTO_P: Pausa 1h forzata";
            return (int)(fineMattino - e1).TotalMinutes + (int)(u2 - ripresa).TotalMinutes;
        }

        c.Entrata1 = e1.ToString("HH:mm");
        c.Uscita1 = u1.ToString("HH:mm");
        c.Entrata2 = e2.ToString("HH:mm");
        c.Uscita2 = u2.ToString("HH:mm");

        int lavorati = (int)(u1 - e1).TotalMinutes + (int)(u2 - e2).TotalMinutes;
        if (pausa < TimesheetRules.MinimumBreakMinutes)
        {
            // BreakTime più corta del minimo: il tempo mancante si considera recuperato.
            c.Note = "Recupero pausa pranzo";
            return lavorati + (TimesheetRules.MinimumBreakMinutes - pausa);
        }

        c.Note = "OK";
        return lavorati;
    }

    /// <summary>Una entrata e una uscita: mattino, pomeriggio o giornata intera con pausa dedotta.</summary>
    private static int TurnoUnico(TimesheetDay c, Assignment d)
    {
        DateTime entrata = d.Entrata1!.Value, uscita = d.Uscita1!.Value;
        int minutiTotali = (int)(uscita - entrata).TotalMinutes;

        if (entrata.Hour >= 12)
        {
            c.Entrata1 = entrata.ToString("HH:mm");
            c.Uscita1 = uscita.ToString("HH:mm");
            c.BreakTime = "0h 0m";
            c.Note = "Turno pomeridiano";
            return minutiTotali;
        }

        if (uscita.Hour < 13 || (uscita.Hour == 13 && uscita.Minute == 0))
        {
            c.Entrata1 = entrata.ToString("HH:mm");
            c.Uscita1 = uscita.ToString("HH:mm");
            c.BreakTime = "0h 0m";
            c.Note = "Turno mattutino";
            return minutiTotali;
        }

        // Giornata a cavallo del pranzo senza stacco timbrato: si deduce l'ora canonica.
        c.Entrata1 = entrata.ToString("HH:mm");
        c.Uscita1 = "12:30*";
        c.Entrata2 = "13:30*";
        c.Uscita2 = uscita.ToString("HH:mm");
        c.BreakTime = "1h 0m";
        c.Note = "AUTO_P: Pausa 1h detratta";
        return minutiTotali - TimesheetRules.ForcedBreakMinutes;
    }

    /// <summary>Una entrata e due uscite: manca il rientro dalla pausa.</summary>
    private static int TurnoEntrataMancante(TimesheetDay c, Assignment d)
    {
        DateTime e1 = d.Entrata1!.Value, u1 = d.Uscita1!.Value;
        DateTime u2 = d.Uscita2 ?? u1;
        int gapUscite = (int)(u2 - u1).TotalMinutes;

        if (gapUscite < 90)
        {
            // Le due uscite sono vicine: la seconda è un doppione, vale il mattino.
            c.Entrata1 = e1.ToString("HH:mm");
            c.Uscita1 = u1.ToString("HH:mm");
            c.BreakTime = "0h 0m";
            c.Note = "Turno mattutino (seconda uscita ignorata)";
            return (int)(u1 - e1).TotalMinutes;
        }

        DateTime ripresa = u1.AddMinutes(TimesheetRules.ForcedBreakMinutes);
        c.Entrata1 = e1.ToString("HH:mm");
        c.Uscita1 = u1.ToString("HH:mm") + "*";
        c.Entrata2 = ripresa.ToString("HH:mm") + "*";
        c.Uscita2 = u2.ToString("HH:mm");
        c.BreakTime = TimesheetRules.FormatDuration(TimesheetRules.ForcedBreakMinutes);
        c.Note = "AUTO_P: Pausa implicita (1 IN / 2 OUT)";
        return (int)(u1 - e1).TotalMinutes + (int)(u2 - ripresa).TotalMinutes;
    }

    /// <summary>Due entrate e una uscita: manca l'uscita finale, si stima alle 17:00.</summary>
    private static int TurnoUscitaMancante(TimesheetDay c, Assignment d)
    {
        DateTime e1 = d.Entrata1!.Value, u1 = d.Uscita1!.Value, e2 = d.Entrata2!.Value;
        int pausa = (int)Math.Max(0, (e2 - u1).TotalMinutes);
        c.BreakTime = TimesheetRules.FormatDuration(pausa);

        var uscitaStimata = new DateTime(e2.Year, e2.Month, e2.Day, 17, 0, 0);
        c.Entrata1 = e1.ToString("HH:mm");
        c.Uscita1 = u1.ToString("HH:mm");
        c.Entrata2 = e2.ToString("HH:mm");
        c.Uscita2 = uscitaStimata.ToString("HH:mm") + "*";
        c.Note = "AUTO_P: Uscita mancante - Stimata 17:00";
        return (int)(u1 - e1).TotalMinutes + (int)(uscitaStimata - e2).TotalMinutes;
    }

    // ── STRAORDINARIO ─────────────────────────────────────────────────────────

    /// <summary>
    /// Divide i minuti lavorati fra ordinario e straordinario, e lo straordinario fra le
    /// fasce della circolare. Feriale, sabato e festivo hanno regole diverse.
    /// </summary>
    private static void ScomponiOvertime(TimesheetDay c, int minutiLavorati, DateTime work_date, bool conStraordinari)
    {
        bool festivo = TimesheetRules.IsHoliday(work_date);
        bool sabato = work_date.DayOfWeek == DayOfWeek.Saturday;

        int minutiNotturni = MinutiNotturni(c);
        int minutiDiurni = minutiLavorati - minutiNotturni;

        if (festivo) FasceFestivo(c, minutiLavorati, minutiNotturni, minutiDiurni);
        else if (sabato) FasceSabato(c, minutiLavorati, minutiNotturni, minutiDiurni);
        else FasceFeriale(c, minutiLavorati, minutiNotturni);

        int ordinari, straordinari;
        if (festivo || sabato)
        {
            ordinari = 0;
            straordinari = minutiLavorati;
        }
        else
        {
            ordinari = Math.Min(minutiLavorati, TimesheetRules.StandardDayMinutes);
            straordinari = Math.Max(0, minutiLavorati - TimesheetRules.StandardDayMinutes);
        }

        c.RegularHours = TimesheetRules.FormatDuration(ordinari);
        c.Overtime = TimesheetRules.FormatDuration(straordinari);

        // Chi non fa straordinario: le ore restano, la maggiorazione no.
        if (!conStraordinari)
        {
            c.Overtime = "0h 0m";
            foreach (string f in c.Fasce.Keys.ToList()) c.Fasce[f] = "0h 0m";
        }
    }

    /// <summary>Feriale: oltre le 8 ore è straordinario, notturno (g) prima di diurno (a).</summary>
    private static void FasceFeriale(TimesheetDay c, int minutiTotali, int minutiNotturni)
    {
        int straordinario = Math.Max(0, minutiTotali - TimesheetRules.StandardDayMinutes);
        if (straordinario == 0) return;

        if (minutiNotturni > 0)
        {
            int notturnoStraord = Math.Min(minutiNotturni, straordinario);
            c.Fasce["G"] = TimesheetRules.FormatDuration(notturnoStraord);
            int residuo = straordinario - notturnoStraord;
            if (residuo > 0) c.Fasce["A"] = TimesheetRules.FormatDuration(residuo);
        }
        else
        {
            c.Fasce["A"] = TimesheetRules.FormatDuration(straordinario);
        }
    }

    /// <summary>Sabato: non è giornata ordinaria, quindi è tutto straordinario.</summary>
    private static void FasceSabato(TimesheetDay c, int minutiTotali, int minutiNotturni, int minutiDiurni)
    {
        if (minutiNotturni > 0)
        {
            c.Fasce["G"] = TimesheetRules.FormatDuration(minutiNotturni);
            if (minutiDiurni > 0) c.Fasce["A"] = TimesheetRules.FormatDuration(minutiDiurni);
        }
        else
        {
            c.Fasce["A"] = TimesheetRules.FormatDuration(minutiTotali);
        }
    }

    /// <summary>Festivo: entro le 8 ore fascia c (o d col riposo compensativo), oltre e/f; notturno h/l/m.</summary>
    private static void FasceFestivo(TimesheetDay c, int minutiTotali, int minutiNotturni, int minutiDiurni,
                                     bool riposoCompensativo = false)
    {
        int entro8h = Math.Min(minutiTotali, TimesheetRules.StandardDayMinutes);
        int oltre8h = Math.Max(0, minutiTotali - TimesheetRules.StandardDayMinutes);

        if (minutiNotturni > 0)
        {
            int notturnoEntro8h = Math.Min(minutiNotturni, entro8h);
            int notturnoOltre8h = Math.Min(Math.Max(0, minutiNotturni - entro8h), oltre8h);

            if (notturnoEntro8h > 0) c.Fasce["H"] = TimesheetRules.FormatDuration(notturnoEntro8h);
            if (notturnoOltre8h > 0)
                c.Fasce[riposoCompensativo ? "M" : "L"] = TimesheetRules.FormatDuration(notturnoOltre8h);

            int diurnoEntro8h = Math.Max(0, entro8h - notturnoEntro8h);
            int diurnoOltre8h = Math.Max(0, oltre8h - notturnoOltre8h);
            if (diurnoEntro8h > 0)
                c.Fasce[riposoCompensativo ? "D" : "C"] = TimesheetRules.FormatDuration(diurnoEntro8h);
            if (diurnoOltre8h > 0)
                c.Fasce[riposoCompensativo ? "F" : "E"] = TimesheetRules.FormatDuration(diurnoOltre8h);
        }
        else
        {
            if (entro8h > 0) c.Fasce[riposoCompensativo ? "D" : "C"] = TimesheetRules.FormatDuration(entro8h);
            if (oltre8h > 0) c.Fasce[riposoCompensativo ? "F" : "E"] = TimesheetRules.FormatDuration(oltre8h);
        }
    }

    /// <summary>
    /// Minuti lavorati dopo le 22:00, letti dagli orari già scritti sul cartellino
    /// (asterischi degli orari stimati compresi).
    /// </summary>
    private static int MinutiNotturni(TimesheetDay c) =>
        NotturniDi(c.Entrata1, c.Uscita1) + NotturniDi(c.Entrata2, c.Uscita2);

    private static int NotturniDi(string entrata, string uscita)
    {
        if (!ProvaOrario(entrata, out int daMinuti)) return 0;
        if (!ProvaOrario(uscita, out int aMinuti)) return 0;

        int soglia = TimesheetRules.NightShiftStartHour * 60;
        if (aMinuti <= soglia) return 0;
        return aMinuti - Math.Max(daMinuti, soglia);
    }

    private static bool ProvaOrario(string valore, out int minutiDaMezzanotte)
    {
        minutiDaMezzanotte = 0;
        if (string.IsNullOrEmpty(valore) || valore is "--:--" or "??:??") return false;

        string pulito = valore.Replace("*", "");
        string[] parti = pulito.Split(':');
        if (parti.Length != 2) return false;
        if (!int.TryParse(parti[0], out int ore) || !int.TryParse(parti[1], out int minuti)) return false;

        minutiDaMezzanotte = ore * 60 + minuti;
        return true;
    }

    private static void ClearTotals(TimesheetDay c)
    {
        c.RegularHours = "0h 0m";
        c.Overtime = "0h 0m";
        foreach (string f in c.Fasce.Keys.ToList()) c.Fasce[f] = "0h 0m";
    }
}
