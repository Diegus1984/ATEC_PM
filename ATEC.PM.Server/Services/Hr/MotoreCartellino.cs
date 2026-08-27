namespace ATEC.PM.Server.Services.Hr;

/// <summary>Una timbratura come arriva dal rilevatore: orario e verso, niente di elaborato.</summary>
/// <param name="Orario">Istante grezzo, mai modificato.</param>
/// <param name="Verso">Verso dichiarato dal terminale (IN/OUT, ENTRATA/USCITA...).</param>
/// <param name="IdEsterno">Identificativo del rilevatore, per risalire alla timbratura originale.</param>
public record TimbraturaGrezza(DateTime Orario, string Verso, long? IdEsterno = null);

/// <summary>Il cartellino di una giornata: cosa risulta lavorato e come si scompone.</summary>
public class Cartellino
{
    public DateTime Giorno { get; init; }

    /// <summary>Orari come vanno letti a video. L'asterisco segnala un orario messo dal sistema.</summary>
    public string Entrata1 { get; set; } = "";
    public string Uscita1 { get; set; } = "";
    public string Entrata2 { get; set; } = "";
    public string Uscita2 { get; set; } = "";

    /// <summary>Ore ordinarie (mai oltre la giornata standard); «---» se non calcolabili.</summary>
    public string OreOrdinarie { get; set; } = "0h 0m";
    public string Straordinario { get; set; } = "0h 0m";
    public string Pausa { get; set; } = "0h 0m";

    /// <summary>Straordinario per fascia CCNL: chiave = lettera della circolare (A, C, D, E, F, G, H, L, M).</summary>
    public Dictionary<string, string> Fasce { get; } = NuoveFasce();

    /// <summary>Cosa è successo: «OK», la pausa dedotta, il turno riconosciuto o l'anomalia.</summary>
    public string Nota { get; set; } = "";

    /// <summary>true se la giornata richiede un intervento umano (timbratura mancante o incoerente).</summary>
    public bool Anomalia => Nota.StartsWith("⚠");

    internal static Dictionary<string, string> NuoveFasce() =>
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
public static class MotoreCartellino
{
    /// <summary>Configurazione della persona che incide sul calcolo.</summary>
    /// <param name="ConStraordinari">false = a questa persona lo straordinario non si conteggia.</param>
    public record ConfigDipendente(bool ConStraordinari = true);

    /// <summary>Timbrature assegnate ai quattro posti del cartellino, già arrotondate.</summary>
    private sealed class Assegnazione
    {
        public DateTime? Entrata1, Uscita1, Entrata2, Uscita2;
        public int NumIngressi, NumUscite;
    }

