using System.Globalization;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Il sollecito della <b>singola giornata</b> (PIANO-HR-PORT-ORIGINALE.md, voci 1 e 3):
/// port di <c>btnMailDipendente_Click</c> e <c>BuildDettaglioAnomalia</c> del
/// <c>ReportPage</c> del programma «Timbrature».
///
/// <para>Qui sta <b>l'unica copia</b> della regola «questa giornata va segnalata»: la usano
/// sia il pulsante 📧 sulla riga sia il filtro «📧 Da segnalare». Se fossero due copie, il
/// filtro mostrerebbe righe senza pulsante — ed è esattamente il difetto che nel VB c'era
/// (<c>ReportProcessor</c> aveva una seconda regola, più larga, che non arrivava mai alla
/// griglia perché <c>ShowMailButton</c> non veniva salvato).</para>
///
/// <para>🪤 <b>Non si usa <see cref="HrDayDto.HasAnomaly"/></b>: da noi è
/// <c>Note.StartsWith("⚠")</c> e prende solo INCOMPLETO ed ERR. Resterebbe fuori
/// <c>AUTO_P: Uscita mancante - Stimata 17:00</c>, che nell'originale il sollecito ce l'ha —
/// ed è giusto: una pausa dedotta è una regola applicata con sicurezza, un'uscita
/// <i>indovinata</i> è un buco vero che la persona deve confermare. Gli altri
/// <c>AUTO_P</c> (pausa dedotta, forzata, detratta) non danno il pulsante: nell'originale
/// non sono in elenco.</para>
/// </summary>
public static class HrDayReminder
{
    /// <summary>Cultura fissa: su un server con locale inglese «dddd dd MMMM yyyy» uscirebbe in inglese.</summary>
    private static readonly CultureInfo ItIT = new("it-IT");

    /// <summary>
    /// Le note che fanno comparire il pulsante, copiate da <c>ReportPage.xaml.vb:208-214</c>.
    /// Confronto ordinale (come <c>String.Contains</c> del VB con Option Compare Binary).
    /// </summary>
    private static readonly string[] NoteDaSegnalare =
    {
        "INCOMPLETO",
        "ERR",
        "Stimata",
        "nessuna timbratura",
        "Permesso rettificato",
        "Permesso annullato",
    };

