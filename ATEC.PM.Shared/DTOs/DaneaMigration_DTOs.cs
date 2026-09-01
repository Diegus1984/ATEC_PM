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
    /// <summary>ID effettivo in Atec_PM (= IdArticolo, salvo ID occupato e rimappato). Serve all'allineamento specchio.</summary>
    public int IdInAtecPm { get; set; }
    /// <summary>Cosa e' successo oltre al trasferimento: ID rimappato, fornitore copiato… (#129)</summary>
    public string Note { get; set; } = "";
}

// Ripescaggio automatico dal VECCHIO archivio (#129): gli articoli nati dopo lo
// spartiacque (cursore) arrivano da soli almeno due volte al giorno + a richiesta.

public class DaneaPullReport
{
    /// <summary>Esito leggibile (spartiacque impostato, nessun nuovo, gia' in corso…).</summary>
    public string Message { get; set; } = "";
    /// <summary>true se un trasferimento e' stato eseguito (Transfer valorizzato).</summary>
    public bool Ran { get; set; }
    /// <summary>Articoli nuovi trovati oltre lo spartiacque in questo giro.</summary>
    public int NewArticles { get; set; }
    /// <summary>Cursore dopo il giro (ultimo IDArticolo del vecchio considerato).</summary>
    public long LastSeenId { get; set; }
    public DaneaTransferReport? Transfer { get; set; }
    /// <summary>Specchio prezzi sugli articoli già trasferiti (gira a ogni giro, anche senza articoli nuovi).</summary>
    public DaneaMirrorReport? Mirror { get; set; }
}

// Specchio prezzi (01/09/2026): per gli articoli GIÀ trasferiti il vecchio archivio
// resta il padrone del prezzo, finché i colleghi ritoccano i listini là dentro.

public class DaneaMirrorReport
{
    /// <summary>Articoli di Atec_PM che esistono anche nel vecchio archivio.</summary>
    public int Checked { get; set; }
    /// <summary>Articoli riscritti in Atec_PM in questo giro.</summary>
    public int Aligned { get; set; }
    /// <summary>Righe riallineate nel Catalogo articoli di ATEC PM.</summary>
    public int CatalogAligned { get; set; }
    /// <summary>Se l'allineamento del Catalogo non è riuscito: in Danea i prezzi SONO corretti.</summary>
    public string CatalogWarning { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>Registro di cosa è cambiato: un articolo può comparire su più campi.</summary>
    public List<DaneaMirrorChange> Changes { get; set; } = new();
}

public class DaneaMirrorChange
{
    public string CodArticolo { get; set; } = "";
    public int IdInAtecPm { get; set; }
    /// <summary>Campo Danea riscritto (vedi PrezziSpecchio.Campi).</summary>
    public string Campo { get; set; } = "";
    public decimal Prima { get; set; }
    public decimal Dopo { get; set; }
}

public class DaneaPullStatus
{
    public bool Enabled { get; set; }
    public int IntervalHours { get; set; }
    /// <summary>false finche' il primo giro non fissa lo spartiacque.</summary>
    public bool Initialized { get; set; }
    public long? LastSeenId { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool IsRunning { get; set; }
    public string LastMessage { get; set; } = "";
    public string? LastError { get; set; }
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