    /// <summary>
    /// Calcola il cartellino di una giornata.
    /// </summary>
    /// <param name="giorno">Giornata di competenza.</param>
    /// <param name="timbrature">Timbrature grezze del giorno, in qualsiasi ordine.</param>
    /// <param name="oggi">Data odierna: serve a riconoscere la giornata ancora in corso.</param>
    /// <param name="config">Configurazione della persona.</param>
    public static Cartellino Calcola(
        DateTime giorno,
        IEnumerable<TimbraturaGrezza> timbrature,
        DateTime oggi,
        ConfigDipendente? config = null)
    {
        config ??= new ConfigDipendente();
        var cartellino = new Cartellino { Giorno = giorno.Date };

        var ordinate = timbrature.OrderBy(t => t.Orario).ToList();
        if (ordinate.Count == 0)
        {
            cartellino.Nota = "";
            return cartellino;
        }

        Assegnazione dati = Assegna(ordinate);

        // Giornata ancora aperta: si mostra quel che c'è, senza calcolare nulla.
        if (giorno.Date == oggi.Date && dati.NumIngressi <= 1 && dati.NumUscite <= 1)
        {
            cartellino.Nota = "Giornata in corso";
            return cartellino;
        }

        int minutiLavorati = Elabora(cartellino, dati);
        if (minutiLavorati < 0) return cartellino;   // ramo d'errore: ha già scritto tutto

        if (minutiLavorati > 0)
            ScomponiStraordinario(cartellino, minutiLavorati, giorno, config.ConStraordinari);
        else
            AzzeraTutto(cartellino);

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
    private static Assegnazione Assegna(List<TimbraturaGrezza> timbrature)
    {
        // Stadio 1 — semantica LAG: il gap si misura dalla riga precedente (anche se
        // scartata), e si tronca ai minuti interi come il CAST AS INTEGER del VB.
        var pulite = new List<TimbraturaGrezza> { timbrature[0] };
        for (int i = 1; i < timbrature.Count; i++)
        {
            int gap = (int)(timbrature[i].Orario - timbrature[i - 1].Orario).TotalMinutes;
            if (gap >= RegoleCartellino.FiltroDoppioniMinuti)
                pulite.Add(timbrature[i]);
        }

        // Stadio 2 — raggruppamento a 30 minuti.
        var filtrate = new List<TimbraturaGrezza>();
        TimbraturaGrezza inizioGruppo = pulite[0];
        for (int i = 1; i < pulite.Count; i++)
        {
            double gap = (pulite[i].Orario - pulite[i - 1].Orario).TotalMinutes;
            if (gap >= 30)
            {
                filtrate.Add(inizioGruppo);
                inizioGruppo = pulite[i];
            }
        }
        filtrate.Add(inizioGruppo);

        var dati = new Assegnazione();
        switch (filtrate.Count)
        {
            case 1:
                dati.Entrata1 = Norm(filtrate[0]);
                dati.NumIngressi = 1; dati.NumUscite = 0;
                break;

            case 2:
                dati.Entrata1 = Norm(filtrate[0]);
                dati.Uscita1 = Norm(filtrate[1]);
                dati.NumIngressi = 1; dati.NumUscite = 1;
                break;

            case 3:
                AssegnaTre(filtrate, dati);
                break;

            default:
                dati.Entrata1 = Norm(filtrate[0]);
                dati.Uscita1 = Norm(filtrate[1]);
                dati.Entrata2 = Norm(filtrate[2]);
                dati.Uscita2 = Norm(filtrate[3]);
                dati.NumIngressi = 2; dati.NumUscite = 2;
                break;
        }
        return dati;
    }

    /// <summary>
    /// Tre timbrature: manca sempre qualcosa, e quale sia lo si deduce dagli orari.
    /// Le soglie (15, 12, 11, 90 min, 180 min) vengono dal motore originale: non toccarle
    /// senza rimisurare il banco di prova.
    /// </summary>
    private static void AssegnaTre(List<TimbraturaGrezza> t, Assegnazione dati)
    {
        DateTime t1 = Norm(t[0]), t2 = Norm(t[1]), t3 = Norm(t[2]);
        double gap12 = (t2 - t1).TotalMinutes;
        double gap23 = (t3 - t2).TotalMinutes;

        // Mattina + due timbrature nel pomeriggio: turno unico, la centrale è di troppo.
        if (t2.Hour >= 15 && t3.Hour >= 15 && t1.Hour < 12)
        {
            dati.Entrata1 = t1; dati.Uscita1 = t3;
            dati.NumIngressi = 1; dati.NumUscite = 1;
            return;
        }

        if (t3.Hour < 15)
        {
            dati.Entrata1 = t1; dati.Uscita1 = t2; dati.Entrata2 = t3;
            dati.NumIngressi = 2; dati.NumUscite = 1;
        }
        else if (gap23 > 90)
        {
            dati.Entrata1 = t1; dati.Uscita1 = t2; dati.Uscita2 = t3;
            dati.NumIngressi = 1; dati.NumUscite = 2;
        }
        else if (t2.Hour >= 12 && t2.Hour <= 14 && gap12 > 180)
        {
            dati.Entrata1 = t1; dati.Entrata2 = t2; dati.Uscita2 = t3;
            dati.NumIngressi = 2; dati.NumUscite = 1;
        }
        else if (t1.Hour >= 11)
        {
            dati.Uscita1 = t1; dati.Entrata2 = t2; dati.Uscita2 = t3;
            dati.NumIngressi = 1; dati.NumUscite = 2;
        }
        else
        {
            dati.Entrata1 = t1; dati.Uscita1 = t2; dati.Entrata2 = t3;
            dati.NumIngressi = 2; dati.NumUscite = 1;
        }
    }

    private static DateTime Norm(TimbraturaGrezza t)
    {
        string verso = (t.Verso ?? "").ToUpperInvariant();
        bool eIngresso = verso.Contains("IN") || verso.Contains("ENTR");
        return RegoleCartellino.Arrotonda(t.Orario, eIngresso);
    }

    // ── TURNI ─────────────────────────────────────────────────────────────────

    /// <summary>Riconosce il turno e riempie il cartellino. Torna i minuti lavorati, o -1 se non calcolabile.</summary>
    private static int Elabora(Cartellino c, Assegnazione d)
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
            // quindi i totali finiscono azzerati (non «---»: qui la giornata è nota, è
            // solo incompleta — nel motore originale questo ramo prosegue apposta).
            c.Nota = "⚠ INCOMPLETO: Solo entrata";
            c.Entrata1 = RegoleCartellino.Orario(d.Entrata1);
            c.Uscita1 = "??:??";
            c.Pausa = "0h 0m";
            return 0;
        }

