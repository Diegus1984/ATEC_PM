using System;
using System.Collections.Generic;
using System.Linq;

namespace ATEC.PM.Shared.DTOs;

// ═══════════════════════════════════════════════════════════════
// GESTIONE TRASFERTA (blocco 6 del piano V32)
// Una commessa ha N step; ogni step ha N righe-persona a 14 colonne.
// I totali NON viaggiano precalcolati dal server per capriccio: le stesse formule servono
// alla griglia mentre si digita, quindi vivono in TravelMath e le usano entrambi i lati.
// ═══════════════════════════════════════════════════════════════

/// <summary>Le quattro calcolatrici di una riga-persona.</summary>
public static class TravelCalcKinds
{
    public const string Hours = "ore";
    public const string Meal = "vitto";
    public const string Allowance = "indennita";
    public const string Car = "auto";

    public static bool IsKnown(string? kind) =>
        kind is Hours or Meal or Allowance or Car;

    /// <summary>Chiave del foglio di calcolo del blocco 5 che tiene il dettaglio.</summary>
    public static string SheetKey(string kind, int rowId) => kind switch
    {
        Hours => CalcKeys.TravelHoursPrefix + rowId,
        Meal => CalcKeys.TravelMealPrefix + rowId,
        Allowance => CalcKeys.TravelAllowancePrefix + rowId,
        Car => CalcKeys.TravelCarPrefix + rowId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Calcolatrice sconosciuta"),
    };
}

/// <summary>Riga-persona di uno step. I campi calcolati sono proprietà, non colonne.</summary>
public class TravelRowDto
{
    public int Id { get; set; }
    public int StepId { get; set; }

    /// <summary>Dipendente scelto in anagrafica; NULL se il nome è solo storico.</summary>
    public int? EmployeeId { get; set; }

    /// <summary>Nome scritto sulla riga: resta leggibile anche se il dipendente sparisce.</summary>
    public string PersonName { get; set; } = "";

    /// <summary>
    /// Giorno della trasferta, per le righe che nascono dal Timesheet (#37/#52): sostituisce
    /// inizio/fine. NULL sulle righe scritte a mano, che continuano a usare le due date.
    /// </summary>
    public DateTime? WorkDate { get; set; }

    /// <summary>"MANUAL" (scritta a mano) o "TIMESHEET" (derivata). Vedi <see cref="TravelSources"/>.</summary>
    public string Source { get; set; } = TravelSources.Manual;

    /// <summary>
    /// Giorni imputati sulle righe derivate: il valore della giornata sulla prima riga di
    /// quella persona, 0 sulle altre — due fasi nello stesso giorno restano UN giorno di
    /// trasferta. Dalla #98 la giornata vale 0,5 se le ore di cantiere scaricate quel giorno
    /// sono al più 4 (<see cref="TravelMath.GiorniDaOre"/>), 1 altrimenti.
    /// NULL sulle righe manuali, dove i giorni si calcolano da inizio/fine.
    /// </summary>
    public decimal? TravelDays { get; set; }

