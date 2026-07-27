using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// DTO per rappresentare un singolo step/riga di pagamento SAL.
public class SalRowDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Step { get; set; } = "";
    public decimal? Perc { get; set; }
    public string Condizione { get; set; } = "";
    public DateTime? DataFatt { get; set; }
    public string Stato { get; set; } = "";      // '' | 'daEmettere' | 'emessa'
    public int SortOrder { get; set; }
    public int RowVersion { get; set; }
    public int? PaidBy { get; set; }
    public DateTime? PaidAt { get; set; }
    public int? IvaPerc { get; set; }             // %IVA intera (default UI 22, mai lato server)
    public int? GgSaldo { get; set; }             // GG saldo fattura (data prevista saldo = data_fatt + gg_saldo)
    public string NFatt { get; set; } = "";       // N° fattura (solo cifre, stringa per zeri iniziali)
    public string ContoSap { get; set; } = "";    // Causale Conto SAP (da anagrafica)
    public string Pagamento { get; set; } = "";   // Stato pagamento (da anagrafica, semantica cablata su 'Pagata')
    public DateTime? DataPagamento { get; set; }  // Data incasso
    public string Note { get; set; } = "";
}

// DTO per rappresentare la testata SAL di una commessa (cliente e valore).
public class SalHeaderDto
{
    public int ProjectId { get; set; }
    public string Cliente { get; set; } = "";
    public decimal? Valore { get; set; }
    public int RowVersion { get; set; }
    public string Po { get; set; } = "";          // PO - Ordine cliente
    public string RifOfferta { get; set; } = "";  // Riferimento Offerta ATEC
    /// <summary>Ragione sociale cliente dalla commessa (sola lettura, da anagrafica).</summary>
    public string CustomerName { get; set; } = "";
}

// Bundle completo che raccoglie header e righe SAL di una singola commessa.
public class SalBundleDto
{
    public SalHeaderDto Header { get; set; } = new();
    public List<SalRowDto> Rows { get; set; } = new();
}

// Richiesta di salvataggio dell'header SAL.
public class SalHeaderSaveRequest
{
    public string Cliente { get; set; } = "";
    public decimal? Valore { get; set; }
    public int? RowVersion { get; set; }
    // null = campo non inviato dal client → preservare il valore corrente; stringa vuota = svuota
    public string? Po { get; set; } = null;          // PO - Ordine cliente
    public string? RifOfferta { get; set; } = null;  // Riferimento Offerta ATEC
}

// Richiesta di creazione/modifica di uno step/riga SAL.
public class SalRowSaveRequest
{
    public string Step { get; set; } = "";
    public decimal? Perc { get; set; }
    public string Condizione { get; set; } = "";
    public DateTime? DataFatt { get; set; }
    public string Stato { get; set; } = "";       // '' | 'daEmettere' | 'emessa' ('pagata' legacy mappata server-side)
    public int? RowVersion { get; set; }
    public int? IvaPerc { get; set; }             // %IVA intera
    public int? GgSaldo { get; set; }             // GG saldo fattura
    // Campi testo v10: null = campo non inviato dal client → preservare il valore corrente; stringa vuota = svuota
    public string? NFatt { get; set; } = null;       // N° fattura (sanificata server-side: solo cifre)
    public string? ContoSap { get; set; } = null;    // Causale Conto SAP
    public string? Pagamento { get; set; } = null;   // Stato pagamento
    public DateTime? DataPagamento { get; set; }  // Data incasso
    public string? Note { get; set; } = null;
}

// Richiesta di riordino per gli step SAL.
public class SalReorderRequest
{
    public List<int> Ids { get; set; } = new();
}

// DTO generico per le anagrafiche SAL (condizioni di pagamento, causali Conto SAP, stati pagamento).
public class SalConditionDto
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    // Colori riga configurabili (#RRGGBB o #RRGGBBAA): usati SOLO da sal_payment_states;
    // per condizioni e causali Conto SAP restano sempre null. null = stato neutro senza tinta.
    // Pura estetica: la semantica (lock, incasso, Cash Flow) resta cablata sulle etichette di sistema.
    public string? ColorBg { get; set; }
    public string? ColorFg { get; set; }
}

