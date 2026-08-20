namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Il fornitore <b>come lo mostra la pagina Fornitori</b>: contatti e anagrafica fiscale
/// compresi. Sta dietro <c>nav.fornitori</c>.
/// <para>Le combo che scelgono un fornitore usano <see cref="SupplierLookupItem"/>.</para>
/// </summary>
public class SupplierListItem
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public bool IsActive { get; set; }
}

/// <summary>
/// Il fornitore come lo mostra una <b>combo</b>: ragione sociale e referente (che le due
/// combo scrivono come sottotitolo), più il flag attivo che serve a filtrare.
///
/// <para>Aperta a tutti gli autenticati: il fornitore si sceglie dalla riga di una DDP e dalla
/// scheda di un articolo. 🪤 Prima quelle combo caricavano l'elenco completo, e con lui email,
/// telefono, <b>partita IVA e codice fiscale</b> di ogni fornitore — dati che nessuna combo ha
/// mai mostrato, ma che stavano nel JSON di chiunque aprisse una DDP.</para>
/// </summary>
public class SupplierLookupItem
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    /// <summary>Il referente: è a video in entrambe le combo, quindi resta qui.</summary>
    public string ContactName { get; set; } = "";
    public bool IsActive { get; set; }
}

public class SupplierSaveRequest
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
