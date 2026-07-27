using System.Data;
using System.Globalization;
using Dapper;

namespace ATEC.PM.Server.Services;

// Sync DDP Officina → Lavorazioni (project_work_requests): ogni riga della distinta
// officina con codice 101 (particolare a disegno) genera/aggiorna la lavorazione
// collegata via ddp_officina_item_id. Gli altri prefissi Codex (201/301/401/…)
// non entrano nel Pannello Lavorazioni.
// Regole concordate (14/07/2026, aggiornato 17/07/2026):
//  - solo le righe con part_number normalizzato che inizia per "101" generano bozze;
//  - i campi derivati (descrizione, disponibilità, data ordine, fornitore ODA,
//    n° ordine = Rif. Danea) seguono la riga DDP anche dopo la promozione;
//  - note trattamento: precompilate SOLO alla creazione; priorità/RDO/consegna restano
//    del pannello (RDO = solo elenco offerte, non scrive più su ODA);
//  - tipo Interna/Esterna: one-shot dallo stato DDP alla creazione (o se ancora vuoto);
//  - la cancellazione della riga DDP elimina la bozza in staging; le lavorazioni già
//    promosse restano e la FK (ON DELETE SET NULL) le scollega.
public static class WorkRequestDdpSync
{
    /// <summary>
    /// True se il codice (con o senza punto di display Codex) è un particolare a disegno (101…).
    /// </summary>
    public static bool IsMechanicalWorkshopCode(string? partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber)) return false;
        string normalized = partNumber.Replace(".", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Trim();
        return normalized.StartsWith("101", StringComparison.Ordinal);
    }

    /// <summary>
    /// Tipo lavorazione dallo stato DDP Officina (one-shot):
    /// DO / RO / IO → External; DC / COS → Internal; altri → "".
    /// </summary>
    public static string TypeFromOfficinaStatus(string? itemStatus)
    {
        string key = (itemStatus ?? "").Trim().ToUpperInvariant();
        return key switch
        {
            "DO" or "RO" or "IO" => "External",
            "DC" or "COS" => "Internal",
            _ => "",
        };
    }

    private sealed class OfficinaRow
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string PartNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Material { get; set; } = "";
        public string Treatment { get; set; } = "";
        public string SupplierName { get; set; } = "";
        public string DaneaRef { get; set; } = "";
        public string ItemStatus { get; set; } = "";
        public DateTime? DateNeeded { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public sealed class SyncResult
    {
        public int WorkRequestId { get; set; }
        public int ProjectId { get; set; }
        public bool Created { get; set; }
    }

    // Descrizione derivata: "Codice 101 — Descrizione · Q pz · Materiale" (parti vuote omesse).
    private static string BuildDescription(OfficinaRow item)
    {
        CultureInfo it = CultureInfo.GetCultureInfo("it-IT");
        string head = string.Join(" — ", new[] { item.PartNumber.Trim(), item.Description.Trim() }
            .Where(s => s.Length > 0));
        List<string> parts = new() { head };
        if (item.Quantity > 0)
            parts.Add($"{item.Quantity.ToString("0.###", it)} pz");
        if (item.Material.Trim().Length > 0)
            parts.Add(item.Material.Trim());
        return string.Join(" · ", parts.Where(s => s.Length > 0));
    }

    /// <summary>
    /// Crea la bozza collegata (se la riga officina non ne ha una) oppure riallinea i
    /// campi derivati della lavorazione esistente. Null se la riga officina non esiste.
    /// </summary>
    public static SyncResult? Upsert(IDbConnection c, int officinaItemId)
    {
        OfficinaRow? item = c.QueryFirstOrDefault<OfficinaRow>(@"
            SELECT o.id AS Id, o.project_id AS ProjectId,
                   COALESCE(o.part_number,'') AS PartNumber, COALESCE(o.description,'') AS Description,
                   COALESCE(o.quantity,0) AS Quantity, COALESCE(o.material,'') AS Material,
                   COALESCE(o.treatment,'') AS Treatment, COALESCE(o.supplier_name,'') AS SupplierName,
                   COALESCE(o.danea_ref,'') AS DaneaRef,
                   COALESCE(o.item_status,'') AS ItemStatus,
                   o.date_needed AS DateNeeded, o.order_date AS OrderDate,
                   o.created_at AS CreatedAt
            FROM ddp_officina_items o WHERE o.id = @Id", new { Id = officinaItemId });
        if (item == null) return null;

        int existingId = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(id), 0) FROM project_work_requests WHERE ddp_officina_item_id = @Id",
            new { Id = officinaItemId });

        // Solo i 101 (particolari a disegno) alimentano le Lavorazioni.
        if (!IsMechanicalWorkshopCode(item.PartNumber))
        {
            if (existingId > 0)
                DeleteStagingFor(c, officinaItemId);
            return null;
        }

        string description = BuildDescription(item);
        bool hasTreatment = item.Treatment.Trim().Length > 0;
        string inferredType = TypeFromOfficinaStatus(item.ItemStatus);
        // ODA da DDP: fornitore (ATEC se Interna) e n° ordine = Rif. Danea.
        string PoSupplierFor(string wrType) =>
            string.Equals(wrType, "Internal", StringComparison.OrdinalIgnoreCase)
                ? "ATEC"
                : item.SupplierName;

        if (existingId > 0)
        {
            // One-shot sul type; ODA (fornitore / n° / data) riallineati sempre dalla DDP.
            c.Execute(@"
                UPDATE project_work_requests SET
                    description = @Description,
                    availability_date = @AvailabilityDate,
                    po_date = @PoDate,
                    po_number = @PoNumber,
                    po_supplier = CASE
                        WHEN type = 'Internal'
                             OR (TRIM(COALESCE(type, '')) = '' AND @Type = 'Internal')
                            THEN 'ATEC'
                        ELSE @PoSupplier
                    END,
                    has_treatment = @HasTreatment,
                    type = CASE
                        WHEN TRIM(COALESCE(type, '')) = '' AND @Type <> '' THEN @Type
                        ELSE type
                    END,
                    row_version = row_version + 1
                WHERE id = @Id", new
            {
                Id = existingId,
                Description = description,
                AvailabilityDate = item.DateNeeded,
                PoDate = item.OrderDate,
                PoNumber = item.DaneaRef,
                PoSupplier = item.SupplierName,
                HasTreatment = hasTreatment ? 1 : 0,
                Type = inferredType,
            });
            return new SyncResult { WorkRequestId = existingId, ProjectId = item.ProjectId, Created = false };
        }

        string poSupplier = PoSupplierFor(inferredType);

        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO project_work_requests (
                project_id, request_date, description, type, priority, availability_date, notes,
                is_ultra_critical, is_delivered, delivered_at, is_staging, rfqs,
                po_supplier, po_number, po_date,
                has_treatment, treatment_date, treatment_notes, is_treatment_confirmed, treatment_confirmed_at,
                created_at, ddp_officina_item_id
            ) VALUES (
                @ProjectId, @RequestDate, @Description, @Type, 2, @AvailabilityDate, '',
                0, 0, NULL, 1, '[]',
                @PoSupplier, @PoNumber, @PoDate,
                @HasTreatment, NULL, @TreatmentNotes, 0, NULL,
                @CreatedAt, @DdpOfficinaItemId
            );
            SELECT LAST_INSERT_ID()", new
        {
            item.ProjectId,
            RequestDate = (item.CreatedAt ?? DateTime.Now).Date,
            Description = description,
            Type = inferredType,
            AvailabilityDate = item.DateNeeded,
            PoSupplier = poSupplier,
            PoNumber = item.DaneaRef,
            PoDate = item.OrderDate,
            HasTreatment = hasTreatment ? 1 : 0,
            TreatmentNotes = item.Treatment,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DdpOfficinaItemId = officinaItemId,
        });
        return new SyncResult { WorkRequestId = newId, ProjectId = item.ProjectId, Created = true };
    }

    /// <summary>
    /// Da chiamare PRIMA di eliminare la riga officina: rimuove la bozza in staging
    /// collegata. Le lavorazioni promosse restano (le scollega la FK ON DELETE SET NULL).
    /// Ritorna la commessa toccata se una bozza è stata eliminata.
    /// </summary>
    public static int? DeleteStagingFor(IDbConnection c, int officinaItemId)
    {
        int? projectId = c.ExecuteScalar<int?>(@"
            SELECT project_id FROM project_work_requests
            WHERE ddp_officina_item_id = @Id AND is_staging = 1", new { Id = officinaItemId });
        if (!projectId.HasValue) return null;
        c.Execute(
            "DELETE FROM project_work_requests WHERE ddp_officina_item_id = @Id AND is_staging = 1",
            new { Id = officinaItemId });
        return projectId;
    }

    /// <summary>
    /// Ritorno di stato: lavorazione consegnata → la riga officina collegata passa a DISP
    /// (chiusura positiva unica della matrice stati v7, che ha assorbito il vecchio COS).
    /// Ritorna (projectId, officinaItemId) se lo stato è effettivamente cambiato.
    /// </summary>
    public static (int ProjectId, int OfficinaItemId)? MarkOfficinaBuilt(IDbConnection c, int workRequestId)
    {
        var link = c.QueryFirstOrDefault<(int? OfficinaItemId, int ProjectId)>(@"
            SELECT ddp_officina_item_id AS OfficinaItemId, project_id AS ProjectId
            FROM project_work_requests WHERE id = @Id", new { Id = workRequestId });
        if (link.OfficinaItemId == null) return null;
        int rows = c.Execute(@"
            UPDATE ddp_officina_items SET item_status = 'DISP', updated_at = NOW()
            WHERE id = @Id AND item_status <> 'DISP'", new { Id = link.OfficinaItemId });
        return rows > 0 ? (link.ProjectId, link.OfficinaItemId.Value) : null;
    }
}
