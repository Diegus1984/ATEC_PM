namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Le timbrature dei giorni confinanti, così come stanno in <c>hr_punches</c>: servono a
/// ricomporre il turno di notte, che nel database arriva spezzato su due giornate.
///
/// <para>Senza contesto il motore lavora esattamente come prima — è così che il banco di
/// prova delle 379 giornate continua a valere.</para>
/// </summary>
/// <param name="Yesterday">Timbrature del giorno precedente, in ordine di orario.</param>
/// <param name="Tomorrow">Timbrature del giorno seguente, in ordine di orario.</param>
public sealed record NightContext(
    IReadOnlyList<RawPunch>? Yesterday = null,
    IReadOnlyList<RawPunch>? Tomorrow = null)
{
    /// <summary>Nessun contesto: nessuna notte da ricomporre.</summary>
    public static readonly NightContext None = new();
}

/// <summary>Come la mezzanotte ha toccato la giornata.</summary>
[Flags]
public enum NightSplit
{
    /// <summary>Giornata che comincia e finisce dentro il suo giorno.</summary>
    None = 0,

    /// <summary>
    /// Le prime ore di oggi erano la coda del turno cominciato IERI: le conta ieri, non oggi.
    /// </summary>
    HandedToPreviousDay = 1,

    /// <summary>
    /// Il turno cominciato oggi scavalca la mezzanotte e finisce domattina: le ore fino
    /// alla timbratura d'uscita le conta OGGI.
    /// </summary>
    RunsPastMidnight = 2,
}

/// <summary>
/// Il turno a cavallo della mezzanotte — il <b>terzo turno</b>.
///
/// <para><b>La regola aziendale</b>: il turno di notte vale per il giorno in cui
/// <b>comincia</b>. Chi entra alle 22 di mercoledì ed esce alle 6 di giovedì ha fatto la
/// giornata di <i>mercoledì</i>, otto ore, e lo straordinario si conta su quelle otto —
/// non su due mezze giornate che non superano mai la soglia.</para>
///
/// <para>Nel database, però, ogni timbratura sta sul suo giorno solare (<c>work_date</c> =
/// giorno del timbro, e <c>hr_punches</c> resta la copia fedele di quel che manda Ecos).
/// Qui il turno viene <b>ricomposto</b>: la giornata si prende le timbrature di domattina
/// che chiudono il suo turno, e lascia a ieri quelle che chiudono il turno di ieri. Dentro
/// la giornata il turno resta poi <b>tagliato a mezzanotte</b> (…→24:00, 00:00→…), così il
/// cartellino ha quattro caselle leggibili e il conto delle ore notturne torna.</para>
///
/// <para><b>Si ricompone solo quando la controparte esiste davvero</b>: l'entrata serale
/// deve trovare l'uscita mattutina del giorno dopo, e viceversa. Una strisciata dimenticata
/// non diventa un turno di notte — resta l'anomalia di sempre. Le soglie che decidono cosa
/// è una notte stanno in <see cref="TimesheetRules"/>, dove stanno tutte le altre.</para>
///
/// <para>Le timbrature di mezzanotte sono <b>sintetiche</b>
/// (<see cref="RawPunch.Synthetic"/>): vivono solo dentro il calcolo.</para>
/// </summary>
public static class NightShift
{
    /// <summary>
    /// Il segno che una giornata ha a che fare con un turno di notte. Sta nella nota del
    /// cartellino, ed è da lì che lo riconosce chi la guarda da fuori (il calendario, per
    /// non segnare come «ore mancanti» una giornata che le sue ore le ha date a ieri).
    /// </summary>
    public const string NoteMarker = "🌙";

    /// <summary>
    /// La frase che distingue la giornata che ha <b>ceduto</b> le sue prime ore al turno
    /// cominciato ieri. Serve al calendario: solo quella giornata non va segnata «ore
    /// mancanti», perché le ore le ha lavorate — le conta il giorno prima.
    /// </summary>
    public const string HandedMarker = "contano sul giorno prima";

