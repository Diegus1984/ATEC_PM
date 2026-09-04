using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MySqlConnector;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Import una-tantum dello storico offerte dall'archivio dell'applicativo autonomo
/// «ATEC Offerte» (<c>atec_offerte.json</c>, alias <c>seed.json</c>): 1.795 offerte dal 2022 e
/// 422 clienti dell'esportazione «Soggetti» del gestionale.
///
/// <para><b>Ripetibile.</b> Ogni riga porta con sé il proprio <c>legacy_id</c> (<c>r4</c> = riga 4
/// del foglio Detail): rieseguire l'import non duplica niente e recupera solo ciò che mancava.
/// Non si può usare anno+numero+sigla come chiave — i quattro duplicati 282-285 del 2025 li
/// condividono tutti e tre.</para>
///
/// <para><b>Non sovrascrive mai.</b> Sui clienti che esistono già riempie soltanto i campi vuoti:
/// l'anagrafica viva di ATEC PM vale più di una fotografia del gestionale di settembre.</para>
/// </summary>
public sealed class SalesOfferImportService
{
    private readonly ILogger<SalesOfferImportService>? _log;

    public SalesOfferImportService(ILogger<SalesOfferImportService>? log = null) => _log = log;

    private static readonly JsonSerializerOptions Opzioni = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Legge l'archivio e lo riversa nel registro.
    /// </summary>
    /// <param name="simulazione">
    /// Se true legge, abbina e conta <b>senza scrivere niente</b>: il rapporto dice cosa
    /// succederebbe. È il modo giusto di provarlo in produzione prima di farlo davvero.
    /// </param>
    public SalesOfferImportReport Importa(MySqlConnection c, Stream json, int? me, bool simulazione = false)
    {
        var cronometro = Stopwatch.StartNew();
        var rapporto = new SalesOfferImportReport { Simulazione = simulazione };

        SeedArchivio archivio = JsonSerializer.Deserialize<SeedArchivio>(json, Opzioni)
                                ?? throw new InvalidOperationException("Archivio illeggibile: JSON non valido.");

        rapporto.ClientiLetti = archivio.Clienti.Count;
        rapporto.OfferteLette = archivio.Offers.Count;
        if (rapporto.OfferteLette == 0)
            rapporto.Avvisi.Add("L'archivio non contiene offerte: file sbagliato?");

        using MySqlTransaction tx = c.BeginTransaction();

        Dictionary<string, int> clienteSeedAId = ImportaClienti(c, tx, archivio, rapporto, simulazione);
        ImportaOfferte(c, tx, archivio, rapporto, clienteSeedAId, me, simulazione);

        if (simulazione) tx.Rollback(); else tx.Commit();

        rapporto.DurataMs = (int)cronometro.ElapsedMilliseconds;
        _log?.LogInformation(
            "[ImportOfferte] {Sim}clienti {CC} creati / {CA} arricchiti · offerte {OI} inserite / {OG} già presenti · collegate cliente {LC}, commessa {LP} · {Ms} ms",
            simulazione ? "SIMULAZIONE — " : "", rapporto.ClientiCreati, rapporto.ClientiArricchiti,
            rapporto.OfferteInserite, rapporto.OfferteGiaPresenti, rapporto.CollegateACliente,
            rapporto.CollegateACommessa, rapporto.DurataMs);

        return rapporto;
    }

    // ══════════════════════════════════════════════════════════════
    // RUBRICA
    // ══════════════════════════════════════════════════════════════

    private sealed record ClienteEsistente(int Id, string Nome, string Codice, string Piva);

