namespace ATEC.PM.Shared.DTOs;

public class CodexListItem
{
    public int Id { get; set; }

    private string _codice = "";
    public string Codice
    {
        get => FormatCodice(_codice);
        set => _codice = value ?? "";
    }

    /// <summary>
    /// Formato canonico di visualizzazione del codice Codex (il DB lo salva senza punti):
    /// rimuove TUTTI i punti, poi ne mette UN solo prima delle ultime 3 cifre.
    /// </summary>
    public static string FormatCodice(string? codice)
    {
        var raw = (codice ?? "").Replace(".", "");
        if (raw.Length > 3)
            return string.Concat(raw.AsSpan(0, raw.Length - 3), ".", raw.AsSpan(raw.Length - 3));
        return raw;
    }
    // Nuova codifica (ampliamento Codex 21/07/2026): compilata a mano, sostituirà il codice
    // vecchio a smantellamento completato. Vuota = riga non ancora ricodificata.
    private string _codiceNuovo = "";
    public string CodiceNuovo
    {
        get => _codiceNuovo.Length > 0 ? FormatCodice(_codiceNuovo) : "";
        set => _codiceNuovo = value ?? "";
    }

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

    // ── Derivazione 101 → 201 (#135) ──────────────────────────────
    // Il grezzo commerciale da cui si ricava questo particolare a disegno. Vuoto = nessuna
    // derivazione (o articolo che non è un 1xx). Arriva già nella lista per non fare una
    // query per riga quando la griglia mostra la colonna.

    /// <summary>Id della riga di <c>codex_item_references</c>: serve alla DELETE.</summary>
    public int? RefCommercialeId { get; set; }

    /// <summary>Id Codex del 201 di derivazione: da qui si arriva ai suoi articoli Danea
    /// (<c>/catalog-mapping/by-codex</c>) senza un giro in più (#142).</summary>
    public int? RefCommercialeCodexId { get; set; }

    private string _refCommercialeCodice = "";
    /// <summary>Codice del 201 di derivazione, col punto come tutti gli altri codici Codex.</summary>
    public string RefCommercialeCodice
    {
        get => _refCommercialeCodice.Length > 0 ? FormatCodice(_refCommercialeCodice) : "";
        set => _refCommercialeCodice = value ?? "";
    }

    public string RefCommercialeDescr { get; set; } = "";
}

public class CodexSyncStatus
{
    public bool IsSyncing { get; set; }
    public DateTime? LastSync { get; set; }
    public int TotalRows { get; set; }
    public string? LastError { get; set; }
}

public class CodexPrefix
{
    public string Codice { get; set; } = "";
    public string Descrizione { get; set; } = "";
    public string Display => $"{Codice} � {Descrizione}";
}

public class CodexReserveRequest
{
    public string Prefisso { get; set; } = "";
}

public class CodexReservationResult
{
    public string Codice { get; set; } = "";
    public int ReservationId { get; set; }
}

public class CodexConfirmRequest
{
    public int ReservationId { get; set; }
    public string Descrizione { get; set; } = "";
}

public class CodexGeneratedCode
{
    public string Codice { get; set; } = "";
    public int Id { get; set; }
}

public class CodexUpdateRequest
{
    public string Descrizione { get; set; } = "";
}

// ── COMPOSIZIONE ────────────────────────────────────────

public class CompositionChildDto
{
    public int Id { get; set; }
    public int ParentCodexId { get; set; }
    public int? ChildCodexId { get; set; }
    public int? ChildCatalogId { get; set; }
    public string ChildCodice { get; set; } = "";
    public string ChildDescr { get; set; } = "";
    public int SortOrder { get; set; }
    public int Quantity { get; set; } = 1;
    public string Source { get; set; } = "codex"; // "codex" o "catalog"
}

