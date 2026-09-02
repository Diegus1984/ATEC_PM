using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.RisorseSync;

/// <summary>
/// Una riga di <c>res_assignments</c> come la legge il motore (Dapper, alias nella SELECT).
/// Le date arrivano come DateTime (colonne DATE) e <c>updated_at</c> è in ORA LOCALE
/// (Europe/Rome: il server scrive <c>NOW()</c>); la conversione la fa <see cref="AllocazioniSync.DaPm"/>.
/// <c>Dipendente</c> è il nome per il dettaglio del registro, non un campo della riga.
/// </summary>
public sealed class AllocazionePm
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Tipo { get; set; } = "OP";
    public DateTime DataInizio { get; set; }
    public DateTime DataFine { get; set; }
    public int? ProjectId { get; set; }
    public string? Descrizione { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? Dipendente { get; set; }
    /// <summary>Il codice della commessa (per il dettaglio, quando la commessa non è mappata); null se la riga non ne ha.</summary>
    public string? Commessa { get; set; }
}

/// <summary>
/// Un'allocazione vista dal motore, con gli id di ATEC PM e l'istante di modifica in UTC:
/// la forma comune in cui si confrontano le righe dei due lati (PIANO-SYNC-RISORSE.md §4.2).
///
/// <para>Normalizzata alla costruzione, con le stesse regole di <c>ResourcesController</c>:
/// tipo fuori da OP/FLEX/FERIE → OP (spazi e minuscole non contano); FERIE → nessuna commessa;
/// descrizione senza spazi ai lati, vuota ≡ null. Così due righe «uguali» hanno la stessa
/// <see cref="AllocazioniSync.Impronta"/> anche se un lato ha scritto «ferie » e l'altro «FERIE».
/// 🪤 Un <c>with</c> NON rinormalizza: si costruisce sempre col costruttore.</para>
/// </summary>
public sealed record RigaAlloc(
    int EmployeeId,
    string Tipo,
    DateOnly Inizio,
    DateOnly Fine,
    int? ProjectId,
    string? Descrizione,
    int? UpdatedBy,
    DateTime? UpdatedAtUtc)
{
    public string Tipo { get; init; } = AllocazioniSync.NormalizzaTipo(Tipo);
    public int? ProjectId { get; init; } = AllocazioniSync.NormalizzaTipo(Tipo) == "FERIE" ? null : ProjectId;
    public string? Descrizione { get; init; } = AllocazioniSync.NormalizzaDescrizione(Descrizione);
}

/// <summary>Cosa fare per una coppia (riga PM, riga VPS) secondo le regole di merge di §4.3.</summary>
public enum AzioneMerge
{
    /// <summary>Uguali e mappa già allineata: niente.</summary>
    Niente,
    /// <summary>Uguali ma la mappa ha un'impronta diversa (o nessuna riga da nessuna parte): solo la mappa.</summary>
    AggiornaHash,
    /// <summary>Copia la riga del VPS in PM.</summary>
    AggiornaPm,
    /// <summary>Copia la riga di PM sul VPS.</summary>
    AggiornaVps,
    /// <summary>La riga è sparita sul VPS: si cancella in PM.</summary>
    CancellaPm,
    /// <summary>La riga è sparita in PM: si cancella sul VPS.</summary>
    CancellaVps,
}

/// <summary>
/// La logica PURA della Fase 2 (allocazioni nei due versi): normalizzazione, impronte,
/// conversioni fra i tre mondi (MySQL di PM, contratto del VPS, forma comune), regole di
/// merge (§4.3) e abbinamento per contenuto. Niente database e niente rete, così ogni regola
/// ha il suo test (<c>AllocazioniSyncTests</c>). Il giro vero sta in
/// <c>RisorseSyncService.SyncAllocazioniAsync</c>.
/// </summary>
public static class AllocazioniSync
{
    // ── Normalizzazione ──────────────────────────────────────────

    /// <summary>OP | FLEX | FERIE, senza spazi e in maiuscolo; tutto il resto → OP (come <c>ResourcesController.NormTipo</c>).</summary>
    public static string NormalizzaTipo(string? tipo)
    {
        string t = (tipo ?? "").Trim().ToUpperInvariant();
        return t is "OP" or "FLEX" or "FERIE" ? t : "OP";
    }