    /// <summary>
    /// La regola dell'originale: la giornata <b>non è oggi</b> e la nota contiene una delle
    /// sei parole chiave. Sul giorno corrente il pulsante non compare mai — la giornata è
    /// ancora aperta e «manca l'uscita» sarebbe una segnalazione a vuoto.
    /// </summary>
    public static bool Serve(string? nota, DateTime workDate, DateTime oggi)
    {
        if (workDate.Date == oggi.Date) return false;
        if (string.IsNullOrEmpty(nota)) return false;

        foreach (string chiave in NoteDaSegnalare)
        {
            if (nota.Contains(chiave, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// L'oggetto della mail. 📌 Rispetto all'originale è caduto il prefisso <c>[eTime]</c>,
    /// come già fatto per il sollecito mensile: la mail parte da ATEC PM, non da eTime.
    /// </summary>
    public static string Oggetto(DateTime data) =>
        $"Segnalazione timbrature — {data.ToString("dd/MM/yyyy", ItIT)}";

    /// <summary>
    /// Il corpo intero, con saluto e firma di <c>btnMailDipendente_Click</c> (righe 283-290).
    /// </summary>
    /// <param name="nomeDipendente">Il nome completo: per il saluto si usa il nome di battesimo.</param>
    /// <param name="firma">
    /// Il nome del mittente configurato (era <c>SettingsManager.GetSmtpFrom</c>). Vuoto quando
    /// è un indirizzo email o non è impostato: nell'originale in quel caso si leggeva
    /// «Ufficio Risorse Umane» due volte di fila, qui resta la sola riga dell'ufficio.
    /// </param>
    public static string Corpo(string nomeDipendente, DateTime data, HrDayDto giornata, string firma)
    {
        string saluto = NomeDiSaluto(nomeDipendente);
        string rigaFirma = string.IsNullOrWhiteSpace(firma) ? "" : firma.Trim() + "\n";

        return
            $"Gentile {saluto},\n\n"
            + $"Ti segnaliamo un'anomalia nelle tue timbrature del giorno {data.ToString("dddd dd MMMM yyyy", ItIT)}:\n\n"
            + $"{Dettaglio(giornata)}\n\n"
            + "Ti preghiamo di verificare e regolarizzare la situazione.\n\n"
            + "Cordiali saluti,\n"
            + rigaFirma
            + "Ufficio Risorse Umane — ATEC S.r.l.";
    }

    /// <summary>
    /// Il blocco centrale: cosa risulta timbrato, che problema è, quante ore ne sono uscite.
    /// Port di <c>BuildDettaglioAnomalia</c>: l'ordine dei rami conta (vince il primo che
    /// combacia) e le spaziature delle etichette sono quelle dell'originale.
    /// </summary>
    public static string Dettaglio(HrDayDto g)
    {
        var sb = new System.Text.StringBuilder();

        // Le timbrature GREZZE, come sono arrivate dal rilevatore: la persona deve
        // riconoscere quello che ha fatto, non il risultato dell'arrotondamento.
        sb.AppendLine("TIMBRATURE REGISTRATE:");
        sb.AppendLine($"  Entrata 1:  {g.Raw.ClockIn1}");
        sb.AppendLine($"  Uscita 1:   {g.Raw.ClockOut1}");
        sb.AppendLine($"  Entrata 2:  {g.Raw.ClockIn2}");
        sb.AppendLine($"  Uscita 2:   {g.Raw.ClockOut2}");
        sb.AppendLine();

        sb.AppendLine("PROBLEMA RILEVATO:");
        string nota = g.Note ?? "";

        if (nota.Contains("Permesso parziale", StringComparison.Ordinal)
            && nota.Contains("nessuna timbratura", StringComparison.Ordinal))
        {
            sb.AppendLine("  ⚠ Risulta un permesso parziale ma nessuna timbratura registrata.");
            sb.AppendLine("");
            sb.AppendLine("  Ti chiediamo di scegliere una delle seguenti opzioni:");
            sb.AppendLine("    1) Inserire le timbrature mancanti del giorno");
            sb.AppendLine("    2) Comunicare all'ufficio HR se il permesso copre l'intera giornata");
            sb.AppendLine("");
            sb.AppendLine("  In assenza di riscontro, la giornata verrà considerata interamente in permesso.");
        }
        else if (nota.Contains("Permesso rettificato", StringComparison.Ordinal))
        {
            sb.AppendLine("  ℹ Le ore di permesso sono state ricalcolate in base alle timbrature effettive.");
            sb.AppendLine($"  {nota}");
        }
        else if (nota.Contains("Permesso annullato", StringComparison.Ordinal))
        {
            sb.AppendLine("  ℹ Il permesso richiesto è stato annullato: risulta una giornata lavorativa completa.");
            sb.AppendLine($"  {nota}");
        }
        else if (nota.Contains("manca una timbratura della notte", StringComparison.Ordinal))
        {
            sb.AppendLine("  ⚠ Turno di notte: le timbrature non si accoppiano.");
            sb.AppendLine("  Verifica l'entrata e l'uscita del turno.");
        }
        else if (nota.Contains("due turni nella stessa giornata", StringComparison.Ordinal))
        {
            sb.AppendLine("  ⚠ Nella stessa giornata risultano due turni distinti.");
            sb.AppendLine("  Verifica che le timbrature siano tutte corrette.");
        }
        else if (nota.Contains("INCOMPLETO", StringComparison.Ordinal))
        {
            sb.AppendLine("  ⚠ Timbrature incomplete — manca l'uscita.");
            sb.AppendLine("  Risulta registrata solo l'entrata.");
        }
        else if (nota.Contains("AUTO_P: Pausa implicita", StringComparison.Ordinal))
        {
            sb.AppendLine("  ⚠ Manca una timbratura di pausa pranzo.");
            sb.AppendLine("  Il sistema ha inserito automaticamente la pausa.");
            sb.AppendLine("  Verifica se hai dimenticato di timbrare uscita/rientro pranzo.");
        }
        else if (nota.Contains("AUTO_P: Pausa 1h detratta", StringComparison.Ordinal))
        {
            sb.AppendLine("  ⚠ Risultano solo 2 timbrature su un turno lungo.");
            sb.AppendLine("  La pausa pranzo di 1h è stata detratta automaticamente.");
        }
        else if (nota.Contains("AUTO_P: Uscita mancante", StringComparison.Ordinal))
        {
            sb.AppendLine("  ⚠ Manca la timbratura di uscita pomeridiana.");
            sb.AppendLine("  Il sistema ha stimato l'uscita alle 17:00.");
        }
        else if (nota.Contains("AUTO_P: Pausa 1h forzata", StringComparison.Ordinal))
        {
            sb.AppendLine("  ⚠ Pausa pranzo di 0 minuti rilevata.");
            sb.AppendLine("  Il sistema ha forzato 1h di pausa (12:30-13:30).");
            sb.AppendLine("  Verifica se hai dimenticato di timbrare uscita/rientro pranzo.");
        }
        else if (nota.Contains("ERR", StringComparison.Ordinal))
        {
            sb.AppendLine("  ❌ Errore nelle timbrature — impossibile elaborare.");
            sb.AppendLine("  Verificare tutte le timbrature del giorno.");
        }
        else if (g.Raw.ClockIn1 == "--:--" && g.Raw.ClockOut1 == "--:--")
        {
            sb.AppendLine("  ⚠ Nessuna timbratura registrata per questo giorno.");
        }
        else
        {
            sb.AppendLine($"  Nota sistema: {nota}");
        }

        sb.AppendLine();
        sb.AppendLine("RISULTATO ELABORAZIONE:");
        sb.AppendLine($"  Ore ordinarie:    {Ore(g.RegularHours)}");
        sb.Append($"  Straordinario:    {Ore(g.Overtime)}");

        return sb.ToString();
    }

    /// <summary>
    /// Il nome per il saluto. Chi chiama passa già il nome di battesimo (la colonna
    /// <c>first_name</c>); qui resta la sola regola dell'originale: da «Rossi, Mario» si
    /// prende quello dopo la virgola.
    ///
    /// <para>🪤 Non si taglia al primo spazio: «Maria Grazia» diventerebbe «Maria», e la
    /// stessa persona si vedrebbe salutata in due modi diversi dal sollecito della giornata
    /// e da quello mensile.</para>
    /// </summary>
    private static string NomeDiSaluto(string nomeCompleto)
    {
        string nome = (nomeCompleto ?? "").Trim();
        if (nome.Length == 0) return "collega";

        int virgola = nome.IndexOf(',');
        return virgola >= 0 ? nome[(virgola + 1)..].Trim() : nome;
    }

    /// <summary>Le giornate senza dati arrivano con le ore vuote: nella mail vale «0h 0m», come nel VB.</summary>
    private static string Ore(string? valore) => string.IsNullOrWhiteSpace(valore) ? "0h 0m" : valore;
}
