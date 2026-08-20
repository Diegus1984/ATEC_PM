using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una «riga spenta» della Sintesi DDP: riga esclusa dalle stampe di Avanzamento oppure già
/// gestita in Dati Mancanti. È un dato di commessa CONDIVISO (decisione 06/08/2026): quello
/// che spegne un utente lo vedono spento anche gli altri. Chiave: commessa + tipo DDP +
/// sezione + id riga; qui viaggiano solo gli ultimi due, il resto sta nella rotta.
/// </summary>
public class DdpRowOffItem
{
    /// <summary>ver|ro|do|dc|tab|par|rit|mit|del|ass|mancanti.</summary>
    public string SectionKey { get; set; } = "";

    /// <summary>Id della riga di distinta (bom_items o ddp_officina_items, secondo il tipo DDP).</summary>
    public int ItemId { get; set; }
}

/// <summary>Corpo della PUT su una singola riga: true = spegni, false = riaccendi.</summary>
public class DdpRowOffSetRequest
{
    public bool Off { get; set; }
}

/// <summary>Corpo del «Tutte spente / Tutte accese» su una sezione.</summary>
public class DdpRowOffBulkRequest
{
    public List<int> ItemIds { get; set; } = new();
    public bool Off { get; set; }
}

/// <summary>Corpo del reset: sezione singola, oppure null per riaccendere l'intera DDP.</summary>
public class DdpRowOffResetRequest
{
    public string? SectionKey { get; set; }
}