    /// <summary>Il limite di <c>res_assignments.descrizione</c> in PM (VARCHAR(500)); sul VPS è TEXT senza limite.</summary>
    public const int LunghezzaDescrizione = 500;

    /// <summary>
    /// Trim; vuota ≡ null; oltre <see cref="LunghezzaDescrizione"/> si taglia PRIMA dell'impronta:
    /// il VPS tiene il testo lungo, PM quello che ci sta, e i due lati calcolano la stessa impronta
    /// (niente «Data too long» a ogni giro, niente ping-pong).
    /// </summary>
    public static string? NormalizzaDescrizione(string? descrizione)
    {
        string d = (descrizione ?? "").Trim();
        if (d.Length > LunghezzaDescrizione) d = d[..LunghezzaDescrizione].TrimEnd();
        return d.Length == 0 ? null : d;
    }

    // ── Impronta ─────────────────────────────────────────────────

    /// <summary>
    /// SHA256 (esadecimale minuscolo) di EmployeeId|Tipo|Inizio|Fine|ProjectId|Descrizione,
    /// con gli id di PM. <c>UpdatedBy</c> e <c>UpdatedAtUtc</c> NON entrano: chi ha toccato la
    /// riga e quando servono a decidere un conflitto, non a dire se la riga è cambiata.
    /// </summary>
    public static string Impronta(RigaAlloc r) =>
        // Cultura invariante su tutto: un'impronta persistita in res_sync_map non deve cambiare col calendario della macchina.
        Sha256(FormattableString.Invariant(
            $"{r.EmployeeId}|{r.Tipo}|{r.Inizio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{r.Fine.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{r.ProjectId?.ToString(CultureInfo.InvariantCulture) ?? ""}|{r.Descrizione ?? ""}"));

