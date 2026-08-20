using System;

namespace ATEC.PM.Shared.DTOs;

public class BomItemListItem : System.ComponentModel.INotifyPropertyChanged
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? CatalogItemId { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public int RowNumber { get; set; }

    private decimal _quantity;
    public decimal Quantity
    {
        get => _quantity;
        set { _quantity = value; OnPropertyChanged(nameof(Quantity)); OnPropertyChanged(nameof(TotalCost)); }
    }

    private decimal _unitCost;
    public decimal UnitCost
    {
        get => _unitCost;
        set { _unitCost = value; OnPropertyChanged(nameof(UnitCost)); OnPropertyChanged(nameof(TotalCost)); }
    }

    public decimal TotalCost => Quantity * UnitCost;

    public int? SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string Manufacturer { get; set; } = "";

    private string _itemStatus = "VER";
    public string ItemStatus
    {
        get => _itemStatus;
        set { _itemStatus = value; OnPropertyChanged(nameof(ItemStatus)); }
    }

    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    // IDDoc dell'ordine fornitore Danea generato da ATEC PM (null = riga mai ordinata via RDO):
    // abilita il link al rendering dell'ordine nel client web.
    public int? DaneaOrderIdDoc { get; set; }
    public DateTime? DateNeeded { get; set; }
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";
    // Snapshot codice ATEC (nuova codifica); vuoto = riga senza mapping.
    public string AtecCode { get; set; } = "";
    public int? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Quando la riga è passata a DISPONIBILE / CONSEGNATO l'ultima volta: ricavata dalla
    /// cronistoria (ddp_item_events), non è un campo scritto a mano. Null = mai consegnata.
    /// </summary>
    public DateTime? DeliveredAt { get; set; }

    // Concorrenza ottimistica: versione vista al caricamento (rispedita nel PUT come ExpectedUpdatedAt).
    public DateTime? UpdatedAt { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class BomItemSaveRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? CatalogItemId { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "PZ";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public int? SupplierId { get; set; }
    public string Manufacturer { get; set; } = "";
    public string ItemStatus { get; set; } = "VER";
    /// <summary>
    /// «Inserito da» (segnalazione #61): lo compila il client alla nascita della riga col nome
    /// di chi è collegato, e da lì è correggibile a mano dalla griglia o dal dialogo.
    /// NULL (campo assente nel JSON) = non toccare — stessa regola di WorkType/WorkHours in
    /// officina: parecchi punti salvano una riga senza conoscere tutti i campi e non devono
    /// cancellare l'autore. Per svuotarlo si manda la stringa vuota.
    /// </summary>
    public string? RequestedBy { get; set; }
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";
    // Snapshot codice ATEC (senza punti in DB; il client può inviare formattato).
    public string AtecCode { get; set; } = "";

    // Il fornitore si aggiorna solo se true: i client che non gestiscono il campo (WPF)
    // lasciano il default false e non azzerano il fornitore impostato dal web.
    public bool UpdateSupplier { get; set; }

    // true = aggiorna anche snapshot catalogo (part_number, costo, manufacturer, catalog_item_id, atec).
    // Usato dal picker alternative fornitore / applicazione vincitore RDO.
    public bool UpdateCatalogSnapshot { get; set; }

    // Il costo unitario si aggiorna solo se true: gli edit inline rimandano il costo della riga
    // invariato e non devono sovrascriverlo (stessa logica di UpdateSupplier). Lo usa il
    // dettaglio RDO, dove il prezzo battuto sull'offerta è anche il costo della riga distinta.
    public bool UpdateUnitCost { get; set; }

    // Concorrenza ottimistica: se valorizzata e diversa da quella sul server → 409 (modificata da altri). Null = nessun controllo.
    public DateTime? ExpectedUpdatedAt { get; set; }
}

/// <summary>Riga inbox Acquisti cross-commessa (DDP commerciale, stati VER/CHEK/RO/DO).</summary>
public class AcquistiInboxItem : BomItemListItem
{
    public string ProjectCode { get; set; } = "";
    public string ProjectTitle { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public int? DaysLate { get; set; }
    /// <summary>true = la riga è già in una RDO non annullata (non riproporla per la gara).</summary>
    public bool InActiveRfq { get; set; }
    /// <summary>RDO viva che contiene la riga (per il link «in gara — RDO #x» nel pannello).</summary>
    public int? ActiveRfqId { get; set; }
    public string ActiveRfqStatus { get; set; } = "";
    /// <summary>Oggetto della RDO (titolo della gara): va in tooltip, NON nella descrizione riga.</summary>
    public string ActiveRfqSubject { get; set; } = "";
}

// Notifica real-time (SignalR) di modifica della distinta (DDP) di una commessa.
// Inviata dal server ai client nel gruppo "project-{ProjectId}"; il client la usa come segnale per ricaricare.
public class DdpChange
{
    public int ProjectId { get; set; }
    public string Action { get; set; } = "";   // "create" | "update" | "delete"
    public int ItemId { get; set; }

    // Quale distinta è cambiata: "COMMERCIAL" | "OFFICINA". Permette ai client di ricaricare
    // solo la griglia interessata (default COMMERCIAL per compatibilità con client vecchi).
    public string DdpType { get; set; } = "COMMERCIAL";
}

// Notifica real-time (SignalR) di modifica ai documenti (file/cartelle su disco) di una commessa.
// Inviata al gruppo "project-{ProjectId}"; il client la usa come segnale per ricaricare l'albero/elenco.
public class DocumentsChange
{
    public int ProjectId { get; set; }
    public string Action { get; set; } = ""; // "upload" | "create" | "rename" | "move" | "delete"
}

// Notifica real-time (SignalR) sull'ANAGRAFICA commesse: creata, modificata o eliminata.
// Inviata al gruppo globale "projects-all": chi ha davanti un elenco di commesse ricarica
// la lista (una commessa eliminata da un collega sparisce senza ricaricare la pagina).
public class ProjectChange
{
    public int ProjectId { get; set; }
    public string Action { get; set; } = ""; // "create" | "update" | "delete"
    public string Code { get; set; } = "";
}

public static class DdpStatusMap
{
    // Fallback per le notifiche cambio-stato. Gli stati "veri" stanno in ddp_statuses (editabili);
    // qui solo per un'etichetta leggibile se la mappa DB non è a portata. ToLabel ritorna la chiave se assente.
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["ANN"] = "ANNULLATO",
        ["SOSP"] = "SOSPESO",
        ["RAM"] = "RIMESSO A MAGAZZINO",
        ["SOST"] = "SOSTITUITO",
        ["DISP"] = "DISPONIBILE / CONSEGNATO",
        ["CON"] = "CONSEGNATO",
        ["COS"] = "COSTRUITO",
        ["MIT"] = "MATERIALE IN TRATTAMENTO",
        ["DC"] = "DA COSTRUIRE",
        ["DO"] = "DA ORDINARE",
        ["ASS"] = "ASSEGNATO AL MONTATORE",
        ["CHEK"] = "MAT. CHE NECESSITA CONTROLLO TECNICO/COMMERCIALE",
        ["IO"] = "IN ORDINE",
        ["PAR"] = "PARZIALMENTE CONSEGNATO o COSTRUITO",
        ["RO"] = "RICHIESTA OFFERTA",
        ["VER"] = "VERIFICARE SE DISPONIBILE A MAG"
    };

    public static string ToLabel(string status) =>
        Labels.TryGetValue(status, out string? label) ? label : status;
}
