using System;
using System.Collections.Generic;
using System.Linq;

namespace ATEC.PM.Shared.DTOs;

// ═══════════════════════════════════════════════════════════════
// TRASFERTA A RIGHE DELLA SEZIONE DI PREVENTIVO (segnalazione #33)
//
// Sostituisce i 7 campi digitati a mano su project_cost_resources (num_trips, km_per_trip,
// cost_per_km, daily_food, daily_hotel, allowance_days, daily_allowance) con una riga per
// persona, sulla stessa forma della Gestione Trasferta (travel_step_rows) — deliberatamente,
// così formule e componenti si trasportano senza conversioni.
//
// UNICA differenza di modello rispetto alla Trasferta: qui il costo di riga è spaccato in due,
// perché il K di ricarico (project_pricing.travel_markup) va SOLO sulle spese e MAI
// sull'indennità (decisione D33-A, 06/08/2026 — richiesta di Paolo Zanoni nella #33).
// In TravelRowDto.TravelCost, invece, tutto è fuso in un numero solo.
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Le tre calcolatrici di una riga di trasferta a preventivo (la Trasferta ne ha quattro:
/// qui manca «ore», perché ore/€ ora/K stanno già nella griglia «Risorse pianificate»).
///
/// Le chiavi dei fogli sono la stessa cosa di <c>CalcKeys.Travel*Prefix</c>, con prefisso
/// <c>preventivo.</c>: owner polimorfo, nessuna FK, quindi la cancellazione dei fogli la deve
/// fare a mano il controller (come <c>TravelController.DeleteRow</c>).
/// ATTENZIONE: <c>project_calc_sheets.calc_key</c> è VARCHAR(40) — i prefissi non si allungano.
/// </summary>
public static class ProjectCostTravelCalcKinds
{
    public const string Meal = "vitto";
    public const string Allowance = "indennita";
    public const string Car = "auto";

    public const string MealPrefix = "preventivo.vitto:";
    public const string AllowancePrefix = "preventivo.indennita:";
    public const string CarPrefix = "preventivo.auto:";

    /// <summary>Tutti i prefissi: servono anche a <c>CalcKeys.IsKnown</c> e alla pulizia dei fogli.</summary>
    public static readonly string[] Prefixes = { MealPrefix, AllowancePrefix, CarPrefix };

    public static bool IsKnown(string? kind) =>
        kind is Meal or Allowance or Car;

    /// <summary>Chiave del foglio di calcolo (blocco 5) che tiene il dettaglio.</summary>
    public static string SheetKey(string kind, int rowId) => kind switch
    {
        Meal => MealPrefix + rowId,
        Allowance => AllowancePrefix + rowId,
        Car => CarPrefix + rowId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Calcolatrice sconosciuta"),
    };

    /// <summary>Colonna della riga in cui la Conferma scrive il totale del foglio.</summary>
    public static string Column(string kind) => kind switch
    {
        Meal => "meal_cost",
        Allowance => "allowance_cost",
        Car => "car_cost",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Calcolatrice sconosciuta"),
    };

    /// <summary>Le chiavi delle 3 calcolatrici di una riga: usate per ripulirle alla cancellazione.</summary>
    public static IEnumerable<string> ForRow(int rowId) =>
        Prefixes.Select(p => p + rowId);

    /// <summary>
    /// Whitelist per <c>CalcKeys.IsKnown</c>: chiave nota solo se al prefisso segue un id valido.
    /// Sta qui, e non in CalcKeys, per avere una sola sorgente dei prefissi.
    /// </summary>
    public static bool IsKnownKey(string? key) =>
        !string.IsNullOrEmpty(key) && Prefixes.Any(p =>
            key.StartsWith(p, StringComparison.Ordinal) &&
            int.TryParse(key.AsSpan(p.Length), out int id) && id > 0);
}

/// <summary>
/// Una riga della tabella trasferta di una sezione di costo con Tag Cliente.
/// I totali sono proprietà calcolate, non colonne: la stessa formula serve alla griglia
/// mentre si digita e al Bilancio, e due copie divergerebbero.
/// </summary>
public class ProjectCostTravelRowDto
{
    public int Id { get; set; }
    public int SectionId { get; set; }

    /// <summary>
    /// Risorsa pianificata della sezione a cui la riga è agganciata; NULL se il nominativo è
    /// stato scritto a mano. FK con ON DELETE SET NULL: cancellando la risorsa la riga resta,
    /// col nome ancora leggibile.
    /// </summary>
    public int? ResourceId { get; set; }

    /// <summary>Nome scritto sulla riga: resta leggibile anche se la risorsa sparisce.</summary>
    public string PersonName { get; set; } = "";

    public int? Nights { get; set; }
    public decimal? NightPrice { get; set; }

    /// <summary>Totale della calcolatrice «Vitto» (Giorni × Diaria).</summary>
    public decimal? MealCost { get; set; }

    /// <summary>Totale della calcolatrice «Indennità» (Giorni × Indennità). MAI ricaricata (D33-A).</summary>
    public decimal? AllowanceCost { get; set; }

    /// <summary>Totale della calcolatrice «Auto» (Km × Rimborso × Numero Tratte).</summary>
    public decimal? CarCost { get; set; }

    /// <summary>Treno/aereo: importo digitato, senza calcolatrice.</summary>
    public decimal? TransportCost { get; set; }

    public int SortOrder { get; set; }
    public int RowVersion { get; set; }

    /// <summary>Colonna «Costo» del gruppo Alloggio/Vitto: notti × prezzo, sola lettura.</summary>
    public decimal? LodgingCost => Nights == null || NightPrice == null ? null : Nights * NightPrice;

    /// <summary>Spese soggette al K: alloggio + vitto + auto + treno/aereo. L'indennità NON c'è.</summary>
    public decimal MarkableCost =>
        (LodgingCost ?? 0) + (MealCost ?? 0) + (CarCost ?? 0) + (TransportCost ?? 0);

    /// <summary>Costo secco della riga (senza nessun ricarico): spese ricaricabili + indennità.</summary>
    public decimal TravelCost => MarkableCost + (AllowanceCost ?? 0);
}

/// <summary>
/// Totali della riga «Totali» in fondo alla tabella: le 5 colonne valorizzate più i due
/// aggregati che servono al Bilancio (ricaricabile e indennità restano separati fino alla fine).
/// Notti e Prezzo non si sommano: sommare i prezzi/notte non vuol dire niente — è già la
/// scelta della Gestione Trasferta.
/// </summary>
public class ProjectCostTravelTotalsDto
{
    public decimal LodgingCost { get; set; }
    public decimal MealCost { get; set; }
    public decimal AllowanceCost { get; set; }
    public decimal CarCost { get; set; }
    public decimal TransportCost { get; set; }

    /// <summary>Spese soggette al K.</summary>
    public decimal MarkableCost => LodgingCost + MealCost + CarCost + TransportCost;

    /// <summary>Costo secco totale (quello che continua ad alimentare la riga gialla).</summary>
    public decimal TravelCost => MarkableCost + AllowanceCost;

    public static ProjectCostTravelTotalsDto Of(IEnumerable<ProjectCostTravelRowDto> rows)
    {
        var t = new ProjectCostTravelTotalsDto();
        foreach (ProjectCostTravelRowDto r in rows)
        {
            t.LodgingCost += r.LodgingCost ?? 0;
            t.MealCost += r.MealCost ?? 0;
            t.AllowanceCost += r.AllowanceCost ?? 0;
            t.CarCost += r.CarCost ?? 0;
            t.TransportCost += r.TransportCost ?? 0;
        }
        return t;
    }
}

/// <summary>
/// Corpo di POST e PUT della riga. Vitto, indennità e auto NON sono qui: quelle colonne le
/// scrivono solo le calcolatrici, insieme al loro dettaglio (altrimenti totale e foglio
/// finirebbero per raccontare due storie diverse).
/// </summary>
public class ProjectCostTravelRowSaveRequest
{
    public int? ResourceId { get; set; }
    public string PersonName { get; set; } = "";
    public int? Nights { get; set; }
    public decimal? NightPrice { get; set; }
    public decimal? TransportCost { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Concorrenza ottimistica: NULL = scrivi comunque.</summary>
    public int? RowVersion { get; set; }
}
