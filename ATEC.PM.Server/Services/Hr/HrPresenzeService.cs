using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Il servizio del cartellino presenze (PIANO-HR-PRESENZE.md, Fase 1): importa le
/// timbrature da EcosAgile in <c>hr_timbrature</c>, rigenera <c>hr_giornate</c> col
/// <see cref="MotoreCartellino"/>, gestisce rettifiche e mappatura dipendenti ↔ Ecos.
///
/// <para><b>Le due tabelle hanno ruoli diversi.</b> <c>hr_timbrature</c> è il grezzo:
/// le righe ECOS rispecchiano Ecos (che del dato è il padrone: se là una timbratura viene
/// corretta, qui si aggiorna la riga con lo stesso <c>id_esterno</c> — l'append-only vieta
/// le correzioni <i>a mano</i>, non il mirror del rilevatore); le correzioni di ATEC PM
/// sono righe separate con <c>origine='RETTIFICA'</c>, autore e motivo. <c>hr_giornate</c>
/// è un risultato: si butta e si ricalcola, mai si corregge.</para>
/// </summary>
public class HrPresenzeService
{
    private const string CursoreKey = "hr_sync_timbrature_da";

    /// <summary>
    /// Margine sottratto al cursore. Un'eventuale sovrapposizione è innocua (l'import è
    /// idempotente), un buco no.
    ///
    /// <para>Il cursore si ricava dal <b>massimo UpdateDate ricevuto</b>, che è l'orologio
    /// DI ECOS — lo stesso su cui l'API valuta il filtro. Solo se nessuna riga porta
    /// UpdateDate si ripiega sul nostro orologio, e allora un'ora di margine copre gli
    /// scarti fra macchine e il cambio dell'ora legale.</para>
    /// </summary>
    private static readonly TimeSpan MargineCursore = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan MargineCursoreOrologioNostro = TimeSpan.FromHours(1);

    /// <summary>Tetto di giornate risanate per giro: il primo import ne ha migliaia, e
    /// vanno fatte, ma a blocchi — il giro dopo riprende da dove ha lasciato.</summary>
    private const int MassimoGiornateRiparate = 5000;

    private readonly DbService _db;
    private readonly EcosClient _ecos;
    private readonly ILogger<HrPresenzeService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HrPresenzeService(DbService db, EcosClient ecos, ILogger<HrPresenzeService> logger)
    {
        _db = db;
        _ecos = ecos;
        _logger = logger;
    }

    // Stato dell'ultimo import, per la pagina (il servizio è singleton).
    public bool ImportInCorso { get; private set; }
    public DateTime? UltimoImport { get; private set; }
    public string UltimoEsito { get; private set; } = "";

    // ── IMPORT DA ECOS ────────────────────────────────────────────────────────