    /// <summary>Questa giornata ha ceduto ore al turno di ieri?</summary>
    public static bool HasHandedHours(string? nota) =>
        !string.IsNullOrEmpty(nota) && nota.Contains(HandedMarker, StringComparison.Ordinal);

    /// <summary>La nota dice che questa giornata ha a che fare con un turno di notte?</summary>
    public static bool IsNightNote(string? nota) =>
        !string.IsNullOrEmpty(nota) && nota.Contains(NoteMarker, StringComparison.Ordinal);

    /// <summary>Verso di entrata, con le sigle che possono arrivare dal rilevatore.</summary>
    public static bool IsEntry(string? direction)
    {
        string v = (direction ?? "").ToUpperInvariant();
        return v.Contains("IN") || v.Contains("ENTR");
    }

    /// <summary>Verso di uscita. Un verso che non si riconosce non è né l'uno né l'altro.</summary>
    public static bool IsExit(string? direction)
    {
        string v = (direction ?? "").ToUpperInvariant();
        return v.Contains("OUT") || v.Contains("USC");
    }

    /// <summary>
    /// Ricompone il turno: toglie le ore che appartengono al turno di ieri e aggiunge quelle
    /// di domattina che chiudono il turno di oggi.
    /// </summary>
    /// <param name="workDate">Giornata di competenza.</param>
    /// <param name="ordered">Timbrature del giorno, già ordinate per orario.</param>
    /// <param name="context">Le timbrature confinanti; <c>null</c> = nessuna ricomposizione.</param>
    /// <param name="split">Cosa è stato spostato.</param>
    public static List<RawPunch> Compose(
        DateTime workDate,
        IReadOnlyList<RawPunch> ordered,
        NightContext? context,
        out NightSplit split)
    {
        split = NightSplit.None;
        var esito = new List<RawPunch>(ordered);
        if (esito.Count == 0 || context is null) return esito;

        // 1. Le mie prime ore sono la coda del turno cominciato ieri? Allora sono di ieri.
        int cedute = CodaDelTurnoDiIeri(esito, context.Yesterday);
        if (cedute > 0)
        {
            esito.RemoveRange(0, cedute);
            split |= NightSplit.HandedToPreviousDay;
        }

        // 2. Il turno che ho aperto stasera si chiude domattina? Allora quelle ore sono mie.
        if (esito.Count > 0 && IsEntry(esito[^1].Direction))
        {
            List<RawPunch> domattina = ChiusuraDiDomani(esito[^1], context.Tomorrow);
            if (domattina.Count > 0)
            {
                esito.AddRange(domattina);
                split |= NightSplit.RunsPastMidnight;
            }
        }

        return esito;
    }

    /// <summary>
    /// Quante timbrature in testa alla giornata appartengono al turno di ieri, quando ieri
    /// si è chiuso con un'entrata che con la prima di queste forma una notte.
    /// </summary>
    private static int CodaDelTurnoDiIeri(List<RawPunch> mie, IReadOnlyList<RawPunch>? ieri)
    {
        if (mie.Count == 0 || ieri is null || ieri.Count == 0) return 0;
        if (!IsEntry(ieri[^1].Direction)) return 0;
        if (!IsExit(mie[0].Direction)) return 0;
        if (!IsNightShift(ieri[^1].PunchedAt, mie[0].PunchedAt)) return 0;

        return LunghezzaCoda(mie, ieri[^1].PunchedAt);
    }

    /// <summary>
    /// Le timbrature di domattina che chiudono il turno aperto stasera. Vuoto se domani non
    /// comincia con un'uscita, o se la coppia non è un turno di notte.
    /// </summary>
    private static List<RawPunch> ChiusuraDiDomani(RawPunch entrata, IReadOnlyList<RawPunch>? domani)
    {
        if (domani is null || domani.Count == 0) return new List<RawPunch>();
        if (!IsExit(domani[0].Direction)) return new List<RawPunch>();
        if (!IsNightShift(entrata.PunchedAt, domani[0].PunchedAt)) return new List<RawPunch>();

        return domani.Take(LunghezzaCoda(domani, entrata.PunchedAt)).ToList();
    }