    /// <summary>
    /// Allinea i 422 soggetti alla rubrica e restituisce la mappa id-del-seed → id-in-rubrica.
    ///
    /// <para>Ordine di riconoscimento: <b>codice gestionale</b> (405 su 422 lo trovano),
    /// poi <b>P. IVA</b>, poi il <b>nome normalizzato</b>. Chi non si riconosce si crea.</para>
    /// </summary>
    private static Dictionary<string, int> ImportaClienti(
        MySqlConnection c, MySqlTransaction tx, SeedArchivio archivio,
        SalesOfferImportReport rapporto, bool simulazione)
    {
        List<ClienteEsistente> esistenti = c.Query<ClienteEsistente>(
            @"SELECT id AS Id, company_name AS Nome, COALESCE(easyfatt_code,'') AS Codice,
                     COALESCE(vat_number,'') AS Piva
              FROM customers", transaction: tx).ToList();

        var perCodice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var perPiva = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var perNome = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ClienteEsistente e in esistenti)
        {
            string cod = ChiaveCodice(e.Codice);
            if (cod.Length > 0) perCodice.TryAdd(cod, e.Id);
            if (e.Piva.Trim().Length > 0) perPiva.TryAdd(e.Piva.Trim(), e.Id);
            string k = SalesOfferMatching.KeyName(e.Nome);
            if (k.Length > 0) perNome.TryAdd(k, e.Id);
        }

        var mappa = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (SeedCliente cl in archivio.Clienti)
        {
            string nome = (cl.Nome ?? "").Trim();
            if (nome.Length == 0) { rapporto.Avvisi.Add($"Cliente «{cl.Id}» senza nome: saltato."); continue; }

            string codice = ChiaveCodice(cl.Codice);
            string piva = (cl.Piva ?? "").Trim();

            int? id = null;
            if (codice.Length > 0 && perCodice.TryGetValue(codice, out int viaCodice)) id = viaCodice;
            else if (piva.Length > 0 && perPiva.TryGetValue(piva, out int viaPiva)) id = viaPiva;
            else if (perNome.TryGetValue(SalesOfferMatching.KeyName(nome), out int viaNome)) id = viaNome;

            if (id == null)
            {
                if (simulazione)
                {
                    rapporto.ClientiCreati++;
                    // In simulazione non c'è un id vero: si segna il posto per non far
                    // sembrare «senza cliente» le offerte che dopo l'import lo avrebbero.
                    if (!string.IsNullOrEmpty(cl.Id)) mappa[cl.Id] = 0;
                    continue;
                }

                // vat_number ha una UNIQUE e il default stringa vuota: due clienti senza P. IVA
                // andrebbero in collisione. NULL invece MySQL lo ammette ripetuto.
                int nuovo = (int)c.ExecuteScalar<long>(@"
                    INSERT INTO customers
                        (company_name, contact_name, email, pec, phone, cell, fax, address,
                         postal_code, city, province, country, website, vat_number, fiscal_code,
                         payment_terms, sdi_code, easyfatt_code, notes, is_active)
                    VALUES
                        (@Nome, @Referente, @Email, @Pec, @Telefono, @Cellulare, @Fax, @Indirizzo,
                         @Cap, @Citta, @Prov, @Nazione, @Sito, @Piva, @Cf,
                         @Pagamento, @Sdi, @Codice, @Note, @Attivo);
                    SELECT LAST_INSERT_ID()",
                    new
                    {
                        Nome = Taglia(nome, 200),
                        Referente = Taglia(cl.Referente, 100),
                        Email = Taglia(cl.Email, 200),
                        Pec = Taglia(cl.Pec, 255),
                        Telefono = Taglia(cl.Telefono, 100),
                        Cellulare = Taglia(cl.Cellulare, 50),
                        Fax = Taglia(cl.Fax, 50),
                        Indirizzo = Taglia(cl.Indirizzo, 300),
                        Cap = Taglia(cl.Cap, 20),
                        Citta = Taglia(cl.Citta, 100),
                        Prov = Taglia(cl.Prov, 10),
                        Nazione = Taglia(cl.Nazione, 100),
                        Sito = Taglia(cl.Sito, 200),
                        Piva = piva.Length > 0 ? Taglia(piva, 50) : null,
                        Cf = Taglia(cl.Cf, 50),
                        Pagamento = Taglia(cl.Pagamento, 255),
                        Sdi = Taglia(cl.Sdi, 50),
                        Codice = Taglia(cl.Codice, 50),
                        Note = cl.Note ?? "",
                        Attivo = cl.Attivo ? 1 : 0,
                    }, tx);

                rapporto.ClientiCreati++;
                if (codice.Length > 0) perCodice[codice] = nuovo;
                if (piva.Length > 0) perPiva[piva] = nuovo;
                perNome.TryAdd(SalesOfferMatching.KeyName(nome), nuovo);
                if (!string.IsNullOrEmpty(cl.Id)) mappa[cl.Id] = nuovo;
                continue;
            }

            if (!string.IsNullOrEmpty(cl.Id)) mappa[cl.Id] = id.Value;

            // Arricchimento: SOLO i campi vuoti. Un valore già scritto in ATEC PM non si tocca —
            // l'anagrafica viva vale più di una fotografia del gestionale.
            if (simulazione)
            {
                rapporto.ClientiArricchiti++; // stima: qui non si può sapere quante colonne cambierebbero
                continue;
            }

            int toccate = c.Execute(@"
                UPDATE customers SET
                    contact_name  = IF(contact_name  = '' OR contact_name  IS NULL, @Referente,  contact_name),
                    email         = IF(email         = '' OR email         IS NULL, @Email,      email),
                    pec           = IF(pec           = '' OR pec           IS NULL, @Pec,        pec),
                    phone         = IF(phone         = '' OR phone         IS NULL, @Telefono,   phone),
                    cell          = IF(cell          = '' OR cell          IS NULL, @Cellulare,  cell),
                    fax           = IF(fax           = '', @Fax,       fax),
                    address       = IF(address       = '' OR address       IS NULL, @Indirizzo,  address),
                    postal_code   = IF(postal_code   = '', @Cap,       postal_code),
                    city          = IF(city          = '', @Citta,     city),
                    province      = IF(province      = '', @Prov,      province),
                    country       = IF(country       = '', @Nazione,   country),
                    website       = IF(website       = '', @Sito,      website),
                    fiscal_code   = IF(fiscal_code   = '' OR fiscal_code   IS NULL, @Cf,         fiscal_code),
                    payment_terms = IF(payment_terms = '' OR payment_terms IS NULL, @Pagamento,  payment_terms),
                    sdi_code      = IF(sdi_code      = '' OR sdi_code      IS NULL, @Sdi,        sdi_code),
                    easyfatt_code = IF(easyfatt_code = '' OR easyfatt_code IS NULL, @Codice,     easyfatt_code)
                WHERE id = @Id",
                new
                {
                    Id = id.Value,
                    Referente = Taglia(cl.Referente, 100),
                    Email = Taglia(cl.Email, 200),
                    Pec = Taglia(cl.Pec, 255),
                    Telefono = Taglia(cl.Telefono, 100),
                    Cellulare = Taglia(cl.Cellulare, 50),
                    Fax = Taglia(cl.Fax, 50),
                    Indirizzo = Taglia(cl.Indirizzo, 300),
                    Cap = Taglia(cl.Cap, 20),
                    Citta = Taglia(cl.Citta, 100),
                    Prov = Taglia(cl.Prov, 10),
                    Nazione = Taglia(cl.Nazione, 100),
                    Sito = Taglia(cl.Sito, 200),
                    Cf = Taglia(cl.Cf, 50),
                    Pagamento = Taglia(cl.Pagamento, 255),
                    Sdi = Taglia(cl.Sdi, 50),
                    Codice = Taglia(cl.Codice, 50),
                }, tx);

            // MySQL conta 0 righe cambiate quando l'UPDATE non modifica niente: è esattamente
            // la distinzione fra «arricchito» e «già completo».
            if (toccate > 0) rapporto.ClientiArricchiti++; else rapporto.ClientiInvariati++;
        }

        return mappa;
    }

