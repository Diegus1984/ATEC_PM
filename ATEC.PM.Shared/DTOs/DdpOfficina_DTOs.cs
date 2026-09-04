using System;
using ATEC.PM.Shared;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Natura della lavorazione di una riga officina (<c>ddp_officina_items.work_type</c>).
/// <para>
/// <see cref="Print3D"/> è arrivata con la segnalazione #87 e si comporta in tutto come
/// <see cref="Internal"/> — stessi stati, stesse viste, stesso posto nel Bilancio: è lavoro
/// fatto in casa. Quello che cambia è la tariffa oraria con cui se ne calcola il costo, ed è
/// per quello che ha un tipo suo invece di essere «interna» e basta.
/// </para>
/// </summary>
public static class OfficinaWorkTypes
{
    public const string Internal = "Internal";
    public const string External = "External";
    public const string Print3D = "Print3D";

    /// <summary>Tipi lavorati in casa: dividono le stesse regole di stato e le stesse viste.</summary>
    public static bool IsInHouse(string? workType) =>
        string.Equals(workType, Internal, StringComparison.OrdinalIgnoreCase)
        || string.Equals(workType, Print3D, StringComparison.OrdinalIgnoreCase);
}

// Riga della DDP Officina (particolari meccanici, tabella dedicata ddp_officina_items).
// I nomi proprietà ricalcano BomItemListItem così le viste condivise (es. Sintesi DDP)
// possono deserializzare le righe officina nello stesso shape senza adattatori.
public class OfficinaItemListItem : System.ComponentModel.INotifyPropertyChanged
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string PartNumber { get; set; } = "";   // Codice 101 (Codex), denormalizzato
    public string Description { get; set; } = "";
    public int RowNumber { get; set; }

    private decimal _quantity;
    public decimal Quantity
    {
        get => _quantity;
        set { _quantity = value; OnPropertyChanged(nameof(Quantity)); OnPropertyChanged(nameof(TotalCost)); }
    }

    /// <summary>Pezzi già prodotti / costruiti (0 … Quantity).</summary>
    public decimal QuantityProduced { get; set; }

    /// <summary>
    /// Ore di lavorazione imputate a mano (officine interne): × tariffa oraria d'anagrafica
    /// = costo unitario. NULL = non imputate (diverso da zero ore). Segnalazione #54.
    /// </summary>
    public decimal? WorkHours { get; set; }

    /// <summary>
    /// Tariffa oraria con cui è stato fatto il conto (#87). NULL = costo scritto a mano o
    /// riga più vecchia della v95. Resta sulla riga perché le tariffe in anagrafica sono più
    /// d'una: senza, riaprendo il particolare non si saprebbe quale è stata scelta.
    /// </summary>
    [DatoSensibile]
    public decimal? HourlyRate { get; set; }

    private decimal? _unitCost;
    /// <summary>Nullable per la regola dei dati sensibili (§12.3): null = «non lo puoi vedere», mai 0 finto.</summary>
    [DatoSensibile]
    public decimal? UnitCost
    {
        get => _unitCost;
        set { _unitCost = value; OnPropertyChanged(nameof(UnitCost)); OnPropertyChanged(nameof(TotalCost)); }
    }

    /// <summary>Calcolata: diventa null da sola quando UnitCost è azzerato dal filtro prezzi.</summary>
    [DatoSensibile]
    public decimal? TotalCost => UnitCost == null ? null : Quantity * UnitCost.Value;

    public string Material { get; set; } = "";     // Materiale (es. ALLUMINIO, C40, INOX...)
    public string Treatment { get; set; } = "";    // Trattamento (es. ANODIZZATO, BRUNITO...)
    public int? SupplierId { get; set; }
    public string SupplierName { get; set; } = ""; // testo libero (officina esterna), non FK

    private string _itemStatus = "DO";
    public string ItemStatus
    {
        get => _itemStatus;
        set { _itemStatus = value; OnPropertyChanged(nameof(ItemStatus)); }
    }

    /// <summary>
    /// Natura della lavorazione: vedi <see cref="OfficinaWorkTypes"/> — "Internal" (officina
    /// ATEC), "External" (fornitore), "Print3D" (stampa 3D, #87). Vuoto = non ancora
    /// classificata. Serve al Bilancio, che scompone la voce «Lavorazioni Officine» in interne
    /// ed esterne: lo stato DDP lo dice solo finché la riga è in corso, questa colonna lo
    /// conserva anche dopo la chiusura.
    /// </summary>
    public string WorkType { get; set; } = "";

    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";

    // ── #142: l'ordine Danea del GREZZO (derivazione #135), per l'occhio in griglia ──
    /// <summary>Codice del 201 di derivazione, col punto. Vuoto = il 101 non ha grezzo.</summary>
    public string GrezzoCodice { get; set; } = "";
    /// <summary>Rif. Danea della riga grezzo in DDP Commerciale ("" = non ancora ordinato).</summary>
    public string GrezzoDaneaRef { get; set; } = "";
    /// <summary>IDDoc dell'ordine Danea del grezzo (null = ordine non generato da ATEC PM).</summary>
    public int? GrezzoDaneaOrderIdDoc { get; set; }

    /// <summary>Il 201 di derivazione non ha NESSUN articolo Danea (01/09/2026): la catena
    /// ambra in griglia apre l'associazione al volo. Calcolato dal server.</summary>
    public bool GrezzoNeedsMapping { get; set; }

    public DateTime? DateNeeded { get; set; }
    /// <summary>Data in cui la riga è passata a In Ordine (IO); auto-compilata al primo IO.</summary>
    public DateTime? OrderDate { get; set; }
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";
    public string Notes { get; set; } = "";

    // «Comanda il padre»: se la riga è stata importata dalla composizione Codex, id della
    // riga padre in distinta + quantità unitaria di composizione (per 1 padre). Al cambio
    // Qtà del padre il server riallinea i figli con delta = CompositionQty × ΔQtà.
    public int? ParentOfficinaItemId { get; set; }
    public decimal? CompositionQty { get; set; }

    public int? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// «Consegnato il» (#82): data di consegna/disponibilità, editabile a mano in griglia.
    /// Null = non ancora valorizzata. In migrazione v90 le righe già chiuse ereditano la data
    /// dall'ultimo passaggio a CON/COS/DISP in cronistoria.
    /// </summary>
    public DateTime? DeliveredAt { get; set; }

    // Concorrenza ottimistica: versione vista al caricamento (rispedita nel PUT come ExpectedUpdatedAt).
    public DateTime? UpdatedAt { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class OfficinaItemSaveRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    /// <summary>Pezzi già prodotti / costruiti (0 … Quantity).</summary>
    public decimal QuantityProduced { get; set; }
    /// <summary>Ore di lavorazione (officine interne). NULL = non imputate. Segnalazione #54.</summary>
    public decimal? WorkHours { get; set; }
    /// <summary>
    /// Tariffa oraria scelta per il calcolo (#87). Stessa regola di WorkHours: NULL = il
    /// chiamante non gestisce il campo → tariffa invariata sulla riga.
    /// </summary>
    [DatoSensibile]
    public decimal? HourlyRate { get; set; }
    /// <summary>Sensibile e nullable: NULL = costo invariato sulla riga (chi non ha il micro prezzi manda null o viene respinto, §12.3).</summary>
    [DatoSensibile]
    public decimal? UnitCost { get; set; }
    public string Material { get; set; } = "";
    public string Treatment { get; set; } = "";
    public int? SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string ItemStatus { get; set; } = "DO";   // DA ORDINARE (default officina; la commerciale parte da VER)
    /// <summary>
    /// "Internal" / "External" / "" (non classificata). Vedi OfficinaItemListItem.WorkType.
    /// NULL (campo assente nel JSON) = non toccare la classificazione esistente: così i
    /// chiamanti che non conoscono il campo non la cancellano.
    /// </summary>
    public string? WorkType { get; set; }
    /// <summary>
    /// «Inserito da» (segnalazione #61). Stessa regola di WorkType: NULL (campo assente nel
    /// JSON) = non toccare l'autore già scritto; stringa vuota = svuotarlo.
    /// </summary>
    public string? RequestedBy { get; set; }
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public DateTime? OrderDate { get; set; }
    /// <summary>«Consegnato il» (#82), editabile in griglia.</summary>
    public DateTime? DeliveredAt { get; set; }
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";
    public string Notes { get; set; } = "";

    // Concorrenza ottimistica: se valorizzata e diversa da quella sul server → 409 (modificata da altri). Null = nessun controllo.
    public DateTime? ExpectedUpdatedAt { get; set; }
    public bool? UpdateCodexPrice { get; set; }
}

/// <summary>Import in distinta officina della composizione Codex di un codice padre (figli diretti).</summary>
public class OfficinaImportCompositionRequest
{
    public int CodexParentId { get; set; }
    public string RequestedBy { get; set; } = "";
}

    public class OfficinaImportCompositionResult
{
    public int Added { get; set; }      // nuove righe inserite (totale sulle due DDP)
    public int Updated { get; set; }    // righe esistenti con quantità sommata
    public int Skipped { get; set; }    // figli non importabili (articoli da Catalogo)
    public decimal ParentQuantity { get; set; } = 1;  // moltiplicatore applicato (Qtà del padre in distinta)
    // #119: i componenti si dividono fra le due distinte, e chi importa deve sapere dove
    // sono finiti — altrimenti «14 nuove righe» in una griglia che ne mostra 9 sembra un bug.
    public int AddedOfficina { get; set; }
    public int AddedCommerciale { get; set; }
}
