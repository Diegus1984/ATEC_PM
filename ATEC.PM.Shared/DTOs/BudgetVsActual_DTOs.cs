using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Confronto preventivo vs consuntivo per una commessa,
/// raggruppato per Gruppo → Sezione Costo.
/// </summary>
public class BudgetVsActualData
{
    public int ProjectId { get; set; }
    public int LinkedQuoteId { get; set; }
    public List<BvaGroupDto> Groups { get; set; } = new();
    public decimal TotalBudgetHours { get; set; }
    public decimal TotalBudgetCost { get; set; }
    public decimal TotalAssignedHours { get; set; }
    public decimal TotalAssignedCost { get; set; }
    public decimal TotalActualHours { get; set; }
    public decimal TotalActualCost { get; set; }

    // Materiali
    public List<BvaMaterialSectionDto> MaterialSections { get; set; } = new();
    public decimal TotalMaterialNetCost { get; set; }
    public decimal TotalMaterialSaleCost { get; set; }

    // Scheda prezzi
    public BvaPricingDto? Pricing { get; set; }

    // Riepilogo economico
    public BvaEconomicSummary? Economic { get; set; }

    // Ordine cliente scomposto in posizioni (tabella «Ordine Commessa»)
    public List<ProjectOrderLineDto> OrderLines { get; set; } = new();

    // Riepilogo Costi voce-per-voce, Preventivati | Consuntivati
    public List<BvaCostLineDto> CostLines { get; set; } = new();