    /// <summary>
    /// Codice gestionale confrontabile: senza spazi e senza zeri davanti. In ATEC PM lo stesso
    /// soggetto compare come <c>0001</c> e nell'archivio offerte come <c>1</c>.
    /// </summary>
    private static string ChiaveCodice(string? codice) => (codice ?? "").Trim().TrimStart('0');

    // ══════════════════════════════════════════════════════════════
    // OFFERTE
    // ══════════════════════════════════════════════════════════════

    private static void ImportaOfferte(
        MySqlConnection c, MySqlTransaction tx, SeedArchivio archivio,
        SalesOfferImportReport rapporto, Dictionary<string, int> clienteSeedAId, int? me, bool simulazione)
    {
        var giaPresenti = c.Query<string>(
            "SELECT legacy_id FROM sales_offers WHERE legacy_id <> ''", transaction: tx)
            .ToHashSet(StringComparer.Ordinal);

        // Rubrica con le chiavi già normalizzate: l'abbinamento gira ~600 volte, e ricalcolare
        // 422 chiavi ogni volta sarebbe un quarto di milione di espressioni regolari per niente.
        List<(int Id, string Key)> rubrica = c.Query<(int Id, string Nome)>(
                "SELECT id AS Id, company_name AS Nome FROM customers", transaction: tx)
            .Select(r => (r.Id, SalesOfferMatching.KeyName(r.Nome)))
            .Where(r => r.Item2.Length > 0)
            .ToList();

        Dictionary<string, int> commesse = c.Query<(int Id, string Code)>(
                "SELECT id AS Id, code AS Code FROM projects", transaction: tx)
            .GroupBy(p => SalesOfferMatching.KeyProjectCode(p.Code))
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var sigleNote = c.Query<string>("SELECT code FROM sales_offer_sellers", transaction: tx)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tipiNoti = c.Query<string>("SELECT code FROM sales_offer_types", transaction: tx)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cacheAbbinamento = new Dictionary<string, int?>(StringComparer.Ordinal);
        var scollegati = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sigleIgnote = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tipiIgnoti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SeedOfferta o in archivio.Offers)
        {
            string legacyId = (o.Id ?? "").Trim();
            if (legacyId.Length > 0 && giaPresenti.Contains(legacyId)) { rapporto.OfferteGiaPresenti++; continue; }

            string sigla = (o.Venditore ?? "").Trim().ToUpperInvariant();
            if (sigla.Length > 0 && !sigleNote.Contains(sigla)) sigleIgnote.Add(sigla);
            string tipo = (o.Tipo ?? "").Trim().ToUpperInvariant();
            if (tipo.Length > 0 && !tipiNoti.Contains(tipo)) tipiIgnoti.Add(tipo);

            // Cliente: prima il collegamento che l'archivio si portava dietro, poi l'abbinamento
            // per nome. Il testo dell'offerta resta quello che è, in ogni caso.
            string nomeCliente = (o.Cliente ?? "").Trim();
            int? clienteId = null;
            if (!string.IsNullOrEmpty(o.ClienteId) && clienteSeedAId.TryGetValue(o.ClienteId, out int diretto) && diretto > 0)
                clienteId = diretto;
            else if (nomeCliente.Length > 0)
            {
                if (!cacheAbbinamento.TryGetValue(nomeCliente, out int? trovato))
                {
                    trovato = SalesOfferMatching.MatchClientPreNormalizzato(rubrica, nomeCliente);
                    cacheAbbinamento[nomeCliente] = trovato;
                }
                clienteId = trovato;
            }

            if (clienteId != null) rapporto.CollegateACliente++;
            else if (nomeCliente.Length > 0)
            {
                rapporto.SenzaCliente++;
                scollegati[nomeCliente] = scollegati.GetValueOrDefault(nomeCliente) + 1;
            }

            string codiceCommessa = (o.Commessa ?? "").Trim();
            int? projectId = null;
            if (codiceCommessa.Length > 0 && commesse.TryGetValue(SalesOfferMatching.KeyProjectCode(codiceCommessa), out int pid))
            {
                projectId = pid;
                rapporto.CollegateACommessa++;
            }

            string stato = (o.Stato ?? "aperta").Trim();
            if (!SalesOfferRules.Stati.Contains(stato))
            {
                rapporto.Avvisi.Add($"Offerta «{legacyId}»: stato «{stato}» sconosciuto, importata come aperta.");
                stato = "aperta";
            }

            rapporto.OfferteInserite++;
            if (simulazione) continue;

            int nuovoId = (int)c.ExecuteScalar<long>(@"
                INSERT INTO sales_offers
                    (year, number, type_code, seller_code, offer_date, customer_id, customer_name,
                     contact_name, description, notes, tag, amount, amount_max, status, chance,
                     order_amount, is_time_material, advance_pct, advance_amount, advance_notes,
                     postponed_year, offer_path, calc_path, next_contact,
                     legacy_id, legacy_number, legacy_ok, is_legacy_series,
                     project_id, project_code, comm_number, created_by, created_at)
                VALUES
                    (@Anno, @Numero, @Tipo, @Sigla, @Data, @ClienteId, @Cliente,
                     @Referente, @Descrizione, @Note, @Tag, @Importo, @ImportoMax, @Stato, @Chance,
                     @ImportoOrdine, @Consuntivo, @AnticipoPct, @AnticipoValore, @AnticipoNote,
                     @Rinvio, @LinkOfferta, @LinkCalcolo, @ProssimoContatto,
                     @LegacyId, @NumeroFile, @OkRaw, @SerieVecchia,
                     @ProjectId, @Commessa, @CommNum, @Me, @Creato);
                SELECT LAST_INSERT_ID()",
                new
                {
                    Anno = o.Anno,
                    Numero = o.Numero,
                    Tipo = Taglia(tipo, 10),
                    Sigla = Taglia(sigla, 10),
                    Data = Data(o.Data),
                    ClienteId = clienteId,
                    Cliente = Taglia(nomeCliente, 300),
                    Referente = Taglia(o.Referente, 200),
                    Descrizione = Taglia(o.Descrizione, 1000),
                    Note = o.Note ?? "",
                    Tag = Taglia(o.Tag, 100),
                    Importo = o.Importo,
                    ImportoMax = o.ImportoMax,
                    Stato = stato,
                    Chance = o.Chance,
                    ImportoOrdine = o.ImportoOrdine,
                    Consuntivo = o.Consuntivo ? 1 : 0,
                    AnticipoPct = o.AnticipoPct,
                    AnticipoValore = o.AnticipoValore,
                    AnticipoNote = Taglia(o.AnticipoNote, 500),
                    Rinvio = o.Rinvio,
                    LinkOfferta = Taglia(o.LinkOfferta, 500),
                    LinkCalcolo = Taglia(o.LinkCalcolo, 500),
                    ProssimoContatto = Data(o.ProssimoContatto),
                    LegacyId = Taglia(legacyId, 20),
                    NumeroFile = Taglia(o.NumeroFile, 50),
                    OkRaw = Taglia(o.OkRaw, 50),
                    SerieVecchia = o.SerieVecchia ? 1 : 0,
                    ProjectId = projectId,
                    Commessa = Taglia(codiceCommessa, 30),
                    CommNum = Taglia(o.CommNum, 20),
                    Me = me,
                    Creato = Data(o.Creato) ?? Data(o.Data) ?? DateTime.Now,
                }, tx);

            foreach (SeedContatto f in o.Followup)
            {
                c.Execute(@"INSERT INTO sales_offer_followups (offer_id, contact_date, notes, created_by, created_at)
                            VALUES (@Id, @Data, @Note, @Me, @Quando)",
                    new
                    {
                        Id = nuovoId,
                        Data = Data(f.Data),
                        Note = f.Note ?? "",
                        Me = me,
                        Quando = Data(f.Ts) ?? DateTime.Now,
                    }, tx);
                rapporto.ContattiInseriti++;
            }
        }

