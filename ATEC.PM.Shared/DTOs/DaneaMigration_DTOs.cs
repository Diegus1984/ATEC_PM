namespace ATEC.PM.Shared.DTOs;

// Trasferimento catalogo Danea vecchio → archivio «Atec_PM» (piano F2,
// PIANO-MIGRAZIONE-DANEA-ATEC.md). Il vecchio archivio è SOLO sorgente.

public class DaneaMigrationStatus
{
    public int OldArticles { get; set; }
    public int NewArticles { get; set; }
    public bool ImagesSourceReachable { get; set; }
    public bool ImagesTargetReachable { get; set; }
    /// <summary>Perche' le immagini non si copiano (vuoto se si copiano): lo mostra il badge.</summary>
    public string ImagesError { get; set; } = "";
    public string OldArchive { get; set; } = "";
    public string NewArchive { get; set; } = "";
    /// <summary>Valori distinti per i filtri colonna (stesso payload dello status: un solo round-trip).</summary>
    public List<string> Categories { get; set; } = new();
    public List<string> Subcategories { get; set; } = new();
    public List<string> Suppliers { get; set; } = new();
    public List<string> Manufacturers { get; set; } = new();
}

public class DaneaFilterOptions
{
    public List<string> Categories { get; set; } = new();
    public List<string> Subcategories { get; set; } = new();
    public List<string> Suppliers { get; set; } = new();
    public List<string> Manufacturers { get; set; } = new();
}

public class DaneaOldArticle
{
    public int IdArticolo { get; set; }
    public string CodArticolo { get; set; } = "";
    public string Descrizione { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Sottocategoria { get; set; } = "";
    public string Udm { get; set; } = "";
    public string Fornitore { get; set; } = "";
    public string Produttore { get; set; } = "";
    public decimal PrezzoForn { get; set; }
    public string Extra1 { get; set; } = "";
    public bool HasImage { get; set; }
    /// <summary>Già presente in Atec_PM (match per CodArticolo): non ritrasferibile.</summary>
    public bool Transferred { get; set; }
}

public class DaneaTransferRequest
{
    public List<int> ArticleIds { get; set; } = new();
}

public class DaneaTransferResult
{
    public int IdArticolo { get; set; }
    public string CodArticolo { get; set; } = "";
    /// <summary>ok | skipped (già presente) | error</summary>
    public string Outcome { get; set; } = "ok";
    public string Error { get; set; } = "";
    public int ImagesCopied { get; set; }
    public string ImageWarning { get; set; } = "";
}

public class DaneaTransferReport
{
    public int Ok { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public int ImagesCopied { get; set; }
    /// <summary>Righe allineate nel Catalogo articoli di ATEC PM (specchio) subito dopo il lotto.</summary>
    public int CatalogAligned { get; set; }
    /// <summary>Se l'allineamento non e' riuscito: gli articoli SONO passati lo stesso.</summary>
    public string CatalogWarning { get; set; } = "";
    public List<DaneaTransferResult> Results { get; set; } = new();
}