// Richiesta di salvataggio per le anagrafiche SAL (condizioni, causali SAP, stati pagamento).
public class SalConditionSaveRequest
{
    public string Label { get; set; } = "";
    // Colori opzionali (#RRGGBB o #RRGGBBAA): considerati SOLO dagli endpoint payment-states
    // (le altre due anagrafiche li ignorano). null = nessun colore (stato neutro).
    public string? ColorBg { get; set; } = null;
    public string? ColorFg { get; set; } = null;
}

// DTO per il prospetto globale delle scadenze SAL aperte (Fase 3).
public class SalProspettoRowDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Step { get; set; } = "";
    public decimal? Perc { get; set; }
    public string Condizione { get; set; } = "";
    public DateTime? DataFatt { get; set; }
    public decimal? Importo { get; set; }
    public int Ord { get; set; }
    public string Alert { get; set; } = "";       // '' (in programma) | 'warn' | 'pre' | 'incasso' | 'attesa'
    public int? GgSaldo { get; set; }             // GG saldo fattura
    public DateTime? DataSaldo { get; set; }      // Data prevista saldo (derivata: data_fatt + gg_saldo)
    public string Stato { get; set; } = "";       // Stato fatturazione
    public string Pagamento { get; set; } = "";   // Stato pagamento
}

// Stato del controllo periodico del prospetto SAL (ultima conferma registrata).
public class SalProspettoCheckDto
{
    public DateTime? CheckedAt { get; set; }      // Data/ora ultimo controllo (NULL se mai fatto)
    public string CheckedByName { get; set; } = ""; // Nome di chi ha confermato l'ultimo controllo
    public int? Days { get; set; }                // Giorni trascorsi dall'ultimo controllo
    public bool Due { get; set; }                 // true = controllo dovuto (mai fatto o periodo scaduto)
    public DateTime? NextDue { get; set; }        // Prossima scadenza del controllo
}

// Riga economica SAL globale (base per Cash Flow / Analisi / drill-down, calcoli in SQL).
public class SalEconomicsRowDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Cliente { get; set; } = "";
    public decimal? Valore { get; set; }          // Importo ordine commessa
    public string Step { get; set; } = "";
    public decimal? Perc { get; set; }
    public decimal? Importo { get; set; }         // valore * perc / 100
    public int? IvaPerc { get; set; }
    public decimal? Iva { get; set; }             // Importo * iva_perc / 100 (0 se iva_perc NULL)
    public decimal? TotIva { get; set; }          // Importo + Iva
    public string Condizione { get; set; } = "";
    public DateTime? DataFatt { get; set; }
    public int? GgSaldo { get; set; }
    public DateTime? DataSaldo { get; set; }      // Data prevista saldo (derivata: data_fatt + gg_saldo)
    public string Stato { get; set; } = "";
    public string Pagamento { get; set; } = "";
}

// Testata economica per il Cash Flow: una per ogni project_sal di commessa attiva
// (anche senza righe SAL — serve al totale Ordini delle commesse Attive).
public class SalEconomicsHeaderDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Cliente { get; set; } = "";
    public decimal? Valore { get; set; }          // Importo ordine commessa
}

// Bundle economico globale SAL: testate (tutte le commesse attive) + righe (dettaglio step).
public class SalEconomicsDto
{
    public List<SalEconomicsHeaderDto> Headers { get; set; } = new();
    public List<SalEconomicsRowDto> Rows { get; set; } = new();
}

// Riepilogo per-commessa dei SAL, per la sidebar PM globale (mirror di MilestoneSummaryDto).
public class SalSummaryDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Total { get; set; }   // righe SAL totali (conteggio del contenitore in sidebar)
    public int Open { get; set; }     // righe con data_fatt e stato non 'emessa' (ipotesi aperte)
    public int Warn { get; set; }     // aperte e scadute (data_fatt <= oggi)
    public int Pre { get; set; }      // aperte e imminenti (da lunedì sett. precedente)
    public int Incasso { get; set; }  // fatture con incasso scaduto (non pagate, data prevista saldo < oggi)
    public decimal PercTotal { get; set; }  // Σ % SAL di tutte le righe (denominatore avanzamento incasso)
    public decimal PercPaid { get; set; }   // Σ % delle righe con Pagamento = «Pagata»
}