        c.Nota = "⚠ ERR: Verificare timbrature";
        c.OreOrdinarie = "---";
        c.Straordinario = "---";
        foreach (string f in c.Fasce.Keys.ToList()) c.Fasce[f] = "---";
        return -1;
    }

    /// <summary>Quattro timbrature: la pausa è quella vera, salvo che sia assente o troppo corta.</summary>
    private static int TurnoRegolare(Cartellino c, Assegnazione d)
    {
        DateTime e1 = d.Entrata1!.Value, u1 = d.Uscita1!.Value;
        DateTime e2 = d.Entrata2!.Value, u2 = d.Uscita2!.Value;
        int pausa = (int)Math.Max(0, (e2 - u1).TotalMinutes);
        c.Pausa = RegoleCartellino.Durata(pausa);

        if (pausa == 0)
        {
            // Nessuno stacco: si impone la pausa canonica 12:30-13:30.
            var fineMattino = new DateTime(e1.Year, e1.Month, e1.Day, 12, 30, 0);
            var ripresa = new DateTime(e1.Year, e1.Month, e1.Day, 13, 30, 0);
            c.Entrata1 = e1.ToString("HH:mm");
            c.Uscita1 = "12:30*";
            c.Entrata2 = "13:30*";
            c.Uscita2 = u2.ToString("HH:mm");
            c.Pausa = "1h 0m";
            c.Nota = "AUTO_P: Pausa 1h forzata";
            return (int)(fineMattino - e1).TotalMinutes + (int)(u2 - ripresa).TotalMinutes;
        }

        c.Entrata1 = e1.ToString("HH:mm");
        c.Uscita1 = u1.ToString("HH:mm");
        c.Entrata2 = e2.ToString("HH:mm");
        c.Uscita2 = u2.ToString("HH:mm");

        int lavorati = (int)(u1 - e1).TotalMinutes + (int)(u2 - e2).TotalMinutes;
        if (pausa < RegoleCartellino.PausaMinimaMinuti)
        {
            // Pausa più corta del minimo: il tempo mancante si considera recuperato.
            c.Nota = "Recupero pausa pranzo";
            return lavorati + (RegoleCartellino.PausaMinimaMinuti - pausa);
        }

        c.Nota = "OK";
        return lavorati;
    }

    /// <summary>Una entrata e una uscita: mattino, pomeriggio o giornata intera con pausa dedotta.</summary>
    private static int TurnoUnico(Cartellino c, Assegnazione d)
    {
        DateTime entrata = d.Entrata1!.Value, uscita = d.Uscita1!.Value;
        int minutiTotali = (int)(uscita - entrata).TotalMinutes;

        if (entrata.Hour >= 12)
        {
            c.Entrata1 = entrata.ToString("HH:mm");
            c.Uscita1 = uscita.ToString("HH:mm");
            c.Pausa = "0h 0m";
            c.Nota = "Turno pomeridiano";
            return minutiTotali;
        }

        if (uscita.Hour < 13 || (uscita.Hour == 13 && uscita.Minute == 0))
        {
            c.Entrata1 = entrata.ToString("HH:mm");
            c.Uscita1 = uscita.ToString("HH:mm");
            c.Pausa = "0h 0m";
            c.Nota = "Turno mattutino";
            return minutiTotali;
        }

        // Giornata a cavallo del pranzo senza stacco timbrato: si deduce l'ora canonica.
        c.Entrata1 = entrata.ToString("HH:mm");
        c.Uscita1 = "12:30*";
        c.Entrata2 = "13:30*";
        c.Uscita2 = uscita.ToString("HH:mm");
        c.Pausa = "1h 0m";
        c.Nota = "AUTO_P: Pausa 1h detratta";
        return minutiTotali - RegoleCartellino.PausaForzataMinuti;
    }