    /// <summary>
    /// Scarica da Ecos le timbrature nuove o modificate e ricalcola le giornate toccate.
    /// <paramref name="completo"/> ignora il cursore e ripassa tutto lo storico (il primo
    /// import lo è comunque: senza cursore si parte dal 2020).
    /// </summary>
    public async Task<HrImportEsitoDto> ImportaAsync(bool completo, CancellationToken ct = default)
    {
        if (!_ecos.Configurato)
            return Fallito("Credenziali Ecos non configurate (sezione Ecos di appsettings): import impossibile.");

        if (!await _gate.WaitAsync(0, ct))
            return Fallito("Import già in corso.");

        ImportInCorso = true;
        try
        {
            // L'istante di riferimento si fissa PRIMA di scaricare: le timbrature nate
            // durante il download le riprenderà il giro dopo, non si perdono.
            DateTime inizio = DateTime.Now;

            using MySqlConnection c = _db.Open();
            DateTime? cursore = completo ? null : LeggiCursore(c);

            string token = await _ecos.TokenAsync(ct);
            List<EcosTimbratura> timbrature = await _ecos.TimbratureAsync(token, cursore, ct);

            HrImportEsitoDto esito = ImportaTimbrature(c, timbrature, completo);

            ScriviCursore(c, NuovoCursore(timbrature, inizio));
            UltimoImport = inizio;
            UltimoEsito = esito.Messaggio;
            _logger.LogInformation("[HR] Import Ecos: {Msg}", esito.Messaggio);
            return esito;
        }
        catch (EcosApiException ex)
        {
            _logger.LogWarning("[HR] Import Ecos fallito: {Msg}", ex.Message);
            UltimoEsito = $"ERRORE: {ex.Message}";
            return Fallito(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un guasto al database non deve uscire come 500 nudo lasciando la pagina
            // Stato all'ultimo esito buono: un import fermo da giorni sarebbe invisibile.
            _logger.LogError(ex, "[HR] Import fallito: {Msg}", ex.Message);
            UltimoEsito = $"ERRORE: {ex.Message}";
            return Fallito($"Import fallito: {ex.Message}");
        }
        finally
        {
            ImportInCorso = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Il cuore dell'import, separato dall'HTTP così i test lo esercitano con dati finti.
    /// Confronta le timbrature scaricate con quelle già presenti (<c>origine='ECOS'</c>,
    /// chiave <c>id_esterno</c>): le nuove si inseriscono, le cambiate si aggiornano
    /// (Ecos ha corretto la timbratura), le identiche non toccano niente. Poi ricalcola
    /// SOLO le giornate toccate — compresa quella <b>vecchia</b> di una timbratura
    /// spostata di giorno, che altrimenti resterebbe calcolata su dati che non ci sono più.
    ///
    /// <para><paramref name="completo"/> = lo scarico è l'intero storico, quindi è anche
    /// l'unico momento in cui si possono riconoscere le <b>cancellazioni</b>: una riga
    /// nostra che là non esiste più va tolta, altrimenti il cartellino continuerebbe a
    /// calcolare su una timbratura fantasma che nessuno può più togliere dalla UI.</para>
    /// </summary>
    internal HrImportEsitoDto ImportaTimbrature(
        MySqlConnection c, IReadOnlyList<EcosTimbratura> timbrature, bool completo = false)
    {
        Dictionary<string, int> mappa = MappaEcos(c);

        // Doppioni di id nello scarico (pagine sovrapposte): vince l'ultimo visto.
        // 🪤 Anche le timbrature di codici NON mappati entrano in `perId`: se una riga con
        // quell'id_esterno è già in casa nostra (il dipendente è stato scollegato dopo un
        // import) la correzione di Ecos va comunque applicata. Il codice mappato serve a
        // sapere DI CHI è una timbratura nuova, non a decidere se aggiornare una vecchia.
        var perId = new Dictionary<string, EcosTimbratura>(StringComparer.OrdinalIgnoreCase);
        var nonAbbinati = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EcosTimbratura t in timbrature)
        {
            if (!mappa.ContainsKey(t.EmplCode)) nonAbbinati.Add($"{t.EmplCode} — {t.Nome}");
            perId[t.IdEsterno] = t;
        }

        // Stato attuale delle righe ECOS coinvolte, a blocchi (l'IN con migliaia di id
        // esiste ma non serve: 500 alla volta bastano e non stressano il parser).
        var esistenti = new Dictionary<string, RigaEsistente>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] blocco in ABlocchi(perId.Keys, 500))
        {
            foreach (RigaEsistente riga in c.Query<RigaEsistente>(
                @"SELECT id_esterno AS IdEsterno, id AS Id, employee_id AS EmployeeId,
                         giorno AS Giorno, orario AS Orario, verso AS Verso, luogo AS Luogo
                  FROM hr_timbrature
                  WHERE origine = 'ECOS' AND id_esterno IN @Ids",
                new { Ids = blocco }))
            {
                esistenti[riga.IdEsterno] = riga;
            }
        }

        int nuove = 0, aggiornate = 0, rimosse = 0;
        var giorniToccati = new HashSet<(int EmployeeId, DateTime Giorno)>();

        using (MySqlTransaction tran = c.BeginTransaction())
        {
            foreach (EcosTimbratura t in perId.Values)
            {
                bool mappato = mappa.TryGetValue(t.EmplCode, out int employeeIdMappato);
                DateTime giorno = t.Orario.Date;

                if (!esistenti.TryGetValue(t.IdEsterno, out RigaEsistente? vecchia))
                {
                    // Timbratura nuova di un codice senza dipendente: si scarta (finirà
                    // nei non abbinati). Senza mappatura non sapremmo a chi darla.
                    if (!mappato) continue;

                    c.Execute(@"
                        INSERT INTO hr_timbrature (employee_id, giorno, orario, verso, origine, id_esterno, luogo)
                        VALUES (@EmployeeId, @Giorno, @Orario, @Verso, 'ECOS', @IdEsterno, @Luogo)",
                        new { EmployeeId = employeeIdMappato, Giorno = giorno, t.Orario, t.Verso, t.IdEsterno, t.Luogo },
                        tran);
                    nuove++;
                    giorniToccati.Add((employeeIdMappato, giorno));
                    continue;
                }

                // Riga già nostra: il proprietario resta quello che ha, a meno che la
                // mappatura attuale dica altro.
                int employeeId = mappato ? employeeIdMappato : vecchia.EmployeeId;
                if (vecchia.Orario != t.Orario
                    || !string.Equals(vecchia.Verso, t.Verso, StringComparison.OrdinalIgnoreCase)
                    || vecchia.EmployeeId != employeeId
                    || !string.Equals(vecchia.Luogo ?? "", t.Luogo ?? "", StringComparison.Ordinal))
                {
                    c.Execute(@"
                        UPDATE hr_timbrature
                        SET employee_id = @EmployeeId, giorno = @Giorno, orario = @Orario,
                            verso = @Verso, luogo = @Luogo
                        WHERE id = @Id",
                        new { EmployeeId = employeeId, Giorno = giorno, t.Orario, t.Verso, t.Luogo, vecchia.Id },
                        tran);
                    aggiornate++;
                    giorniToccati.Add((vecchia.EmployeeId, vecchia.Giorno));
                    giorniToccati.Add((employeeId, giorno));
                }
            }

            if (completo)
                rimosse = RimuoviCancellateSuEcos(c, tran, perId.Keys, mappa.Values, giorniToccati);

            tran.Commit();
        }

        // Le giornate rimaste «in corso» in un import precedente (entrata senza uscita a
        // fine giornata) non riceveranno mai altre timbrature: si chiudono d'ufficio al
        // primo import del giorno dopo, altrimenti resterebbero sospese per sempre.
        foreach (var sospesa in c.Query<(int EmployeeId, DateTime Giorno)>(
            "SELECT employee_id AS EmployeeId, giorno AS Giorno FROM hr_giornate WHERE nota = 'Giornata in corso' AND giorno < CURDATE()"))
        {
            giorniToccati.Add(sospesa);
        }

        foreach ((int employeeId, DateTime giorno) in giorniToccati)
            RicalcolaGiornata(c, employeeId, giorno);

        // Rete di sicurezza: qualunque giornata rimasta indietro (ricalcolo interrotto a
        // metà, regole cambiate, cartellino orfano) si rimette in pari qui.
        int riparate = RiparaGiornate(c);

        string messaggio =
            $"{nuove} timbrature nuove, {aggiornate} aggiornate, {giorniToccati.Count} giornate ricalcolate"
            + (rimosse > 0 ? $", {rimosse} cancellate su Ecos rimosse" : "")
            + (riparate > 0 ? $", {riparate} giornate rimesse in pari" : "")
            + (nonAbbinati.Count > 0 ? $"; {nonAbbinati.Count} codici Ecos senza dipendente collegato" : "");

        return new HrImportEsitoDto
        {
            Successo = true,
            Messaggio = messaggio,
            TimbratureNuove = nuove,
            TimbratureAggiornate = aggiornate,
            GiornateRicalcolate = giorniToccati.Count + riparate,
            NonAbbinati = nonAbbinati.ToList(),
        };
    }

