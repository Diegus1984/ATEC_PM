// ── Dati condivisi della trasferta di preventivo (segnalazione #33) ────────
//
// Le righe servono in due punti della stessa sezione — la tabella e la riga gialla di
// riepilogo — quindi la query sta qui, in un file senza componenti: chi le usa condivide
// la stessa chiave e quindi la stessa copia in cache (una sola GET per sezione).

import { useQuery } from "@tanstack/react-query"

import {
  fetchCostTravelRows,
  type ProjectCostTravelRow,
} from "@/lib/api/project-costing-travel"

/**
 * Chiave della cache. Senza `sectionId` identifica tutte le sezioni della commessa:
 * è la forma da invalidare quando arriva un evento realtime sul bilancio.
 */
export function costTravelRowsKey(
  projectId: number,
  sectionId?: number
): (string | number)[] {
  return sectionId == null
    ? ["project-cost-travel-rows", projectId]
    : ["project-cost-travel-rows", projectId, sectionId]
}

/** Righe trasferta di una sezione. Si interroga solo sulle sezioni con Tag Cliente. */
export function useCostTravelRows(
  projectId: number,
  sectionId: number,
  enabled: boolean
) {
  return useQuery({
    queryKey: costTravelRowsKey(projectId, sectionId),
    queryFn: () => fetchCostTravelRows(projectId, sectionId),
    enabled: enabled && projectId > 0 && sectionId > 0,
  })
}

/** Totali di colonna della tabella (gemelli di `TravelTotalsDto.Of` della Trasferta). */
export interface CostTravelTotals {
  lodgingCost: number
  mealCost: number
  allowanceCost: number
  carCost: number
  transportCost: number
  /** Alloggio + vitto + auto + treno/aereo: la parte che prende il K. */
  markableCost: number
  /** Costo secco: ricaricabili + indennità, senza K. */
  travelCost: number
}

export function sumCostTravelRows(
  rows: ProjectCostTravelRow[]
): CostTravelTotals {
  const totals: CostTravelTotals = {
    lodgingCost: 0,
    mealCost: 0,
    allowanceCost: 0,
    carCost: 0,
    transportCost: 0,
    markableCost: 0,
    travelCost: 0,
  }
  for (const row of rows) {
    totals.lodgingCost += row.lodgingCost ?? 0
    totals.mealCost += row.mealCost ?? 0
    totals.allowanceCost += row.allowanceCost ?? 0
    totals.carCost += row.carCost ?? 0
    totals.transportCost += row.transportCost ?? 0
    totals.markableCost += row.markableCost
    totals.travelCost += row.travelCost
  }
  return totals
}

/**
 * Il K si scrive sempre come moltiplicatore a tre decimali con la virgola («1,450»).
 * `String(1.45)` darebbe «1.45», che in un campo che parla di moltiplicatori si legge
 * male ed è esattamente l'equivoco che la regola vuole evitare.
 */
export function formatMarkup(value: number): string {
  return value.toFixed(3).replace(".", ",")
}