    /// <summary>Una entrata e due uscite: manca il rientro dalla pausa.</summary>
    private static int TurnoEntrataMancante(Cartellino c, Assegnazione d)
    {
        DateTime e1 = d.Entrata1!.Value, u1 = d.Uscita1!.Value;
        DateTime u2 = d.Uscita2 ?? u1;
        int gapUscite = (int)(u2 - u1).TotalMinutes;

        if (gapUscite < 90)
        {
            // Le due uscite sono vicine: la seconda è un doppione, vale il mattino.
            c.Entrata1 = e1.ToString("HH:mm");
            c.Uscita1 = u1.ToString("HH:mm");
            c.Pausa = "0h 0m";
            c.Nota = "Turno mattutino (seconda uscita ignorata)";
            return (int)(u1 - e1).TotalMinutes;
        }

        DateTime ripresa = u1.AddMinutes(RegoleCartellino.PausaForzataMinuti);
        c.Entrata1 = e1.ToString("HH:mm");
        c.Uscita1 = u1.ToString("HH:mm") + "*";
        c.Entrata2 = ripresa.ToString("HH:mm") + "*";
        c.Uscita2 = u2.ToString("HH:mm");
        c.Pausa = RegoleCartellino.Durata(RegoleCartellino.PausaForzataMinuti);
        c.Nota = "AUTO_P: Pausa implicita (1 IN / 2 OUT)";
        return (int)(u1 - e1).TotalMinutes + (int)(u2 - ripresa).TotalMinutes;
    }

    /// <summary>Due entrate e una uscita: manca l'uscita finale, si stima alle 17:00.</summary>
    private static int TurnoUscitaMancante(Cartellino c, Assegnazione d)
    {
        DateTime e1 = d.Entrata1!.Value, u1 = d.Uscita1!.Value, e2 = d.Entrata2!.Value;
        int pausa = (int)Math.Max(0, (e2 - u1).TotalMinutes);
        c.Pausa = RegoleCartellino.Durata(pausa);

        var uscitaStimata = new DateTime(e2.Year, e2.Month, e2.Day, 17, 0, 0);
        c.Entrata1 = e1.ToString("HH:mm");
        c.Uscita1 = u1.ToString("HH:mm");
        c.Entrata2 = e2.ToString("HH:mm");
        c.Uscita2 = uscitaStimata.ToString("HH:mm") + "*";
        c.Nota = "AUTO_P: Uscita mancante - Stimata 17:00";
        return (int)(u1 - e1).TotalMinutes + (int)(uscitaStimata - e2).TotalMinutes;
    }

    // ── STRAORDINARIO ─────────────────────────────────────────────────────────

    /// <summary>
    /// Divide i minuti lavorati fra ordinario e straordinario, e lo straordinario fra le
    /// fasce della circolare. Feriale, sabato e festivo hanno regole diverse.
    /// </summary>
    private static void ScomponiStraordinario(Cartellino c, int minutiLavorati, DateTime giorno, bool conStraordinari)
    {
        bool festivo = RegoleCartellino.EFestivo(giorno);
        bool sabato = giorno.DayOfWeek == DayOfWeek.Saturday;

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
            ordinari = Math.Min(minutiLavorati, RegoleCartellino.MinutiGiornataStandard);
            straordinari = Math.Max(0, minutiLavorati - RegoleCartellino.MinutiGiornataStandard);
        }

        c.OreOrdinarie = RegoleCartellino.Durata(ordinari);
        c.Straordinario = RegoleCartellino.Durata(straordinari);

