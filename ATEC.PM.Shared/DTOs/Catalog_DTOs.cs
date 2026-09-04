namespace ATEC.PM.Shared.DTOs;

public class CatalogItemListItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Unit { get; set; } = "";
    /// <summary>
    /// Costo unitario d'acquisto. <b>Nullable per la regola dei dati sensibili</b> (§12.3):
    /// null = «non lo puoi vedere», mai uno 0,00 € finto.
    /// <para>🪤 Fino al 20/08 questo campo scavalcava il micro «vede prezzi» delle DDP: chi
    /// aveva i costi azzerati nella griglia li rileggeva interi dal picker del Catalogo, che
    /// sta dentro la stessa sezione di commessa. Il filtro non c'entrava: era spento, perché
    /// ricava i micro dalle CHIAVI dell'endpoint e l'endpoint non ne aveva nessuna.</para>
    /// </summary>
    [DatoSensibile]
    public decimal? UnitCost { get; set; }

    /// <summary>Prezzo di listino: stessa storia di <see cref="UnitCost"/>.</summary>
    [DatoSensibile]
    public decimal? ListPrice { get; set; }

    public int? SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string SupplierCode { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public bool IsActive { get; set; }

    // Mapping Danea ↔ codice ATEC (Extra1): codice NUOVO Codex associato all'articolo
    // (formattato col punto di display) e riga Codex risolta. Vuoto = non associato.
    public string AtecCode { get; set; } = "";
    public int? CodexItemId { get; set; }
    public int? EasyfattId { get; set; }
}

// ── MAPPING DANEA ↔ CODICE ATEC (piano Acquisti, 21/07/2026) ──────────────

public class CatalogMappingAssignRequest
{
    public int CatalogItemId { get; set; }
    public int CodexItemId { get; set; }
    // Riassegnazione consapevole: true = sovrascrivi un'associazione esistente
    // (il client chiede conferma esplicita prima di rimandare con Force).
    public bool Force { get; set; }
}

// Associazione partendo da una riga distinta (Inbox Acquisti): l'articolo Danea
// lo risolve il server (catalog_item_id o match esatto sul part_number).
public class CatalogMappingAssignFromBomRequest
{
    public int BomItemId { get; set; }
    public int CodexItemId { get; set; }
    public bool Force { get; set; }
}

public class CatalogMappingAssignResult
{
    public bool Assigned { get; set; }
    // L'articolo è già associato a un altro codice: serve la conferma (Force).
    public bool RequiresForce { get; set; }
    public string CurrentAtecCode { get; set; } = "";
}

public class CatalogMappingUnassignRequest
{
    public int CatalogItemId { get; set; }
}

public class CatalogItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Unit { get; set; } = "PZ";
    public decimal UnitCost { get; set; }
    public decimal ListPrice { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierCode { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