    /// <summary>
    /// Toglie le righe ECOS che nello scarico completo non compaiono più: là sono state
    /// cancellate, e siccome il grezzo non si corregge a mano nessuno potrebbe più
    /// levarle. Si limita ai dipendenti <b>mappati</b>: per gli altri lo scarico non è
    /// una fotografia completa (le loro timbrature nuove le stiamo scartando), e
    /// confrontarli vorrebbe dire cancellare righe legittime.
    /// </summary>
    private int RimuoviCancellateSuEcos(
        MySqlConnection c, MySqlTransaction tran,
        IEnumerable<string> idVisti, IEnumerable<int> dipendentiMappati,
        HashSet<(int, DateTime)> giorniToccati)
    {
        var visti = new HashSet<string>(idVisti, StringComparer.OrdinalIgnoreCase);
        int[] dipendenti = dipendentiMappati.Distinct().ToArray();
        if (dipendenti.Length == 0) return 0;

        List<RigaEsistente> nostre = c.Query<RigaEsistente>(
            @"SELECT id AS Id, id_esterno AS IdEsterno, employee_id AS EmployeeId, giorno AS Giorno,
                     orario AS Orario, verso AS Verso, luogo AS Luogo
              FROM hr_timbrature
              WHERE origine = 'ECOS' AND employee_id IN @Dipendenti",
            new { Dipendenti = dipendenti }, tran).ToList();

        List<RigaEsistente> sparite = nostre
            .Where(r => r.IdEsterno != null && !visti.Contains(r.IdEsterno))
            .ToList();
        if (sparite.Count == 0) return 0;

        foreach (long[] blocco in ABlocchi(sparite.Select(r => r.Id), 500))
            c.Execute("DELETE FROM hr_timbrature WHERE id IN @Ids", new { Ids = blocco }, tran);

        foreach (RigaEsistente r in sparite)
            giorniToccati.Add((r.EmployeeId, r.Giorno));

        _logger.LogInformation(
            "[HR] {N} timbrature cancellate su Ecos rimosse anche qui (import completo).", sparite.Count);
        return sparite.Count;
    }