    private static string Sha256(string testo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo))).ToLowerInvariant();

    // ── Fusi orari ───────────────────────────────────────────────

    /// <summary>
    /// Il fuso di ATEC PM: <c>Europe/Rome</c> (Linux/ICU) o «W. Europe Standard Time»
    /// (Windows senza ICU). Se manca anche quello si ripiega sul fuso della macchina, che in
    /// produzione è comunque quello italiano.
    /// </summary>
    internal static readonly TimeZoneInfo FusoItalia = TrovaFuso();

    private static TimeZoneInfo TrovaFuso()
    {
        foreach (string id in new[] { "Europe/Rome", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    /// <summary>
    /// Ora locale (Europe/Rome, come la scrive <c>NOW()</c> in MySQL) → UTC con Kind Utc.
    /// Un'ora che non esiste (il buco del passaggio all'ora legale) si sposta avanti di
    /// un'ora invece di far cadere il giro.
    /// </summary>
    public static DateTime UtcDaLocale(DateTime locale)
    {
        DateTime senzaKind = DateTime.SpecifyKind(locale, DateTimeKind.Unspecified);
        if (FusoItalia.IsInvalidTime(senzaKind)) senzaKind = senzaKind.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(senzaKind, FusoItalia);
    }

    /// <summary>UTC → ora locale (Europe/Rome), Kind Unspecified: è un'ora «da orologio a muro», quella che va in <c>updated_at</c>.</summary>
    public static DateTime LocaleDaUtc(DateTime utc)
    {
        DateTime comeUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(comeUtc, FusoItalia), DateTimeKind.Unspecified);
    }

    // ── Conversioni ──────────────────────────────────────────────

    /// <summary>La mappa letta al contrario: id VPS → id PM.</summary>
    public static Dictionary<int, int> Inversa(IReadOnlyDictionary<int, RisorseSyncMap.Voce> mappa)
    {
        var inversa = new Dictionary<int, int>(mappa.Count);
        foreach (KeyValuePair<int, RisorseSyncMap.Voce> kv in mappa)
            inversa[kv.Value.RemoteId] = kv.Key;
        return inversa;
    }

    /// <summary>
    /// Una riga del VPS nella forma comune, con gli id tradotti in PM. Ritorna null se il
    /// dipendente VPS non è mappato, o se la commessa VPS non lo è (e la riga non è una FERIE,
    /// che la commessa la perde comunque): la riga si salta (§4.3, «mai cancellata»). Un autore
    /// non mappato diventa null, non ferma la riga.
    /// 🪤 Mai azzerare la commessa in silenzio (lo speculare di <see cref="VersoVps"/>): la riga
    /// entrerebbe in PM senza commessa e alla prima modifica fatta in PM la copia «senza commessa»
    /// tornerebbe sul VPS, che perderebbe il legame.
    /// </summary>
    public static RigaAlloc? DaVps(
        SyncAssignmentDto v,
        IReadOnlyDictionary<int, int> dipendentiVpsPm,
        IReadOnlyDictionary<int, int> commesseVpsPm)
    {
        if (!dipendentiVpsPm.TryGetValue(v.EmployeeId, out int employeeId)) return null;
        int? projectId = null;
        if (v.ProjectId is int p && NormalizzaTipo(v.Tipo) != "FERIE")
        {
            if (!commesseVpsPm.TryGetValue(p, out int pm)) return null;
            projectId = pm;
        }
        int? updatedBy = v.UpdatedBy is int u && dipendentiVpsPm.TryGetValue(u, out int autore) ? autore : null;
        DateTime? updatedAt = v.UpdatedAt is DateTime t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null;
        return new RigaAlloc(employeeId, v.Tipo, v.DataInizio, v.DataFine, projectId, v.Descrizione, updatedBy, updatedAt);
    }

    /// <summary>Una riga di MySQL nella forma comune: <c>updated_at</c> (ora locale) → UTC.</summary>
    public static RigaAlloc DaPm(AllocazionePm a) =>
        new(a.EmployeeId, a.Tipo, DateOnly.FromDateTime(a.DataInizio), DateOnly.FromDateTime(a.DataFine),
            a.ProjectId, a.Descrizione, a.UpdatedBy,
            a.UpdatedAt is DateTime t ? UtcDaLocale(t) : null);

    /// <summary>
    /// Il DTO per <c>POST /api/sync/assignments</c>: id tradotti in VPS (dipendente e commessa
    /// obbligatori — non mappati sollevano, il motore li salta a monte; autore null se non
    /// mappato), <paramref name="idVps"/> null = crea. Service e altra attività non viaggiano
    /// (in PM sono sempre null).
    /// 🪤 Mai azzerare la commessa in silenzio: l'impronta salvata in mappa è quella della riga
    /// PM (con la commessa) e al giro dopo il VPS «senza commessa» vincerebbe, cancellandola in PM.
    /// </summary>
    public static SyncAssignmentUpsertDto VersoVps(
        RigaAlloc r, int? idVps,
        IReadOnlyDictionary<int, RisorseSyncMap.Voce> mappaDipendenti,
        IReadOnlyDictionary<int, RisorseSyncMap.Voce> mappaCommesse)
    {
        if (!mappaDipendenti.TryGetValue(r.EmployeeId, out RisorseSyncMap.Voce dip))
            throw new InvalidOperationException($"Dipendente PM {r.EmployeeId} non mappato sul VPS.");
        int? projectId = null;
        if (r.ProjectId is int p)
        {
            if (!mappaCommesse.TryGetValue(p, out RisorseSyncMap.Voce com))
                throw new InvalidOperationException($"Commessa PM {p} non mappata sul VPS.");
            projectId = com.RemoteId;
        }
        return new SyncAssignmentUpsertDto
        {
            Id = idVps,
            EmployeeId = dip.RemoteId,
            Tipo = r.Tipo,
            DataInizio = r.Inizio,
            DataFine = r.Fine,
            ProjectId = projectId,
            ServiceId = null,
            OtherActivityId = null,
            Descrizione = r.Descrizione,
            UpdatedBy = r.UpdatedBy is int u && mappaDipendenti.TryGetValue(u, out RisorseSyncMap.Voce autore) ? autore.RemoteId : null,
            UpdatedAt = r.UpdatedAtUtc,
        };
    }

    // ── Merge (§4.3) ─────────────────────────────────────────────

    /// <summary>
    /// La tabella di §4.3 per una coppia mappata. <paramref name="improntaSincronizzata"/> è
    /// il <c>synced_hash</c> della mappa (null = mai allineata).
    /// <list type="bullet">
    /// <item>entrambe presenti, impronte uguali → <see cref="AzioneMerge.Niente"/>
    /// (<see cref="AzioneMerge.AggiornaHash"/> se la mappa dice un'altra cosa);</item>
    /// <item>cambiata solo in PM (il VPS è ancora com'era all'ultimo allineamento) → <see cref="AzioneMerge.AggiornaVps"/>;
    /// cambiata solo sul VPS → <see cref="AzioneMerge.AggiornaPm"/>;</item>
    /// <item>cambiate entrambe → CONFLITTO: vince l'<c>UpdatedAtUtc</c> più recente; chi non ce
    /// l'ha perde; nessuno dei due ce l'ha → vince il VPS (è lui la verità storica, §2);</item>
    /// <item>sparita in PM → <see cref="AzioneMerge.CancellaVps"/>; sparita sul VPS →
    /// <see cref="AzioneMerge.CancellaPm"/>: la cancellazione vince SEMPRE, anche se l'altro
    /// lato nel frattempo è cambiato (si conta come conflitto, così finisce nel registro);</item>
    /// <item>sparite entrambe → <see cref="AzioneMerge.AggiornaHash"/>, cioè togliere la mappa.</item>
    /// </list>
    /// </summary>
    public static (AzioneMerge Azione, bool Conflitto) Decidi(RigaAlloc? pm, RigaAlloc? vps, string? improntaSincronizzata)
    {
        if (pm == null && vps == null) return (AzioneMerge.AggiornaHash, false);
        if (pm == null)
            return (AzioneMerge.CancellaVps, improntaSincronizzata != null && Impronta(vps!) != improntaSincronizzata);
        if (vps == null)
            return (AzioneMerge.CancellaPm, improntaSincronizzata != null && Impronta(pm) != improntaSincronizzata);

        string ip = Impronta(pm);
        string iv = Impronta(vps);
        if (ip == iv)
            return (iv == improntaSincronizzata ? AzioneMerge.Niente : AzioneMerge.AggiornaHash, false);
        if (iv == improntaSincronizzata) return (AzioneMerge.AggiornaVps, false);   // solo PM cambiata
        if (ip == improntaSincronizzata) return (AzioneMerge.AggiornaPm, false);    // solo VPS cambiata

        // Cambiate entrambe (o mappa senza impronta): vince l'ultima modifica, null perde, VPS a parità.
        bool vincePm = pm.UpdatedAtUtc != null && (vps.UpdatedAtUtc == null || pm.UpdatedAtUtc > vps.UpdatedAtUtc);
        return (vincePm ? AzioneMerge.AggiornaVps : AzioneMerge.AggiornaPm, true);
    }

    // ── Abbinamento per contenuto ────────────────────────────────

    /// <summary>
    /// Fra le righe SENZA mappa dei due lati, abbina quelle con la stessa impronta, una a una,
    /// in ordine di id: la stessa allocazione presente su entrambi i lati (bootstrap, mappa
    /// persa, riga creata sul VPS e poi la mappa non salvata) entra in mappa invece di essere
    /// creata una seconda volta. Due righe PM identiche e una sola VPS: si abbina la prima, la
    /// seconda verrà creata (com'è giusto: in PM ce ne sono due).
    /// </summary>
    public static List<(int IdPm, int IdVps, string Impronta)> AbbinaPerContenuto(
        IReadOnlyList<(int Id, RigaAlloc Riga)> pmNonMappate,
        IReadOnlyList<(int Id, RigaAlloc Riga)> vpsNonMappate)
    {
        var libereVps = new Dictionary<string, Queue<int>>();
        foreach ((int id, RigaAlloc riga) in vpsNonMappate.OrderBy(x => x.Id))
        {
            string impronta = Impronta(riga);
            if (!libereVps.TryGetValue(impronta, out Queue<int>? coda))
                libereVps[impronta] = coda = new Queue<int>();
            coda.Enqueue(id);
        }

        var coppie = new List<(int IdPm, int IdVps, string Impronta)>();
        foreach ((int id, RigaAlloc riga) in pmNonMappate.OrderBy(x => x.Id))
        {
            string impronta = Impronta(riga);
            if (libereVps.TryGetValue(impronta, out Queue<int>? coda) && coda.Count > 0)
                coppie.Add((id, coda.Dequeue(), impronta));
        }
        return coppie;
    }

    /// <summary>«03/09-05/09» (stesso anno) per il dettaglio del registro.</summary>
    public static string Periodo(RigaAlloc r) =>
        $"{r.Inizio.ToString("dd/MM", CultureInfo.InvariantCulture)}-{r.Fine.ToString("dd/MM", CultureInfo.InvariantCulture)}";
}