public class CompositionTreeNode
{
    public int CompositionId { get; set; }
    public int CodexId { get; set; }
    public int? CatalogId { get; set; }
    public string Codice { get; set; } = "";
    public string Descr { get; set; } = "";
    public string Source { get; set; } = "codex"; // "codex" o "catalog"
    public int Quantity { get; set; } = 1;
    public List<CompositionTreeNode> Children { get; set; } = new();
}

public class AddCompositionRequest
{
    public int ParentCodexId { get; set; }
    public int? ChildCodexId { get; set; }
    public int? ChildCatalogId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class UpdateCompositionRequest
{
    public int Quantity { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Notifica real-time (SignalR `CompositionChanged`, hub /hubs/codex) di modifica composizione.</summary>
public class CompositionChange
{
    public int ParentCodexId { get; set; }
    public string Action { get; set; } = ""; // create | delete
    public int CompositionId { get; set; }
}

// ── RIFERIMENTI 101 → 201/401 ──────────────────────────────

public class CodexItemReference
{
    public int Id { get; set; }
    public int SourceCodexId { get; set; }
    public int RefCodexId { get; set; }
    public string RefType { get; set; } = "";
    public string RefCodice { get; set; } = "";
    public string RefDescr { get; set; } = "";
}

public class AddCodexReferenceRequest
{
    public int SourceCodexId { get; set; }
    public int RefCodexId { get; set; }
    public string RefType { get; set; } = "";
}

// ── IMPORTAZIONE COMPOSIZIONE ──────────────────────────────

public class CodexImportItem
{
    public string Code { get; set; } = "";
    public int Quantity { get; set; }
}

public class CodexImportPreviewResult
{
    public string Code { get; set; } = "";
    public int Quantity { get; set; }
    public string? Descr { get; set; }
    public int? Id { get; set; }
    public string? Source { get; set; } // "codex" | "catalog"
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}

public class CodexImportCommitRequest
{
    public int ParentId { get; set; }
    public List<CodexImportItem> Items { get; set; } = new();
    public bool ReplaceExisting { get; set; }
}
// ── NUOVA CODIFICA (ricodifica manuale, ampliamento Codex 21/07/2026) ──────

public class CodexNewCodeSaveRequest
{
    // Codice nuovo da assegnare alla riga (con o senza punto di display); vuoto = rimuovi.
    public string NewCode { get; set; } = "";
    // Prenotazione ottenuta da /api/codex/new-code/reserve: liberata al salvataggio.
    public int? ReservationId { get; set; }
}

public class CodexNewCodeReserveRequest
{
    // Famiglia della nuova codifica (3 cifre: 201 generici, 211 elettrici, 221 pneumatici, …).
    public string Family { get; set; } = "";
}

public class CodexRecodeStats
{
    public int Total { get; set; }
    public int Done { get; set; }
}

public class CodexBulkAssignRequest
{
    // Righe Codex selezionate dall'operatore (le già ricodificate vengono saltate).
    public List<int> Ids { get; set; } = new();
    public string Family { get; set; } = "";
}

// Anteprima dell'assegnazione massiva: vecchio codice → nuovo codice PRENOTATO.
// L'operatore la conferma nel form (bulk-commit) o la annulla (bulk-release).
public class CodexBulkReserveItem
{
    public int Id { get; set; }
    public string Codice { get; set; } = "";
    public string Descr { get; set; } = "";
    public string NewCode { get; set; } = "";
    public int ReservationId { get; set; }
}

public class CodexBulkReserveResult
{
    public List<CodexBulkReserveItem> Items { get; set; } = new();
    public int Skipped { get; set; }
}

public class CodexBulkCommitRequest
{
    public List<CodexBulkReserveItem> Items { get; set; } = new();
}

public class CodexBulkReleaseRequest
{
    public List<int> ReservationIds { get; set; } = new();
}

public class CodexBulkAssignResult
{
    public int Assigned { get; set; }
    public int Skipped { get; set; }
}

public class CodexBulkRemoveRequest
{
    public List<int> Ids { get; set; } = new();
}
