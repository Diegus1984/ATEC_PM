namespace ATEC.PM.Shared.DTOs;

public class CodexListItem
{
    public int Id { get; set; }
    public string Codice { get; set; } = "";
    public string CodeForn { get; set; } = "";
    public string Fornitore { get; set; } = "";
    public decimal PrezzoForn { get; set; }
    public string Iva { get; set; } = "";
    public string Produttore { get; set; } = "";
    public DateTime Data { get; set; }
    public string Descr { get; set; } = "";
    public string Note { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Tipologia { get; set; } = "";
    public string Extra1 { get; set; } = "";
    public string Extra2 { get; set; } = "";
    public string Extra3 { get; set; } = "";
    public string CodeProd { get; set; } = "";
    public string Spec { get; set; } = "";
    public int Oper { get; set; }
    public string Um { get; set; } = "";
    public string Ubicazione { get; set; } = "";
    public string Codexforn { get; set; } = "";
}

public class CodexSyncStatus
{
    public bool IsSyncing { get; set; }
    public DateTime? LastSync { get; set; }
    public int TotalRows { get; set; }
    public string? LastError { get; set; }
}

public class CodexNewItemRequest
{
    public string Prefisso { get; set; } = "";      // 101, 102, 201, etc
    public string Descrizione { get; set; } = "";
}

public class CodexGeneratedCode
{
    public string Codice { get; set; } = "";        // 101-280226-001
    public int Id { get; set; }
}