    /// <summary>
    /// Rimette in pari i cartellini rimasti indietro: giornate con timbrature ma senza
    /// riga in <c>hr_giornate</c> (ricalcolo interrotto a metà), giornate calcolate con
    /// regole più vecchie (<c>regole_versione</c>) e giornate più vecchie della loro
    /// ultima timbratura. Toglie anche i cartellini orfani, rimasti senza timbrature.
    ///
    /// <para>Senza questa passata un ricalcolo interrotto lasciava cartellini mancanti
    /// <b>per sempre</b>: l'import successivo trovava le timbrature identiche, non aveva
    /// niente da ricalcolare e nessuno si accorgeva del buco.</para>
    /// </summary>
    public int RiparaGiornate(MySqlConnection c)
    {
        List<(int EmployeeId, DateTime Giorno)> daRifare = c.Query<(int, DateTime)>(
            @"SELECT t.employee_id, t.giorno
              FROM (SELECT employee_id, giorno, MAX(creata_il) AS ultima
                    FROM hr_timbrature GROUP BY employee_id, giorno) t
              LEFT JOIN hr_giornate g
                     ON g.employee_id = t.employee_id AND g.giorno = t.giorno
              WHERE g.id IS NULL
                 OR g.regole_versione < @Versione
                 OR g.calcolato_il < t.ultima
              ORDER BY t.giorno DESC
              LIMIT @Limite",
            new { Versione = RegoleCartellino.Versione, Limite = MassimoGiornateRiparate }).ToList();

        foreach ((int employeeId, DateTime giorno) in daRifare)
            RicalcolaGiornata(c, employeeId, giorno);

        // Cartellini senza più timbrature sotto (le ultime sono state cancellate su Ecos).
        int orfani = c.Execute(
            @"DELETE g FROM hr_giornate g
              LEFT JOIN hr_timbrature t
                     ON t.employee_id = g.employee_id AND t.giorno = g.giorno
              WHERE t.id IS NULL");

        int totale = daRifare.Count + orfani;
        if (totale > 0)
            _logger.LogInformation(
                "[HR] Rimesse in pari {N} giornate ({Orfane} cartellini orfani rimossi).", totale, orfani);
        return totale;
    }

    // ── RICALCOLO GIORNATE ────────────────────────────────────────────────────

    /// <summary>
    /// Butta e rifà il cartellino di (dipendente, giorno) dalle timbrature grezze
    /// (ECOS e RETTIFICA insieme: il motore non distingue). Zero timbrature = la
    /// giornata si dissolve, non resta un cartellino orfano.
    /// </summary>
    public void RicalcolaGiornata(MySqlConnection c, int employeeId, DateTime giorno)
    {
        List<TimbraturaGrezza> grezze = c.Query<(DateTime Orario, string Verso)>(
                @"SELECT orario AS Orario, verso AS Verso
                  FROM hr_timbrature
                  WHERE employee_id = @EmployeeId AND giorno = @Giorno
                  ORDER BY orario",
                new { EmployeeId = employeeId, Giorno = giorno.Date })
            .Select(t => new TimbraturaGrezza(t.Orario, t.Verso))
            .ToList();

        if (grezze.Count == 0)
        {
            c.Execute("DELETE FROM hr_giornate WHERE employee_id = @EmployeeId AND giorno = @Giorno",
                new { EmployeeId = employeeId, Giorno = giorno.Date });
            return;
        }

        // ConStraordinari per tutti finché l'anagrafica non porta il flag (il motore VB
        // aveva IncludeOvertime per dipendente; arriverà con la pagina di configurazione).
        Cartellino cart = MotoreCartellino.Calcola(giorno.Date, grezze, DateTime.Today);

        c.Execute(@"
            INSERT INTO hr_giornate
                (employee_id, giorno, entrata1, uscita1, entrata2, uscita2,
                 minuti_ordinari, minuti_straord, minuti_pausa, fasce_json, nota, anomalia,
                 calcolato_il, regole_versione)
            VALUES
                (@EmployeeId, @Giorno, @Entrata1, @Uscita1, @Entrata2, @Uscita2,
                 @MinutiOrdinari, @MinutiStraord, @MinutiPausa, @FasceJson, @Nota, @Anomalia,
                 NOW(), @RegoleVersione)
            ON DUPLICATE KEY UPDATE
                entrata1 = VALUES(entrata1), uscita1 = VALUES(uscita1),
                entrata2 = VALUES(entrata2), uscita2 = VALUES(uscita2),
                minuti_ordinari = VALUES(minuti_ordinari), minuti_straord = VALUES(minuti_straord),
                minuti_pausa = VALUES(minuti_pausa), fasce_json = VALUES(fasce_json),
                nota = VALUES(nota), anomalia = VALUES(anomalia),
                calcolato_il = NOW(), regole_versione = VALUES(regole_versione)",
            new
            {
                EmployeeId = employeeId,
                Giorno = giorno.Date,
                cart.Entrata1,
                cart.Uscita1,
                cart.Entrata2,
                cart.Uscita2,
                MinutiOrdinari = MinutiDa(cart.OreOrdinarie),
                MinutiStraord = MinutiDa(cart.Straordinario),
                MinutiPausa = MinutiDa(cart.Pausa),
                FasceJson = FasceJson(cart),
                cart.Nota,
                cart.Anomalia,
                RegoleVersione = RegoleCartellino.Versione,
            });
    }

    // ── CARTELLINO MENSILE ────────────────────────────────────────────────────

    public HrCartellinoMeseDto CartellinoMese(int employeeId, int anno, int mese)
    {
        var primo = new DateTime(anno, mese, 1);
        DateTime ultimo = primo.AddMonths(1).AddDays(-1);

        using MySqlConnection c = _db.Open();

        var dipendente = c.QueryFirstOrDefault<(string Nome, string? EcosCode)>(
            @"SELECT CONCAT_WS(' ', first_name, last_name) AS Nome, ecos_empl_code AS EcosCode
              FROM employees WHERE id = @Id", new { Id = employeeId });

        var giornate = c.Query<GiornataRiga>(
                @"SELECT giorno, entrata1, uscita1, entrata2, uscita2,
                         minuti_ordinari AS MinutiOrdinari, minuti_straord AS MinutiStraord,
                         minuti_pausa AS MinutiPausa, fasce_json AS FasceJson, nota, anomalia
                  FROM hr_giornate
                  WHERE employee_id = @Id AND giorno BETWEEN @Da AND @A",
                new { Id = employeeId, Da = primo, A = ultimo })
            .ToDictionary(g => g.Giorno.Date);

        var timbrature = c.Query<TimbraturaRiga>(
                @"SELECT t.id, t.giorno, t.orario, t.verso, t.origine, t.motivo,
                         CONCAT_WS(' ', e.first_name, e.last_name) AS CreataDa
                  FROM hr_timbrature t
                  LEFT JOIN employees e ON e.id = t.creata_da
                  WHERE t.employee_id = @Id AND t.giorno BETWEEN @Da AND @A
                  ORDER BY t.orario",
                new { Id = employeeId, Da = primo, A = ultimo })
            .GroupBy(t => t.Giorno.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dto = new HrCartellinoMeseDto
        {
            EmployeeId = employeeId,
            EmployeeName = dipendente.Nome ?? "",
            Anno = anno,
            Mese = mese,
            EcosCollegato = !string.IsNullOrWhiteSpace(dipendente.EcosCode),
        };

        for (DateTime giorno = primo; giorno <= ultimo; giorno = giorno.AddDays(1))
        {
            var riga = new HrGiornataDto
            {
                Giorno = giorno,
                Festivo = RegoleCartellino.EFestivo(giorno),
            };

            if (giornate.TryGetValue(giorno, out GiornataRiga? g))
            {
                bool nonCalcolabile = g.Nota.StartsWith("⚠ ERR");
                riga.HaDati = true;
                riga.Entrata1 = g.Entrata1 ?? "";
                riga.Uscita1 = g.Uscita1 ?? "";
                riga.Entrata2 = g.Entrata2 ?? "";
                riga.Uscita2 = g.Uscita2 ?? "";
                riga.OreOrdinarie = nonCalcolabile ? "---" : RegoleCartellino.Durata(g.MinutiOrdinari);
                riga.Straordinario = nonCalcolabile ? "---" : RegoleCartellino.Durata(g.MinutiStraord);
                riga.Pausa = RegoleCartellino.Durata(g.MinutiPausa);
                riga.Fasce = LeggiFasce(g.FasceJson);
                riga.Nota = g.Nota;
                riga.Anomalia = g.Anomalia;
            }

            if (timbrature.TryGetValue(giorno, out List<TimbraturaRiga>? grezze))
            {
                riga.Timbrature = grezze.Select(t => new HrTimbraturaDto
                {
                    Id = t.Id,
                    Orario = t.Orario,
                    Verso = t.Verso,
                    Origine = t.Origine,
                    Motivo = t.Motivo,
                    CreataDa = t.CreataDa,
                }).ToList();
            }

            dto.Giornate.Add(riga);
        }

        return dto;
    }

    // ── RETTIFICHE ────────────────────────────────────────────────────────────

    /// <summary>
    /// Aggiunge una timbratura di rettifica (la originale, se c'è, resta: il grezzo non si
    /// corregge) e ricalcola la giornata. Autore e motivo sono obbligatori: senza, la
    /// cronistoria di chi ha cambiato cosa non esiste.
    ///
    /// <para>🪤 <b>Nessuno rettifica sé stesso.</b> Il piano (§8, «dimenticanza
    /// timbratura») vuole il giustificativo del dipendente e l'approvazione del
    /// responsabile: se chi ha la scrittura potesse aggiungersi un'uscita alle 19:00
    /// sarebbe giudice e parte, e quello straordinario nascerebbe senza un secondo occhio.
    /// Oggi la scrittura ce l'ha solo l'Admin, ma la Fase 2 la darà ai 12 responsabili.</para>
    /// </summary>
    public string? Rettifica(HrRettificaRequest req, int autoreId)
    {
        string verso = (req.Verso ?? "").Trim().ToUpperInvariant();
        if (verso is not ("IN" or "OUT"))
            return "Verso non valido: ammessi IN e OUT.";
        if (string.IsNullOrWhiteSpace(req.Motivo))
            return "Il motivo della rettifica è obbligatorio.";
        if (req.EmployeeId == autoreId)
            return "Non puoi rettificare il tuo cartellino: la correzione la registra un'altra persona.";

        using MySqlConnection c = _db.Open();
        int esiste = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM employees WHERE id = @Id AND status <> 'TERMINATED'",
            new { Id = req.EmployeeId });
        if (esiste == 0) return "Dipendente non trovato o cessato.";

        c.Execute(@"
            INSERT INTO hr_timbrature (employee_id, giorno, orario, verso, origine, motivo, creata_da)
            VALUES (@EmployeeId, @Giorno, @Orario, @Verso, 'RETTIFICA', @Motivo, @Autore)",
            new
            {
                req.EmployeeId,
                Giorno = req.Orario.Date,
                req.Orario,
                Verso = verso,
                Motivo = req.Motivo.Trim(),
                Autore = autoreId,
            });

        RicalcolaGiornata(c, req.EmployeeId, req.Orario.Date);
        return null;
    }

    /// <summary>
    /// Toglie una rettifica sbagliata. SOLO rettifiche: il grezzo dei rilevatori non si
    /// cancella mai, nemmeno da Admin. E non la propria: chi non può aggiungersi una
    /// timbratura non deve nemmeno poter togliere quella che il responsabile gli ha messo.
    /// <para>La cancellazione è fisica, quindi resta scritta nel log applicativo (chi,
    /// quando, su chi): altrimenti una correzione sgradita sparirebbe senza tracce.</para>
    /// </summary>
    public string? EliminaRettifica(long id, int autoreId)
    {
        using MySqlConnection c = _db.Open();
        var riga = c.QueryFirstOrDefault<RigaDaEliminare>(
            @"SELECT employee_id AS EmployeeId, giorno AS Giorno, origine AS Origine,
                     orario AS Orario, verso AS Verso, motivo AS Motivo
              FROM hr_timbrature WHERE id = @Id",
            new { Id = id });

        if (riga == null) return "Timbratura non trovata.";
        if (!string.Equals(riga.Origine, "RETTIFICA", StringComparison.OrdinalIgnoreCase))
            return "Si possono eliminare solo le rettifiche: il grezzo del rilevatore resta.";
        if (riga.EmployeeId == autoreId)
            return "Non puoi eliminare una rettifica sul tuo cartellino.";

        c.Execute("DELETE FROM hr_timbrature WHERE id = @Id", new { Id = id });
        _logger.LogInformation(
            "[HR] Rettifica eliminata da dipendente {Autore}: era {Verso} del {Orario:yyyy-MM-dd HH:mm} " +
            "sul cartellino di {Dipendente}, motivo «{Motivo}».",
            autoreId, riga.Verso, riga.Orario, riga.EmployeeId, riga.Motivo);

        RicalcolaGiornata(c, riga.EmployeeId, riga.Giorno);
        return null;
    }

    // ── MAPPATURA DIPENDENTI ↔ ECOS ───────────────────────────────────────────

    /// <summary>
    /// L'elenco per la pagina di mappatura: i dipendenti in forza <b>più i cessati che
    /// hanno ancora un codice collegato</b>.
    /// <para>🪤 I cessati vanno mostrati: restano nella mappa dell'import (le loro ultime
    /// timbrature devono arrivare), quindi finché tengono il codice nessun nuovo assunto
    /// può riceverlo — e senza vederli in elenco non ci sarebbe modo di scollegarli.</para>
    /// </summary>
    public List<HrMappaturaRigaDto> Mappatura()
    {
        using MySqlConnection c = _db.Open();
        return c.Query<HrMappaturaRigaDto>(
            @"SELECT id AS EmployeeId,
                     CONCAT_WS(' ', first_name, last_name,
                               CASE WHEN status = 'TERMINATED' THEN '(cessato)' ELSE NULL END) AS Nome,
                     ecos_empl_code AS EcosEmplCode
              FROM employees
              WHERE user_role <> 'ADMIN' AND first_name NOT LIKE '[%'
                AND (status <> 'TERMINATED'
                     OR (ecos_empl_code IS NOT NULL AND ecos_empl_code <> ''))
              ORDER BY (status = 'TERMINATED'), last_name, first_name").ToList();
    }

    /// <summary>Collega (o scollega, con codice vuoto) un dipendente al suo EmplCode Ecos.
    /// Un codice non può stare su due persone: le timbrature saprebbero di chi sono a caso.
    /// <para>La difesa vera è l'indice UNIQUE (migrazione M108): il controllo qui sotto
    /// serve a dare il nome di chi occupa il codice, ma fra la lettura e la scrittura
    /// passa un istante, e in quell'istante due salvataggi quasi simultanei passerebbero
    /// entrambi.</para></summary>
    public string? AggiornaMappatura(int employeeId, string? ecosEmplCode)
    {
        string? codice = string.IsNullOrWhiteSpace(ecosEmplCode) ? null : ecosEmplCode.Trim();

        using MySqlConnection c = _db.Open();
        if (codice != null)
        {
            string? occupatoDa = c.ExecuteScalar<string?>(
                @"SELECT CONCAT_WS(' ', first_name, last_name) FROM employees
                  WHERE ecos_empl_code = @Codice AND id <> @Id LIMIT 1",
                new { Codice = codice, Id = employeeId });
            if (occupatoDa != null)
                return $"Il codice Ecos {codice} è già collegato a {occupatoDa}.";
        }

        try
        {
            int toccate = c.Execute(
                "UPDATE employees SET ecos_empl_code = @Codice WHERE id = @Id",
                new { Codice = codice, Id = employeeId });
            return toccate == 0 ? "Dipendente non trovato." : null;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return $"Il codice Ecos {codice} è appena stato collegato a un altro dipendente.";
        }
    }

    // ── STATO ─────────────────────────────────────────────────────────────────

    public HrStatoDto Stato()
    {
        using MySqlConnection c = _db.Open();
        var conteggi = c.QuerySingle<(long Timbrature, long Giornate, int Collegati, int Attivi)>(@"
            SELECT (SELECT COUNT(*) FROM hr_timbrature) AS Timbrature,
                   (SELECT COUNT(*) FROM hr_giornate) AS Giornate,
                   (SELECT COUNT(*) FROM employees
                     WHERE status <> 'TERMINATED' AND ecos_empl_code IS NOT NULL AND ecos_empl_code <> '') AS Collegati,
                   (SELECT COUNT(*) FROM employees
                     WHERE status <> 'TERMINATED' AND user_role <> 'ADMIN' AND first_name NOT LIKE '[%') AS Attivi");

        return new HrStatoDto
        {
            Configurato = _ecos.Configurato,
            ImportInCorso = ImportInCorso,
            UltimoImport = UltimoImport,
            UltimoEsito = UltimoEsito,
            TimbratureTotali = conteggi.Timbrature,
            GiornateTotali = conteggi.Giornate,
            DipendentiCollegati = conteggi.Collegati,
            DipendentiAttivi = conteggi.Attivi,
        };
    }

    // ── ATTREZZI ──────────────────────────────────────────────────────────────

    private static Dictionary<string, int> MappaEcos(MySqlConnection c)
    {
        var mappa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var riga in c.Query<(int Id, string Codice)>(
            "SELECT id AS Id, ecos_empl_code AS Codice FROM employees WHERE ecos_empl_code IS NOT NULL AND ecos_empl_code <> ''"))
        {
            mappa[riga.Codice.Trim()] = riga.Id;
        }
        return mappa;
    }

    /// <summary>
    /// Il nuovo cursore: il <b>massimo UpdateDate ricevuto</b> meno un margine — è
    /// l'orologio di Ecos, lo stesso su cui l'API valuta il filtro, quindi i due valori
    /// sono confrontabili davvero.
    ///
    /// <para>🪤 Il ripiego sul nostro orologio (nessuna riga con UpdateDate, o scarico
    /// vuoto) prende un margine di un'ora: fra due macchine c'è sempre uno scarto, e la
    /// notte del cambio d'ora ce n'è una intera. Con dieci minuti, le correzioni nate
    /// dentro quella finestra non sarebbero tornate mai più.</para>
    /// </summary>
    internal static DateTime NuovoCursore(IReadOnlyList<EcosTimbratura> timbrature, DateTime inizio)
    {
        DateTime? massimo = timbrature
            .Where(t => t.UpdateDate.HasValue)
            .Select(t => t.UpdateDate!.Value)
            .DefaultIfEmpty()
            .Max();

        return massimo is { } m && m != default
            ? m - MargineCursore
            : inizio - MargineCursoreOrologioNostro;
    }

    private static DateTime? LeggiCursore(MySqlConnection c)
    {
        string? valore = c.ExecuteScalar<string?>(
            "SELECT config_value FROM app_config WHERE config_key = @K", new { K = CursoreKey });
        return DateTime.TryParse(valore, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out DateTime data)
            ? data
            : null;
    }

    private static void ScriviCursore(MySqlConnection c, DateTime valore)
    {
        c.Execute(@"
            INSERT INTO app_config (config_key, config_value, description)
            VALUES (@K, @V, 'HR: importate da Ecos le timbrature con UpdateDate >= di questo istante')
            ON DUPLICATE KEY UPDATE config_value = @V",
            new { K = CursoreKey, V = valore.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) });
    }

    /// <summary>«8h 30m» → 510. «---», vuoto o irriconoscibile → 0 (la nota dice perché).</summary>
    internal static int MinutiDa(string? durata)
    {
        if (string.IsNullOrWhiteSpace(durata)) return 0;
        string[] parti = durata.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parti.Length != 2) return 0;
        if (!parti[0].EndsWith('h') || !parti[1].EndsWith('m')) return 0;
        if (!int.TryParse(parti[0][..^1], out int ore)) return 0;
        if (!int.TryParse(parti[1][..^1], out int minuti)) return 0;
        return ore * 60 + minuti;
    }

    /// <summary>
    /// Le sole fasce con un valore vero, come JSON; null se non ce ne sono.
    /// 🪤 Si escludono anche i «---»: nelle giornate non calcolabili il motore li mette in
    /// tutte e nove le fasce, e finivano nel database come nove voci di straordinario
    /// fittizie (a video, nove righe di tooltip senza senso).
    /// </summary>
    internal static string? FasceJson(Cartellino cart)
    {
        Dictionary<string, string> nonZero = cart.Fasce
            .Where(f => f.Value is not ("0h 0m" or "---") && !string.IsNullOrEmpty(f.Value))
            .ToDictionary(f => f.Key, f => f.Value);
        return nonZero.Count == 0 ? null : JsonSerializer.Serialize(nonZero);
    }

    private static Dictionary<string, string> LeggiFasce(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static IEnumerable<T[]> ABlocchi<T>(IEnumerable<T> valori, int dimensione)
    {
        var blocco = new List<T>(dimensione);
        foreach (T v in valori)
        {
            blocco.Add(v);
            if (blocco.Count == dimensione)
            {
                yield return blocco.ToArray();
                blocco.Clear();
            }
        }
        if (blocco.Count > 0) yield return blocco.ToArray();
    }

    private static HrImportEsitoDto Fallito(string messaggio) =>
        new() { Successo = false, Messaggio = messaggio };

    /// <summary>Una riga ECOS già in casa nostra, come serve al confronto dell'import.</summary>
    private sealed class RigaEsistente
    {
        public long Id { get; set; }
        public string? IdEsterno { get; set; }
        public int EmployeeId { get; set; }
        public DateTime Giorno { get; set; }
        public DateTime Orario { get; set; }
        public string Verso { get; set; } = "";
        public string? Luogo { get; set; }
    }

    // Righe di lettura Dapper (classi: i value-tuple non reggono le colonne nullable).
    private sealed class GiornataRiga
    {
        public DateTime Giorno { get; set; }
        public string? Entrata1 { get; set; }
        public string? Uscita1 { get; set; }
        public string? Entrata2 { get; set; }
        public string? Uscita2 { get; set; }
        public int MinutiOrdinari { get; set; }
        public int MinutiStraord { get; set; }
        public int MinutiPausa { get; set; }
        public string? FasceJson { get; set; }
        public string Nota { get; set; } = "";
        public bool Anomalia { get; set; }
    }

    private sealed class RigaDaEliminare
    {
        public int EmployeeId { get; set; }
        public DateTime Giorno { get; set; }
        public string Origine { get; set; } = "";
        public DateTime Orario { get; set; }
        public string Verso { get; set; } = "";
        public string? Motivo { get; set; }
    }

    private sealed class TimbraturaRiga
    {
        public long Id { get; set; }
        public DateTime Giorno { get; set; }
        public DateTime Orario { get; set; }
        public string Verso { get; set; } = "";
        public string Origine { get; set; } = "";
        public string? Motivo { get; set; }
        public string? CreataDa { get; set; }
    }
}
