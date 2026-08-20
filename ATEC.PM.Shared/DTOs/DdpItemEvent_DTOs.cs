namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una voce della cronistoria di una riga di distinta: «il giorno X è passata da A a B,
/// per mano di Y». Da questi eventi si ricavano le date «ordinato il», «consegnato il»…
/// senza aggiungere una colonna al database per ogni stato.
/// </summary>
public class DdpItemEventDto
{
    public int Id { get; set; }

    /// <summary>Stato di partenza; null per la prima voce o per gli eventi ricostruiti.</summary>
    public string? FromStatus { get; set; }
    public string? FromLabel { get; set; }

    public string ToStatus { get; set; } = "";
    public string ToLabel { get; set; } = "";
    public string? ToColorBg { get; set; }
    public string? ToColorFg { get; set; }

    public DateTime ChangedAt { get; set; }

    /// <summary>Vuoto per gli eventi di sistema o ricostruiti dal pregresso.</summary>
    public string ChangedBy { get; set; } = "";

    /// <summary>UTENTE, SISTEMA o RICOSTR (dedotto dalle date già presenti prima della cronistoria).</summary>
    public string Origin { get; set; } = "UTENTE";

    public string Note { get; set; } = "";
}