    /// <summary>
    /// Le ore del Timesheet che avevano generato questa riga non ci sono più (cancellate o
    /// spostate). La riga resta con i suoi importi e viene segnalata a video: dentro può
    /// esserci una spesa vera imputata a mano.
    /// </summary>
    public bool HoursMissing { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>Sabati / domeniche fuori dal conteggio dei giorni, per riga.</summary>
    public bool ExcludeSat { get; set; }
    public bool ExcludeSun { get; set; }

    /// <summary>Ore trasferta: totale della calcolatrice «Ore» (Giorni × Ore Lav.).</summary>
    public decimal? Hours { get; set; }

    /// <summary>Tariffa oraria della riga: proposta dal reparto del dipendente, modificabile.</summary>
    public decimal? HourlyRate { get; set; }

    public int? Nights { get; set; }
    public decimal? NightPrice { get; set; }

    /// <summary>Totali delle altre tre calcolatrici.</summary>
    public decimal? MealCost { get; set; }
    public decimal? AllowanceCost { get; set; }
    public decimal? CarCost { get; set; }

    /// <summary>Treno/aereo: importo digitato, senza calcolatrice.</summary>
    public decimal? TransportCost { get; set; }

    public int SortOrder { get; set; }
    public int RowVersion { get; set; }

    /// <summary>
    /// Giorni trasferta. Riga derivata dal Timesheet → il valore deciso dal motore
    /// (1, 0,5 o 0, vedi <see cref="TravelDays"/>); riga a mano → fine − inizio + 1 con
    /// l'esclusione di sabato/domenica, come è sempre stato.
    /// </summary>
    public decimal? Days =>
        TravelDays ?? TravelMath.Days(StartDate, EndDate, ExcludeSat, ExcludeSun);

    /// <summary>La riga arriva dal Timesheet: fase, persona e giorno non si toccano a mano.</summary>
    public bool IsDerived => Source == TravelSources.Timesheet;

    /// <summary>Costi Personale = tariffa oraria × ore.</summary>
    public decimal? PersonnelCost => HourlyRate == null || Hours == null ? null : HourlyRate * Hours;

    /// <summary>Costo alloggio = notti × prezzo.</summary>
    public decimal? LodgingCost => Nights == null || NightPrice == null ? null : Nights * NightPrice;

    /// <summary>Costi trasferta della riga: alloggio + vitto + indennità + auto + treno/aereo.</summary>
    public decimal TravelCost =>
        (LodgingCost ?? 0) + (MealCost ?? 0) + (AllowanceCost ?? 0)
        + (CarCost ?? 0) + (TransportCost ?? 0);

    public decimal TotalCost => (PersonnelCost ?? 0) + TravelCost;
}

/// <summary>Totali di uno step o di un'intera commessa: le 8 colonne valorizzate della riga Totali.</summary>
public class TravelTotalsDto
{
    public decimal Days { get; set; }
    public decimal Hours { get; set; }
    public decimal PersonnelCost { get; set; }
    public decimal LodgingCost { get; set; }
    public decimal MealCost { get; set; }
    public decimal AllowanceCost { get; set; }
    public decimal CarCost { get; set; }
    public decimal TransportCost { get; set; }

    /// <summary>Badge «Totale costi trasferta»: tutto tranne il personale.</summary>
    public decimal TravelCost =>
        LodgingCost + MealCost + AllowanceCost + CarCost + TransportCost;

    /// <summary>Badge «Totale costi step».</summary>
    public decimal TotalCost => PersonnelCost + TravelCost;

    public static TravelTotalsDto Of(IEnumerable<TravelRowDto> rows)
    {
        var t = new TravelTotalsDto();
        foreach (TravelRowDto r in rows)
        {
            t.Days += r.Days ?? 0;
            t.Hours += r.Hours ?? 0;
            t.PersonnelCost += r.PersonnelCost ?? 0;
            t.LodgingCost += r.LodgingCost ?? 0;
            t.MealCost += r.MealCost ?? 0;
            t.AllowanceCost += r.AllowanceCost ?? 0;
            t.CarCost += r.CarCost ?? 0;
            t.TransportCost += r.TransportCost ?? 0;
        }
        return t;
    }
}

/// <summary>Provenienza di una riga di trasferta (#37/#52).</summary>
public static class TravelSources
{
    /// <summary>Scritta a mano: nessun automatismo la tocca.</summary>
    public const string Manual = "MANUAL";

    /// <summary>Nata dalle ore del Timesheet su una fase di cantiere.</summary>
    public const string Timesheet = "TIMESHEET";
}

public class TravelStepDto
{
    public int Id { get; set; }