    /// <summary>
    /// Quante timbrature in testa a una giornata sono la <b>coda di un turno cominciato il
    /// giorno prima</b> — e la stessa risposta la danno tutte e due le parti, perché è la
    /// stessa funzione a calcolarla: il giorno che cede e quello che prende non possono
    /// contare le stesse ore due volte né perderle.
    ///
    /// <para>🪤 Non basta contare le uscite: <b>dentro un turno di notte si timbra anche la
    /// pausa</b>. Un'uscita all'una e il rientro all'una e mezza fanno parte del turno; una
    /// entrata a distanza di ore, no — quella è il turno dopo. La soglia è
    /// <see cref="TimesheetRules.NightShiftMaxBreakMinutes"/>, e la coda finisce sempre su
    /// un'uscita.</para>
    /// </summary>
    private static int LunghezzaCoda(IReadOnlyList<RawPunch> giorno, DateTime inizioTurno)
    {
        int n = 0;
        int ultimaUscita = 0;

        while (n < giorno.Count && IsExit(giorno[n].Direction))
        {
            // 🪤 Il tetto sulla durata vale sul turno INTERO, non solo sulla prima uscita:
            // senza, la coda si allungherebbe di pausa in pausa fino a inghiottire la
            // giornata dopo — 22 ore in un cartellino solo.
            if ((giorno[n].PunchedAt - inizioTurno).TotalMinutes > TimesheetRules.NightShiftMaxMinutes)
                break;

            n++;
            ultimaUscita = n;

            // 🪤 Il rientro dev'essere ravvicinato E ancora dentro la notte: uscire alle 6
            // e rientrare alle 8 non è una pausa, è la giornata dopo che comincia.
            if (n < giorno.Count
                && IsEntry(giorno[n].Direction)
                && giorno[n].PunchedAt.Hour < TimesheetRules.NightShiftEndHour
                && (giorno[n].PunchedAt - giorno[n - 1].PunchedAt).TotalMinutes
                    <= TimesheetRules.NightShiftMaxBreakMinutes)
            {
                n++;
            }
        }

        // Se dopo l'ultima pausa non è più tornato a timbrare l'uscita, quella entrata non
        // è del turno di ieri: apre qualcosa d'altro e resta al suo giorno.
        return ultimaUscita;
    }

    /// <summary>
    /// Taglia a mezzanotte il turno che la scavalca: un'uscita alle <c>24:00</c> e una
    /// entrata alle <c>00:00</c>, così la giornata resta leggibile nelle quattro caselle del
    /// cartellino e le ore di notte si contano dalla parte giusta.
    /// </summary>
    public static List<RawPunch> SplitAtMidnight(DateTime workDate, IReadOnlyList<RawPunch> punches)
    {
        DateTime mezzanotte = workDate.Date.AddDays(1);
        var esito = new List<RawPunch>(punches.Count + 2);
        bool tagliato = false;

        foreach (RawPunch t in punches)
        {
            // Una timbratura ESATTAMENTE a mezzanotte è già il confine: non c'è da tagliare.
            if (!tagliato && t.PunchedAt > mezzanotte)
            {
                esito.Add(new RawPunch(mezzanotte, "OUT", null, Synthetic: true));
                esito.Add(new RawPunch(mezzanotte, "IN", null, Synthetic: true));
                tagliato = true;
            }
            esito.Add(t);
        }

        return esito;
    }

    /// <summary>
    /// Quella coppia entrata/uscita è un turno di notte? Deve scavalcare una sola
    /// mezzanotte, cominciare abbastanza tardi, finire abbastanza presto e durare quanto
    /// dura un turno: fuori da questi paletti è una timbratura sbagliata, non una notte.
    /// </summary>
    public static bool IsNightShift(DateTime entrata, DateTime uscita) =>
        uscita > entrata
        && uscita.Date == entrata.Date.AddDays(1)
        && entrata.Hour >= TimesheetRules.NightShiftEarliestStartHour
        && uscita.Hour < TimesheetRules.NightShiftLatestEndHour
        && (uscita - entrata).TotalMinutes <= TimesheetRules.NightShiftMaxMinutes;
}