        // Chi non fa straordinario: le ore restano, la maggiorazione no.
        if (!conStraordinari)
        {
            c.Straordinario = "0h 0m";
            foreach (string f in c.Fasce.Keys.ToList()) c.Fasce[f] = "0h 0m";
        }
    }

    /// <summary>Feriale: oltre le 8 ore è straordinario, notturno (g) prima di diurno (a).</summary>
    private static void FasceFeriale(Cartellino c, int minutiTotali, int minutiNotturni)
    {
        int straordinario = Math.Max(0, minutiTotali - RegoleCartellino.MinutiGiornataStandard);
        if (straordinario == 0) return;

        if (minutiNotturni > 0)
        {
            int notturnoStraord = Math.Min(minutiNotturni, straordinario);
            c.Fasce["G"] = RegoleCartellino.Durata(notturnoStraord);
            int residuo = straordinario - notturnoStraord;
            if (residuo > 0) c.Fasce["A"] = RegoleCartellino.Durata(residuo);
        }
        else
        {
            c.Fasce["A"] = RegoleCartellino.Durata(straordinario);
        }
    }

    /// <summary>Sabato: non è giornata ordinaria, quindi è tutto straordinario.</summary>
    private static void FasceSabato(Cartellino c, int minutiTotali, int minutiNotturni, int minutiDiurni)
    {
        if (minutiNotturni > 0)
        {
            c.Fasce["G"] = RegoleCartellino.Durata(minutiNotturni);
            if (minutiDiurni > 0) c.Fasce["A"] = RegoleCartellino.Durata(minutiDiurni);
        }
        else
        {
            c.Fasce["A"] = RegoleCartellino.Durata(minutiTotali);
        }
    }

    /// <summary>Festivo: entro le 8 ore fascia c (o d col riposo compensativo), oltre e/f; notturno h/l/m.</summary>
    private static void FasceFestivo(Cartellino c, int minutiTotali, int minutiNotturni, int minutiDiurni,
                                     bool riposoCompensativo = false)
    {
        int entro8h = Math.Min(minutiTotali, RegoleCartellino.MinutiGiornataStandard);
        int oltre8h = Math.Max(0, minutiTotali - RegoleCartellino.MinutiGiornataStandard);

        if (minutiNotturni > 0)
        {
            int notturnoEntro8h = Math.Min(minutiNotturni, entro8h);
            int notturnoOltre8h = Math.Min(Math.Max(0, minutiNotturni - entro8h), oltre8h);

            if (notturnoEntro8h > 0) c.Fasce["H"] = RegoleCartellino.Durata(notturnoEntro8h);
            if (notturnoOltre8h > 0)
                c.Fasce[riposoCompensativo ? "M" : "L"] = RegoleCartellino.Durata(notturnoOltre8h);

            int diurnoEntro8h = Math.Max(0, entro8h - notturnoEntro8h);
            int diurnoOltre8h = Math.Max(0, oltre8h - notturnoOltre8h);
            if (diurnoEntro8h > 0)
                c.Fasce[riposoCompensativo ? "D" : "C"] = RegoleCartellino.Durata(diurnoEntro8h);
            if (diurnoOltre8h > 0)
                c.Fasce[riposoCompensativo ? "F" : "E"] = RegoleCartellino.Durata(diurnoOltre8h);
        }
        else
        {
            if (entro8h > 0) c.Fasce[riposoCompensativo ? "D" : "C"] = RegoleCartellino.Durata(entro8h);
            if (oltre8h > 0) c.Fasce[riposoCompensativo ? "F" : "E"] = RegoleCartellino.Durata(oltre8h);
        }
    }

    /// <summary>
    /// Minuti lavorati dopo le 22:00, letti dagli orari già scritti sul cartellino
    /// (asterischi degli orari stimati compresi).
    /// </summary>
    private static int MinutiNotturni(Cartellino c) =>
        NotturniDi(c.Entrata1, c.Uscita1) + NotturniDi(c.Entrata2, c.Uscita2);

    private static int NotturniDi(string entrata, string uscita)
    {
        if (!ProvaOrario(entrata, out int daMinuti)) return 0;
        if (!ProvaOrario(uscita, out int aMinuti)) return 0;

        int soglia = RegoleCartellino.OraInizioNotturno * 60;
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

    private static void AzzeraTutto(Cartellino c)
    {
        c.OreOrdinarie = "0h 0m";
        c.Straordinario = "0h 0m";
        foreach (string f in c.Fasce.Keys.ToList()) c.Fasce[f] = "0h 0m";
    }
}
