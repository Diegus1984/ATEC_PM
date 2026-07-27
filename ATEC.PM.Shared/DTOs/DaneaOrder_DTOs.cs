namespace ATEC.PM.Shared.DTOs;

// Rendering di un ordine fornitore Danea (archivio Atec_PM) per il popup web:
// lettura diretta da TDocTestate/TDocRighe/TDocIva, nessuna scrittura.

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

    // Snapshot anagrafica fornitore com'è sulla testata del documento.
    public string SupplierName { get; set; } = "";
    public string SupplierAddress { get; set; } = "";
    public string SupplierZip { get; set; } = "";
    public string SupplierCity { get; set; } = "";
    public string SupplierProvince { get; set; } = "";
    public string SupplierCountry { get; set; } = "";
    public string SupplierVat { get; set; } = "";

    public decimal TotNet { get; set; }
    public decimal TotVat { get; set; }
    public decimal TotDoc { get; set; }

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
    public decimal UnitPrice { get; set; }
    public string VatCode { get; set; } = "";
    public decimal NetAmount { get; set; }
    public decimal GrossAmount { get; set; }
}

public class DaneaOrderVatView
{
    public string VatCode { get; set; } = "";
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
}
