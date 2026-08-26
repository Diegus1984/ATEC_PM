namespace ATEC.PM.Shared.DTOs;

// Ciclo RDO Acquisti (piano Fase 3): testata + righe BOM + offerte fornitori.

public class PurchaseRfqListItem
{
    public int Id { get; set; }
    public string AtecCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "DRAFT";
    public string Notes { get; set; } = "";
    public int? CreatedBy { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int ItemCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public int OfferCount { get; set; }
    /// <summary>Numero dell'ordine fornitore creato in Danea (null = non ancora generato).</summary>
    public int? DaneaOrderNum { get; set; }
    /// <summary>IDDoc dell'ordine Danea: chiave per il rendering (GET /api/danea-orders/{idDoc}).</summary>
    public int? DaneaOrderIdDoc { get; set; }

    // Vincitore e commessa (le RDO sono mono-commessa): chiavi del raggruppamento
    // fornitore+commessa nel pannello «Ordini da generare» del client web.
    public int? WinnerSupplierId { get; set; }
    public string WinnerSupplierName { get; set; } = "";
    public decimal? WinnerUnitPrice { get; set; }
    public int? ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
}

public class PurchaseRfqDetail : PurchaseRfqListItem
{
    public List<PurchaseRfqItemDto> Items { get; set; } = new();
    public List<PurchaseRfqOfferDto> Offers { get; set; } = new();
}

public class PurchaseRfqItemDto
{
    public int Id { get; set; }
    public int RfqId { get; set; }
    public int BomItemId { get; set; }
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public decimal Quantity { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string ItemStatus { get; set; } = "";
    public decimal? UnitCost { get; set; }
    public DateTime? DateNeeded { get; set; }
    public string DaneaRef { get; set; } = "";
    public int? DaneaOrderIdDoc { get; set; }
    /// <summary>
    /// Codice ATEC della riga di distinta. Serve all'aggiudicazione per non riscrivere
    /// l'identità di una riga che è di un ALTRO articolo (gare nate miste).
    /// </summary>
    public string AtecCode { get; set; } = "";
}

public class PurchaseRfqOfferDto
{
    public int Id { get; set; }
    public int RfqId { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string SupplierEmail { get; set; } = "";
    public int? CatalogItemId { get; set; }
    public string CatalogCode { get; set; } = "";
    public decimal? UnitPrice { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string Notes { get; set; } = "";
    public DateTime? EmailSentAt { get; set; }
    public bool IsWinner { get; set; }
}

public class PurchaseRfqCreateRequest
{
    public string AtecCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<int> BomItemIds { get; set; } = new();
    public List<int> SupplierIds { get; set; } = new();
}

public class PurchaseRfqOfferSaveRequest
{
    public int SupplierId { get; set; }
    public int? CatalogItemId { get; set; }
    public decimal? UnitPrice { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string Notes { get; set; } = "";
}

public class PurchaseRfqCreateOrderRequest
{
    /// <summary>Data prevista conclusione ordine (facoltativa) mostrata in Danea.</summary>
    public DateTime? ExpectedDate { get; set; }
}

/// <summary>
/// Ordine Danea multi-RDO: più RDO chiuse dello stesso fornitore vincitore e della
/// stessa commessa → un ordine multi-riga (una riga per RDO).
/// </summary>
public class PurchaseRfqCreateOrderMultiRequest
{
    public List<int> RfqIds { get; set; } = new();
    /// <summary>Data prevista conclusione ordine (facoltativa) mostrata in Danea.</summary>
    public DateTime? ExpectedDate { get; set; }
}

/// <summary>
/// Offerta in attesa di richiesta email: una riga per (RDO aperta senza ordine ×
/// fornitore non ancora contattato). Il client le raggruppa per fornitore e compone
/// la mailto con l'articolo Danea del fornitore (codice+descrizione dal catalogo).
/// </summary>
public class PurchaseRfqEmailCandidate
{
    public int OfferId { get; set; }
    public int RfqId { get; set; }
    public string AtecCode { get; set; } = "";
    public string RfqDescription { get; set; } = "";
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string SupplierEmail { get; set; } = "";
    /// <summary>Codice articolo Danea del fornitore (vuoto se offerta senza articolo).</summary>
    public string CatalogCode { get; set; } = "";
    public string CatalogDescription { get; set; } = "";
    public decimal Quantity { get; set; }
    public int? ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
}

/// <summary>Registra l'invio (mailto) delle richieste offerta per le offerte indicate.</summary>
public class PurchaseRfqMarkEmailedRequest
{
    public List<int> OfferIds { get; set; } = new();
}

// ── Richiesta offerta DIRETTA dalle righe distinta (flusso Diego 23/07/2026):
// l'utente spunta le righe da acquistare, il sistema trova DA SOLO i fornitori
// possibili (quello della riga + gli equivalenti via codice ATEC), crea le RDO
// sotto il cofano e compone le mailto. L'ordine nasce SOLO dopo (prezzi→vincitore).

public class PurchaseRfqOfferPlanRequest
{
    public List<int> BomItemIds { get; set; } = new();
}

/// <summary>Un fornitore interpellabile per le righe selezionate, con i suoi articoli.</summary>
/// <summary>
/// Il piano di interpello: quale fornitore per quali righe. <b>Senza email</b> — l'indirizzo
/// serve a MANDARE la richiesta d'offerta, e quel percorso ha il suo DTO
/// (<see cref="PurchaseRfqOfferDto"/>, che il dialogo Acquisti usa per il <c>mailto:</c>).
/// Qui era una copia che nessuno leggeva: un contatto in meno che gira.
/// </summary>
public class OfferPlanSupplier
{
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public List<OfferPlanItem> Items { get; set; } = new();
}

public class OfferPlanItem
{
    public int BomItemId { get; set; }
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public int? CatalogItemId { get; set; }
    /// <summary>Codice articolo Danea di QUESTO fornitore (dalla riga o dal mapping ATEC).</summary>
    public string ArticleCode { get; set; } = "";
    public string ArticleDescription { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal? ListCost { get; set; }
    /// <summary>true = è il fornitore già indicato sulla riga; false = alternativa dal mapping.</summary>
    public bool IsRowSupplier { get; set; }
}

public class PurchaseRfqRequestOffersSelection
{
    public int BomItemId { get; set; }
    public List<int> SupplierIds { get; set; } = new();
}

/// <summary>Crea le RDO (una per commessa × codice) con le offerte dei fornitori scelti.</summary>
public class PurchaseRfqRequestOffersRequest
{
    public List<PurchaseRfqRequestOffersSelection> Selections { get; set; } = new();
}

public class PurchaseRfqSelectWinnerRequest
{
    public int OfferId { get; set; }
    /// <summary>Stato da applicare alle righe BOM (default RO se ammesso, altrimenti DO).</summary>
    public string TargetStatus { get; set; } = "RO";
}
