namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una riga del picker delle DDP, vista Codex: un articolo Codex con (eventualmente)
/// UNO dei suoi articoli Danea abbinati — un abbinamento = una riga (#128): così il
/// codice fornitore/produttore si CERCA e si SCEGLIE direttamente, e con più fornitori
/// sullo stesso codice ATEC si vede una riga per fornitore (multi-fornitore).
/// </summary>
public class CodexPickerRow
{
    public int CodexId { get; set; }
    /// <summary>Codice ATEC: il codice nuovo se ricodificato, altrimenti il codice.</summary>
    public string CodiceAtec { get; set; } = "";
    public string Descr { get; set; } = "";

    // Campi Codex di ripiego, per le righe senza articolo Danea abbinato.
    public string UmCodex { get; set; } = "";
    public string FornitoreCodex { get; set; } = "";
    [DatoSensibile]
    public decimal? PrezzoCodex { get; set; }

    // Articolo Danea abbinato (CatalogItemId null = codice senza abbinamento).
    public int? CatalogItemId { get; set; }
    public string CodiceArticolo { get; set; } = "";
    public string UnitArticolo { get; set; } = "";
    [DatoSensibile]
    public decimal? CostoArticolo { get; set; }
    public int? SupplierId { get; set; }
    public string FornitoreNome { get; set; } = "";
    public string Produttore { get; set; } = "";
}