    /// <summary>Fase di commessa da cui nasce lo step (#37/#52). NULL = step aperto a mano.</summary>
    public int? ProjectPhaseId { get; set; }

    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public int RowVersion { get; set; }
    public List<TravelRowDto> Rows { get; set; } = new();
    public TravelTotalsDto Totals => TravelTotalsDto.Of(Rows);
}

/// <summary>Riga della tabella «Riepilogo Trasferta»: un nominativo, i suoi totali su tutti gli step.</summary>
public class TravelSummaryRowDto
{
    public string PersonName { get; set; } = "";
    public TravelTotalsDto Totals { get; set; } = new();
}

/// <summary>Trasferta completa di una commessa.</summary>
public class TravelPlanDto
{
    public int ProjectId { get; set; }
    public List<TravelStepDto> Steps { get; set; } = new();

    /// <summary>Una riga per nominativo distinto, in ordine alfabetico italiano.</summary>
    public List<TravelSummaryRowDto> Summary { get; set; } = new();

    /// <summary>«Totale Riepilogo»: la somma di tutti gli step.</summary>
    public TravelTotalsDto GrandTotals { get; set; } = new();
}

/// <summary>Card della pagina «Gestione Trasferta»: i KPI per commessa.</summary>
public class TravelProjectSummaryDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";
    public int StepCount { get; set; }
    public decimal Days { get; set; }
    public decimal Hours { get; set; }
    public decimal PersonnelCost { get; set; }
    public decimal TravelCost { get; set; }

    /// <summary>
    /// Giorni di trasferta PREVENTIVATI (#98): risorse pianificate delle sezioni DA_CLIENTE,
    /// GG × il valore della giornata secondo <see cref="TravelMath.GiorniDaOre"/> sulle Ore/g.
    /// </summary>
    public decimal PlannedDays { get; set; }

    /// <summary>Costo delle ore di cantiere/trasferta dal timesheet (#92): informativo,
    /// NON entra nel SyncToBudget — quelle ore stanno già in «Risorse Atec».</summary>
    public decimal TravelHoursCost { get; set; }

    /// <summary>Ore di cantiere a consuntivo (#96): stesso perimetro DA_CLIENTE del costo qui sopra.</summary>
    public decimal TravelHours { get; set; }

    /// <summary>Monte ore pianificate delle fasi cantiere (#96): phase_assignments delle sezioni DA_CLIENTE.</summary>
    public decimal PlannedHours { get; set; }

    /// <summary>Costo preventivato di quelle ore, come lo calcola il Bilancio (tariffa media di sezione).</summary>
    public decimal PlannedHoursCost { get; set; }

    /// <summary>«Spese Trasferta» a preventivo: la stessa voce del Riepilogo Costi del Bilancio.</summary>
    public decimal PlannedTravelCost { get; set; }

    // ── Scarico ore da verificare (#102) ─────────────────────────
    // Perimetro: solo le ore su fasi «da cliente», che sono quelle che generano righe
    // di trasferta. Vedi Services/ScaricoOre.cs.

    /// <summary>Persone con ore di cantiere arrivate dopo l'ultima «Verifica effettuata».</summary>
    public int PendingPeople { get; set; }

    /// <summary>Ore di cantiere non ancora verificate.</summary>
    public decimal PendingHours { get; set; }

    /// <summary>Primo giorno dello scarico non verificato.</summary>
    public DateTime? PendingFrom { get; set; }

    /// <summary>Ultimo giorno dello scarico non verificato.</summary>
    public DateTime? PendingTo { get; set; }

    /// <summary>Quando è stata data l'ultima «Verifica effettuata». Null = mai guardata.</summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Chi l'ha data.</summary>
    public string VerifiedByName { get; set; } = "";

    /// <summary>La card va in rosso: c'è dello scarico che nessuno ha ancora guardato.</summary>
    public bool NeedsVerification => PendingPeople > 0;
}

// ── Corpi delle richieste ────────────────────────────────────────

