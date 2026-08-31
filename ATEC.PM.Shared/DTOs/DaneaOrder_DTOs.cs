namespace ATEC.PM.Shared.DTOs;

// Rendering di un ordine fornitore Danea (archivio Atec_PM) per il popup web:
// lettura diretta da TDocTestate/TDocRighe/TDocIva, nessuna scrittura.
// I campi economici sono [DatoSensibile] (micro «prices») e nullable: il filtro
// li azzera scrivendo null — su un decimal secco scriverebbe 0, che col
// WhenWritingNull resterebbe nel JSON come un prezzo vero.

public class DaneaOrderView
{
    public int IdDoc { get; set; }
    public int Num { get; set; }
    public DateTime? Date { get; set; }
    public string DescDoc { get; set; } = "";
    public string OrderStatus { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public DateTime? ExpectedDate { get; set; }
    public string InternalNote { get; set; } = "";
    // "VECCHIO" quando l'ordine arriva dal vecchio archivio Danea (Srl-2020-2021):
    // durante la migrazione i Rif. Danea scritti a mano possono puntare lì, e il
    // popup deve dire da quale archivio sta leggendo. Null/assente = archivio attuale.
    public string? Archivio { get; set; }
    // Numerazioni ripartite da capo con la migrazione: lo stesso numero può esistere
    // in ENTRAMBI gli archivi. True = trovato nell'attuale ma un omonimo sta anche
    // nel vecchio — il popup deve avvisare di controllare il fornitore.
    public bool AmbiguoConVecchio { get; set; }

    // Snapshot anagrafica fornitore com'è sulla testata del documento.
    public string SupplierName { get; set; } = "";
    public string SupplierAddress { get; set; } = "";
    public string SupplierZip { get; set; } = "";
    public string SupplierCity { get; set; } = "";
    public string SupplierProvince { get; set; } = "";
    public string SupplierCountry { get; set; } = "";
    public string SupplierVat { get; set; } = "";

    [DatoSensibile]
    public decimal? TotNet { get; set; }
    [DatoSensibile]
    public decimal? TotVat { get; set; }
    [DatoSensibile]
    public decimal? TotDoc { get; set; }

    public List<DaneaOrderRowView> Rows { get; set; } = new();
    public List<DaneaOrderVatView> VatSummary { get; set; } = new();
}

public class DaneaOrderRowView
{
    public string Code { get; set; } = "";
    public string SupplierCode { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    [DatoSensibile]
    public decimal? UnitPrice { get; set; }
    public string VatCode { get; set; } = "";
    [DatoSensibile]
    public decimal? NetAmount { get; set; }
    [DatoSensibile]
    public decimal? GrossAmount { get; set; }
}

public class DaneaOrderVatView
{
    public string VatCode { get; set; } = "";
    [DatoSensibile]
    public decimal? NetAmount { get; set; }
    [DatoSensibile]
    public decimal? VatAmount { get; set; }
}
