namespace ATEC.PM.Server.Services.Hr;

/// <summary>Una timbratura come arriva dal rilevatore: punched_at e direction, niente di elaborato.</summary>
/// <param name="Orario">Istante grezzo, mai modificato.</param>
/// <param name="Verso">Verso dichiarato dal terminale (IN/OUT, ENTRATA/USCITA...).</param>
/// <param name="ExternalId">Identificativo del rilevatore, per risalire alla timbratura originale.</param>
/// <param name="Synthetic">
/// true = non l'ha strisciata nessuno, l'ha messa il sistema. Oggi capita solo alla
/// mezzanotte che taglia un turno di notte (<see cref="NightShift"/>): in
/// <c>hr_punches</c> non entra mai.
/// </param>
public record RawPunch(DateTime PunchedAt, string Direction, long? ExternalId = null, bool Synthetic = false);

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

        /// <summary>Una terza sessione è rimasta fuori dalle quattro caselle: ore perse.</summary>
        public bool NotteFuoriPosto;

        /// <summary>Entrate e uscite della notte non si accoppiano: manca una timbratura.</summary>
        public bool NotteSpaiata;

        /// <summary>Minuti di pausa timbrati DENTRO il turno di notte, già arrotondati.</summary>
        public int PauseTimbrate;

        /// <summary>Quanti di quei minuti di pausa cadono in fascia notturna.</summary>
        public int PauseNotturne;

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
    /// <param name="night">
    /// Le timbrature dei giorni confinanti, per riconoscere un turno a cavallo della
    /// mezzanotte. Omettendole il motore si comporta esattamente come prima.
    /// </param>
    public static TimesheetDay Calcola(
        DateTime work_date,
        IEnumerable<RawPunch> timbrature,
        DateTime oggi,
        EmployeeConfig? config = null,
        NightContext? night = null)
    {
        config ??= new EmployeeConfig();
        var cartellino = new TimesheetDay { WorkDate = work_date.Date };

        var ordinate = timbrature.OrderBy(t => t.PunchedAt).ToList();
        if (ordinate.Count == 0)
        {
            cartellino.Note = "";
            return cartellino;
        }

        // Il terzo turno vale per il giorno in cui comincia: prima di ogni conto si
        // ricompone il turno di notte — si prendono le ore di domattina che lo chiudono e
        // si lasciano a ieri quelle che chiudono il turno di ieri.
        List<RawPunch> delTurno = NightShift.Compose(work_date.Date, ordinate, night, out NightSplit notte);

        if (delTurno.Count == 0)
        {
            // Oggi c'è stata solo la coda del turno di ieri: le ore le conta ieri, e questa
            // giornata lo dice invece di restare muta a zero.
            cartellino.Note = $"{NightShift.NoteMarker} Notte: le ore di stanotte contano sul giorno prima";
            return cartellino;
        }

        Assignment dati = Assign(delTurno, work_date.Date, notte);

        // I tre stadi si mostrano affiancati: il grezzo c'è sempre, il normalizzato solo
        // quando la giornata è chiusa — su una giornata in corso non c'è niente da
        // arrotondare, ed è così anche nell'originale.
        RiempiStadio(cartellino, dati.RawEntrata1, dati.RawUscita1, dati.RawEntrata2, dati.RawUscita2,
            dati.NumIngressi, dati.NumUscite, grezzo: true);

        // Giornata ancora aperta: si mostra quel che c'è, senza calcolare nulla.
        if (work_date.Date == oggi.Date && dati.NumIngressi <= 1 && dati.NumUscite <= 1)
        {
            cartellino.Note = "Giornata in corso";
            return AnnotaNotte(cartellino, notte, dati);
        }

        RiempiStadio(cartellino, dati.Entrata1, dati.Uscita1, dati.Entrata2, dati.Uscita2,
            dati.NumIngressi, dati.NumUscite, grezzo: false);

        int minutiLavorati = ProcessDay(cartellino, dati);
        if (minutiLavorati < 0) return AnnotaNotte(cartellino, notte, dati);   // ramo d'errore: ha già scritto tutto

        // 🪤 Le pause timbrate dentro il turno si tolgono QUI, una volta sola e per tutti i
        // rami: dentro un ramo solo, basterebbe una sessione annullata dall'arrotondamento
        // per farle sparire — e le ore di pausa finirebbero pagate.
        if (dati.PauseTimbrate > 0)
        {
            minutiLavorati = Math.Max(0, minutiLavorati - dati.PauseTimbrate);
            cartellino.BreakTime = TimesheetRules.FormatDuration(dati.PauseTimbrate);
        }

        if (minutiLavorati > 0)
            ScomponiOvertime(cartellino, minutiLavorati, work_date, config.CountsOvertime, dati.PauseNotturne);
        else
            ClearTotals(cartellino);

        return AnnotaNotte(cartellino, notte, dati);
    }

    /// <summary>
    /// Dice in coda alla nota che la giornata è mezzo turno di notte. In <b>coda</b> e non
    /// in testa: <see cref="TimesheetDay.HasAnomaly"/> guarda il primo carattere, e una
    /// giornata storta deve restare storta anche se contiene una notte.
    /// </summary>
    private static TimesheetDay AnnotaNotte(TimesheetDay c, NightSplit notte, Assignment dati)
    {
        if (notte == NightSplit.None) return c;

        // 🪤 «Giornata in corso» non è solo una scritta: è la CHIAVE con cui l'import
        // ripesca le giornate rimaste indietro (`WHERE note = 'Giornata in corso'`).
        // Appendendoci qualcosa, quelle giornate non le ritroverebbe più nessuno e
        // resterebbero congelate a zero ore.
        if (c.Note == "Giornata in corso") return c;

        // L'avviso va in TESTA col suo ⚠ — così `HasAnomaly` lo vede e la giornata si
        // segnala — e dice «INCOMPLETO» perché è la parola con cui il sollecito riconosce
        // una giornata da far verificare alla persona.
        string? avviso =
            dati.NotteFuoriPosto ? "⚠ INCOMPLETO: due turni nella stessa giornata, da verificare"
            : dati.NotteSpaiata ? "⚠ INCOMPLETO: manca una timbratura della notte"
            : null;

        var pezzi = new List<string>(4);

        // Se il motore ha già segnalato di suo («⚠ INCOMPLETO: Solo entrata») non si
        // raddoppia l'avviso.
        bool conAvviso = avviso is not null && !c.Note.StartsWith('⚠');
        if (conAvviso) pezzi.Add(avviso!);
        // Con un avviso in testa, il laconico «OK» del motore non aggiunge niente.
        if (!string.IsNullOrWhiteSpace(c.Note) && !(conAvviso && c.Note == "OK")) pezzi.Add(c.Note);

        if (notte.HasFlag(NightSplit.RunsPastMidnight))
            pezzi.Add($"{NightShift.NoteMarker} Notte: il turno finisce domattina");
        if (notte.HasFlag(NightSplit.HandedToPreviousDay))
            pezzi.Add($"{NightShift.NoteMarker} Notte: le prime ore contano sul giorno prima");

        c.Note = string.Join(" · ", pezzi);
        return c;
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
    private static Assignment Assign(List<RawPunch> timbrature, DateTime workDate, NightSplit notte)
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

        // Il taglio di mezzanotte si aggiunge DOPO i due stadi, e per una ragione precisa:
        // non è una strisciata, e passando davanti ai filtri si comporterebbe come tale.
        // Una notte che comincia alle 23:45 avrebbe la sua chiusura a un quarto d'ora di
        // distanza — meno di 30 minuti, «stesso gesto ripetuto» — e il raggruppamento si
        // mangerebbe una delle due. In coda ai filtri, invece, le caselle le trova libere.
        var dati = new Assignment();

        if (notte.HasFlag(NightSplit.RunsPastMidnight))
        {
            filtrate = NightShift.SplitAtMidnight(workDate, filtrate);

            // 🪤 Con la mezzanotte in mezzo le euristiche qui sotto NON valgono più: sono
            // tarate sui turni diurni («la terza dopo le 15», «la prima prima delle 12») e
            // un'uscita alle 24:00 è un DateTime del giorno dopo, quindi `.Hour` vale 0 e
            // passa per primo mattino. Da lì uscivano giornate da ventotto ore. Sul turno
            // di notte si assegna per VERSO, che è affidabile: è il verso che l'ha fatto
            // riconoscere.
            AssignPaired(filtrate, workDate, dati);
            return dati;
        }

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

                // 🪤 Il cartellino ha quattro caselle: dalla quinta in poi il motore
                // originale butta via, ed è un comportamento che non si tocca. Ma se a
                // restare fuori è la mezzanotte di un turno di notte, quel che si butta
                // sono ORE lavorate — e in silenzio, con la nota che dichiara pure una
                // notte regolare. Meglio dirlo.
                break;
        }
        return dati;
    }

    /// <summary>
    /// Assegnazione a COPPIE: ogni entrata con la sua uscita, nell'ordine in cui sono
    /// arrivate. La usa solo la giornata che contiene un moncone di notte.
    ///
    /// <para>Quello che non torna si dice invece di indovinarlo: due entrate di fila,
    /// un'uscita senza entrata, un turno ancora aperto o una terza coppia che nelle quattro
    /// caselle non ci sta. Il motore originale, in quei casi, tirava a stimare — e con una
    /// mezzanotte in mezzo tirava malissimo.</para>
    /// </summary>
    private static void AssignPaired(List<RawPunch> timbrature, DateTime workDate, Assignment dati)
    {
        var coppie = new List<(RawPunch Entrata, RawPunch Uscita)>();
        RawPunch? aperta = null;
        bool spaiata = false;

        foreach (RawPunch t in timbrature)
        {
            if (NightShift.IsEntry(t.Direction))
            {
                if (aperta is not null) spaiata = true;   // due entrate di fila: manca un'uscita
                aperta = t;
            }
            else if (NightShift.IsExit(t.Direction))
            {
                if (aperta is null) { spaiata = true; continue; }
                coppie.Add((aperta, t));
                aperta = null;
            }
            else
            {
                spaiata = true;   // verso che non si riconosce
            }
        }

        if (aperta is not null) spaiata = true;   // turno rimasto aperto
        dati.NotteSpaiata = spaiata;

        if (coppie.Count == 0)
        {
            Posiziona(dati, timbrature[0], null, null, null, numIngressi: 1, numUscite: 0);
            return;
        }

        // 🪤 Dentro un turno di notte si timbra anche la pausa, e con la mezzanotte in mezzo
        // le sessioni diventano tre — per quattro caselle. Non se ne butta via nessuna: le
        // sessioni si comprimono in due (di qua e di là dalla mezzanotte) e le pause
        // timbrate finiscono dove devono stare, nei minuti di pausa.
        DateTime mezzanotte = workDate.Date.AddDays(1);
        var primaDiMezzanotte = coppie.Where(c => Norm(c.Uscita) <= mezzanotte).ToList();
        var dopoMezzanotte = coppie.Where(c => Norm(c.Uscita) > mezzanotte).ToList();

        var sessione1 = Comprimi(primaDiMezzanotte, out int pause1, out int notte1, out bool staccoLungo1);
        var sessione2 = Comprimi(dopoMezzanotte, out int pause2, out int notte2, out bool staccoLungo2);
        dati.PauseTimbrate = pause1 + pause2;
        dati.PauseNotturne = notte1 + notte2;

        // Uno stacco troppo lungo per essere una pausa: nella stessa giornata ci sono due
        // turni distinti. Le ore si contano lo stesso, ma la giornata va guardata.
        dati.NotteFuoriPosto = staccoLungo1 || staccoLungo2;

        // 🪤 Una sessione che l'arrotondamento riduce a durata zero (entrata alle 23:50 →
        // 24:00) non è lavoro: posizionata, resterebbe una casella «00:00 · 24:00» che al
        // conteggio delle ore notturne vale otto ore.
        if (sessione1 is not null && Norm(sessione1.Value.Entrata) >= Norm(sessione1.Value.Uscita))
            sessione1 = null;
        if (sessione2 is not null && Norm(sessione2.Value.Entrata) >= Norm(sessione2.Value.Uscita))
            sessione2 = null;

        if (sessione1 is null && sessione2 is null)
        {
            Posiziona(dati, coppie[0].Entrata, null, null, null, numIngressi: 1, numUscite: 0);
            return;
        }

        if (sessione1 is null || sessione2 is null)
        {
            // Tutte le sessioni stanno dalla stessa parte della mezzanotte.
            var unica = sessione1 ?? sessione2!.Value;
            Posiziona(dati, unica.Entrata, unica.Uscita, null, null, numIngressi: 1, numUscite: 1);
            return;
        }

        Posiziona(dati, sessione1.Value.Entrata, sessione1.Value.Uscita,
            sessione2.Value.Entrata, sessione2.Value.Uscita, numIngressi: 2, numUscite: 2);
    }

    /// <summary>
    /// Riduce più sessioni consecutive a una sola — dalla prima entrata all'ultima uscita —
    /// e restituisce a parte i minuti di pausa che stanno in mezzo.
    /// </summary>
    private static (RawPunch Entrata, RawPunch Uscita)? Comprimi(
        List<(RawPunch Entrata, RawPunch Uscita)> sessioni,
        out int pause, out int pauseNotturne, out bool staccoLungo)
    {
        pause = 0;
        pauseNotturne = 0;
        staccoLungo = false;
        if (sessioni.Count == 0) return null;

        // Le pause si misurano sugli orari arrotondati, come le ore che ne vengono tolte.
        for (int i = 1; i < sessioni.Count; i++)
        {
            DateTime da = Norm(sessioni[i - 1].Uscita), a = Norm(sessioni[i].Entrata);
            int stacco = (int)Math.Max(0, (a - da).TotalMinutes);

            pause += stacco;
            if (stacco > TimesheetRules.NightShiftMaxBreakMinutes) staccoLungo = true;

            // 🪤 La pausa va tolta anche dalle ore di NOTTE, non solo dal totale: la
            // maggiorazione si paga sulle ore lavorate, e una pausa alle due è comunque
            // notturna. Senza, di sabato la fascia arrivava a valere più delle ore pagate.
            pauseNotturne += NotturniFra(da.Hour * 60 + da.Minute, a.Hour * 60 + a.Minute);
        }

        return (sessioni[0].Entrata, sessioni[^1].Uscita);
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
            c.RawEntrata1 = ClockIn(entrata1, c.WorkDate);
            c.RawUscita1 = ClockOut(uscita1, c.WorkDate);
            c.RawEntrata2 = ClockIn(entrata2, c.WorkDate);
            c.RawUscita2 = ClockOut(uscita2, c.WorkDate);
            c.RawTotal = TimesheetRules.FormatDuration(minuti);
            c.RawBreak = TimesheetRules.FormatDuration(pausa);
        }
        else
        {
            c.NormEntrata1 = ClockIn(entrata1, c.WorkDate);
            c.NormUscita1 = ClockOut(uscita1, c.WorkDate);
            c.NormEntrata2 = ClockIn(entrata2, c.WorkDate);
            c.NormUscita2 = ClockOut(uscita2, c.WorkDate);
            c.NormTotal = TimesheetRules.FormatDuration(minuti);
            c.NormBreak = TimesheetRules.FormatDuration(pausa);
        }
    }

    private static DateTime Norm(RawPunch t) =>
        TimesheetRules.RoundTime(t.PunchedAt, NightShift.IsEntry(t.Direction));

    /// <summary>
    /// L'orario di un'ENTRATA, «07:30» come <see cref="TimesheetRules.FormatClock"/>.
    /// La mezzanotte, per un'entrata, è l'inizio: <b>00:00</b>.
    /// </summary>
    private static string ClockIn(DateTime? valore, DateTime workDate) =>
        TimesheetRules.FormatClock(valore);

    /// <summary>
    /// L'orario di un'USCITA, con una differenza sola: la mezzanotte che CHIUDE la giornata
    /// si scrive <b>24:00</b> e non «00:00».
    ///
    /// <para>Non è un vezzo grafico. Un turno di notte tagliato a mezzanotte finisce
    /// all'istante 00:00 del giorno dopo — lo stesso istante in cui ricomincia — e le due
    /// caselle devono leggersi in modo diverso: «24:00» la fine, «00:00» la ripresa.
    /// Scritte tutte e due «00:00» sembrerebbero un errore, e il conteggio delle ore
    /// notturne, che legge questi orari come stringhe, misurerebbe zero.</para>
    /// </summary>
    private static string ClockOut(DateTime? valore, DateTime workDate) =>
        valore.HasValue && valore.Value == workDate.Date.AddDays(1)
            ? "24:00"
            : TimesheetRules.FormatClock(valore);

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
            c.Entrata1 = ClockIn(d.Entrata1, c.WorkDate);
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

        // Le due sessioni si toccano ALLA MEZZANOTTE: non sono due turni con una pausa in
        // mezzo, è UN turno solo tagliato in due. Guai a imporgli la pausa delle 12:30.
        // 🪤 La condizione sulla mezzanotte è indispensabile: senza, il ramo scatterebbe
        // anche su una giornata diurna in cui l'uscita di pranzo e il rientro coincidono —
        // e lì la pausa d'ufficio va imposta, altrimenti si regala un'ora.
        if (e2 == u1 && u1 == c.WorkDate.Date.AddDays(1))
        {
            c.Entrata1 = ClockIn(e1, c.WorkDate);
            c.Uscita1 = ClockOut(u1, c.WorkDate);
            c.Entrata2 = ClockIn(e2, c.WorkDate);
            c.Uscita2 = ClockOut(u2, c.WorkDate);
            c.Note = d.PauseTimbrate > 0 ? "Turno notturno (pausa timbrata)" : "Turno notturno";
            return (int)(u1 - e1).TotalMinutes + (int)(u2 - e2).TotalMinutes;
        }

        if (pausa == 0)
        {
            // Nessuno stacco: si impone la pausa canonica 12:30-13:30.
            var fineMattino = new DateTime(e1.Year, e1.Month, e1.Day, 12, 30, 0);
            var ripresa = new DateTime(e1.Year, e1.Month, e1.Day, 13, 30, 0);
            c.Entrata1 = ClockIn(e1, c.WorkDate);
            c.Uscita1 = "12:30*";
            c.Entrata2 = "13:30*";
            c.Uscita2 = ClockOut(u2, c.WorkDate);
            c.BreakTime = "1h 0m";
            c.Note = "AUTO_P: Pausa 1h forzata";
            return (int)(fineMattino - e1).TotalMinutes + (int)(u2 - ripresa).TotalMinutes;
        }

        c.Entrata1 = ClockIn(e1, c.WorkDate);
        c.Uscita1 = ClockOut(u1, c.WorkDate);
        c.Entrata2 = ClockIn(e2, c.WorkDate);
        c.Uscita2 = ClockOut(u2, c.WorkDate);

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
            c.Entrata1 = ClockIn(entrata, c.WorkDate);
            c.Uscita1 = ClockOut(uscita, c.WorkDate);
            c.BreakTime = "0h 0m";
            c.Note = "Turno pomeridiano";
            return minutiTotali;
        }

        if (uscita.Hour < 13 || (uscita.Hour == 13 && uscita.Minute == 0))
        {
            c.Entrata1 = ClockIn(entrata, c.WorkDate);
            c.Uscita1 = ClockOut(uscita, c.WorkDate);
            c.BreakTime = "0h 0m";
            c.Note = "Turno mattutino";
            return minutiTotali;
        }

        if (d.PauseTimbrate > 0)
        {
            // La pausa c'è ed è timbrata (la toglie chi ci chiama): non se ne deduce un'altra.
            c.Entrata1 = ClockIn(entrata, c.WorkDate);
            c.Uscita1 = ClockOut(uscita, c.WorkDate);
            c.Note = "Turno notturno (pausa timbrata)";
            return minutiTotali;
        }

        // Giornata a cavallo del pranzo senza stacco timbrato: si deduce l'ora canonica.
        c.Entrata1 = ClockIn(entrata, c.WorkDate);
        c.Uscita1 = "12:30*";
        c.Entrata2 = "13:30*";
        c.Uscita2 = ClockOut(uscita, c.WorkDate);
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
            c.Entrata1 = ClockIn(e1, c.WorkDate);
            c.Uscita1 = ClockOut(u1, c.WorkDate);
            c.BreakTime = "0h 0m";
            c.Note = "Turno mattutino (seconda uscita ignorata)";
            return (int)(u1 - e1).TotalMinutes;
        }

        DateTime ripresa = u1.AddMinutes(TimesheetRules.ForcedBreakMinutes);
        c.Entrata1 = ClockIn(e1, c.WorkDate);
        c.Uscita1 = ClockOut(u1, c.WorkDate) + "*";
        c.Entrata2 = ClockIn(ripresa, c.WorkDate) + "*";
        c.Uscita2 = ClockOut(u2, c.WorkDate);
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
        c.Entrata1 = ClockIn(e1, c.WorkDate);
        c.Uscita1 = ClockOut(u1, c.WorkDate);
        c.Entrata2 = ClockIn(e2, c.WorkDate);
        c.Uscita2 = ClockOut(uscitaStimata, c.WorkDate) + "*";
        c.Note = "AUTO_P: Uscita mancante - Stimata 17:00";
        return (int)(u1 - e1).TotalMinutes + (int)(uscitaStimata - e2).TotalMinutes;
    }

    // ── STRAORDINARIO ─────────────────────────────────────────────────────────

    /// <summary>
    /// Divide i minuti lavorati fra ordinario e straordinario, e lo straordinario fra le
    /// fasce della circolare. Feriale, sabato e festivo hanno regole diverse.
    /// </summary>
    private static void ScomponiOvertime(
        TimesheetDay c, int minutiLavorati, DateTime work_date, bool conStraordinari, int pauseNotturne)
    {
        bool festivo = TimesheetRules.IsHoliday(work_date);
        bool sabato = work_date.DayOfWeek == DayOfWeek.Saturday;

        int minutiNotturni = Math.Max(0, MinutiNotturni(c) - pauseNotturne);
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

        // 🪤 Notturno sì, ma solo quello che cade DENTRO lo straordinario. In una giornata
        // normale è lo stesso conto di sempre (il notturno sta in coda, e in coda sta lo
        // straordinario); nel moncone di un turno di notte le ore notturne sono in TESTA,
        // e sono ordinarie: senza questo distinguo uno straordinario fatto di pomeriggio
        // si prenderebbe la maggiorazione del notturno.
        int notturnoStraord = minutiNotturni > 0 ? Math.Min(minutiNotturni, MinutiNotturniInCoda(c, straordinario)) : 0;

        if (notturnoStraord > 0)
        {
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

    private static int NotturniDi(string entrata, string uscita) =>
        ProvaOrario(entrata, out int daMinuti) && ProvaOrario(uscita, out int aMinuti)
            ? NotturniFra(daMinuti, aMinuti)
            : 0;

    /// <summary>Minuti di notte dentro l'intervallo, contati in minuti da mezzanotte.</summary>
    private static int NotturniFra(int daMinuti, int aMinuti)
    {
        if (aMinuti <= daMinuti) return 0;

        int sera = TimesheetRules.NightShiftStartHour * 60;   // 22:00
        int alba = TimesheetRules.NightShiftEndHour * 60;     //  6:00

        // La notte è spezzata in due dalla mezzanotte: la coda della sera (dalle 22:00 in
        // poi, «24:00» compreso) e la testa del mattino (fino alle 6:00). Una giornata
        // normale tocca solo la prima; il moncone di un turno di notte solo la seconda.
        int dallaSera = aMinuti > sera ? aMinuti - Math.Max(daMinuti, sera) : 0;
        int finoAllAlba = daMinuti < alba ? Math.Min(aMinuti, alba) - daMinuti : 0;

        return dallaSera + finoAllAlba;
    }

    /// <summary>
    /// I minuti di notte che cadono negli <b>ultimi</b> <paramref name="minuti"/> lavorati:
    /// è lì che matura lo straordinario. Si scorrono le sessioni dall'ultima alla prima.
    /// </summary>
    private static int MinutiNotturniInCoda(TimesheetDay c, int minuti)
    {
        if (minuti <= 0) return 0;

        (string Entrata, string Uscita)[] sessioni =
        {
            (c.Entrata2, c.Uscita2),
            (c.Entrata1, c.Uscita1),
        };

        int notturni = 0, restanti = minuti;
        foreach ((string entrata, string uscita) in sessioni)
        {
            if (restanti <= 0) break;
            if (!ProvaOrario(entrata, out int da) || !ProvaOrario(uscita, out int a) || a <= da) continue;

            int presi = Math.Min(a - da, restanti);
            notturni += NotturniFra(a - presi, a);
            restanti -= presi;
        }
        return notturni;
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