public class TravelStepSaveRequest
{
    public string Description { get; set; } = "";
    public int? RowVersion { get; set; }
}

public class TravelRowSaveRequest
{
    public int? EmployeeId { get; set; }
    public string PersonName { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool ExcludeSat { get; set; }
    public bool ExcludeSun { get; set; }
    public decimal? HourlyRate { get; set; }
    public int? Nights { get; set; }
    public decimal? NightPrice { get; set; }
    public decimal? TransportCost { get; set; }

    /// <summary>Concorrenza ottimistica: NULL = scrivi comunque.</summary>
    public int? RowVersion { get; set; }
}

/// <summary>Nuovo ordine degli step (o delle righe di uno step) dopo un drag&amp;drop.</summary>
public class TravelReorderRequest
{
    public List<int> Ids { get; set; } = new();
}

/// <summary>
/// Aggrega costi Trasferta (#67): scrive gli stessi campi Alloggio/Vitto e Altri costi
/// su più righe selezionate in un colpo solo (una sola sync Bilancio).
/// Solo i nomi in <see cref="Fields"/> vengono applicati; un valore null azzera la colonna.
/// </summary>
public class TravelApplyCostsRequest
{
    public List<int> RowIds { get; set; } = new();

    public int? Nights { get; set; }
    public decimal? NightPrice { get; set; }
    public decimal? MealCost { get; set; }
    public decimal? AllowanceCost { get; set; }
    public decimal? CarCost { get; set; }
    public decimal? TransportCost { get; set; }

    /// <summary>
    /// Campi da scrivere: nights, nightPrice, mealCost, allowanceCost, carCost, transportCost.
    /// </summary>
    public List<string> Fields { get; set; } = new();
}

// ── Formule condivise ────────────────────────────────────────────

public static class TravelMath
{
    /// <summary>
    /// Giorni di trasferta: differenza INCLUSIVA fra inizio e fine, con esclusione selettiva
    /// dei sabati e/o delle domeniche. NULL se manca una data o se la fine precede l'inizio
    /// (un numero negativo sarebbe peggio di un «—»).
    /// </summary>
    public static int? Days(DateTime? start, DateTime? end, bool excludeSat, bool excludeSun)
    {
        if (start == null || end == null) return null;
        DateTime a = start.Value.Date, b = end.Value.Date;
        int span = (int)(b - a).TotalDays;
        if (span < 0) return null;
        if (!excludeSat && !excludeSun) return span + 1;

        int n = 0;
        for (int i = 0; i <= span; i++)
        {
            DayOfWeek wd = a.AddDays(i).DayOfWeek;
            if (wd == DayOfWeek.Saturday && excludeSat) continue;
            if (wd == DayOfWeek.Sunday && excludeSun) continue;
            n++;
        }
        return n;
    }

    /// <summary>
    /// Quanto vale una giornata di trasferta date le ore di cantiere di quel giorno (#98,
    /// regola di Paolo Zanoni): fino a 4 ore scaricate sul tag cliente è MEZZA giornata
    /// (0,5), oltre è una giornata intera (1). La stessa soglia è riscritta in SQL nel
    /// backfill della migrazione v98 e nel previsto di <c>TravelPlanService.Summaries</c>:
    /// se cambia qui deve cambiare anche là.
    /// </summary>
    public static decimal GiorniDaOre(decimal ore) => ore <= 4 ? 0.5m : 1m;

    /// <summary>Nominativi distinti di tutti gli step, in ordine alfabetico italiano.</summary>
    public static List<string> DistinctNames(IEnumerable<TravelStepDto> steps)
    {
        var italian = StringComparer.Create(
            System.Globalization.CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);
        return steps
            .SelectMany(s => s.Rows)
            .Select(r => (r.PersonName ?? "").Trim())
            .Where(n => n.Length > 0)
            .Distinct(italian)
            .OrderBy(n => n, italian)
            .ToList();
    }
}