        rapporto.NomiNonCollegati = scollegati
            .OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .Select(x => new SalesOfferUnlinkedName { Nome = x.Key, Offerte = x.Value })
            .ToList();

        if (sigleIgnote.Count > 0)
            rapporto.Avvisi.Add("Sigle venditore non in anagrafica (importate lo stesso): " + string.Join(", ", sigleIgnote.OrderBy(x => x)));
        if (tipiIgnoti.Count > 0)
            rapporto.Avvisi.Add("Tipi impianto non in anagrafica (importati lo stesso): " + string.Join(", ", tipiIgnoti.OrderBy(x => x)));
    }

    // ══════════════════════════════════════════════════════════════
    // AIUTI
    // ══════════════════════════════════════════════════════════════

    private static string Taglia(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>
    /// Le date dell'archivio sono ISO (<c>2022-01-03</c>, o un istante completo per i campi di
    /// tracciatura). Quelle impossibili — nell'Excel ce n'erano, e <c>seed.py</c> le ha spostate
    /// nelle note — arrivano qui come stringa vuota: NULL, non un 01/01/0001.
    /// </summary>
    private static DateTime? Data(string? iso) =>
        string.IsNullOrWhiteSpace(iso)
            ? null
            : DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? d : null;

    // ══════════════════════════════════════════════════════════════
    // IL FORMATO DELL'ARCHIVIO (atec_offerte.json)
    // ══════════════════════════════════════════════════════════════

    private sealed class SeedArchivio
    {
        [JsonPropertyName("offers")] public List<SeedOfferta> Offers { get; set; } = new();
        [JsonPropertyName("clienti")] public List<SeedCliente> Clienti { get; set; } = new();
    }

    private sealed class SeedOfferta
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("anno")] public int Anno { get; set; }
        [JsonPropertyName("numero")] public int? Numero { get; set; }
        [JsonPropertyName("tipo")] public string? Tipo { get; set; }
        [JsonPropertyName("venditore")] public string? Venditore { get; set; }
        [JsonPropertyName("data")] public string? Data { get; set; }
        [JsonPropertyName("cliente")] public string? Cliente { get; set; }
        [JsonPropertyName("clienteId")] public string? ClienteId { get; set; }
        [JsonPropertyName("referente")] public string? Referente { get; set; }
        [JsonPropertyName("descrizione")] public string? Descrizione { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("tag")] public string? Tag { get; set; }
        [JsonPropertyName("importo")] public decimal? Importo { get; set; }
        [JsonPropertyName("importoMax")] public decimal? ImportoMax { get; set; }
        [JsonPropertyName("stato")] public string? Stato { get; set; }
        [JsonPropertyName("chance")] public int? Chance { get; set; }
        [JsonPropertyName("importoOrdine")] public decimal? ImportoOrdine { get; set; }
        [JsonPropertyName("consuntivo")] public bool Consuntivo { get; set; }
        [JsonPropertyName("anticipoPct")] public decimal? AnticipoPct { get; set; }
        [JsonPropertyName("anticipoValore")] public decimal? AnticipoValore { get; set; }
        [JsonPropertyName("anticipoNote")] public string? AnticipoNote { get; set; }
        [JsonPropertyName("rinvio")] public int? Rinvio { get; set; }
        [JsonPropertyName("linkOfferta")] public string? LinkOfferta { get; set; }
        [JsonPropertyName("linkCalcolo")] public string? LinkCalcolo { get; set; }
        [JsonPropertyName("prossimoContatto")] public string? ProssimoContatto { get; set; }
        [JsonPropertyName("numeroFile")] public string? NumeroFile { get; set; }
        [JsonPropertyName("okRaw")] public string? OkRaw { get; set; }
        [JsonPropertyName("serieVecchia")] public bool SerieVecchia { get; set; }
        [JsonPropertyName("commessa")] public string? Commessa { get; set; }
        [JsonPropertyName("commNum")] public string? CommNum { get; set; }
        [JsonPropertyName("creato")] public string? Creato { get; set; }
        [JsonPropertyName("followup")] public List<SeedContatto> Followup { get; set; } = new();
    }

    private sealed class SeedContatto
    {
        [JsonPropertyName("ts")] public string? Ts { get; set; }
        [JsonPropertyName("data")] public string? Data { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
    }

    private sealed class SeedCliente
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("nome")] public string? Nome { get; set; }
        [JsonPropertyName("codice")] public string? Codice { get; set; }
        [JsonPropertyName("piva")] public string? Piva { get; set; }
        [JsonPropertyName("cf")] public string? Cf { get; set; }
        [JsonPropertyName("indirizzo")] public string? Indirizzo { get; set; }
        [JsonPropertyName("cap")] public string? Cap { get; set; }
        [JsonPropertyName("citta")] public string? Citta { get; set; }
        [JsonPropertyName("prov")] public string? Prov { get; set; }
        [JsonPropertyName("nazione")] public string? Nazione { get; set; }
        [JsonPropertyName("sdi")] public string? Sdi { get; set; }
        [JsonPropertyName("referente")] public string? Referente { get; set; }
        [JsonPropertyName("telefono")] public string? Telefono { get; set; }
        [JsonPropertyName("cellulare")] public string? Cellulare { get; set; }
        [JsonPropertyName("fax")] public string? Fax { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("pec")] public string? Pec { get; set; }
        [JsonPropertyName("sito")] public string? Sito { get; set; }
        [JsonPropertyName("pagamento")] public string? Pagamento { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("attivo")] public bool Attivo { get; set; } = true;
    }
}