    /// <summary>
    /// Dettaglio del calcolo della voce «Lavorazioni Officine» a preventivo: è l'unica delle
    /// 4 voci che non ha una struttura che la alimenti (le altre tre vengono da risorse
    /// pianificate, materiali e trasferta). Arriva sempre, anche vuoto, così la finestra di
    /// calcolo si apre senza una seconda chiamata.
    /// </summary>
    public ProjectCalcSheetDto WorkshopBudgetSheet { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
// ORDINE COMMESSA
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Riga della tabella «Ordine Commessa»: una posizione dell'ordine cliente.
/// <c>Amount</c> è NULL finché non viene digitato (≠ 0): il Totale Ordine è
/// «—» solo se nessuna riga ha un importo.
/// </summary>
public class ProjectOrderLineDto
{
    public int Id { get; set; }
    public string OrderRef { get; set; } = "";
    public string OrderPosition { get; set; } = "";
    public decimal? Amount { get; set; }
    public int SortOrder { get; set; }
    public int RowVersion { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// FOGLI DI CALCOLO A RIGHE («finestra di calcolo» del Riepilogo Costi)
// ═══════════════════════════════════════════════════════════════

/// <summary>Chiavi dei fogli di calcolo (una per voce di costo alimentata a righe).</summary>
public static class CalcKeys
{
    /// <summary>«Lavorazioni Officine», lato Costi Preventivati.</summary>
    public const string WorkshopBudget = "lavorazioni.budget";

    /// <summary>«Spese Trasferta / indennità», lato Costi Consuntivati (alimentata dagli step).</summary>
    public const string TravelActual = "spese.actual";

    /// <summary>
    /// Le 4 calcolatrici di una riga-persona di trasferta. La chiave porta l'id della riga
    /// (owner polimorfo: niente FK, la cancellazione dei fogli la fa il TravelController).
    /// </summary>
    public const string TravelHoursPrefix = "trasferta.ore:";
    public const string TravelMealPrefix = "trasferta.vitto:";
    public const string TravelAllowancePrefix = "trasferta.indennita:";
    public const string TravelCarPrefix = "trasferta.auto:";

    public static readonly string[] TravelRowPrefixes =
        { TravelHoursPrefix, TravelMealPrefix, TravelAllowancePrefix, TravelCarPrefix };

    /// <summary>Chiavi delle 4 calcolatrici di una riga: usate anche per ripulirle alla cancellazione.</summary>
    public static IEnumerable<string> ForTravelRow(int rowId) =>
        TravelRowPrefixes.Select(p => p + rowId);

    /// <summary>
    /// Le 3 calcolatrici di una riga di trasferta della SEZIONE DI PREVENTIVO (segnalazione #33):
    /// stesso schema polimorfo della trasferta, ma l'id in coda è quello di
    /// <c>project_cost_travel_rows</c>. Manca la calcolatrice «ore»: nel preventivo le ore
    /// stanno già nella griglia delle risorse pianificate.
    /// I prefissi sono corti di proposito — <c>project_calc_sheets.calc_key</c> è VARCHAR(40).
    /// </summary>
    public const string BudgetTravelMealPrefix = "preventivo.vitto:";
    public const string BudgetTravelAllowancePrefix = "preventivo.indennita:";
    public const string BudgetTravelCarPrefix = "preventivo.auto:";

    public static readonly string[] BudgetTravelRowPrefixes =
        { BudgetTravelMealPrefix, BudgetTravelAllowancePrefix, BudgetTravelCarPrefix };

    /// <summary>Chiavi delle 3 calcolatrici di una riga di trasferta a preventivo: servono anche
    /// a cancellare i fogli quando si cancella la riga (owner polimorfo: nessuna FK li ripulisce).</summary>
    public static IEnumerable<string> ForBudgetTravelRow(int rowId) =>
        BudgetTravelRowPrefixes.Select(p => p + rowId);

    /// <summary>
    /// Whitelist: una chiave non registrata qui viene rifiutata da GET e PUT del foglio.
    /// Le chiavi polimorfe valgono solo se in coda al prefisso c'è un id di riga positivo.
    /// </summary>
    public static bool IsKnown(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (key == WorkshopBudget || key == TravelActual) return true;
        return TravelRowPrefixes.Concat(BudgetTravelRowPrefixes).Any(p =>
            key.StartsWith(p, StringComparison.Ordinal) &&
            int.TryParse(key.AsSpan(p.Length), out int id) && id > 0);
    }
}

/// <summary>Sezioni del foglio «Lavorazioni Officine»: gli stessi valori di <c>ddp_officina_items.work_type</c>.</summary>
public static class CalcGroups
{
    public const string External = "External";   // «Officine esterne»
    public const string Internal = "Internal";   // «Officine interne»
}

/// <summary>
/// Una riga del foglio di calcolo. Un'unica forma per tutte le modalità:
/// <c>Quantity</c> × <c>UnitCost</c>, con <c>Quantity</c> facoltativa.
/// </summary>
public class ProjectCalcRowDto
{
    public int Id { get; set; }

    /// <summary>Sezione del foglio (<see cref="CalcGroups"/>); "" se il foglio ha una sola sezione.</summary>
    public string GroupKey { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Ore (Officine interne). NULL nelle esterne, dove il costo è già l'importo di riga.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Costo orario se c'è la quantità, altrimenti importo della riga.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>
    /// Terzo fattore facoltativo (calcolatrice Auto: Km × Rimborso × Numero Tratte).
    /// NULL vale 1: le modalità a due fattori non lo usano.
    /// </summary>
    public decimal? Multiplier { get; set; }

    /// <summary>Importo di riga effettivo: calcolato, oppure congelato se <see cref="AmountPinned"/>.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Override manuale (lucchetto): l'importo digitato vince sul calcolo — come <c>contingency_pinned</c>.</summary>
    public bool AmountPinned { get; set; }

    /// <summary>Ricarico di VENDITA (D4): moltiplicatore 1,000–9,999, non una percentuale.</summary>
    public decimal MarkupValue { get; set; } = 1.450m;

    /// <summary>
    /// Provenienza della riga generata automaticamente (come <c>project_cashflow_categories.linked_source</c>):
    /// "" = riga scritta a mano, che nessuna rigenerazione deve toccare.
    /// </summary>
    public string LinkedSource { get; set; } = "";

    public int SortOrder { get; set; }

    /// <summary>
    /// Importo dal calcolo. Senza quantità il <see cref="UnitCost"/> vale come importo manuale
    /// della riga (comportamento del prototipo V32, confermato il 04/08/2026): è quello che
    /// rende una riga «a forfait» esprimibile senza inventarsi delle ore.
    /// </summary>
    public decimal? ComputedAmount =>
        UnitCost == null ? null
        : (Quantity == null ? UnitCost : Quantity * UnitCost) * (Multiplier ?? 1);

    /// <summary>Quello che fa testo: il valore congelato se c'è il lucchetto, altrimenti il calcolo.</summary>
    public decimal? EffectiveAmount => AmountPinned ? Amount : ComputedAmount;

    /// <summary>Vendita = Costo × markup (D4: ricarico di vendita, il costo resta il costo).</summary>
    public decimal? SaleAmount => EffectiveAmount * MarkupValue;

    /// <summary>Riga vuota da scartare alla conferma.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Description)
        && Quantity == null && UnitCost == null && Multiplier == null && Amount == null
        && string.IsNullOrWhiteSpace(LinkedSource);
}

/// <summary>Foglio completo di una voce: righe + totali già calcolati dal server.</summary>
public class ProjectCalcSheetDto
{
    public string CalcKey { get; set; } = "";

    /// <summary>Concorrenza ottimistica dell'INTERO foglio (la conferma riscrive tutte le righe).</summary>
    public int RowVersion { get; set; }
    public List<ProjectCalcRowDto> Rows { get; set; } = new();

    /// <summary>Totale costo: NULL se nessuna riga ha un importo (→ «—», non 0,00 €).</summary>
    public decimal? Total { get; set; }

    /// <summary>Totale vendita (Σ costo × markup): NULL alla stessa condizione del costo.</summary>
    public decimal? SaleTotal { get; set; }
}

/// <summary>Corpo della PUT del foglio: la conferma della finestra di calcolo, in un colpo solo.</summary>
public class ProjectCalcSheetSaveRequest
{
    /// <summary>Concorrenza ottimistica: NULL = scrivi comunque.</summary>
    public int? RowVersion { get; set; }
    public List<ProjectCalcRowDto> Rows { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
// PAGINA /bilancio (cross-commessa)
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Card della pagina Bilancio Commessa: i due KPI di redditività a consuntivo,
/// gli stessi che il conto economico mostra dentro la commessa.
/// </summary>
public class BilancioSummaryDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>Totale Ordine (somma delle righe ordine ≡ projects.revenue).</summary>
    public decimal OrderTotal { get; set; }
    public decimal ActualResourceCost { get; set; }
    public decimal ActualMaterialCost { get; set; }
    public decimal ActualWorkshopCost { get; set; }
    public decimal ActualTravelCost { get; set; }
    public decimal ActualTotalCost { get; set; }

    /// <summary>«Consuntivo Redditività»: Totale Ordine − Totale Costi Consuntivati. NULL se non calcolabile.</summary>
    public decimal? Profitability { get; set; }
    /// <summary>«Consuntivo % Redditività». NULL se il Totale Ordine è nullo o zero.</summary>
    public decimal? ProfitabilityPct { get; set; }
}

/// <summary>Risposta di GET /api/bilancio/summary: soglia + una riga per commessa.</summary>
public class BilancioSummaryResponse
{
    /// <summary>
    /// Sotto questa percentuale (confronto STRETTO) la card diventa rossa, come nel prototipo V32.
    /// Parametrica: <see cref="BilancioSettingsDto"/>, default 20.
    /// </summary>
    public decimal ThresholdPct { get; set; } = 20;
    public List<BilancioSummaryDto> Projects { get; set; } = new();
}

/// <summary>Impostazioni della pagina Bilancio (chiave/valore in res_settings).</summary>
public class BilancioSettingsDto
{
    public decimal ThresholdPct { get; set; } = 20;
}

/// <summary>Corpo di POST/PUT di una riga ordine.</summary>
public class ProjectOrderLineSaveRequest
{
    public string OrderRef { get; set; } = "";
    public string OrderPosition { get; set; } = "";
    public decimal? Amount { get; set; }
    /// <summary>Concorrenza ottimistica (solo PUT): NULL = scrivi comunque.</summary>
    public int? RowVersion { get; set; }
    /// <summary>Solo POST: inserisci subito sotto questa riga. NULL = in coda.</summary>
    public int? AfterLineId { get; set; }
}

/// <summary>
/// Una voce del «Riepilogo Costi», con i due valori affiancati.
/// NULL = voce non valorizzata su quel lato (resa «—», non 0,00 €).
/// </summary>
public class BvaCostLineDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal? Budget { get; set; }
    public decimal? Actual { get; set; }
    /// <summary>Scomposizione mostrata sotto la voce (es. interne/esterne delle officine).</summary>
    public string BudgetHint { get; set; } = "";
    public string ActualHint { get; set; } = "";
}

public class BvaGroupDto
{
    public string GroupName { get; set; } = "";
    public string Color { get; set; } = "#6B7280";
    public int SortOrder { get; set; }
    public List<BvaSectionDto> Sections { get; set; } = new();
    // Totali gruppo
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal AssignedHours { get; set; }
    public decimal AssignedCost { get; set; }
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }
}

public class BvaSectionDto
{
    public int SectionId { get; set; }
    public string SectionName { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE"; // IN_SEDE / DA_CLIENTE
    public int? TemplateId { get; set; }

    // --- Preventivo (da project_cost_resources) ---
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal BudgetSale { get; set; }
    public List<BvaBudgetResourceDto> BudgetResources { get; set; } = new();

    // --- Assegnate (da phase_assignments.planned_hours) ---
    public decimal AssignedHours { get; set; }
    public decimal AssignedCost { get; set; }

    // --- Consuntivo (da timesheet_entries via phase_templates.cost_section_template_id) ---
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }
    public List<BvaActualEmployeeDto> ActualEmployees { get; set; } = new();

    // ── Totali trasferta della sezione (solo DA_CLIENTE), a COSTO SECCO ──
    // Due sorgenti possibili, mai sommate fra loro (regola anti-doppio-conteggio, #33):
    //   • le righe di project_cost_travel_rows, se la sezione ne ha almeno una;
    //   • altrimenti i 7 campi trasferta delle risorse pianificate (modello legacy).
    // Quando la sorgente sono le righe, i due contenitori storici restano popolati perché
    // la riga gialla e la testata di sezione li leggono: «vitto/hotel» = alloggio + vitto,
    // «viaggi» = auto + treno/aereo.

    /// <summary>Spostamenti: viaggi in auto (legacy) oppure auto + treno/aereo (righe).</summary>
    public decimal BudgetTravelCost { get; set; }

    /// <summary>Alloggio e vitto: giorni × (vitto+hotel) (legacy) oppure notti×prezzo + vitto (righe).</summary>
    public decimal BudgetAccommodationCost { get; set; }

    /// <summary>Indennità: MAI ricaricata (decisione D33-A).</summary>
    public decimal BudgetAllowanceCost { get; set; }

    /// <summary>true = la sezione è compilata con le righe nuove, i 7 campi legacy sono ignorati.</summary>
    public bool BudgetTravelFromRows { get; set; }

    /// <summary>Spese soggette al K di ricarico (tutto tranne l'indennità), a costo secco.</summary>
    public decimal BudgetTravelMarkableCost => BudgetTravelCost + BudgetAccommodationCost;

    /// <summary>Alias di <see cref="BudgetAllowanceCost"/> con il nome usato dal piano #33.</summary>
    public decimal BudgetTravelAllowanceCost => BudgetAllowanceCost;

    /// <summary>
    /// Totale trasferta della sezione a COSTO SECCO (senza K): è quello che accende la riga
    /// gialla e il «+ Trasferta» in testata. Il valore ricaricato vive solo nel riepilogo di
    /// commessa, dove il K è noto.
    /// </summary>
    public decimal BudgetTotalTravelCost => BudgetTravelMarkableCost + BudgetAllowanceCost;

    // Delta
    public decimal DeltaHours => ActualHours - BudgetHours;
    public decimal DeltaCost => ActualCost - BudgetCost;
}

/// <summary>Riga preventivo: risorsa pianificata</summary>
public class BvaBudgetResourceDto
{
    public int ResourceId { get; set; }
    public int? EmployeeId { get; set; }
    public string ResourceName { get; set; } = "";
    public decimal WorkDays { get; set; }
    public decimal HoursPerDay { get; set; }
    public decimal TotalHours { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal MarkupValue { get; set; }
    public decimal TotalSale { get; set; }

    // Trasferta (solo DA_CLIENTE)
    public int NumTrips { get; set; }
    public decimal KmPerTrip { get; set; }
    public decimal CostPerKm { get; set; }
    public decimal DailyFood { get; set; }
    public decimal DailyHotel { get; set; }
    public int AllowanceDays { get; set; }
    public decimal DailyAllowance { get; set; }

    public decimal TravelCost => NumTrips * KmPerTrip * CostPerKm;
    public decimal AccommodationCost => WorkDays * (DailyFood + DailyHotel);
    public decimal AllowanceCost => AllowanceDays * DailyAllowance;
    public decimal TotalTravelCost => TravelCost + AccommodationCost + AllowanceCost;
}

/// <summary>Raggruppamento per dipendente con dettaglio entry</summary>
public class BvaActualEmployeeDto
{
    public string EmployeeName { get; set; } = "";
    public decimal TotalHours { get; set; }
    public decimal TotalCost { get; set; }
    public List<BvaActualDetailDto> Details { get; set; } = new();
}

public class BvaActualDetailDto
{
    public DateTime WorkDate { get; set; }
    public string PhaseName { get; set; } = "";
    public string EntryType { get; set; } = "";
    public decimal Hours { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal TotalCost { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// MATERIALI
// ═══════════════════════════════════════════════════════════════

public class BvaMaterialSectionDto
{
    public int SectionId { get; set; }
    public string SectionName { get; set; } = "";
    public decimal MarkupValue { get; set; }
    public decimal CommissionMarkup { get; set; }
    public List<BvaMaterialItemDto> Items { get; set; } = new();
    public decimal TotalNetCost { get; set; }
    public decimal TotalSaleCost { get; set; }
}

public class BvaMaterialItemDto
{
    public int Id { get; set; }
    public int? ParentItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal MarkupValue { get; set; }
    public string ItemType { get; set; } = "MATERIAL";
    public decimal NetCost => Quantity * UnitCost;
    public decimal SaleCost => NetCost * MarkupValue;
}

public class BvaPricingDto
{
    public decimal NetCost { get; set; }           // Risorse + materiali + trasferte
    public decimal ContingencyPct { get; set; }
    public decimal ContingencyAmount { get; set; }
    public decimal OfferPrice { get; set; }        // NetCost + Contingency
    public decimal NegotiationPct { get; set; }
    public decimal NegotiationAmount { get; set; }
    public decimal FinalPrice { get; set; }        // OfferPrice + Negotiation
}

/// <summary>
/// Riepilogo conto economico commessa.
/// Proprietà semplici: i totali li calcola per intero il controller. (Fino al 04/08/2026
/// la classe era INotifyPropertyChanged con setter che si ricalcolavano a vicenda —
/// residuo del client WPF ritirato; con la nuova voce «Lavorazioni Officine» quel
/// ricalcolo avrebbe sommato una voce in meno a seconda dell'ordine di assegnazione.)
/// </summary>
public class BvaEconomicSummary
{
    // Prezzi
    /// <summary>
    /// Prezzo offerta finale: derivato dalla Scheda Prezzi, oppure
    /// <c>project_pricing.final_price_override</c> se il PM l'ha imputato a mano (#35).
    /// </summary>
    public decimal FinalOfferPrice { get; set; }

    /// <summary>
    /// true = <see cref="FinalOfferPrice"/> è un valore imputato a mano, non il calcolato.
    /// Serve al client per dirlo a video: un prezzo che non torna con la Scheda Prezzi e non
    /// dichiara di essere manuale sembra un errore di calcolo.
    /// </summary>
    public bool FinalOfferPriceIsManual { get; set; }

    /// <summary>Totale Ordine: somma delle righe di project_order_lines (≡ projects.revenue).</summary>
    public decimal OrderPrice { get; set; }

    /// <summary>
    /// «Totale Costi di Vendita»: ≡ <see cref="TotalBudgetSaleCost"/>. Dal 06/08/2026 (#34) è
    /// CALCOLATO e non più digitato a mano in <c>projects.sale_total</c>.
    /// NULL = la commessa non ha ancora niente di preventivato.
    /// </summary>
    public decimal? SaleTotal { get; set; }

    /// <summary>
    /// «Margine di Sicurezza» = Totale Ordine − Totale Costi di Vendita (importo).
    /// <para>
    /// Si chiamava «Delta Ordine» fino al 06/08/2026; Paolo l'ha ribattezzato con la #34.
    /// <b>Non</b> è la <see cref="ContingencyAmount"/>, che resta una % di imprevisti calcolata
    /// sul costo di vendita: sono due numeri diversi e la finestra di spiegazione lo dice.
    /// NULL solo se mancano entrambi i termini.
    /// </para>
    /// </summary>
    public decimal? OrderDelta { get; set; }

    /// <summary>
    /// «TOTALE COSTI» (#34): somma della colonna <b>Netti</b> di tutte le sezioni di costo, più
    /// le lavorazioni officine a costo e la trasferta di preventivo. È il costo che l'azienda
    /// prevede di sostenere, prima di qualunque ricarico di vendita.
    /// </summary>
    public decimal TotalBudgetNetCost { get; set; }

    /// <summary>
    /// «TOTALE COSTI DI VENDITA» (#34): somma della colonna <b>Vendita</b> di tutte le sezioni,
    /// più la vendita delle lavorazioni e la trasferta di preventivo. È lo stesso numero che la
    /// Scheda Prezzi mostra con la stessa etichetta (#36) e da cui partono Contingency, Offerta
    /// e Prezzo finale — un valore solo, un nome solo.
    /// <para>
    /// La trasferta entra qui con lo stesso importo che ha in <see cref="TotalBudgetNetCost"/>:
    /// dopo la rimozione del K (06/08/2026) non ha più un lato vendita distinto dal costo.
    /// </para>
    /// </summary>
    public decimal TotalBudgetSaleCost { get; set; }

    // Totali preventivo
    public decimal TotalNetCost { get; set; }
    public decimal ContingencyAmount { get; set; }
    public decimal BudgetCost { get; set; }

    // Budget dettaglio (le 4 voci del Riepilogo Costi, lato Preventivati)
    public decimal BudgetResourceHours { get; set; }
    public decimal BudgetResourceCost { get; set; }
    public decimal BudgetMaterialCost { get; set; }

    /// <summary>«Lavorazioni Officine» a preventivo: somma delle righe della finestra di calcolo.</summary>
    public decimal BudgetWorkshopCost { get; set; }
    public decimal BudgetWorkshopInternalCost { get; set; }
    public decimal BudgetWorkshopExternalCost { get; set; }

    /// <summary>Vendita delle lavorazioni (costo × markup): entra nel costo netto della Scheda Prezzi.</summary>
    public decimal BudgetWorkshopSale { get; set; }

    /// <summary>
    /// «Spese Trasferta / indennità» a preventivo, <b>a costo secco</b>:
    /// <see cref="BudgetTravelMarkableCost"/> + <see cref="BudgetAllowanceCost"/>.
    /// <para>
    /// Il K di ricarico introdotto dalla #33 è stato rimosso il 06/08/2026 su richiesta di Paolo
    /// («restano solo importi di costo Netto senza ricarichi»). Da allora questa voce è omogenea
    /// alle altre tre del Riepilogo e ai due lati preventivo/consuntivo.
    /// </para>
    /// </summary>
    public decimal BudgetTravelCost { get; set; }

    /// <summary>Spese di trasferta vere e proprie (alloggio, vitto, auto, treno/aereo).</summary>
    public decimal BudgetTravelMarkableCost { get; set; }

    /// <summary>Indennità di trasferta a preventivo.</summary>
    public decimal BudgetAllowanceCost { get; set; }

    // Consuntivo dettaglio (le stesse 4 voci, lato Consuntivati)
    public decimal ActualResourceHours { get; set; }
    public decimal ActualResourceCost { get; set; }
    /// <summary>Solo materiali commerciali (bom_items): dal 04/08/2026 le officine sono voce a sé.</summary>
    public decimal ActualMaterialCost { get; set; }
    public decimal ActualWorkshopCost { get; set; }
    public decimal ActualWorkshopInternalCost { get; set; }
    public decimal ActualWorkshopExternalCost { get; set; }
    /// <summary>Righe officina di cui non si sa se interne o esterne (né lavorazione né stato lo dicono).</summary>
    public decimal ActualWorkshopUnclassifiedCost { get; set; }
    public decimal ActualTravelCost { get; set; }

    /// <summary>
    /// true quando la trasferta è compilata a righe (blocco 6) e quindi il costo lo decide
    /// il modulo, non il campo digitato a mano: la UI lo dice invece di offrire una modifica
    /// che verrebbe ignorata.
    /// </summary>
    public bool ActualTravelFromPlan { get; set; }
    public decimal ActualTotalCost { get; set; }

    // Redditività — Totale Ordine meno i costi, sui DUE lati (in € e in %)
    public decimal BudgetProfitability { get; set; }
    public decimal BudgetProfitabilityPct { get; set; }
    public decimal Profitability { get; set; }
    public decimal ProfitabilityPct { get; set; }

    // Avanzamento
    public int ActiveTechnicians { get; set; }
    public int TotalPhases { get; set; }
    public int CompletedPhases { get; set; }
    public int ProgressPct { get; set; }
}
