using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Registro Offerte — la serie commerciale <b>parallela</b> ai Preventivi.
///
/// <para>Numera tutte le offerte emesse da ATEC (<c>S097-2026-EC</c>), comprese quelle nate
/// fuori dal gestionale, e si aggancia a un preventivo (<c>quote_id</c>) o a una commessa
/// (<c>project_id</c>) quando esistono. Sostituisce l'Excel «NUMERI OFFERTE».
/// Piano: <c>docs/piani/PIANO-ANDAMENTO-COMMERCIALE.md</c>.</para>
///
/// <para><b>Le regole non stanno qui.</b> Numerazione, unicità e validazione vivono in
/// <see cref="SalesOfferRules"/>, che è codice puro e provato a parte: qui restano solo le
/// query, i permessi e il registro modifiche.</para>
/// </summary>
[ApiController]
[Route("api/sales-offers")]
[Authorize]
[RequireFeature("nav.offerte")]
public class SalesOffersController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly FeatureAccessService _access;
    private readonly SalesOfferImportService _import;

    public SalesOffersController(
        DbService db, IHubContext<ProjectHub> hub, FeatureAccessService access, SalesOfferImportService import)
    {
        _db = db;
        _hub = hub;
        _access = access;
        _import = import;
    }

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    private string? CurrentRole => User.FindFirst(ClaimTypes.Role)?.Value;

    /// <summary>Chi può mettere mano anche alle offerte degli altri venditori.</summary>
    private bool PuoModificareAltrui() =>
        _access.CanWriteUser(CurrentEmployeeId, CurrentRole, "action.edit_offerta_altrui");

    /// <summary>
    /// Le sigle che appartengono a chi sta chiamando: quelle dei venditori agganciati alla sua
    /// scheda dipendente. Chi non è agganciato a nessuna sigla non ha offerte «proprie».
    /// </summary>
    private List<string> MieSigle(MySqlConnector.MySqlConnection c) =>
        CurrentEmployeeId <= 0
            ? new List<string>()
            : c.Query<string>("SELECT code FROM sales_offer_sellers WHERE employee_id = @Me",
                new { Me = CurrentEmployeeId }).ToList();

    /// <summary>
    /// Il chiamante può scrivere su un'offerta di quel venditore?
    /// <para>Ha il permesso pieno, oppure la sigla è una delle sue. Il ruolo «solo proprie
    /// offerte» del vecchio applicativo si ottiene esattamente così: <c>action.edit_offerta</c>
    /// senza <c>action.edit_offerta_altrui</c>.</para>
    /// </summary>
    private bool PuoScrivereSu(MySqlConnector.MySqlConnection c, string? sellerCode)
    {
        if (!_access.CanWriteUser(CurrentEmployeeId, CurrentRole, "action.edit_offerta")) return false;
        if (PuoModificareAltrui()) return true;
        string sigla = (sellerCode ?? "").Trim();
        return sigla.Length > 0 && MieSigle(c).Contains(sigla, StringComparer.OrdinalIgnoreCase);
    }

    private void Notifica(string action, int offerId) =>
        _hub.Clients.Group(ProjectHub.SalesOffersGroup)
            .SendAsync("SalesOffersChanged", new { action, offerId }).SenzaAttesa("SalesOffersChanged");

    // ══════════════════════════════════════════════════════════════
    // LETTURE
    // ══════════════════════════════════════════════════════════════

    /// <summary>Le colonne su cui si può ordinare: lista chiusa, il resto ricade sul default.</summary>
    private static readonly Dictionary<string, string> OrdinamentiAmmessi = new(StringComparer.OrdinalIgnoreCase)
    {
        ["number"] = "o.year, o.number",
        ["date"] = "o.offer_date",
        ["customer"] = "o.customer_name",
        ["amount"] = "o.amount",
        ["status"] = "o.status",
        ["chance"] = "o.chance",
        ["seller"] = "o.seller_code",
        ["type"] = "o.type_code",
        ["updated"] = "o.updated_at",
    };

    private const string SelectRiga = @"
        SELECT o.id AS Id, o.year AS Year, o.number AS Number,
               o.type_code AS TypeCode, COALESCE(t.name,'') AS TypeName, COALESCE(t.category,'') AS TypeCategory,
               o.seller_code AS SellerCode, COALESCE(s.name,'') AS SellerName, s.employee_id AS SellerEmployeeId,
               o.offer_date AS OfferDate,
               o.customer_id AS CustomerId, o.customer_name AS CustomerName,
               COALESCE(cu.company_name,'') AS CustomerCompanyName, o.contact_name AS ContactName,
               o.description AS Description, COALESCE(o.notes,'') AS Notes, o.tag AS Tag,
               o.amount AS Amount, o.amount_max AS AmountMax,
               o.status AS Status, o.chance AS Chance,
               o.order_amount AS OrderAmount, o.is_time_material AS IsTimeMaterial,
               o.advance_pct AS AdvancePct, o.advance_amount AS AdvanceAmount, o.advance_notes AS AdvanceNotes,
               o.postponed_year AS PostponedYear,
               o.offer_path AS OfferPath, o.calc_path AS CalcPath,
               o.next_contact AS NextContact,
               o.legacy_number AS LegacyNumber, o.is_legacy_series AS IsLegacySeries,
               o.quote_id AS QuoteId, COALESCE(q.quote_number,'') AS QuoteNumber,
               o.project_id AS ProjectId, COALESCE(p.code,'') AS ProjectCode,
               o.row_version AS RowVersion, o.created_at AS CreatedAt, o.updated_at AS UpdatedAt,
               COALESCE(CONCAT(ec.first_name,' ',ec.last_name),'') AS CreatedByName,
               COALESCE(CONCAT(eu.first_name,' ',eu.last_name),'') AS UpdatedByName
        FROM sales_offers o
        LEFT JOIN sales_offer_types t ON t.code = o.type_code
        LEFT JOIN sales_offer_sellers s ON s.code = o.seller_code
        LEFT JOIN customers cu ON cu.id = o.customer_id
        LEFT JOIN quotes q ON q.id = o.quote_id
        LEFT JOIN projects p ON p.id = o.project_id
        LEFT JOIN employees ec ON ec.id = o.created_by
        LEFT JOIN employees eu ON eu.id = o.updated_by";

    /// <summary>Elenco filtrato e paginato del registro.</summary>
    [HttpGet]
    public IActionResult GetList(
        [FromQuery] int? year, [FromQuery] string? status, [FromQuery] string? sellerCode,
        [FromQuery] string? typeCode, [FromQuery] int? customerId, [FromQuery] int? chance,
        [FromQuery] string? search, [FromQuery] string? sortBy, [FromQuery] string? sortDir,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            using var c = _db.Open();

            var where = new List<string>();
            var pars = new DynamicParameters();

            if (year is > 0) { where.Add("o.year = @Year"); pars.Add("Year", year); }
            if (!string.IsNullOrWhiteSpace(status)) { where.Add("o.status = @Status"); pars.Add("Status", status); }
            if (!string.IsNullOrWhiteSpace(sellerCode)) { where.Add("o.seller_code = @Seller"); pars.Add("Seller", sellerCode); }
            if (!string.IsNullOrWhiteSpace(typeCode)) { where.Add("o.type_code = @Type"); pars.Add("Type", typeCode); }
            if (customerId is > 0) { where.Add("o.customer_id = @Cust"); pars.Add("Cust", customerId); }
            // chance = -1 significa «non valutata»: 0 è un valore vero e non va confuso col vuoto.
            if (chance == -1) where.Add("o.chance IS NULL");
            else if (chance != null) { where.Add("o.chance = @Chance"); pars.Add("Chance", chance); }

            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add(@"(o.customer_name LIKE @Q OR o.description LIKE @Q OR o.contact_name LIKE @Q
                            OR o.legacy_number LIKE @Q OR o.tag LIKE @Q
                            OR CONCAT(o.type_code, LPAD(o.number,3,'0'), '-', o.year, '-', o.seller_code) LIKE @Q)");
                pars.Add("Q", "%" + search.Trim() + "%");
            }

            string filtro = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";
            int total = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offers o" + filtro, pars);

            string ordine = OrdinamentiAmmessi.TryGetValue(sortBy ?? "", out string? col) ? col : "o.year, o.number";
            string verso = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

            pars.Add("Take", Math.Clamp(pageSize, 1, 500));
            pars.Add("Skip", Math.Max(0, (Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 500)));

            List<SalesOfferDto> righe = c.Query<SalesOfferDto>(
                $"{SelectRiga}{filtro} ORDER BY {ordine} {verso} LIMIT @Take OFFSET @Skip", pars).ToList();

            foreach (SalesOfferDto r in righe)
                r.Composed = SalesOfferRules.Composed(r.TypeCode, r.Number, r.Year, r.SellerCode);

            return Ok(ApiResponse<SalesOfferListResponse>.Ok(new SalesOfferListResponse { Items = righe, Total = total }));
        }
        catch (Exception ex) { return Ok(ApiResponse<SalesOfferListResponse>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Scheda completa: la riga più gli appunti di contatto e il registro modifiche.</summary>
    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        try
        {
            using var c = _db.Open();
            SalesOfferDto? o = c.QueryFirstOrDefault<SalesOfferDto>($"{SelectRiga} WHERE o.id = @Id", new { Id = id });
            if (o == null) return Ok(ApiResponse<SalesOfferDto>.Fail("Offerta non trovata"));

            o.Composed = SalesOfferRules.Composed(o.TypeCode, o.Number, o.Year, o.SellerCode);

            o.Followups = c.Query<SalesOfferFollowupDto>(@"
                SELECT f.id AS Id, f.offer_id AS OfferId, f.contact_date AS ContactDate,
                       COALESCE(f.notes,'') AS Notes, f.created_at AS CreatedAt,
                       COALESCE(CONCAT(e.first_name,' ',e.last_name),'') AS CreatedByName
                FROM sales_offer_followups f
                LEFT JOIN employees e ON e.id = f.created_by
                WHERE f.offer_id = @Id
                ORDER BY f.contact_date DESC, f.id DESC", new { Id = id }).ToList();

            o.Log = c.Query<SalesOfferLogDto>(@"
                SELECT l.id AS Id, l.field AS Field, COALESCE(l.old_value,'') AS OldValue,
                       COALESCE(l.new_value,'') AS NewValue, l.changed_at AS ChangedAt,
                       COALESCE(CONCAT(e.first_name,' ',e.last_name),'') AS ChangedByName
                FROM sales_offer_log l
                LEFT JOIN employees e ON e.id = l.changed_by
                WHERE l.offer_id = @Id
                ORDER BY l.changed_at DESC, l.id DESC
                LIMIT 200", new { Id = id }).ToList();

            return Ok(ApiResponse<SalesOfferDto>.Ok(o));
        }
        catch (Exception ex) { return Ok(ApiResponse<SalesOfferDto>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>
    /// Il prossimo numero libero dell'anno, già composto con tipo e sigla se il client li passa.
    /// È quello che la Dashboard mostra sotto «Prossimo numero».
    /// </summary>
    [HttpGet("next-number")]
    public IActionResult GetNextNumber([FromQuery] int? year, [FromQuery] string? typeCode, [FromQuery] string? sellerCode)
    {
        try
        {
            using var c = _db.Open();
            int anno = year is > 0 ? year.Value : DateTime.Now.Year;
            int numero = SalesOfferRules.NextNumber(c, anno);
            return Ok(ApiResponse<SalesOfferNextNumberDto>.Ok(new SalesOfferNextNumberDto
            {
                Year = anno,
                Number = numero,
                Composed = SalesOfferRules.Composed(typeCode, numero, anno, sellerCode),
            }));
        }
        catch (Exception ex) { return Ok(ApiResponse<SalesOfferNextNumberDto>.Fail($"Errore: {ex.Message}")); }
    }

    // ══════════════════════════════════════════════════════════════
    // SCRITTURE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea un'offerta. Se <c>Number</c> è nullo lo assegna il server.
    ///
    /// <para><b>Il numero preso nel frattempo non è un errore</b>: in creazione il numero che
    /// arriva dal client è sempre una proposta (gliel'ha data <c>next-number</c>), e fra la
    /// proposta e il salvataggio può essersi inserito un altro venditore. In quel caso si
    /// assegna il successivo e si risponde <c>Renumbered = true</c>, così il client lo dice
    /// invece di far finta di niente. In <b>modifica</b>, invece, un numero già usato è un
    /// errore: lì il numero l'ha scelto una persona.</para>
    /// </summary>
    [HttpPost]
    public IActionResult Create([FromBody] SalesOfferSaveRequest req)
    {
        try
        {
            using var c = _db.Open();

            if (!PuoScrivereSu(c, req.SellerCode))
                return Ok(ApiResponse<SalesOfferCreatedDto>.Fail(
                    "Puoi creare offerte solo a tuo nome (serve «Modifica offerte di altri venditori»)"));

            // In creazione il numero è una proposta: l'unicità la si risolve rinumerando.
            Dictionary<string, string> errori = SalesOfferRules.Validate(req, numeroOccupato: null);
            if (errori.Count > 0)
                return Ok(ApiResponse<SalesOfferCreatedDto>.Fail(string.Join(" · ", errori.Values)));

            using var tx = c.BeginTransaction();

            bool rinumerata = false;
            int numero;
            if (req.Number is > 0 && !SalesOfferRules.NumberTaken(c, req.Year, req.Number.Value, null, tx))
            {
                numero = req.Number.Value;
            }
            else
            {
                rinumerata = req.Number is > 0;
                numero = SalesOfferRules.NextNumber(c, req.Year, tx);
            }

            int me = CurrentEmployeeId;
            int id = (int)c.ExecuteScalar<long>(@"
                INSERT INTO sales_offers
                    (year, number, type_code, seller_code, offer_date, customer_id, customer_name,
                     contact_name, description, notes, tag, amount, amount_max, status, chance,
                     order_amount, is_time_material, advance_pct, advance_amount, advance_notes,
                     postponed_year, offer_path, calc_path, next_contact, quote_id, project_id,
                     created_by, updated_by, updated_at)
                VALUES
                    (@Year, @Number, @TypeCode, @SellerCode, @OfferDate, @CustomerId, @CustomerName,
                     @ContactName, @Description, @Notes, @Tag, @Amount, @AmountMax, @Status, @Chance,
                     @OrderAmount, @IsTimeMaterial, @AdvancePct, @AdvanceAmount, @AdvanceNotes,
                     @PostponedYear, @OfferPath, @CalcPath, @NextContact, @QuoteId, @ProjectId,
                     @Me, @Me, CURRENT_TIMESTAMP);
                SELECT LAST_INSERT_ID()",
                new
                {
                    req.Year,
                    Number = numero,
                    TypeCode = Taglia(req.TypeCode, 10),
                    SellerCode = Taglia(req.SellerCode, 10),
                    req.OfferDate,
                    req.CustomerId,
                    CustomerName = Taglia(req.CustomerName, 300),
                    ContactName = Taglia(req.ContactName, 200),
                    Description = Taglia(req.Description, 1000),
                    Notes = req.Notes ?? "",
                    Tag = Taglia(req.Tag, 100),
                    req.Amount,
                    req.AmountMax,
                    Status = req.Status,
                    req.Chance,
                    req.OrderAmount,
                    IsTimeMaterial = req.IsTimeMaterial ? 1 : 0,
                    req.AdvancePct,
                    req.AdvanceAmount,
                    AdvanceNotes = Taglia(req.AdvanceNotes, 500),
                    req.PostponedYear,
                    OfferPath = Taglia(req.OfferPath, 500),
                    CalcPath = Taglia(req.CalcPath, 500),
                    req.NextContact,
                    req.QuoteId,
                    req.ProjectId,
                    Me = me > 0 ? me : (int?)null,
                }, tx);

            ScriviLog(c, tx, id, "creazione", null, SalesOfferRules.Composed(req.TypeCode, numero, req.Year, req.SellerCode), me);
            tx.Commit();

            Notifica("create", id);
            return Ok(ApiResponse<SalesOfferCreatedDto>.Ok(new SalesOfferCreatedDto
            {
                Id = id,
                Year = req.Year,
                Number = numero,
                Composed = SalesOfferRules.Composed(req.TypeCode, numero, req.Year, req.SellerCode),
                Renumbered = rinumerata,
            }, rinumerata ? $"Il numero proposto era stato preso: assegnato il {SalesOfferRules.Pad3(numero)}" : ""));
        }
        catch (Exception ex) { return Ok(ApiResponse<SalesOfferCreatedDto>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Modifica un'offerta, con concorrenza ottimistica su <c>row_version</c>.</summary>
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] SalesOfferSaveRequest req)
    {
        try
        {
            using var c = _db.Open();

            SalesOfferDto? prima = c.QueryFirstOrDefault<SalesOfferDto>($"{SelectRiga} WHERE o.id = @Id", new { Id = id });
            if (prima == null) return Ok(ApiResponse<bool>.Fail("Offerta non trovata"));

            // Serve il diritto sia su chi ce l'ha adesso sia su chi ce l'avrà dopo: senza il
            // secondo controllo un venditore potrebbe intestarsi l'offerta di un altro (o
            // regalargli la propria) aggirando il permesso.
            if (!PuoScrivereSu(c, prima.SellerCode) || !PuoScrivereSu(c, req.SellerCode))
                return Ok(ApiResponse<bool>.Fail(
                    "Puoi modificare solo le tue offerte (serve «Modifica offerte di altri venditori»)"));

            bool occupato = req.Number is > 0 && SalesOfferRules.NumberTaken(c, req.Year, req.Number.Value, id);
            Dictionary<string, string> errori = SalesOfferRules.Validate(req, occupato);
            if (errori.Count > 0) return Ok(ApiResponse<bool>.Fail(string.Join(" · ", errori.Values)));

            using var tx = c.BeginTransaction();
            int me = CurrentEmployeeId;

            int righe = c.Execute(@"
                UPDATE sales_offers SET
                    year = @Year, number = @Number, type_code = @TypeCode, seller_code = @SellerCode,
                    offer_date = @OfferDate, customer_id = @CustomerId, customer_name = @CustomerName,
                    contact_name = @ContactName, description = @Description, notes = @Notes, tag = @Tag,
                    amount = @Amount, amount_max = @AmountMax, status = @Status, chance = @Chance,
                    order_amount = @OrderAmount, is_time_material = @IsTimeMaterial,
                    advance_pct = @AdvancePct, advance_amount = @AdvanceAmount, advance_notes = @AdvanceNotes,
                    postponed_year = @PostponedYear, offer_path = @OfferPath, calc_path = @CalcPath,
                    next_contact = @NextContact, quote_id = @QuoteId, project_id = @ProjectId,
                    row_version = row_version + 1, updated_by = @Me, updated_at = CURRENT_TIMESTAMP
                WHERE id = @Id AND (@RowVersion IS NULL OR row_version = @RowVersion)",
                new
                {
                    Id = id,
                    req.Year,
                    req.Number,
                    TypeCode = Taglia(req.TypeCode, 10),
                    SellerCode = Taglia(req.SellerCode, 10),
                    req.OfferDate,
                    req.CustomerId,
                    CustomerName = Taglia(req.CustomerName, 300),
                    ContactName = Taglia(req.ContactName, 200),
                    Description = Taglia(req.Description, 1000),
                    Notes = req.Notes ?? "",
                    Tag = Taglia(req.Tag, 100),
                    req.Amount,
                    req.AmountMax,
                    Status = req.Status,
                    req.Chance,
                    req.OrderAmount,
                    IsTimeMaterial = req.IsTimeMaterial ? 1 : 0,
                    req.AdvancePct,
                    req.AdvanceAmount,
                    AdvanceNotes = Taglia(req.AdvanceNotes, 500),
                    req.PostponedYear,
                    OfferPath = Taglia(req.OfferPath, 500),
                    CalcPath = Taglia(req.CalcPath, 500),
                    req.NextContact,
                    req.QuoteId,
                    req.ProjectId,
                    Me = me > 0 ? me : (int?)null,
                    req.RowVersion,
                }, tx);

            if (righe == 0)
            {
                tx.Rollback();
                return Ok(ApiResponse<bool>.Fail(
                    "Qualcuno ha modificato questa offerta nel frattempo: ricarica la scheda e riprova"));
            }

            ScriviDifferenze(c, tx, id, prima, req, me);
            tx.Commit();

            Notifica("update", id);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Elimina un'offerta (appunti e registro modifiche seguono in cascata).</summary>
    [HttpDelete("{id}")]
    [RequireFeature("action.delete_offerta")]
    public IActionResult Delete(int id)
    {
        try
        {
            using var c = _db.Open();
            string? sigla = c.ExecuteScalar<string>("SELECT seller_code FROM sales_offers WHERE id = @Id", new { Id = id });
            if (sigla == null) return Ok(ApiResponse<bool>.Fail("Offerta non trovata"));

            if (!PuoScrivereSu(c, sigla))
                return Ok(ApiResponse<bool>.Fail("Puoi eliminare solo le tue offerte"));

            c.Execute("DELETE FROM sales_offers WHERE id = @Id", new { Id = id });
            Notifica("delete", id);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>
    /// Aggiunge un appunto di contatto e, se indicata, sposta la data del prossimo contatto.
    /// È la funzione da cui nasce l'elenco «Da richiamare».
    /// </summary>
    [HttpPost("{id}/followup")]
    public IActionResult AddFollowup(int id, [FromBody] SalesOfferFollowupSaveRequest req)
    {
        try
        {
            using var c = _db.Open();
            var prima = c.QueryFirstOrDefault<(string SellerCode, DateTime? NextContact)>(
                "SELECT seller_code AS SellerCode, next_contact AS NextContact FROM sales_offers WHERE id = @Id",
                new { Id = id });
            if (prima.SellerCode == null) return Ok(ApiResponse<bool>.Fail("Offerta non trovata"));

            if (!PuoScrivereSu(c, prima.SellerCode))
                return Ok(ApiResponse<bool>.Fail("Puoi annotare contatti solo sulle tue offerte"));

            if (string.IsNullOrWhiteSpace(req.Notes) && req.NextContact == null)
                return Ok(ApiResponse<bool>.Fail("Scrivi l'appunto oppure indica il prossimo contatto"));

            using var tx = c.BeginTransaction();
            int me = CurrentEmployeeId;

            c.Execute(@"INSERT INTO sales_offer_followups (offer_id, contact_date, notes, created_by)
                        VALUES (@Id, @Data, @Note, @Me)",
                new
                {
                    Id = id,
                    Data = req.ContactDate ?? DateTime.Today,
                    Note = req.Notes ?? "",
                    Me = me > 0 ? me : (int?)null,
                }, tx);

            if (req.NextContact != prima.NextContact)
            {
                c.Execute(@"UPDATE sales_offers
                            SET next_contact = @Next, row_version = row_version + 1,
                                updated_by = @Me, updated_at = CURRENT_TIMESTAMP
                            WHERE id = @Id",
                    new { Id = id, Next = req.NextContact, Me = me > 0 ? me : (int?)null }, tx);
                ScriviLog(c, tx, id, "nextContact", Testo(prima.NextContact), Testo(req.NextContact), me);
            }

            ScriviLog(c, tx, id, "contatto", null, req.Notes ?? "", me);
            tx.Commit();

            Notifica("followup", id);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>
    /// Chiude in blocco le offerte ancora aperte degli anni passati.
    ///
    /// <para><b>Perché serve.</b> Nel vecchio Excel le offerte perse non venivano quasi mai
    /// chiuse: sulle 1.795 righe storiche ce ne sono 1.364 «aperte» e appena 7 «perse». Finché
    /// non si passa di qui, il portafoglio dell'Andamento racconta una bugia grossa.</para>
    /// </summary>
    [HttpPost("bulk-close")]
    [RequireFeature("action.bulk_close_offerte")]
    public IActionResult BulkClose([FromBody] SalesOfferBulkCloseRequest req)
    {
        try
        {
            string nuovo = (req.Status ?? "persa").Trim();
            if (nuovo != "persa" && nuovo != "sospesa")
                return Ok(ApiResponse<int>.Fail("La chiusura in blocco ammette solo «persa» o «sospesa»"));
            if (req.UntilYear < 2000 || req.UntilYear >= DateTime.Now.Year)
                return Ok(ApiResponse<int>.Fail("Si chiudono solo gli anni già conclusi"));

            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            int me = CurrentEmployeeId;

            // Il registro modifiche PRIMA dell'update: dopo, il vecchio stato non c'è più.
            c.Execute(@"
                INSERT INTO sales_offer_log (offer_id, field, old_value, new_value, changed_by)
                SELECT id, 'status', status, @Nuovo, @Me
                FROM sales_offers
                WHERE status = 'aperta' AND year <= @Anno",
                new { Nuovo = nuovo, Anno = req.UntilYear, Me = me > 0 ? me : (int?)null }, tx);

            int righe = c.Execute(@"
                UPDATE sales_offers
                SET status = @Nuovo, row_version = row_version + 1,
                    updated_by = @Me, updated_at = CURRENT_TIMESTAMP
                WHERE status = 'aperta' AND year <= @Anno",
                new { Nuovo = nuovo, Anno = req.UntilYear, Me = me > 0 ? me : (int?)null }, tx);

            tx.Commit();
            Notifica("bulk-close", 0);
            return Ok(ApiResponse<int>.Ok(righe, $"{righe} offerte chiuse come «{nuovo}»"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    // ══════════════════════════════════════════════════════════════
    // IMPORT DELLO STORICO
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Riversa nel registro l'archivio dell'applicativo autonomo ATEC Offerte
    /// (<c>atec_offerte.json</c> / <c>seed.json</c>): 1.795 offerte dal 2022 e i 422 soggetti
    /// del gestionale.
    ///
    /// <para><b>Simula per default.</b> Senza <c>simulazione=false</c> legge, abbina e conta
    /// senza scrivere una riga: il rapporto dice cosa succederebbe. Un import dello storico si
    /// guarda prima di farlo, non dopo.</para>
    ///
    /// <para>È ripetibile: ogni riga porta il proprio <c>legacy_id</c> e una seconda esecuzione
    /// recupera solo ciò che mancava.</para>
    /// </summary>
    [HttpPost("import-seed")]
    [RequireFeature("action.import_offerte")]
    public IActionResult ImportSeed(IFormFile? file, [FromQuery] bool simulazione = true)
    {
        try
        {
            if (file == null || file.Length == 0)
                return Ok(ApiResponse<SalesOfferImportReport>.Fail("Nessun file: carica l'archivio JSON delle offerte"));

            using var c = _db.Open();
            using Stream s = file.OpenReadStream();
            SalesOfferImportReport rapporto = _import.Importa(c, s, CurrentEmployeeId > 0 ? CurrentEmployeeId : null, simulazione);

            if (!simulazione && rapporto.OfferteInserite > 0) Notifica("import", 0);

            string riassunto = simulazione
                ? $"Simulazione: entrerebbero {rapporto.OfferteInserite} offerte e {rapporto.ClientiCreati} clienti nuovi"
                : $"{rapporto.OfferteInserite} offerte importate, {rapporto.ClientiCreati} clienti creati";
            return Ok(ApiResponse<SalesOfferImportReport>.Ok(rapporto, riassunto));
        }
        catch (Exception ex) { return Ok(ApiResponse<SalesOfferImportReport>.Fail($"Errore: {ex.Message}")); }
    }

    // ══════════════════════════════════════════════════════════════
    // ANAGRAFICHE
    // ══════════════════════════════════════════════════════════════

    [HttpGet("types")]
    public IActionResult GetTypes([FromQuery] bool includeInactive = false)
    {
        try
        {
            using var c = _db.Open();
            List<SalesOfferTypeDto> tipi = c.Query<SalesOfferTypeDto>(
                @"SELECT code AS Code, name AS Name, category AS Category,
                         sort_order AS SortOrder, is_active AS IsActive
                  FROM sales_offer_types" + (includeInactive ? "" : " WHERE is_active = 1") +
                " ORDER BY sort_order, code").ToList();
            return Ok(ApiResponse<List<SalesOfferTypeDto>>.Ok(tipi));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<SalesOfferTypeDto>>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>
    /// Salva i tipi impianto: inserisce i nuovi e aggiorna gli esistenti.
    /// <para><b>Non cancella quelli che mancano dall'elenco</b>: un tipo tolto qui sparirebbe dal
    /// numero composto di tutte le offerte storiche che lo usano. Si spegne con
    /// <c>IsActive = false</c>, e resta leggibile sullo storico.</para>
    /// </summary>
    [HttpPut("types")]
    [RequireFeature("action.edit_offer_types")]
    public IActionResult SaveTypes([FromBody] List<SalesOfferTypeDto> tipi)
    {
        try
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();

            foreach (SalesOfferTypeDto t in tipi ?? new List<SalesOfferTypeDto>())
            {
                string code = Taglia(t.Code, 10).ToUpperInvariant().Trim();
                if (code.Length == 0) continue;
                string cat = SalesOfferRules.Categorie.Contains(t.Category) ? t.Category : "Altro";

                c.Execute(@"
                    INSERT INTO sales_offer_types (code, name, category, sort_order, is_active)
                    VALUES (@Code, @Name, @Cat, @Sort, @Active)
                    ON DUPLICATE KEY UPDATE name = @Name, category = @Cat, sort_order = @Sort, is_active = @Active",
                    new { Code = code, Name = Taglia(t.Name, 100), Cat = cat, Sort = t.SortOrder, Active = t.IsActive ? 1 : 0 }, tx);
            }

            tx.Commit();
            Notifica("types", 0);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpGet("sellers")]
    public IActionResult GetSellers([FromQuery] bool includeInactive = false)
    {
        try
        {
            using var c = _db.Open();
            List<SalesOfferSellerDto> v = c.Query<SalesOfferSellerDto>(
                @"SELECT code AS Code, name AS Name, employee_id AS EmployeeId, is_active AS IsActive
                  FROM sales_offer_sellers" + (includeInactive ? "" : " WHERE is_active = 1") +
                " ORDER BY code").ToList();
            return Ok(ApiResponse<List<SalesOfferSellerDto>>.Ok(v));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<SalesOfferSellerDto>>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>Salva i venditori: come i tipi, nessuna cancellazione — le sigle sono storia.</summary>
    [HttpPut("sellers")]
    [RequireFeature("action.edit_offer_types")]
    public IActionResult SaveSellers([FromBody] List<SalesOfferSellerDto> venditori)
    {
        try
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();

            foreach (SalesOfferSellerDto v in venditori ?? new List<SalesOfferSellerDto>())
            {
                string code = Taglia(v.Code, 10).ToUpperInvariant().Trim();
                if (code.Length == 0) continue;

                c.Execute(@"
                    INSERT INTO sales_offer_sellers (code, name, employee_id, is_active)
                    VALUES (@Code, @Name, @Emp, @Active)
                    ON DUPLICATE KEY UPDATE name = @Name, employee_id = @Emp, is_active = @Active",
                    new { Code = code, Name = Taglia(v.Name, 200), Emp = v.EmployeeId, Active = v.IsActive ? 1 : 0 }, tx);
            }

            tx.Commit();
            Notifica("sellers", 0);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ══════════════════════════════════════════════════════════════
    // REGISTRO MODIFICHE
    // ══════════════════════════════════════════════════════════════

    private static string Taglia(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..max];
    }

    private static string? Testo(object? v) => v switch
    {
        null => null,
        DateTime d => d.ToString("yyyy-MM-dd"),
        bool b => b ? "1" : "0",
        decimal m => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    private static void ScriviLog(MySqlConnector.MySqlConnection c, System.Data.IDbTransaction tx,
        int offerId, string campo, string? da, string? a, int me) =>
        c.Execute(@"INSERT INTO sales_offer_log (offer_id, field, old_value, new_value, changed_by)
                    VALUES (@Id, @Campo, @Da, @A, @Me)",
            new { Id = offerId, Campo = campo, Da = Taglia(da, 500), A = Taglia(a, 500), Me = me > 0 ? me : (int?)null }, tx);

    /// <summary>
    /// Scrive una riga di registro per ogni campo tracciato che è cambiato davvero. I campi
    /// sono quelli di <see cref="SalesOfferRules.CampiTracciati"/>: da lì l'Andamento
    /// ricostruisce il portafoglio nel tempo, quindi cambiarli cambia il racconto.
    /// </summary>
    private static void ScriviDifferenze(MySqlConnector.MySqlConnection c, System.Data.IDbTransaction tx,
        int id, SalesOfferDto prima, SalesOfferSaveRequest dopo, int me)
    {
        void Confronta(string campo, object? a, object? b)
        {
            string? sa = Testo(a), sb = Testo(b);
            if (!string.Equals(sa ?? "", sb ?? "", StringComparison.Ordinal))
                ScriviLog(c, tx, id, campo, sa, sb, me);
        }

        Confronta("status", prima.Status, dopo.Status);
        Confronta("chance", prima.Chance, dopo.Chance);
        Confronta("amount", prima.Amount, dopo.Amount);
        Confronta("amountMax", prima.AmountMax, dopo.AmountMax);
        Confronta("orderAmount", prima.OrderAmount, dopo.OrderAmount);
        Confronta("isTimeMaterial", prima.IsTimeMaterial, dopo.IsTimeMaterial);
        Confronta("customerId", prima.CustomerId, dopo.CustomerId);
        Confronta("customerName", prima.CustomerName, dopo.CustomerName);
        Confronta("offerDate", prima.OfferDate, dopo.OfferDate);
        Confronta("number", prima.Number, dopo.Number);
        Confronta("year", prima.Year, dopo.Year);
        Confronta("typeCode", prima.TypeCode, dopo.TypeCode);
        Confronta("sellerCode", prima.SellerCode, dopo.SellerCode);
        Confronta("postponedYear", prima.PostponedYear, dopo.PostponedYear);
        Confronta("nextContact", prima.NextContact, dopo.NextContact);
        Confronta("quoteId", prima.QuoteId, dopo.QuoteId);
        Confronta("projectId", prima.ProjectId, dopo.ProjectId);
    }
}
