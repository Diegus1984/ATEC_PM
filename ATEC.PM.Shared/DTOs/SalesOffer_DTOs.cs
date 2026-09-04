using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// ═══════════════════════════════════════════════════════════════════════════════
// REGISTRO OFFERTE — DTO
//
// Il registro è la serie commerciale PARALLELA ai Preventivi: numera tutte le offerte
// emesse da ATEC, comprese quelle nate fuori dal gestionale (a mano, per mail, su Excel),
// e si aggancia a un preventivo (`QuoteId`) o a una commessa (`ProjectId`) quando esistono.
// Vedi docs/piani/PIANO-ANDAMENTO-COMMERCIALE.md.
//
// Nessun [Required] sui DTO in ingresso, per la regola del blocco B: la validazione è
// esplicita in SalesOfferRules e risponde con un messaggio in italiano, non con un 400
// generato dal model binder.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Una riga del registro, così come la legge l'elenco e la scheda.</summary>
public class SalesOfferDto
{
    public int Id { get; set; }

    public int Year { get; set; }
    /// <summary>Progressivo dell'anno. NULL solo sulle righe storiche migrate senza numero.</summary>
    public int? Number { get; set; }
    /// <summary>Numero composto: <c>S097-2026-EC</c>. Calcolato, mai persistito.</summary>
    public string Composed { get; set; } = "";

    public string TypeCode { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string TypeCategory { get; set; } = "";

    public string SellerCode { get; set; } = "";
    public string SellerName { get; set; } = "";
    public int? SellerEmployeeId { get; set; }

    public DateTime? OfferDate { get; set; }

    public int? CustomerId { get; set; }
    /// <summary>Nome cliente come scritto sull'offerta: non si riscrive mai collegando la rubrica.</summary>
    public string CustomerName { get; set; } = "";
    /// <summary>Ragione sociale del cliente collegato (vuota se il nome non è agganciato).</summary>
    public string CustomerCompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";

    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Tag { get; set; } = "";

    public decimal? Amount { get; set; }
    /// <summary>Estremo alto della forbice di prezzo («fino a»). Le statistiche usano <see cref="Amount"/>.</summary>
    public decimal? AmountMax { get; set; }

    public string Status { get; set; } = "";
    /// <summary>0–100 a passi di 10. NULL = non valutata.</summary>
    public int? Chance { get; set; }

    public decimal? OrderAmount { get; set; }
    public bool IsTimeMaterial { get; set; }

    public decimal? AdvancePct { get; set; }
    public decimal? AdvanceAmount { get; set; }
    public string AdvanceNotes { get; set; } = "";

    public int? PostponedYear { get; set; }

    public string OfferPath { get; set; } = "";
    public string CalcPath { get; set; } = "";

    public DateTime? NextContact { get; set; }

    public string LegacyNumber { get; set; } = "";
    public bool IsLegacySeries { get; set; }

    public int? QuoteId { get; set; }
    public string QuoteNumber { get; set; } = "";
    public int? ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";

    public int RowVersion { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedByName { get; set; } = "";
    public string UpdatedByName { get; set; } = "";

    public List<SalesOfferFollowupDto> Followups { get; set; } = new();
    public List<SalesOfferLogDto> Log { get; set; } = new();
}

/// <summary>Corpo di POST e PUT. <c>Number</c> nullo in creazione = «assegna tu il prossimo».</summary>
public class SalesOfferSaveRequest
{
    public int Year { get; set; }
    public int? Number { get; set; }
    public string TypeCode { get; set; } = "";
    public string SellerCode { get; set; } = "";
    public DateTime? OfferDate { get; set; }

    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string ContactName { get; set; } = "";

    public string Description { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Tag { get; set; } = "";

    public decimal? Amount { get; set; }
    public decimal? AmountMax { get; set; }

    public string Status { get; set; } = "";
    public int? Chance { get; set; }

    public decimal? OrderAmount { get; set; }
    public bool IsTimeMaterial { get; set; }

    public decimal? AdvancePct { get; set; }
    public decimal? AdvanceAmount { get; set; }
    public string AdvanceNotes { get; set; } = "";

    public int? PostponedYear { get; set; }

    public string OfferPath { get; set; } = "";
    public string CalcPath { get; set; } = "";

    public DateTime? NextContact { get; set; }

    public int? QuoteId { get; set; }
    public int? ProjectId { get; set; }

    /// <summary>Concorrenza ottimistica (solo PUT): NULL = scrivi comunque.</summary>
    public int? RowVersion { get; set; }
}

/// <summary>Esito della creazione: l'id nuovo e il numero effettivamente assegnato.</summary>
public class SalesOfferCreatedDto
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Number { get; set; }
    public string Composed { get; set; } = "";
    /// <summary>
    /// True se il numero proposto era stato preso nel frattempo e ne è stato assegnato un altro:
    /// il client lo dice all'utente invece di far finta di niente.
    /// </summary>
    public bool Renumbered { get; set; }
}

/// <summary>Un appunto di contatto con il referente.</summary>
public class SalesOfferFollowupDto
{
    public int Id { get; set; }
    public int OfferId { get; set; }
    public DateTime? ContactDate { get; set; }
    public string Notes { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
}

/// <summary>Corpo di POST followup: l'appunto e, se indicata, la data del prossimo contatto.</summary>
public class SalesOfferFollowupSaveRequest
{
    public DateTime? ContactDate { get; set; }
    public string Notes { get; set; } = "";
    /// <summary>Se valorizzata aggiorna anche <c>next_contact</c> sull'offerta.</summary>
    public DateTime? NextContact { get; set; }
}

/// <summary>Una riga del registro modifiche, campo per campo.</summary>
public class SalesOfferLogDto
{
    public int Id { get; set; }
    public string Field { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public string ChangedByName { get; set; } = "";
    public DateTime? ChangedAt { get; set; }
}

/// <summary>Pagina di elenco: righe + totale per la paginazione.</summary>
public class SalesOfferListResponse
{
    public List<SalesOfferDto> Items { get; set; } = new();
    public int Total { get; set; }
}

/// <summary>Tipo di impianto: entra nel numero composto e dà la categoria all'analisi di mercato.</summary>
public class SalesOfferTypeDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Impianto | Intervento | Ricambio | Altro.</summary>
    public string Category { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Venditore del registro: la sigla è il dato storico e non si perde mai, il legame con
/// <c>employees</c> è facoltativo perché fra i venditori ci sono persone non più in azienda.
/// </summary>
public class SalesOfferSellerDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int? EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Numero proposto per un anno: quello che la Dashboard mostra come «prossimo».</summary>
public class SalesOfferNextNumberDto
{
    public int Year { get; set; }
    public int Number { get; set; }
    public string Composed { get; set; } = "";
}

/// <summary>Corpo della chiusura in blocco delle aperte di anni passati.</summary>
public class SalesOfferBulkCloseRequest
{
    /// <summary>Si chiudono le offerte con <c>year &lt;= UntilYear</c>.</summary>
    public int UntilYear { get; set; }
    /// <summary>Stato di destinazione: <c>persa</c> (default) o <c>sospesa</c>.</summary>
    public string Status { get; set; } = "persa";
}
